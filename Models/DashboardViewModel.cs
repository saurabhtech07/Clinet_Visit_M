using System;
using System.Collections.Generic;

namespace ClientVisitManagement.Models;

/// <summary>
/// Dashboard overview - har value DB se LIVE count hoti hai, kuch cache nahi.
/// Agar koi record delete/update hoga to dashboard khud hi reflect karega.
/// </summary>
public class DashboardViewModel
{
    // ---------------- Client / Visit KPIs ----------------
    public int TotalClients { get; set; }
    public int ActiveClients { get; set; }
    public int TotalVisits { get; set; }
    public int VisitsThisMonth { get; set; }
    public int VisitsToday { get; set; }
    public int NewClientsThisMonth { get; set; }

    // ---------------- Follow-up KPIs ----------------
    public int PendingFollowUps { get; set; }
    public int DueTodayFollowUps { get; set; }
    public int OverdueFollowUps { get; set; }
    public int FollowUpsDone { get; set; }

    // ---------------- User KPIs ----------------
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }

    // ---------------- Support / Daily Support KPIs ----------------
    public int SupportTodayCalls { get; set; }
    public int SupportTotalCalls { get; set; }
    public int SupportOpenCalls { get; set; }
    public int SupportClosedToday { get; set; }
    public int SupportPending { get; set; }

    // ---------------- Charts ----------------
    public List<NameValue> MonthlyVisits { get; set; } = new();
    public List<NameValue> FollowUpStatuses { get; set; } = new();
    public List<NameValue> SupportStatuses { get; set; } = new();
    public List<NameValue> TopClients { get; set; } = new();
    public List<NameValue> SupportPriorities { get; set; } = new();

    // ---------------- Recent (live) ----------------
    public List<RecentVisit> RecentVisits { get; set; } = new();
    public List<RecentTicket> RecentTickets { get; set; } = new();

    public DateTime GeneratedOn { get; set; } = DateTime.Now;

    public bool HasSupportData { get; set; }
}

/// <summary>Chart ka ek segment - Name (label) + Value (count/amount).</summary>
public class NameValue
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

public class RecentVisit
{
    public string? ClientName { get; set; }
    public string? VisitDate { get; set; }
    public string? PersonName { get; set; }
    public string? PersonMet { get; set; }
}

public class RecentTicket
{
    public string? TicketNo { get; set; }
    public string? ClientName { get; set; }
    public string? Status { get; set; }
    public string? SupportDate { get; set; }
}