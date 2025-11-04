using Azure.AI.DocumentIntelligence;
using Azure;
using TextExtractor.Models;
using Newtonsoft.Json;

namespace TextExtractor.Services;
public class DocumentIntelligenceDataFrameService
{
    private readonly DocumentIntelligenceClient _client;

    public DocumentIntelligenceDataFrameService(string endpoint, string key)
    {
        var credential = new AzureKeyCredential(key);
        _client = new DocumentIntelligenceClient(new Uri(endpoint), credential);
    }

    public async Task AnalyzeDocument(string pdfPath)
    {
        Console.WriteLine("Sending to Azure Document Intelligence (Layout Model)...");

        byte[] fileBytes = await File.ReadAllBytesAsync(pdfPath);
        BinaryData binaryData = BinaryData.FromBytes(fileBytes);

        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-layout",
            binaryData
        );

        var result = operation.Value;

        ProcessResult(result);

    }

    private void ProcessResult(AnalyzeResult result)
    {
        var pageDetails = new List<DocumentPageDetails>();

        foreach (var page in result.Pages)
        {
            var pageHeader = page.Lines.Where(x => x.Content.Contains("Two Year", StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault()?.Content ?? "";

            var formType = page.Lines.Where(x => x.Content.Contains("Form", StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault()?.Content ?? "";

            var pageNumber = page.PageNumber;

            pageDetails.Add(new DocumentPageDetails
            {
                PageNumber = pageNumber,
                PageHeader = pageHeader,
                FormType = formType
            });
        }

        pageDetails.Count();

        var report = GenerateHybridStructure(result, pageDetails);

        SaveStructuredJson(report);

    }


    public TaxReport GenerateHybridStructure(AnalyzeResult result, List<DocumentPageDetails> pageDetails)
    {
        var report = new TaxReport
        {
            Metadata = new ReportMetadata(),
            PageDetails = pageDetails,
            Tables = new List<TableData>()
        };

        foreach (var table in result.Tables)
        {
            if (table == null)
                continue;

            var tableData = ProcessTable(table, report.Tables.Count);
            report.Tables.Add(tableData);
        }

        return report;
    }

    private TableData ProcessTable(DocumentTable table, int tableIndex)
    {
        var tableData = new TableData
        {
            TableIndex = tableIndex,
            Columns = new List<ColumnDefinition>(),
            Data = new List<RowData>()
        };

        int columnCount = table.ColumnCount;

        var headerCells = table.Cells.Where(c => c.RowIndex == 0).OrderBy(c => c.ColumnIndex);

        foreach (var headerCell in headerCells)
        {
            tableData.Columns.Add(new ColumnDefinition
            {
                ColumnIndex = headerCell.ColumnIndex,
                ColumnId = $"col_{headerCell.ColumnIndex}",
                ColumnName = headerCell.Content ?? $"Column_{headerCell.ColumnIndex}"
            });
        }

        var rowGroups = table.Cells
            .Where(c => c.RowIndex > 0)
            .GroupBy(c => c.RowIndex)
            .OrderBy(g => g.Key);

        foreach (var rowGroup in rowGroups)
        {
            var rowData = new RowData
            {
                RowIndex = rowGroup.Key,
                RowId = $"row_{rowGroup.Key}",
                Fields = new List<FieldData>()
            };

            var cellsInRow = rowGroup.OrderBy(c => c.ColumnIndex);

            foreach (var cell in cellsInRow)
            {
                rowData.Fields.Add(new FieldData
                {
                    ColumnIndex = cell.ColumnIndex,
                    ColumnId = $"col_{cell.ColumnIndex}",
                    Value = cell.Content ?? "",
                    CleanedValue = cell.Content ?? ""
                });
            }

            tableData.Data.Add(rowData);
        }

        return tableData;
    }

    private void SaveStructuredJson(TaxReport report)
    {
        try
        {
            string jsonPath = Path.Combine(GetProjectRoot(), "tax_comparison_hybrid.json");

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };

            string json = JsonConvert.SerializeObject(report, settings);
            File.WriteAllText(jsonPath, json);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TextExtractor.Models;

public class TaxReport
{
    public ReportMetadata Metadata { get; set; }
    public List<DocumentPageDetails> PageDetails { get; set; }
    public List<TableData> Tables { get; set; }
}

public class DocumentPageDetails
{
    public int PageNumber { get; set; }

    public string PageHeader { get; set; }

    public string FormType { get; set; }
}

public class ReportMetadata
{
    public string TaxpayerName { get; set; }
    public string TaxpayerId { get; set; }
    public int Year1 { get; set; }
    public int Year2 { get; set; }
}

public class TableData
{
    public int TableIndex { get; set; }
    public List<ColumnDefinition> Columns { get; set; }
    public List<RowData> Data { get; set; }
}

public class ColumnDefinition
{
    public int ColumnIndex { get; set; }
    public string ColumnId { get; set; }
    public string ColumnName { get; set; }
}

public class RowData
{
    public int RowIndex { get; set; }
    public string RowId { get; set; }
    public List<FieldData> Fields { get; set; }
}

public class FieldData
{
    public int ColumnIndex { get; set; }
    public string ColumnId { get; set; }
    public string Value { get; set; }
    public string CleanedValue { get; set; }
}
