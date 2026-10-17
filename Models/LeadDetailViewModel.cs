namespace ClientVisitManagement.Models;

/// <summary>
/// Lead Detail (360°) page ka model.
/// Lead ki poori info + activity history + follow-up history ek jagah.
/// </summary>
public class LeadDetailViewModel
{
    public int LeadId { get; set; }

    /// <summary>Leads ki row (StateName / AssignedToName / ActivityCount sameta).</summary>
    public Dictionary<string, object?> Lead { get; set; } = new();

    /// <summary>LeadActivity rows (naya pehle) — AssignedName ke saath.</summary>
    public List<Dictionary<string, object?>> Activities { get; set; } = new();

    /// <summary>DailyFollowUp rows is lead ke (naya pehle).</summary>
    public List<Dictionary<string, object?>> FollowUps { get; set; } = new();

    /// <summary>Assigned To dropdown ke liye.</summary>
    public List<User> Users { get; set; } = new();

    // ---- Convenience getters (view me kaam aate hain) ----
    public T? Get<T>(string col)
    {
        if (!Lead.TryGetValue(col, out var v) || v == null || v is DBNull) return default;
        try { return (T)Convert.ChangeType(v, typeof(T)); } catch { return default; }
    }

    public string Str(string col) => Lead.TryGetValue(col, out var v) && v != null && v is not DBNull ? v.ToString() ?? "" : "";

    public int ActivityCount => Activities.Count;
    public int FollowUpCount => FollowUps.Count;
}
