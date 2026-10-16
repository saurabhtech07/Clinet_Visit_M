namespace ClientVisitManagement.Models;

/// <summary>
/// Daily Follow Up page ka model.
/// Grid (DynamicTableViewModel) + 4 dashboard cards + filters + dropdown lookups.
/// </summary>
public class DailyFollowUpViewModel
{
    // Grid
    public DynamicTableViewModel Grid { get; set; } = new();

    // 4 Cards
    public int TotalFollowUps { get; set; }
    public int PendingCount { get; set; }
    public int DueTodayCount { get; set; }
    public int OverdueCount { get; set; }

    // Filters
    public string? FilterStatus { get; set; }
    public string? FilterPriority { get; set; }
    public int? FilterAssignedTo { get; set; }
    public DateTime? FilterFrom { get; set; }
    public DateTime? FilterTo { get; set; }

    // Dropdown lookups
    public List<User> Users { get; set; } = new();

    // Fixed option sets (form + filter dono use karte hain)
    public static readonly string[] StatusOptions = { "Pending", "Done" };
    public static readonly string[] PriorityOptions = { "Low", "Medium", "High" };
    public static readonly string[] FollowUpTypeOptions = { "Phone Call", "Online", "Office" };
}
