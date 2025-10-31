namespace TaxComparisonExtractor.Models;

/// <summary>
/// Hybrid structure combining GoFileRoom-style field definitions with table structure
/// </summary>
public class HybridTaxReport
{
    public DocumentInfo DocumentInfo { get; set; } = new();
    public List<TableData> Tables { get; set; } = new();
}

public class DocumentInfo
{
    public int PageCount { get; set; }
    public int TableCount { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class TableData
{
    public string FormType { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    
    /// <summary>
    /// Column definitions (like GoFileRoom's index definitions)
    /// </summary>
    public List<ColumnDefinition> Columns { get; set; } = new();
    
    /// <summary>
    /// Row data with field values
    /// </summary>
    public List<RowData> Data { get; set; } = new();
}

public class ColumnDefinition
{
    public int ColumnIndex { get; set; }
    public string ColumnId { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty; // "text" or "numeric"
}

public class RowData
{
    public int RowIndex { get; set; }
    public string RowId { get; set; } = string.Empty;
    
    /// <summary>
    /// Field values for each column (like GoFileRoom style)
    /// </summary>
    public List<FieldValue> Fields { get; set; } = new();
    
    /// <summary>
    /// Parsed line item info (if applicable)
    /// </summary>
    public LineItemInfo? LineItem { get; set; }
}

public class FieldValue
{
    public int ColumnIndex { get; set; }
    public string ColumnId { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string CleanedValue { get; set; } = string.Empty;
}

public class LineItemInfo
{
    public string LineNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}