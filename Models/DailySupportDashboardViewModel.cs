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
        public DailySupportGridViewModel TicketTable { get; set; } = new DailySupportGridViewModel();
    }

    /// Daily Support grid (calls list) ke liye pagination + filter info.
    /// DynamicTableViewModel me sirf SearchTerm/FilterStatus hota hai,
    /// date aur user filter bhi chahiye (pager ke links me filters preserve
    /// karne ke liye) - isliye yaha extra fields rakhe hain.
    public class DailySupportGridViewModel : DynamicTableViewModel
    {
        public string FromDate { get; set; }
        public string ToDate { get; set; }
        public string FilterUser { get; set; }
    }
}
