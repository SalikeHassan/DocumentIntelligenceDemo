using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Data.Analysis;
using Newtonsoft.Json; 
using TaxComparisonExtractor.Services;

namespace TaxComparisonExtractor;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Tax Comparison Report Extractor ===\n");

        // Azure credentials - replace with your actual values

        
      // Get the project root directory (go up from bin/Debug/net9.0 to project root)
        string projectRoot = Directory.GetCurrentDirectory();
        
        // Navigate up from bin/Debug/net9.0/ to project root
        while (!File.Exists(Path.Combine(projectRoot, "TaxComparisonExtractor.csproj")))
        {
            var parent = Directory.GetParent(projectRoot);
            if (parent == null)
            {
                Console.WriteLine("Error: Could not find project root directory.");
                return;
            }
            projectRoot = parent.FullName;
        }
        
        // Path to your PDF in the project root
        string pdfPath = Path.Combine(projectRoot, "Documents", "comparison.pdf");

        Console.WriteLine($"Project Root: {projectRoot}");
        Console.WriteLine($"Looking for PDF at: {pdfPath}\n");

        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"Error: PDF not found at {pdfPath}");
            Console.WriteLine("Please place your PDF in the Documents folder at the project root.");
            return;
        }

        try
        {
            var processor = new DocumentProcessor(endpoint, key);
            await processor.ProcessTaxComparisonAsync(pdfPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
    }
}