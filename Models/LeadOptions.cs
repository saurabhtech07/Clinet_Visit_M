namespace ClientVisitManagement.Models;

/// <summary>
/// Leads / CRM ke fixed option sets — form, filter aur badges sab yahin se.
/// </summary>
public static class LeadOptions
{
    // Deal pipeline stages (Status = pipeline stage)
    public static readonly string[] StatusOptions =
        { "New", "Contacted", "Qualified", "Proposal Sent", "Negotiation", "Won", "Lost" };

    // Lead temperature
    public static readonly string[] PriorityOptions = { "Hot", "Warm", "Cold" };

    // Lead kahan se aaya
    public static readonly string[] SourceOptions =
        { "Website", "Referral", "Cold Call", "Exhibition", "Social Media", "Walk-in", "Email Campaign", "Other" };

    // Activity history type
    public static readonly string[] ActivityTypeOptions =
        { "Call", "Meeting", "Email", "Visit", "WhatsApp", "Note" };

    // Activity outcome
    public static readonly string[] OutcomeOptions =
        { "Positive", "Neutral", "Negative", "No Response", "Converted" };
}
