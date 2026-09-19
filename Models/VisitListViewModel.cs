// namespace ClientVisitManagement.Models;

// public class VisitListViewModel
// {
//     public List<ClientVisit> Visits { get; set; } = new();
//     public string? SearchTerm { get; set; }
//     public DateTime? FromDate { get; set; }
//     public DateTime? ToDate { get; set; }
//     public int Page { get; set; } = 1;
//     public int PageSize { get; set; } = 5;
//     public int TotalCount { get; set; }
//     public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
// }


using System;
using System.Collections.Generic;

namespace ClientVisitManagement.Models
{
    public class VisitListViewModel
    {
        public List<ClientVisit> Visits { get; set; } = new List<ClientVisit>();
        public string? SearchTerm { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalCount { get; set; }
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }
    }
}