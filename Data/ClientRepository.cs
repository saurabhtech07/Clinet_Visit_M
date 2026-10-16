

using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

public class ClientRepository
{
    private readonly DBHelper _db;

    public ClientRepository(DBHelper db) => _db = db;

    public List<Client> GetAll()
    {
        var list = new List<Client>();
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(@"
            SELECT c.*, s.StateName, ct.Name as CompanyTypeName,
                   ISNULL(NULLIF(u.FullName,''), u.Username) as AssignedToName
            FROM Client c
            LEFT JOIN StateMaster s ON c.StateId = s.StateId
            LEFT JOIN CompanyType ct ON c.CompanyTypeId = ct.Id
            LEFT JOIN Users u ON c.AssignedTo = u.UserId
            ORDER BY c.ClientId DESC", con);
        con.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(Map(reader));
        return list;
    }

    public int GetTotalClients()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM Client", con);
        con.Open();
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Company Master me ClientName already exists hai ya nahi (case-insensitive).
    /// excludeId diya jaye to usi client ko chhod kar check karta hai (Edit ke liye).
    /// </summary>
    public bool ClientNameExists(string? name, int? excludeId = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM Client WHERE UPPER(LTRIM(RTRIM(ClientName))) = UPPER(LTRIM(RTRIM(@Name)))" +
            (excludeId.HasValue ? " AND ClientId <> @Id" : ""), con);
        cmd.Parameters.AddWithValue("@Name", name.Trim());
        if (excludeId.HasValue) cmd.Parameters.AddWithValue("@Id", excludeId.Value);
        con.Open();
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public int GetTotalVisits()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM ClientVisit", con);
        con.Open();
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int GetThisMonthVisits()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(@"
            SELECT COUNT(*) FROM ClientVisit
            WHERE VisitDate >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
              AND VisitDate < DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))", con);
        con.Open();
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int GetActiveClients()
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("SELECT COUNT(DISTINCT ClientId) FROM ClientVisit", con);
        con.Open();
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public Client? GetById(int id)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(@"
            SELECT c.*, s.StateName, ct.Name as CompanyTypeName,
                   ISNULL(NULLIF(u.FullName,''), u.Username) as AssignedToName
            FROM Client c
            LEFT JOIN StateMaster s ON c.StateId = s.StateId
            LEFT JOIN CompanyType ct ON c.CompanyTypeId = ct.Id
            LEFT JOIN Users u ON c.AssignedTo = u.UserId
            WHERE c.ClientId = @Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        con.Open();
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public int Add(Client c)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(@"
            INSERT INTO Client
            (ClientName, Address1, Address2, City, StateId, Pincode, LandlineNo, MobileNo, Email, ContactPerson, CompanyTypeId, AssignedTo)
            OUTPUT INSERTED.ClientId
            VALUES
            (@Name, @Add1, @Add2, @City, @StateId, @Pincode, @Landline, @Mobile, @Email, @ContactPerson, @CompanyTypeId, @AssignedTo)", con);
        AddParams(cmd, c);
        con.Open();
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Update(Client c)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand(@"
            UPDATE Client SET
                ClientName=@Name,
                Address1=@Add1,
                Address2=@Add2,
                City=@City,
                StateId=@StateId,
                Pincode=@Pincode,
                LandlineNo=@Landline,
                MobileNo=@Mobile,
                Email=@Email,
                ContactPerson=@ContactPerson,
                CompanyTypeId=@CompanyTypeId,
                AssignedTo=@AssignedTo
            WHERE ClientId=@Id", con);
        AddParams(cmd, c);
        cmd.Parameters.AddWithValue("@Id", c.ClientId);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    // Client delete karte waqt usse juda saara data bhi hata do,
    // taaki wo har jagah (visits, follow-ups, support) se gayab ho jaye.
    public void Delete(int id)
    {
        using var con = _db.GetConnection();
        con.Open();
        using var tx = con.BeginTransaction();
        try
        {
            // ORDER MATTERS: child tables pehle, phir Client (FK safe)
            var steps = new[]
            {
                // Us client ke visits ke follow-ups
                "DELETE FROM FollowUp WHERE VisitId IN (SELECT VisitId FROM ClientVisit WHERE ClientId=@Id)",
                // Client ke apne follow-ups
                "DELETE FROM FollowUp WHERE ClientId=@Id",
                // Client ke visits
                "DELETE FROM ClientVisit WHERE ClientId=@Id",
                // Client ke support tickets
                "DELETE FROM DailySupport WHERE ClientId=@Id"
            };

            foreach (var sql in steps)
            {
                using var cmd = new SqlCommand(sql, con, tx);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.ExecuteNonQuery();
            }

            using (var delClient = new SqlCommand("DELETE FROM Client WHERE ClientId=@Id", con, tx))
            {
                delClient.Parameters.AddWithValue("@Id", id);
                delClient.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void AddParams(SqlCommand cmd, Client c)
    {
        cmd.Parameters.AddWithValue("@Name", (object?)c.ClientName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Add1", (object?)c.Address1 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Add2", (object?)c.Address2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@City", (object?)c.City ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StateId", (object?)c.StateId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Pincode", (object?)c.Pincode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Landline", (object?)c.LandlineNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Mobile", (object?)c.MobileNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Email", (object?)c.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ContactPerson", (object?)c.ContactPerson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CompanyTypeId", (object?)c.CompanyTypeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AssignedTo", (object?)c.AssignedTo ?? DBNull.Value);
    }

private static Client Map(SqlDataReader r) => new()
{
    ClientId = Convert.ToInt32(r["ClientId"]),
    ClientName = r["ClientName"]?.ToString(),
    Address1 = r["Address1"]?.ToString(),
    Address2 = r["Address2"]?.ToString(),
    City = r["City"]?.ToString(),
    StateId = r["StateId"] != DBNull.Value ? Convert.ToInt32(r["StateId"]) : (int?)null,
    StateName = r["StateName"]?.ToString(),
    Pincode = r["Pincode"]?.ToString(),
    LandlineNo = r["LandlineNo"]?.ToString(),
    MobileNo = r["MobileNo"]?.ToString(),
    Email = r["Email"]?.ToString(),
    ContactPerson = r["ContactPerson"] == DBNull.Value ? null : r["ContactPerson"]?.ToString(),
    CompanyTypeId = r["CompanyTypeId"] != DBNull.Value ? Convert.ToInt32(r["CompanyTypeId"]) : (int?)null,
    CompanyTypeName = r["CompanyTypeName"] != DBNull.Value ? r["CompanyTypeName"]?.ToString() : "",
    AssignedTo = r["AssignedTo"] != DBNull.Value ? Convert.ToInt32(r["AssignedTo"]) : (int?)null,
    AssignedToName = r["AssignedToName"] == DBNull.Value ? null : r["AssignedToName"]?.ToString()
};


public List<CompanyType> GetCompanyTypes()
{
    var list = new List<CompanyType>();
    using var con = _db.GetConnection();
    using var cmd = new SqlCommand("SELECT Id, Name FROM CompanyType ORDER BY Name", con);
    con.Open();
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        list.Add(new CompanyType
        {
            Id = Convert.ToInt32(reader["Id"]),
            Name = reader["Name"].ToString()
        });
    }
    return list;
}
}