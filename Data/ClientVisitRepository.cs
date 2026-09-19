


using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

public class ClientVisitRepository
{
    private readonly DBHelper _db;
    public ClientVisitRepository(DBHelper db) => _db = db;

// Line 7-12 pe ye 2 jagah change kar de:

private const string SelectJoin =
    "SELECT v.*, c.ClientName, c.City, " +
    "s.StateName AS State, c.Pincode, " +
    "u.Username AS VisitedBy " +
    "FROM ClientVisit v " +
    "LEFT JOIN Client c ON v.ClientId = c.ClientId " +  // <-- CHANGE 1: INNER -> LEFT
    "LEFT JOIN StateMaster s ON c.StateId = s.StateId " +
    "LEFT JOIN Users u ON v.UserId = u.UserId";

    public (List<ClientVisit> Items, int TotalCount) Search(
        string? term, DateTime? fromDate, DateTime? toDate,
        int page, int pageSize, string? sort = null, string direction = "DESC")
    {
        var where = " WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(term))
            where += " AND (c.ClientName LIKE @Term OR c.MobileNo LIKE @Term OR CAST(v.ClientId AS VARCHAR) LIKE @Term OR u.Username LIKE @Term OR s.StateName LIKE @Term)";
        if (fromDate.HasValue) where += " AND v.VisitDate >= @From";
        if (toDate.HasValue) where += " AND v.VisitDate <= @To";

        var sortColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VisitId"] = "v.VisitId", ["ClientId"] = "v.ClientId", ["ClientName"] = "c.ClientName",
            ["City"] = "c.City", ["State"] = "s.StateName", ["VisitedBy"] = "u.Username",
            ["PersonName"] = "v.PersonName", ["VisitDate"] = "v.VisitDate",
            ["NextFollowUpDate"] = "v.NextFollowUpDate", ["FollowUpStatus"] = "v.FollowUpStatus"
        };

        string orderColumn = !string.IsNullOrWhiteSpace(sort) && sortColumns.TryGetValue(sort, out var column)? column : "v.VisitId";
        string orderDirection = string.Equals(direction, "ASC", StringComparison.OrdinalIgnoreCase)? "ASC" : "DESC";

        using var con = _db.GetConnection();
        con.Open();
using var countCmd = new SqlCommand("SELECT COUNT(*) FROM ClientVisit v LEFT JOIN Client c ON v.ClientId = c.ClientId LEFT JOIN StateMaster s ON c.StateId = s.StateId LEFT JOIN Users u ON v.UserId = u.UserId " + where, con); // <-- CHANGE 2: INNER -> LEFT        AddFilterParams(countCmd, term, fromDate, toDate);
        int total = Convert.ToInt32(countCmd.ExecuteScalar());

        using var cmd = new SqlCommand(SelectJoin + where + $" ORDER BY {orderColumn} {orderDirection} OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", con);
        AddFilterParams(cmd, term, fromDate, toDate);
        cmd.Parameters.AddWithValue("@Skip", Math.Max(0, page - 1) * pageSize);
        cmd.Parameters.AddWithValue("@Take", pageSize);

        var list = new List<ClientVisit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return (list, total);
    }

    public int GetTotalClientsVisited()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("SELECT COUNT(DISTINCT ClientId) FROM ClientVisit", con);
        con.Open(); return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int GetVisitsThisMonth()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM ClientVisit WHERE YEAR(VisitDate) = YEAR(GETDATE()) AND MONTH(VisitDate) = MONTH(GETDATE())", con);
        con.Open(); return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int GetTodaysVisits()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM ClientVisit WHERE CAST(VisitDate AS DATE) = CAST(GETDATE() AS DATE)", con);
        con.Open(); return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Ab ye dynamic hai - FollowUpStatus se count karega
    public int GetPendingFollowUps()
    {
        try
        {
            using var con = _db.GetConnection();
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM ClientVisit WHERE FollowUpStatus = 'Pending'", con);
            con.Open(); return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    public List<ClientVisit> SearchAll(string? term, DateTime? fromDate, DateTime? toDate)
    {
        var where = " WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(term))
            where += " AND (c.ClientName LIKE @Term OR c.MobileNo LIKE @Term OR CAST(v.ClientId AS VARCHAR) LIKE @Term OR u.Username LIKE @Term OR s.StateName LIKE @Term)";
        if (fromDate.HasValue) where += " AND v.VisitDate >= @From";
        if (toDate.HasValue) where += " AND v.VisitDate <= @To";

        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(SelectJoin + where + " ORDER BY v.VisitId DESC", con);
        AddFilterParams(cmd, term, fromDate, toDate);
        con.Open();
        var list = new List<ClientVisit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public List<ClientVisit> GetTransactions()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(SelectJoin + " ORDER BY v.VisitId DESC", con);
        con.Open();
        var list = new List<ClientVisit>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public ClientVisit? GetById(int id)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(SelectJoin + " WHERE v.VisitId = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        con.Open();
        using var reader = cmd.ExecuteReader();
        return reader.Read()? Map(reader) : null;
    }

    // ADD - Save Fix + Dynamic fields support
    public void Add(ClientVisit v)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(
            """
            INSERT INTO ClientVisit
            (ClientId, UserId, VisitDate, VisitTime, PersonMet, PersonName, Designation, ContactNo, Email, DiscussionRequirement, NextFollowUpDate, FollowUpStatus, Remarks, CreatedOn)
            VALUES
            (@ClientId, @UserId, @Date, @Time, @Met, @Name, @Designation, @Contact, @Email, @Discussion, @NextFollowUp, @FollowStatus, @Remarks, GETDATE())
            """, con);
        AddParams(cmd, v);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void Update(ClientVisit v)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(
            """
            UPDATE ClientVisit SET
                ClientId=@ClientId, UserId=@UserId, VisitDate=@Date, VisitTime=@Time, PersonMet=@Met,
                PersonName=@Name, Designation=@Designation, ContactNo=@Contact, Email=@Email,
                DiscussionRequirement=@Discussion, NextFollowUpDate=@NextFollowUp, FollowUpStatus=@FollowStatus, Remarks=@Remarks
            WHERE VisitId=@Id
            """, con);
        AddParams(cmd, v);
        cmd.Parameters.AddWithValue("@Id", v.VisitId);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("DELETE FROM ClientVisit WHERE VisitId=@Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    private static void AddFilterParams(SqlCommand cmd, string? term, DateTime? fromDate, DateTime? toDate)
    {
        if (!string.IsNullOrWhiteSpace(term)) cmd.Parameters.AddWithValue("@Term", "%" + term + "%");
        if (fromDate.HasValue) cmd.Parameters.AddWithValue("@From", fromDate.Value);
        if (toDate.HasValue) cmd.Parameters.AddWithValue("@To", toDate.Value);
    }

    // INSERT PARAMETERS - Ab NextFollowUp + FollowStatus add hai - Save fix yahi se hua hai
    private static void AddParams(SqlCommand cmd, ClientVisit v)
    {
        cmd.Parameters.AddWithValue("@ClientId", v.ClientId);
        cmd.Parameters.AddWithValue("@UserId", v.UserId);
        cmd.Parameters.AddWithValue("@Date", v.VisitDate);
        cmd.Parameters.AddWithValue("@Time", (object?)v.VisitTime?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Met", (object?)v.PersonMet?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Name", (object?)v.PersonName?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Designation", (object?)v.Designation?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Contact", (object?)v.ContactNo?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Email", (object?)v.Email?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Discussion", (object?)v.DiscussionRequirement?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NextFollowUp", (object?)v.NextFollowUpDate?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FollowStatus", (object?)v.FollowUpStatus?? "Pending");
        cmd.Parameters.AddWithValue("@Remarks", (object?)v.Remarks?? DBNull.Value);
    }

    // =========================================================
    // MAP - Fixed version (Table error fix)
    // =========================================================

    private static ClientVisit Map(SqlDataReader r) => new()
    {
        VisitId = Convert.ToInt32(r["VisitId"]),
        ClientId = Convert.ToInt32(r["ClientId"]),
        ClientCode = "CL-" + Convert.ToInt32(r["ClientId"]).ToString("D6"),
        ClientName = r["ClientName"]?.ToString(),
        City = r["City"]?.ToString(),
        State = r["State"]?.ToString(),
        Pincode = r["Pincode"]?.ToString(),
        UserId = r["UserId"] == DBNull.Value ? 0 : Convert.ToInt32(r["UserId"]),
        VisitedBy = r["VisitedBy"]?.ToString(),
        VisitDate = Convert.ToDateTime(r["VisitDate"]),
        VisitTime = r["VisitTime"]?.ToString(),
        PersonMet = r["PersonMet"]?.ToString(),
        PersonName = r["PersonName"]?.ToString(),
        Designation = r["Designation"]?.ToString(),
        ContactNo = r["ContactNo"]?.ToString(),
        Email = r["Email"]?.ToString(),
        DiscussionRequirement = r["DiscussionRequirement"]?.ToString(),
        Remarks = r["Remarks"]?.ToString(),

        // Naye fields - Safe check ke saath
        NextFollowUpDate = HasColumn(r, "NextFollowUpDate") && r["NextFollowUpDate"] != DBNull.Value 
            ? Convert.ToDateTime(r["NextFollowUpDate"]) 
            : null,

        FollowUpStatus = HasColumn(r, "FollowUpStatus") 
            ? r["FollowUpStatus"]?.ToString() 
            : "Pending",

        CreatedOn = HasColumn(r, "CreatedOn") && r["CreatedOn"] != DBNull.Value 
            ? Convert.ToDateTime(r["CreatedOn"]) 
            : DateTime.Now
    };

    // Helper - Column exist karta hai ya nahi check karne ke liye
    private static bool HasColumn(SqlDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}