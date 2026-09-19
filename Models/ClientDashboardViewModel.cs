namespace ClientVisitManagement.Models;

public class ClientDashboardViewModel
{
    public DynamicTableViewModel Clients { get; set; } = new();

    public int TotalClients { get; set; }
    public int TotalVisits { get; set; }
    public int ThisMonthVisits { get; set; }
    public int ActiveClients { get; set; }
}