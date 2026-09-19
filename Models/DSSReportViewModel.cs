using System.Collections.Generic;

namespace ClientVisitManagement.Models
{
    public class DSSReportViewModel
    {
        public List<Dictionary<string, object?>> Calls { get; set; } = new List<Dictionary<string, object?>>();
        public int TotalCalls { get; set; }
        public int OpenCalls { get; set; }
        public int ClosedCalls { get; set; }
        public int PendingCalls { get; set; }
        public int TodayCalls { get; set; }
        public int ClosedToday { get; set; }

        public string? FromDate { get; set; }
        public string? ToDate { get; set; }
        public string? FilterStatus { get; set; }
        public string? FilterPriority { get; set; }
        public string? FilterChannel { get; set; }
        public string? FilterCategory { get; set; }
        public string? FilterCustomer { get; set; }
        public string? FilterEngineer { get; set; }
        public string? SearchTerm { get; set; }

        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
