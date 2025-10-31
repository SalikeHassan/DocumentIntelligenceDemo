using Azure;
using Azure.AI.DocumentIntelligence;
using TaxComparisonExtractor.Models;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace TaxComparisonExtractor.Services;

public class DocumentProcessor
{
    private readonly DocumentIntelligenceClient _client;

    public DocumentProcessor(string endpoint, string key)
    {
        var credential = new AzureKeyCredential(key);
        _client = new DocumentIntelligenceClient(new Uri(endpoint), credential);
    }

    private string NormalizeWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    public async Task ProcessTaxComparisonAsync(string pdfPath)
    {
        Console.WriteLine($"Processing PDF: {pdfPath}\n");

        byte[] fileBytes = await File.ReadAllBytesAsync(pdfPath);
        BinaryData binaryData = BinaryData.FromBytes(fileBytes);

        Console.WriteLine("Sending document to Azure Document Intelligence...");

        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-layout",
            binaryData
        );

        AnalyzeResult result = operation.Value;

        Console.WriteLine($"✓ Analysis complete!\n");
        Console.WriteLine($"Document has {result.Pages?.Count ?? 0} page(s)");
        Console.WriteLine($"Found {result.Tables?.Count ?? 0} table(s)\n");

        ExtractTables(result);
        // Generate hybrid structure
        var hybridReport = GenerateHybridStructure(result);

        // Save the structured JSON
        SaveStructuredJson(hybridReport);
    }
    
 private void ExtractTables(AnalyzeResult result)
    {
        Console.WriteLine("=== EXTRACTED TABLES ===\n");

        for (int tableIndex = 0; tableIndex < result.Tables.Count; tableIndex++)
        {
            DocumentTable table = result.Tables[tableIndex];
            Console.WriteLine($"--- Table {tableIndex + 1} ---");
            Console.WriteLine($"Rows: {table.RowCount}, Columns: {table.ColumnCount}");
            Console.WriteLine();

            // Create a 2D array to hold table data with normalized whitespace
            string[,] tableData = new string[table.RowCount, table.ColumnCount];

            // Fill the array with cell content (with null handling and whitespace normalization)
            foreach (DocumentTableCell cell in table.Cells)
            {
                tableData[cell.RowIndex, cell.ColumnIndex] = NormalizeWhitespace(cell.Content ?? "");
            }

            // Print table in a formatted way
            for (int row = 0; row < table.RowCount; row++)
            {
                for (int col = 0; col < table.ColumnCount; col++)
                {
                    Console.Write($"{tableData[row, col],-30} | ");
                }
                Console.WriteLine();
                
                // Print separator after header row
                if (row == 0)
                {
                    Console.WriteLine(new string('-', table.ColumnCount * 33));
                }
            }
            Console.WriteLine("\n");
        }
    }

    private HybridTaxReport GenerateHybridStructure(AnalyzeResult result)
    {
        Console.WriteLine("=== GENERATING HYBRID STRUCTURE ===\n");

        var report = new HybridTaxReport
        {
            DocumentInfo = ExtractDocumentInfo(result),
            Tables = new List<TableData>()
        };

        if (result.Tables == null || result.Tables.Count == 0)
        {
            Console.WriteLine("No tables found in document.\n");
            return report;
        }

        // Process each table
        for (int tableIndex = 0; tableIndex < result.Tables.Count; tableIndex++)
        {
            try
            {
                DocumentTable? table = result.Tables[tableIndex];
                if (table == null)
                {
                    Console.WriteLine($"Table {tableIndex} is null, skipping...\n");
                    continue;
                }

                Console.WriteLine($"Processing Table {tableIndex}...");
                Console.WriteLine($"  Rows: {table.RowCount}, Columns: {table.ColumnCount}");

                // Create 2D array with normalized whitespace
                string[,] tableData = new string[table.RowCount, table.ColumnCount];

                // Initialize all cells with empty strings
                for (int r = 0; r < table.RowCount; r++)
                {
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        tableData[r, c] = "";
                    }
                }

                // Fill with actual data
                if (table.Cells != null)
                {
                    foreach (DocumentTableCell? cell in table.Cells)
                    {
                        if (cell != null)
                        {
                            tableData[cell.RowIndex, cell.ColumnIndex] = NormalizeWhitespace(cell.Content);
                        }
                    }
                }

                // Create table structure
                var tableStructure = new TableData
                {
                    FormType = DetectFormType(tableData, table.RowCount, table.ColumnCount),
                    RowCount = table.RowCount,
                    ColumnCount = table.ColumnCount,
                    Columns = new List<ColumnDefinition>(),
                    Data = new List<RowData>()
                };

                Console.WriteLine($"  Form Type: {tableStructure.FormType}");

                // Define columns
                for (int col = 0; col < table.ColumnCount; col++)
                {
                    string columnName = tableData[0, col];
                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        columnName = $"Column_{col}";
                    }

                    tableStructure.Columns.Add(new ColumnDefinition
                    {
                        ColumnIndex = col,
                        ColumnId = $"col_{col}",
                        ColumnName = columnName,
                        DataType = InferDataType(tableData, col, table.RowCount)
                    });
                }

                // Extract rows as field values
                for (int row = 1; row < table.RowCount; row++) // Skip header
                {
                    var rowData = new RowData
                    {
                        RowIndex = row,
                        RowId = $"row_{row}",
                        Fields = new List<FieldValue>()
                    };

                    // Each cell becomes a field value
                    for (int col = 0; col < table.ColumnCount; col++)
                    {
                        string cellValue = tableData[row, col] ?? "";
                        
                        var columnDef = tableStructure.Columns[col];
                        
                        rowData.Fields.Add(new FieldValue
                        {
                            ColumnIndex = col,
                            ColumnId = columnDef?.ColumnId ?? $"col_{col}",
                            ColumnName = columnDef?.ColumnName ?? $"Column_{col}",
                            Value = cellValue,
                            CleanedValue = CleanValue(cellValue, columnDef?.DataType ?? "text")
                        });
                    }

                    // Try to parse line item
                    var lineItem = TryExtractLineItem(rowData.Fields);
                    if (lineItem != null)
                    {
                        rowData.LineItem = lineItem;
                    }

                    tableStructure.Data.Add(rowData);
                }

                report.Tables.Add(tableStructure);
                Console.WriteLine($"  ✓ Extracted {tableStructure.Data.Count} data rows\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing table {tableIndex}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}\n");
            }
        }

        return report;
    }

    private DocumentInfo ExtractDocumentInfo(AnalyzeResult result)
    {
        var info = new DocumentInfo
        {
            PageCount = result.Pages?.Count ?? 0,
            TableCount = result.Tables?.Count ?? 0,
            Metadata = new Dictionary<string, string>()
        };

        if (result.Tables == null || result.Tables.Count == 0)
        {
            return info;
        }

        // Extract global metadata
        foreach (var table in result.Tables)
        {
            if (table == null) continue;

            string[,] tableData = new string[table.RowCount, table.ColumnCount];
            
            // Initialize
            for (int r = 0; r < table.RowCount; r++)
            {
                for (int c = 0; c < table.ColumnCount; c++)
                {
                    tableData[r, c] = "";
                }
            }

            // Fill data
            if (table.Cells != null)
            {
                foreach (DocumentTableCell? cell in table.Cells)
                {
                    if (cell != null)
                    {
                        tableData[cell.RowIndex, cell.ColumnIndex] = NormalizeWhitespace(cell.Content);
                    }
                }
            }

            // Search first few rows
            for (int row = 0; row < Math.Min(3, table.RowCount); row++)
            {
                for (int col = 0; col < table.ColumnCount; col++)
                {
                    string cell = tableData[row, col] ?? "";
                    if (string.IsNullOrWhiteSpace(cell)) continue;

                    // Taxpayer name
                    if (cell.Contains("&") && cell.Length > 10 && !info.Metadata.ContainsKey("taxpayerName"))
                    {
                        info.Metadata["taxpayerName"] = cell;
                    }

                    // Taxpayer ID
                    var ssnMatch = Regex.Match(cell, @"\d{3}-\d{2}-\d{4}");
                    if (ssnMatch.Success && !info.Metadata.ContainsKey("taxpayerId"))
                    {
                        info.Metadata["taxpayerId"] = ssnMatch.Value;
                    }
                }
            }
        }

        return info;
    }

    private string DetectFormType(string[,] tableData, int rowCount, int colCount)
    {
        try
        {
            for (int row = 0; row < Math.Min(3, rowCount); row++)
            {
                for (int col = 0; col < colCount; col++)
                {
                    string cell = tableData[row, col] ?? "";
                    string cellLower = cell.ToLower();

                    if (cellLower.Contains("form 1040") || cellLower.Contains("1040"))
                        return "Federal (1040)";
                    if (cellLower.Contains("ohio"))
                        return "State - Ohio";
                    if (cellLower.Contains("north carolina") || cellLower.Contains("d-400"))
                        return "State - North Carolina";
                    if (cellLower.Contains("california") || cellLower.Contains("540"))
                        return "State - California";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warning: Error detecting form type: {ex.Message}");
        }

        return "Unknown";
    }

    private string InferDataType(string[,] tableData, int col, int rowCount)
    {
        try
        {
            // Sample a few cells to infer type
            int numericCount = 0;
            int textCount = 0;

            for (int row = 1; row < Math.Min(6, rowCount); row++)
            {
                string cell = tableData[row, col] ?? "";
                if (string.IsNullOrWhiteSpace(cell)) continue;

                // Check if numeric (contains numbers, $, commas, etc.)
                if (Regex.IsMatch(cell, @"[\d$,\(\)\-\.]"))
                {
                    numericCount++;
                }
                else
                {
                    textCount++;
                }
            }

            return numericCount > textCount ? "numeric" : "text";
        }
        catch
        {
            return "text";
        }
    }

    private string CleanValue(string? value, string dataType)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        try
        {
            if (dataType == "numeric")
            {
                value = value.Replace("$", "")
                             .Replace(",", "")
                             .Replace(" ", "")
                             .Replace("|", "")
                             .Trim();
                
                if (value.StartsWith("(") && value.EndsWith(")"))
                {
                    value = "-" + value.Trim('(', ')');
                }
            }

            return value;
        }
        catch
        {
            return value ?? "";
        }
    }

    private LineItemInfo? TryExtractLineItem(List<FieldValue>? fields)
    {
        if (fields == null || fields.Count == 0)
            return null;

        try
        {
            // Look for line number pattern in any field
            foreach (var field in fields)
            {
                if (field?.Value == null) continue;

                var match = Regex.Match(field.Value, @"^(\d+)\.\s*(.+)$");
                if (match.Success)
                {
                    return new LineItemInfo
                    {
                        LineNumber = match.Groups[1].Value,
                        Description = match.Groups[2].Value.Trim()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Warning: Error parsing line item: {ex.Message}");
        }

        return null;
    }

    private void SaveStructuredJson(HybridTaxReport report)
    {
        try
        {
            Console.WriteLine("=== SAVING HYBRID JSON ===\n");

            string jsonPath = Path.Combine(GetProjectRoot(), "tax_comparison_hybrid.json");
            
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            
            string json = JsonConvert.SerializeObject(report, settings);
            File.WriteAllText(jsonPath, json);
            
            Console.WriteLine($"✓ Hybrid JSON saved to: {jsonPath}");
            Console.WriteLine($"  - Tables: {report.Tables?.Count ?? 0}");
            Console.WriteLine($"  - Total Rows: {report.Tables?.Sum(t => t.Data?.Count ?? 0) ?? 0}");
            
            if (report.DocumentInfo?.Metadata?.ContainsKey("taxpayerName") == true)
            {
                Console.WriteLine($"  - Taxpayer: {report.DocumentInfo.Metadata["taxpayerName"]}");
            }
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving JSON: {ex.Message}\n");
        }
    }

    private string GetProjectRoot()
    {
        string currentDir = Directory.GetCurrentDirectory();
        DirectoryInfo? directory = new DirectoryInfo(currentDir);
        
        while (directory != null)
        {
            if (directory.GetFiles("*.csproj").Length > 0)
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        
        return currentDir;
    }
}