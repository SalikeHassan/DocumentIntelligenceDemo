import PyPDF2
import os
from pdf2image import convert_from_path
import pytesseract
from pathlib import Path

class TaxPdfSplitter:
    def __init__(self, pdf_path):
        self.pdf_path = pdf_path
        self.output_dir = os.path.join(os.path.dirname(pdf_path), "split_pdfs")
        
    def split_pdf(self):
        """Main method to split the PDF by tax forms"""
        print(f"=== SPLITTING SCANNED PDF: {os.path.basename(self.pdf_path)} ===\n")
        
        # Step 1: Detect form type on each page using OCR
        page_form_types = self.detect_form_types_with_ocr()
        
        if not page_form_types:
            print("⚠️ No pages found in PDF.\n")
            return []
        
        print("\nDetected forms per page:")
        for page_info in page_form_types:
            print(f"  Page {page_info['page_num']}: {page_info['form_type']}")
        print()
        
        # Step 2: Group pages intelligently
        form_groups = self.group_pages_intelligently(page_form_types)
        
        print("Grouped forms:")
        for group in form_groups:
            pages = ", ".join(map(str, group['pages']))
            print(f"  {group['form_type']}: Pages {pages}")
        print()
        
        # Step 3: Create split PDFs
        split_pdfs = self.create_split_pdfs(form_groups)
        
        print(f"✓ Created {len(split_pdfs)} PDF files\n")
        return split_pdfs
    
    def detect_form_types_with_ocr(self):
        """Detect form types using OCR on scanned PDFs"""
        page_form_types = []
        
        print("🔍 Running OCR on scanned PDF pages...\n")
        
        try:
            # Convert PDF pages to images
            print("Converting PDF to images...")
            images = convert_from_path(self.pdf_path, dpi=150)
            print(f"✓ Converted {len(images)} pages to images\n")
            
            for page_num, image in enumerate(images):
                print(f"Processing page {page_num + 1}...")
                
                # Run OCR on the image
                text = pytesseract.image_to_string(image)
                
                print(f"  OCR extracted {len(text)} characters")
                
                if len(text) > 0:
                    # Show preview
                    preview = text[:200].replace('\n', ' ')
                    print(f"  Preview: {preview}...")
                else:
                    print("  ⚠️ No text extracted!")
                
                # Detect form type from FULL text (not just header)
                text_lower = text.lower()
                
                # Detect form type
                form_type = self.identify_form_type(text_lower, page_num)
                
                print(f"  → Detected: {form_type}\n")
                
                page_form_types.append({
                    'page_num': page_num + 1,
                    'page_index': page_num,
                    'form_type': form_type,
                    'text_length': len(text)
                })
                
        except Exception as e:
            print(f"❌ Error during OCR: {e}")
            import traceback
            traceback.print_exc()
            return []
        
        return page_form_types
    
    def identify_form_type(self, text_lower, page_num):
        """
        Identify form type from OCR text
        Priority order:
        1. Federal Form 1040
        2. Specific state forms (by state name or form number)
        3. Generic state (if contains "state" keywords)
        """
        
        # Federal detection
        if "form 1040" in text_lower or ("1040" in text_lower and "comparison" in text_lower):
            return "Federal"
        
        # State-specific detection
        # North Carolina
        if "north carolina" in text_lower:
            if "d-400" in text_lower or "d 400" in text_lower:
                return "State - North Carolina (D-400)"
            return "State - North Carolina"
        
        # Ohio
        if "ohio" in text_lower:
            if "it-1040" in text_lower or "it 1040" in text_lower or "it1040" in text_lower:
                return "State - Ohio (IT-1040)"
            if "nonresident" in text_lower:
                return "State - Ohio (Nonresident)"
            return "State - Ohio"
        
        # California
        if "california" in text_lower:
            if "540" in text_lower:
                return "State - California (540)"
            return "State - California"
        
        # New York
        if "new york" in text_lower:
            if "it-201" in text_lower or "it 201" in text_lower:
                return "State - New York (IT-201)"
            return "State - New York"
        
        # Texas
        if "texas" in text_lower:
            return "State - Texas"
        
        # Florida
        if "florida" in text_lower:
            return "State - Florida"
        
        # Pennsylvania
        if "pennsylvania" in text_lower:
            return "State - Pennsylvania"
        
        # New Jersey
        if "new jersey" in text_lower:
            return "State - New Jersey"
        
        # Illinois
        if "illinois" in text_lower:
            return "State - Illinois"
        
        # Massachusetts
        if "massachusetts" in text_lower:
            return "State - Massachusetts"
        
        # Generic state detection (if no specific state found)
        if "state" in text_lower and ("income tax" in text_lower or "return" in text_lower or "comparison" in text_lower):
            return f"State - Unknown (Page {page_num + 1})"
        
        return "Unknown"
    
    def group_pages_intelligently(self, page_form_types):
        """
        Group pages intelligently:
        - Federal: Can span multiple pages (1, 2, ...)
        - States: Each state is separate (no grouping across different states)
        """
        if not page_form_types:
            return []
        
        groups = []
        current_group = None
        
        for page_info in page_form_types:
            form_type = page_info['form_type']
            
            if current_group is None:
                # Start first group
                current_group = {
                    'form_type': form_type,
                    'pages': [page_info['page_num']],
                    'page_indices': [page_info['page_index']]
                }
            else:
                # Check if this page belongs to current group
                if self.should_group_with_previous(current_group['form_type'], form_type):
                    # Add to current group
                    current_group['pages'].append(page_info['page_num'])
                    current_group['page_indices'].append(page_info['page_index'])
                else:
                    # Save current group and start new one
                    groups.append(current_group)
                    current_group = {
                        'form_type': form_type,
                        'pages': [page_info['page_num']],
                        'page_indices': [page_info['page_index']]
                    }
        
        # Add last group
        if current_group:
            groups.append(current_group)
        
        return groups
    
    def should_group_with_previous(self, previous_form, current_form):
        """
        Determine if current page should be grouped with previous
        
        Rules:
        - Federal pages stay together (Federal + Federal = same group)
        - State pages DON'T group together (State X + State Y = different groups)
        - Unknown pages group with previous if it's Federal
        """
        
        # Same exact form type = group together
        if previous_form == current_form:
            return True
        
        # Federal continuation: Federal + Unknown = group together
        if previous_form == "Federal" and current_form == "Unknown":
            return True
        
        # Different forms = separate groups
        return False
    
    def create_split_pdfs(self, form_groups):
        """Create separate PDF files for each form group"""
        split_pdfs = []
        
        # Create output directory
        if os.path.exists(self.output_dir):
            import shutil
            shutil.rmtree(self.output_dir)
        os.makedirs(self.output_dir)
        
        try:
            with open(self.pdf_path, 'rb') as file:
                pdf_reader = PyPDF2.PdfReader(file)
                
                for counter, group in enumerate(form_groups, start=1):
                    # Create filename
                    form_name = group['form_type'].replace(" ", "_").replace("(", "").replace(")", "").replace("-", "")
                    output_filename = f"{counter}_{form_name}.pdf"
                    output_path = os.path.join(self.output_dir, output_filename)
                    
                    # Create new PDF writer
                    pdf_writer = PyPDF2.PdfWriter()
                    
                    # Add pages to new PDF
                    for page_index in group['page_indices']:
                        pdf_writer.add_page(pdf_reader.pages[page_index])
                    
                    # Write to file
                    with open(output_path, 'wb') as output_file:
                        pdf_writer.write(output_file)
                    
                    split_pdfs.append({
                        'file_path': output_path,
                        'form_type': group['form_type'],
                        'pages': group['pages']
                    })
                    
                    print(f"✓ Created: {output_filename}")
                    print(f"    Form: {group['form_type']}")
                    print(f"    Pages: {', '.join(map(str, group['pages']))}")
                    
        except Exception as e:
            print(f"❌ Error creating split PDFs: {e}")
            import traceback
            traceback.print_exc()
            return []
        
        return split_pdfs


# Test the splitter
if __name__ == "__main__":
    # PUT YOUR PDF PATH HERE
    pdf_path = "comparison.pdf"
    
    if not os.path.exists(pdf_path):
        print(f"❌ PDF file not found: {pdf_path}")
    else:
        splitter = TaxPdfSplitter(pdf_path)
        split_pdfs = splitter.split_pdf()
        
        print("\n=== SPLIT RESULTS ===")
        for pdf in split_pdfs:
            print(f"\nFile: {os.path.basename(pdf['file_path'])}")
            print(f"  Type: {pdf['form_type']}")
            print(f"  Pages: {', '.join(map(str, pdf['pages']))}")