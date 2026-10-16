namespace ClientVisitManagement.Models;

/// <summary>
/// User-wise Daily Report summary grid ka View popup model.
/// Ek SP user ka us din ka kaam: naye leads / follow-ups / converted leads ki list.
/// </summary>
public class DailyReportUserDetailVM
{
    public string UserName { get; set; } = "";

    public DateTime Day { get; set; }

    public int NewLeads { get; set; }
    public int TotalFollowUps { get; set; }
    public int ConvertedLeads { get; set; }

    /// <summary>Us din ke naye leads (Leads table se).</summary>
    public List<Dictionary<string, object?>> Leads { get; set; } = new();

    /// <summary>Us din ke follow ups (DailyFollowUp table se).</summary>
    public List<Dictionary<string, object?>> FollowUps { get; set; } = new();

    /// <summary>Us din convert hue leads.</summary>
    public List<Dictionary<string, object?>> Converted { get; set; } = new();
}
