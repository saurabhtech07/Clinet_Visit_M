using System.Collections.Generic;

namespace ClientVisitManagement.Models
{
    public class DailySupportDashboardViewModel
    {
        public int TodayCalls { get; set; }
        public int TotalCalls { get; set; }
        public int OpenCalls { get; set; }
        public int ClosedToday { get; set; }
        public int PendingCalls { get; set; }
        public string SearchTerm { get; set; }
        public string FromDate { get; set; }
        public string ToDate { get; set; }
        public string FilterStatus { get; set; }
        public string FilterUser { get; set; }
        public List<Dictionary<string, object?>> Users { get; set; } = new();
        public DynamicTableViewModel TicketTable { get; set; } = new DynamicTableViewModel();
    }
}