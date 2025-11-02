import os
import re
import json
from azure.core.credentials import AzureKeyCredential
from azure.ai.documentintelligence import DocumentIntelligenceClient
from dotenv import load_dotenv


# =============================================
#  OCR JSON Builder (Replicates C# Logic)
# =============================================
class OcrJsonBuilder:
    def __init__(self):
        load_dotenv()

        endpoint = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT")
        key = os.getenv("AZURE_DOCUMENT_INTELLIGENCE_KEY")

        if not endpoint or not key:
            raise ValueError("❌ Azure credentials not found in .env file")

        self.client = DocumentIntelligenceClient(
            endpoint=endpoint, credential=AzureKeyCredential(key)
        )

    # -----------------------------------------
    # 1. Entry point for split PDFs
    # -----------------------------------------
    def process_split_pdfs(self, split_pdfs):
        print("\n=== PROCESSING SPLIT PDFs WITH AZURE LAYOUT MODEL ===\n")

        report = {
            "documentInfo": {"pageCount": 0, "tableCount": 0, "metadata": {}},
            "tables": []
        }

        for pdf_info in split_pdfs:
            pdf_path = pdf_info["file_path"]
            form_type = pdf_info.get("form_type", "Unknown")

            print(f"📄 Processing: {os.path.basename(pdf_path)}")
            print(f"   Form Type: {form_type}")

            result = self.run_azure_layout(pdf_path)
            if not result:
                print(f"   ⚠️ Skipping (no result)\n")
                continue

            # Update document info
            report["documentInfo"]["pageCount"] += len(result.pages or [])
            report["documentInfo"]["tableCount"] += len(result.tables or [])

            # Extract metadata (taxpayer name, id)
            self.extract_metadata(result, report["documentInfo"]["metadata"])

            # Build structured tables
            tables = self.generate_hybrid_structure(result, form_type)
            report["tables"].extend(tables)

        print("\n=== SAVING HYBRID JSON ===\n")
        with open("tax_comparison_hybrid.json", "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)

        print("✅ Saved: tax_comparison_hybrid.json")
        print(f"  - Tables: {len(report['tables'])}")
        print(f"  - Total Pages: {report['documentInfo']['pageCount']}")
        print(f"  - Taxpayer: {report['documentInfo']['metadata'].get('taxpayerName', 'Unknown')}\n")

        return report

    # -----------------------------------------
    # 2. Run Azure Document Intelligence
    # -----------------------------------------
    def run_azure_layout(self, pdf_path):
        try:
            with open(pdf_path, "rb") as f:
                pdf_bytes = f.read()

            print("   🔍 Sending to Azure (prebuilt-layout)...")
            poller = self.client.begin_analyze_document("prebuilt-layout", pdf_bytes)
            result = poller.result()
            print(f"   ✓ Layout extraction complete: {len(result.pages)} pages, {len(result.tables)} tables")
            return result
        except Exception as e:
            print(f"   ❌ Error: {e}")
            return None

    # -----------------------------------------
    # 3. Extract metadata (name, SSN)
    # -----------------------------------------
    def extract_metadata(self, result, metadata):
        for table in result.tables or []:
            grid = [["" for _ in range(table.column_count)] for _ in range(table.row_count)]
            for cell in table.cells:
                grid[cell.row_index][cell.column_index] = (cell.content or "").strip()

            for r in range(min(3, table.row_count)):
                for c in range(table.column_count):
                    cell = grid[r][c]
                    if not cell:
                        continue

                    # Taxpayer name (contains &)
                    if "&" in cell and len(cell) > 10 and "taxpayerName" not in metadata:
                        metadata["taxpayerName"] = cell

                    # Taxpayer ID (XXX-XX-XXXX)
                    ssn = re.search(r"\d{3}-\d{2}-\d{4}", cell)
                    if ssn and "taxpayerId" not in metadata:
                        metadata["taxpayerId"] = ssn.group(0)

    # -----------------------------------------
    # 4. Generate hybrid JSON tables
    # -----------------------------------------
    def generate_hybrid_structure(self, result, form_type):
        tables_out = []
        for t_index, table in enumerate(result.tables or []):
            try:
                grid = [["" for _ in range(table.column_count)] for _ in range(table.row_count)]
                for cell in table.cells:
                    grid[cell.row_index][cell.column_index] = (cell.content or "").strip()

                table_data = {
                    "formType": self.detect_form_type(grid),
                    "rowCount": table.row_count,
                    "columnCount": table.column_count,
                    "columns": [],
                    "data": []
                }

                # Define columns (header row)
                for c in range(table.column_count):
                    col_name = grid[0][c] or f"Column_{c}"
                    table_data["columns"].append({
                        "columnIndex": c,
                        "columnId": f"col_{c}",
                        "columnName": col_name,
                        "dataType": self.infer_data_type(grid, c)
                    })

                # Add rows (skip header)
                for r in range(1, table.row_count):
                    fields = []
                    for c, col in enumerate(table_data["columns"]):
                        val = grid[r][c]
                        fields.append({
                            "columnIndex": c,
                            "columnId": col["columnId"],
                            "columnName": col["columnName"],
                            "value": val,
                            "cleanedValue": self.clean_value(val, col["dataType"])
                        })

                    line_item = self.try_extract_line_item(fields)
                    row_data = {
                        "rowIndex": r,
                        "rowId": f"row_{r}",
                        "fields": fields,
                    }
                    if line_item:
                        row_data["lineItem"] = line_item

                    table_data["data"].append(row_data)

                tables_out.append(table_data)
                print(f"   ✓ Table {t_index + 1}: {table.row_count} rows, {table.column_count} cols")

            except Exception as ex:
                print(f"   ⚠️ Error processing table {t_index}: {ex}")

        return tables_out

    # -----------------------------------------
    # 5. Detect Form Type (Fixed placement)
    # -----------------------------------------
    def detect_form_type(self, grid):
        for r in range(min(3, len(grid))):
            for cell in grid[r]:
                c = cell.lower()
                if "1040" in c:
                    return "Federal (1040)"
                if "ohio" in c:
                    return "State - Ohio"
                if "north carolina" in c or "d-400" in c:
                    return "State - North Carolina"
                if "california" in c or "540" in c:
                    return "State - California"
        return "Unknown"

    # -----------------------------------------
    # 6. Helpers
    # -----------------------------------------
    def infer_data_type(self, grid, col):
        numeric_count = 0
        text_count = 0
        for r in grid[1:6]:
            if len(r) <= col:
                continue
            cell = r[col]
            if not cell:
                continue
            if re.search(r"[\d$,\(\)\-\.]", cell):
                numeric_count += 1
            else:
                text_count += 1
        return "numeric" if numeric_count > text_count else "text"

    def clean_value(self, value, data_type):
        if not value:
            return ""
        if data_type == "numeric":
            v = value.replace("$", "").replace(",", "").replace(" ", "").replace("|", "").strip()
            if v.startswith("(") and v.endswith(")"):
                v = "-" + v[1:-1]
            return v
        return value.strip()

    def try_extract_line_item(self, fields):
        for f in fields:
            val = f["value"]
            match = re.match(r"^(\d+)\.\s*(.+)$", val)
            if match:
                return {
                    "lineNumber": match.group(1),
                    "description": match.group(2)
                }
        return None


# =============================================
#  Runner (Main)
# =============================================
def main():
    from pdf_splitter import TaxPdfSplitter

    pdf_path = "comparison.pdf"
    if not os.path.exists(pdf_path):
        print(f"❌ PDF not found: {pdf_path}")
        return

    print("=" * 60)
    print("STEP 1: SPLITTING PDF")
    print("=" * 60)
    splitter = TaxPdfSplitter(pdf_path)
    split_pdfs = splitter.split_pdf()

    if not split_pdfs:
        print("❌ No PDFs were split")
        return

    print("\n" + "=" * 60)
    print("STEP 2: RUNNING AZURE OCR (LAYOUT MODEL)")
    print("=" * 60)
    builder = OcrJsonBuilder()
    builder.process_split_pdfs(split_pdfs)


if __name__ == "__main__":
    main()