using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

/// <summary>
/// Dashboard ka sab data LIVE SQL queries se aata hai - kuch bhi cache nahi.
/// Har query apne try/catch me hai: agar koi table/column ho hi nahi (jaise
/// naya feature), dashboard tootne ke bajaye sirf us block ko 0/empty dikha
/// dega, baaki sab normal chalta rahega.
/// </summary>
public class DashboardRepository
{
    private readonly DBHelper _db;

    public DashboardRepository(DBHelper db) => _db = db;

    public DashboardViewModel GetOverview()
    {
        var vm = new DashboardViewModel();

        using var con = _db.GetConnection();
        con.Open();

        // ---------------- Client / Visit ----------------
        vm.TotalClients     = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM Client"));
        vm.ActiveClients    = Exec(() => ScalarInt(con, "SELECT COUNT(DISTINCT ClientId) FROM ClientVisit"));
        vm.TotalVisits      = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM ClientVisit"));
        vm.VisitsThisMonth  = Exec(() => ScalarInt(con, """
            SELECT COUNT(*) FROM ClientVisit
            WHERE VisitDate >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
              AND VisitDate <  DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
            """));
        vm.VisitsToday = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM ClientVisit WHERE CAST(VisitDate AS DATE) = CAST(GETDATE() AS DATE)"));
        vm.NewClientsThisMonth = Exec(() => ScalarInt(con, """
            SELECT COUNT(*) FROM Client
            WHERE CreatedOn >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
            """));

        // ---------------- Follow-up (FollowUp table se; agar table na ho to purane ClientVisit se fallback) ---------------- 
        vm.PendingFollowUps = ExecWithFallback(con,
            "SELECT COUNT(*) FROM FollowUp WHERE FollowUpStatus='Pending'",
            "SELECT COUNT(*) FROM ClientVisit WHERE FollowUpStatus='Pending'");
        vm.DueTodayFollowUps = ExecWithFallback(con,
            "SELECT COUNT(*) FROM FollowUp WHERE FollowUpStatus='Pending' AND NextFollowUpDate = CAST(GETDATE() AS DATE)",
            "SELECT COUNT(*) FROM ClientVisit WHERE FollowUpStatus='Pending' AND NextFollowUpDate = CAST(GETDATE() AS DATE)");
        vm.OverdueFollowUps = ExecWithFallback(con,
            "SELECT COUNT(*) FROM FollowUp WHERE FollowUpStatus='Pending' AND NextFollowUpDate IS NOT NULL AND NextFollowUpDate < CAST(GETDATE() AS DATE)",
            "SELECT COUNT(*) FROM ClientVisit WHERE FollowUpStatus IN ('Pending','In Progress') AND NextFollowUpDate < CAST(GETDATE() AS DATE)");
        vm.FollowUpsDone = ExecWithFallback(con,
            "SELECT COUNT(*) FROM FollowUp WHERE FollowUpStatus='Done'",
            "SELECT COUNT(*) FROM ClientVisit WHERE FollowUpStatus IN ('Done','Completed','Closed')");

        // ---------------- Users ----------------
        vm.TotalUsers  = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM Users"));
        vm.ActiveUsers = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM Users WHERE IsActive = 1"));

        // ---------------- Daily Support ----------------
        vm.SupportTodayCalls  = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM DailySupport WHERE CAST(SupportDate AS DATE) = CAST(GETDATE() AS DATE)"));
        vm.SupportTotalCalls  = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM DailySupport"));
        vm.SupportOpenCalls   = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM DailySupport WHERE Status IN ('Open','In Progress','On Hold','Pending')"));
        vm.SupportClosedToday = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM DailySupport WHERE Status = 'Closed' AND CAST(SupportDate AS DATE) = CAST(GETDATE() AS DATE)"));
        vm.SupportPending     = Exec(() => ScalarInt(con, "SELECT COUNT(*) FROM DailySupport WHERE Status IN ('In Progress','Pending','On Hold')"));
        vm.HasSupportData = vm.SupportTotalCalls > 0;

        // ---------------- Charts ----------------
        vm.MonthlyVisits = ExecList(con, """
            SELECT CONVERT(VARCHAR(3), VisitDate, 100) AS Label, COUNT(*) AS Cnt
            FROM ClientVisit
            WHERE VisitDate >= DATEADD(MONTH, -5, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
            GROUP BY CONVERT(VARCHAR(3), VisitDate, 100), YEAR(VisitDate)
            ORDER BY MIN(VisitDate)
            """, "Label", "Cnt");

        vm.FollowUpStatuses = ExecListFallback(con,
            "SELECT ISNULL(NULLIF(FollowUpStatus, ''), 'Not Set') AS Name, COUNT(*) AS Cnt FROM FollowUp GROUP BY FollowUpStatus",
            "SELECT ISNULL(NULLIF(FollowUpStatus, ''), 'Not Set') AS Name, COUNT(*) AS Cnt FROM ClientVisit GROUP BY FollowUpStatus",
            "Name", "Cnt");

        vm.SupportStatuses = ExecList(con, """
            SELECT ISNULL(NULLIF(Status, ''), 'Not Set') AS Name, COUNT(*) AS Cnt
            FROM DailySupport GROUP BY Status
            """, "Name", "Cnt");

        vm.TopClients = ExecList(con, """
            SELECT TOP 5 c.ClientName AS Name, COUNT(cv.VisitId) AS Cnt
            FROM ClientVisit cv LEFT JOIN Client c ON cv.ClientId = c.ClientId
            GROUP BY c.ClientName ORDER BY Cnt DESC
            """, "Name", "Cnt");

        vm.SupportPriorities = ExecList(con, """
            SELECT ISNULL(NULLIF(Priority, ''), 'Not Set') AS Name, COUNT(*) AS Cnt
            FROM DailySupport GROUP BY Priority
            """, "Name", "Cnt");

        // ---------------- Recent (live) ----------------
        vm.RecentVisits = ExecRows(con, """
            SELECT TOP 6 c.ClientName, CONVERT(VARCHAR(12), cv.VisitDate, 103) AS VisitDate,
                   cv.PersonName, cv.PersonMet
            FROM ClientVisit cv LEFT JOIN Client c ON cv.ClientId = c.ClientId
            ORDER BY cv.VisitId DESC
            """, r => new RecentVisit
            {
                ClientName = r.TryGetValue("ClientName", out var n) ? n?.ToString() : null,
                VisitDate  = r.TryGetValue("VisitDate",  out var d) ? d?.ToString() : null,
                PersonName = r.TryGetValue("PersonName", out var p) ? p?.ToString() : null,
                PersonMet  = r.TryGetValue("PersonMet",  out var m) ? m?.ToString() : null
            });

        vm.RecentTickets = ExecRows(con, """
            SELECT TOP 6 d.TicketNo, c.ClientName, d.Status,
                   CONVERT(VARCHAR(16), d.SupportDate, 103) AS SupportDate
            FROM DailySupport d LEFT JOIN Client c ON d.ClientId = c.ClientId
            ORDER BY d.SupportId DESC
            """, r => new RecentTicket
            {
                TicketNo    = r.TryGetValue("TicketNo",    out var t) ? t?.ToString() : null,
                ClientName  = r.TryGetValue("ClientName",  out var n) ? n?.ToString() : null,
                Status      = r.TryGetValue("Status",      out var s) ? s?.ToString() : null,
                SupportDate = r.TryGetValue("SupportDate", out var d) ? d?.ToString() : null
            });

        return vm;
    }

    // =================================================================================
    //  Helpers - har query safe detection ke saath
    // =================================================================================

    /// <summary>Safe scalar: query fail ho ya negative, hamesha 0..N loutta hai.</summary>
    private static int Exec(Func<int> query)
    {
        try { return Math.Max(0, query()); }
        catch { return 0; }
    }

    /// <summary>Pehle nayi FollowUp table try karo, warna purane ClientVisit se fallback.</summary>
    private static int ExecWithFallback(SqlConnection con, string primary, string fallback)
    {
        try { return Math.Max(0, ScalarInt(con, primary)); }
        catch
        {
            try { return Math.Max(0, ScalarInt(con, fallback)); }
            catch { return 0; }
        }
    }

    private static int ScalarInt(SqlConnection con, string sql)
    {
        using var cmd = new SqlCommand(sql, con);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<NameValue> ExecList(SqlConnection con, string sql, string nameCol, string valCol)
    {
        try
        {
            var list = new List<NameValue>();
            using var cmd = new SqlCommand(sql, con);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader[nameCol]?.ToString() ?? "";
                var val = reader[valCol] is DBNull ? 0 : Convert.ToInt32(reader[valCol]);
                if (!string.IsNullOrWhiteSpace(name)) list.Add(new NameValue { Name = name, Value = val });
            }
            return list;
        }
        catch
        {
            return new List<NameValue>();
        }
    }

    private static List<NameValue> ExecListFallback(SqlConnection con, string primary, string fallback, string nameCol, string valCol)
    {
        try
        {
            var list = ExecList(con, primary, nameCol, valCol);
            if (list.Count > 0) return list;
        }
        catch { }
        return ExecList(con, fallback, nameCol, valCol);
    }

    private static List<T> ExecRows<T>(SqlConnection con, string sql, Func<Dictionary<string, object?>, T> rowMapper)
    {
        try
        {
            var rows = new List<T>();
            using var cmd = new SqlCommand(sql, con);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(rowMapper(row));
            }
            return rows;
        }
        catch
        {
            return new List<T>();
        }
    }
}