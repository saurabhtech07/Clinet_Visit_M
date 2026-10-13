using System;
using System.Collections.Generic;

namespace ClientVisitManagement.Models;

/// <summary>
/// Follow-up Center page ka model.
/// Har section (Overdue/Today/Upcoming/No Date/Done) live DB query se banta hai.
/// </summary>
public class FollowUpViewModel
{
    public List<FollowUpRow> All        { get; set; } = new();
    public List<FollowUpRow> Overdue    { get; set; } = new();
    public List<FollowUpRow> DueToday   { get; set; } = new();
    public List<FollowUpRow> Upcoming   { get; set; } = new();
    public List<FollowUpRow> NoSchedule { get; set; } = new();
    public List<FollowUpRow> Done       { get; set; } = new();

    // Filters
    public int? FilterUserId { get; set; }
    public string? Search    { get; set; }
    public string? StatusFilter { get; set; }  // "" | pending | overdue | today | upcoming | nodate | done
    public string? Section   { get; set; }   // overdue|today|upcoming|pending|done

    // Paging (grid)
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; } = "DESC";

    // Dropdowns ke liye
    public List<User>   Users   { get; set; } = new();
    public List<Client> Clients { get; set; } = new();
}

public class FollowUpRow
{
    public int FollowUpId    { get; set; }
    public int ClientId      { get; set; }
    public string? ClientCode { get; set; }
    public string? ClientName { get; set; }
    public string? City      { get; set; }

    public int? VisitId      { get; set; }
    public int? UserId       { get; set; }
    public string? AssignedTo { get; set; }

    public string? SourceType { get; set; }        // Visit / Manual (aage Lead, Support)
    public string? Description { get; set; }
    public DateTime? NextFollowUpDate { get; set; }
    public string? FollowUpStatus { get; set; }    // Pending / Done
    public string? Remarks     { get; set; }
    public DateTime? DoneOn    { get; set; }
    public DateTime? CreatedOn { get; set; }
}