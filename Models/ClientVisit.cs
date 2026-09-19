// namespace ClientVisitManagement.Models;

// public class ClientVisit
// {
//     public int VisitId { get; set; } // PK
//     public int ClientId { get; set; } // FK

//     public string? ClientCode { get; set; }
//     public string? ClientName { get; set; }
//     public string? City { get; set; }
//     public string? State { get; set; }
//     public string? Pincode { get; set; }

//     public int UserId { get; set; }
//     public string? VisitedBy { get; set; }

//     public DateTime VisitDate { get; set; }
//     public string? VisitTime { get; set; }

//     public string? PersonMet { get; set; }
//     public string? PersonName { get; set; }
//     public string? Designation { get; set; }
//     public string? ContactNo { get; set; }
//     public string? Email { get; set; }

//     public string? DiscussionRequirement { get; set; }

//     // Ye 3 fields missing the - Ab add kiye
//     public DateTime? NextFollowUpDate { get; set; }
//     public string? FollowUpStatus { get; set; } // Pending / Done / Overdue
//     public DateTime CreatedOn { get; set; } = DateTime.Now;

//     public string? Remarks { get; set; }
// }


using System;

namespace ClientVisitManagement.Models
{
    public class ClientVisit
    {
        public int VisitId { get; set; }
        public int ClientId { get; set; }
        public int UserId { get; set; }

        // Display fields
        public string? ClientCode { get; set; }
        public string? ClientName { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Pincode { get; set; }
        public string? VisitedBy { get; set; }

        public DateTime VisitDate { get; set; }
        public string? VisitTime { get; set; }
        public string? PersonMet { get; set; }
        public string? PersonName { get; set; }
        public string? Designation { get; set; }
        public string? ContactNo { get; set; }
        public string? Email { get; set; }
        public string? DiscussionRequirement { get; set; }
        public DateTime? NextFollowUpDate { get; set; }
        public string? FollowUpStatus { get; set; }
        public string? Remarks { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}