namespace ClientVisitManagement.Models;

/// <summary>
/// Daily Report page ka model — daily lead activity entry.
/// Grid (DynamicTableViewModel) + summary cards + new-leads-by-user + filters + user lookup.
/// NOTE: Daily Report ek plain record hai — isme koi Status / approval workflow nahi.
/// </summary>
public class DailyReportViewModel
{
    // Grid
    public DynamicTableViewModel Grid { get; set; } = new();

    // Cards
    public int TotalLeads { get; set; }        // Total Records/saare leads (all-time)
    public int TotalLeadsToday { get; set; }   // aaj ke leads
    public int TotalFollowUps { get; set; }    // aaj ke follow ups

    // User-wise summary grid
    public bool IsUserSummary { get; set; }
    public DateTime SummaryDay { get; set; }

    // Filters
    public int? FilterAssignedTo { get; set; }
    public DateTime? FilterFrom { get; set; }
    public DateTime? FilterTo { get; set; }

    // Dropdown lookups
    public List<User> Users { get; set; } = new();
}
