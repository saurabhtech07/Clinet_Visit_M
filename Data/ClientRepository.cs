// using Microsoft.Data.SqlClient;
// using ClientVisitManagement.Models;

// namespace ClientVisitManagement.Data;

// public class ClientRepository
// {
//     private readonly DBHelper _db;
//     public ClientRepository(DBHelper db) => _db = db;

//     public List<Client> GetAll()
//     {
//         var list = new List<Client>();
//         using var con = _db.GetConnection();
//         using var cmd = new SqlCommand("SELECT * FROM Client ORDER BY ClientId DESC", con);
//         con.Open();
//         using var reader = cmd.ExecuteReader();
//         while (reader.Read()) list.Add(Map(reader));
//         return list;
//     }


// public int GetTotalClients()
// {
//     using var con = _db.GetConnection();
//     using var cmd = new SqlCommand("SELECT COUNT(*) FROM Client", con);
//     con.Open();
//     return Convert.ToInt32(cmd.ExecuteScalar());
// }

// public int GetTotalVisits()
// {
//     using var con = _db.GetConnection();
//     using var cmd = new SqlCommand("SELECT COUNT(*) FROM ClientVisit", con);
//     con.Open();
//     return Convert.ToInt32(cmd.ExecuteScalar());
// }

// public int GetThisMonthVisits()
// {
//     using var con = _db.GetConnection();

//     using var cmd = new SqlCommand(@"
//         SELECT COUNT(*)
//         FROM ClientVisit
//         WHERE VisitDate >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
//           AND VisitDate < DATEADD(MONTH, 1,
//               DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))", con);

//     con.Open();
//     return Convert.ToInt32(cmd.ExecuteScalar());
// }

// public int GetActiveClients()
// {
//     using var con = _db.GetConnection();

//     using var cmd = new SqlCommand(@"
//         SELECT COUNT(DISTINCT ClientId)
//         FROM ClientVisit", con);

//     con.Open();
//     return Convert.ToInt32(cmd.ExecuteScalar());
// }
//     public Client? GetById(int id)
//     {
//         using var con = _db.GetConnection();
//         using var cmd = new SqlCommand("SELECT * FROM Client WHERE ClientId=@Id", con);
//         cmd.Parameters.AddWithValue("@Id", id);
//         con.Open();
//         using var reader = cmd.ExecuteReader();
//         return reader.Read() ? Map(reader) : null;
//     }

//     public int Add(Client c)
//     {
//         using var con = _db.GetConnection();
//         using var cmd = new SqlCommand(
//             "INSERT INTO Client (ClientName, Address1, Address2, City, State, Pincode, LandlineNo, MobileNo, Email, CompanyType, NatureOfBusiness, Category, CustomerProfile) " +
//             "OUTPUT INSERTED.ClientId " +
//             "VALUES (@Name, @Add1, @Add2, @City, @State, @Pincode, @Landline, @Mobile, @Email, @CompType, @Nature, @Category, @Profile)", con);
//         AddParams(cmd, c);
//         con.Open();
//         return (int)cmd.ExecuteScalar();
//     }

//     public void Update(Client c)
//     {
//         using var con = _db.GetConnection();
//         using var cmd = new SqlCommand(
//             "UPDATE Client SET ClientName=@Name, Address1=@Add1, Address2=@Add2, City=@City, State=@State, Pincode=@Pincode, " +
//             "LandlineNo=@Landline, MobileNo=@Mobile, Email=@Email, CompanyType=@CompType, NatureOfBusiness=@Nature, " +
//             "Category=@Category, CustomerProfile=@Profile WHERE ClientId=@Id", con);
//         AddParams(cmd, c);
//         cmd.Parameters.AddWithValue("@Id", c.ClientId);
//         con.Open();
//         cmd.ExecuteNonQuery();
//     }

//     public void Delete(int id)
//     {
//         using var con = _db.GetConnection();
//         using var cmd = new SqlCommand("DELETE FROM Client WHERE ClientId=@Id", con);
//         cmd.Parameters.AddWithValue("@Id", id);
//         con.Open();
//         cmd.ExecuteNonQuery();
//     }

//     private static void AddParams(SqlCommand cmd, Client c)
//     {
//         cmd.Parameters.AddWithValue("@Name", c.ClientName);
//         cmd.Parameters.AddWithValue("@Add1", c.Address1);
//         cmd.Parameters.AddWithValue("@Add2", (object?)c.Address2 ?? DBNull.Value);
//         cmd.Parameters.AddWithValue("@City", c.City);
//         cmd.Parameters.AddWithValue("@State", c.State);
//         cmd.Parameters.AddWithValue("@Pincode", c.Pincode);
//         cmd.Parameters.AddWithValue("@Landline", (object?)c.LandlineNo ?? DBNull.Value);
//         cmd.Parameters.AddWithValue("@Mobile", (object?)c.MobileNo ?? DBNull.Value);
//         cmd.Parameters.AddWithValue("@Email", (object?)c.Email ?? DBNull.Value);
//         cmd.Parameters.AddWithValue("@CompType", (object?)c.CompanyType ?? DBNull.Value);
//         cmd.Parameters.AddWithValue("@Nature", (object?)c.NatureOfBusiness ?? DBNull.Value);
//         cmd.Parameters.AddWithValue("@Category", (object?)c.Category ?? DBNull.Value);
//         cmd.Parameters.AddWithValue("@Profile", (object?)c.CustomerProfile ?? DBNull.Value);
//     }

//     private static Client Map(SqlDataReader r) => new()
//     {
//         ClientId = (int)r["ClientId"],
//         ClientName = r["ClientName"].ToString()!,
//         Address1 = r["Address1"].ToString()!,
//         Address2 = r["Address2"]?.ToString(),
//         City = r["City"].ToString()!,
//         State = r["State"].ToString()!,
//         Pincode = r["Pincode"].ToString()!,
//         LandlineNo = r["LandlineNo"]?.ToString(),
//         MobileNo = r["MobileNo"]?.ToString(),
//         Email = r["Email"]?.ToString(),
//         CompanyType = r["CompanyType"]?.ToString(),
//         NatureOfBusiness = r["NatureOfBusiness"]?.ToString(),
//         Category = r["Category"]?.ToString(),
//         CustomerProfile = r["CustomerProfile"]?.ToString()
//     };
// }

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
            SELECT c.*, s.StateName, ct.Name as CompanyTypeName
            FROM Client c
            LEFT JOIN StateMaster s ON c.StateId = s.StateId
            LEFT JOIN CompanyType ct ON c.CompanyTypeId = ct.Id
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
            SELECT c.*, s.StateName, ct.Name as CompanyTypeName
            FROM Client c
            LEFT JOIN StateMaster s ON c.StateId = s.StateId
            LEFT JOIN CompanyType ct ON c.CompanyTypeId = ct.Id
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
            (ClientName, Address1, Address2, City, StateId, Pincode, LandlineNo, MobileNo, Email, CompanyTypeId)
            OUTPUT INSERTED.ClientId
            VALUES
            (@Name, @Add1, @Add2, @City, @StateId, @Pincode, @Landline, @Mobile, @Email, @CompanyTypeId)", con);
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
                CompanyTypeId=@CompanyTypeId
            WHERE ClientId=@Id", con);
        AddParams(cmd, c);
        cmd.Parameters.AddWithValue("@Id", c.ClientId);
        con.Open();
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var con = _db.GetConnection();
        using var cmd = new SqlCommand("DELETE FROM Client WHERE ClientId=@Id", con);
        cmd.Parameters.AddWithValue("@Id", id);
        con.Open();
        cmd.ExecuteNonQuery();
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
        cmd.Parameters.AddWithValue("@CompanyTypeId", (object?)c.CompanyTypeId ?? DBNull.Value);
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
    CompanyTypeId = r["CompanyTypeId"] != DBNull.Value ? Convert.ToInt32(r["CompanyTypeId"]) : (int?)null,
    CompanyTypeName = r["CompanyTypeName"] != DBNull.Value ? r["CompanyTypeName"]?.ToString() : ""
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