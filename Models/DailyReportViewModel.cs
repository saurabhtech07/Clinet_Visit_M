namespace ClientVisitManagement.Models;

/// <summary>
/// Daily Report page ka model — daily lead activity entry.
/// Grid (DynamicTableViewModel) + 4 summary cards + filters + user lookup.
/// </summary>
public class DailyReportViewModel
{
    // Grid
    public DynamicTableViewModel Grid { get; set; } = new();

    // 4 Cards
    public int TotalReports { get; set; }
    public int TotalCalls { get; set; }
    public int TotalVisits { get; set; }
    public int TotalNewLeads { get; set; }

    // Filters
    public int? FilterAssignedTo { get; set; }
    public string? FilterStatus { get; set; }
    public DateTime? FilterFrom { get; set; }
    public DateTime? FilterTo { get; set; }

    // Dropdown lookups
    public List<User> Users { get; set; } = new();

    // Fixed option set (form + filter dono use karte hain)
    public static readonly string[] StatusOptions = { "Pending", "Done" };
}
