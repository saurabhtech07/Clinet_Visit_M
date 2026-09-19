namespace ClientVisitManagement.Models;

public class DynamicTableViewModel
{
    public List<string> Columns { get; set; } = new();

    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    public int TotalRecords { get; set; }

    public int CurrentPage { get; set; }

    public int PageSize { get; set; }

    public string? SearchTerm { get; set; }

    public string? SortColumn { get; set; }

    public string SortDirection { get; set; } = "DESC";

    public int TotalPages =>
        PageSize > 0
            ? (int)Math.Ceiling(TotalRecords / (double)PageSize)
            : 0;
}