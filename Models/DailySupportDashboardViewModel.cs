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

        /// <summary>
        /// "My Work" dashboard ke liye mere saare open tickets (bina pagination).
        /// Grid ki jagah ye list cards ke roop me dikhti hai.
        /// </summary>
        public List<Dictionary<string, object?>> MyTasks { get; set; } = new();

        /// <summary>"My Work" mode ON hai kya? (same page, sirf mera kaam)</summary>
        public bool IsMyWork { get; set; }
        /// <summary>Grid ke pagination/Edit links kis base URL par banne hain</summary>
        public string BaseUrl { get; set; } = "/DailySupport";
        /// <summary>Logged-in user ka UserId (null = login nahi hai)</summary>
        public int? MyUserId { get; set; }
        /// <summary>Logged-in user ka naam</summary>
        public string MyUserName { get; set; } = "";
        /// <summary>Badge ke liye - mere OPEN (Closed ke alawa) tickets ki sankhya</summary>
        public int MyPendingCount { get; set; }
        /// <summary>Mujhe address hue unread comments ki total sankhya (My Work banner ke liye)</summary>
        public int MyUnreadComments { get; set; }
        /// <summary>Unread comments jinpar mera open task nahi (Closed / reassign ho gaya) - alag block</summary>
        public List<Dictionary<string, object?>> MyNewComments { get; set; } = new();
        /// <summary>Admin/Manager hai kya? (sirf ye dusron ko kaam assign kar sakte hain)</summary>
        public bool CanAssign { get; set; }
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
        /// <summary>Pagination + Edit link kis URL par bana hai (My Work mode me same page)</summary>
        public string BaseUrl { get; set; } = "/DailySupport";
        /// <summary>"My Work" mode ON hai kya? (Edit link me flag carry karne ke liye)</summary>
        public bool IsMyWork { get; set; }
        /// <summary>Grid me Re-Assign button dikhana hai kya? (sirf Admin/Manager)</summary>
        public bool CanAssign { get; set; }
    }
}
