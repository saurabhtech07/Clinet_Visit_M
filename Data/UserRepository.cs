using Microsoft.Data.SqlClient;
using ClientVisitManagement.Models;

namespace ClientVisitManagement.Data;

public class UserRepository
{
    private readonly DBHelper _db;

    public UserRepository(DBHelper db)
    {
        _db = db;
    }

    // =========================================================
    // GET ALL USERS
    // =========================================================

    public List<User> GetAll()
    {
        var list = new List<User>();

        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            SELECT
                u.UserId,
                u.FullName,
                u.Username,
                u.PasswordHash,
                u.Email,
                u.Mobile,
                u.RoleId,
                u.IsActive,
                u.CreatedDate,
                r.RoleName
            FROM Users u
            INNER JOIN Roles r
                ON u.RoleId = r.RoleId
            ORDER BY u.UserId DESC", con);

        con.Open();

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }


    // =========================================================
    // GET USER BY USERNAME
    // LOGIN KE LIYE
    // =========================================================

    public User? GetByUsername(string username)
    {
        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            SELECT
                u.UserId,
                u.FullName,
                u.Username,
                u.PasswordHash,
                u.Email,
                u.Mobile,
                u.RoleId,
                u.IsActive,
                u.CreatedDate,
                r.RoleName
            FROM Users u
            INNER JOIN Roles r
                ON u.RoleId = r.RoleId
            WHERE u.Username = @Username
              AND u.IsActive = 1", con);

        cmd.Parameters.AddWithValue("@Username", username);

        con.Open();

        using var reader = cmd.ExecuteReader();

        return reader.Read() ? Map(reader) : null;
    }


    // =========================================================
    // GET USER BY ID
    // =========================================================

    public User? GetById(int id)
    {
        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            SELECT
                u.UserId,
                u.FullName,
                u.Username,
                u.PasswordHash,
                u.Email,
                u.Mobile,
                u.RoleId,
                u.IsActive,
                u.CreatedDate,
                r.RoleName
            FROM Users u
            INNER JOIN Roles r
                ON u.RoleId = r.RoleId
            WHERE u.UserId = @UserId", con);

        cmd.Parameters.AddWithValue("@UserId", id);

        con.Open();

        using var reader = cmd.ExecuteReader();

        return reader.Read() ? Map(reader) : null;
    }


    // =========================================================
    // GET ACTIVE ROLES
    // =========================================================

    public List<Role> GetRoles()
    {
        var list = new List<Role>();

        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            SELECT
                RoleId,
                RoleName,
                IsActive,
                CreatedDate
            FROM Roles
            WHERE IsActive = 1
            ORDER BY RoleName", con);

        con.Open();

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(new Role
            {
                RoleId = (int)reader["RoleId"],
                RoleName = reader["RoleName"].ToString()!,
                IsActive = (bool)reader["IsActive"],
                CreatedDate = (DateTime)reader["CreatedDate"]
            });
        }

        return list;
    }




    // =========================================================
// DELETE USER
// =========================================================

public void Delete(int userId)
{
    using var con = _db.GetConnection();

    using var cmd = new SqlCommand(@"
        DELETE FROM Users
        WHERE UserId = @UserId", con);

    cmd.Parameters.AddWithValue("@UserId", userId);

    con.Open();

    cmd.ExecuteNonQuery();
}


    // =========================================================
    // ADD USER
    // =========================================================

    public int Add(User user)
    {
        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            INSERT INTO Users
            (
                FullName,
                Username,
                PasswordHash,
                Email,
                Mobile,
                RoleId,
                IsActive
            )
            OUTPUT INSERTED.UserId
            VALUES
            (
                @FullName,
                @Username,
                @PasswordHash,
                @Email,
                @Mobile,
                @RoleId,
                @IsActive
            )", con);

        cmd.Parameters.AddWithValue("@FullName", user.FullName);
        cmd.Parameters.AddWithValue("@Username", user.Username);
        cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);

        cmd.Parameters.AddWithValue(
            "@Email",
            (object?)user.Email ?? DBNull.Value
        );

        cmd.Parameters.AddWithValue(
            "@Mobile",
            (object?)user.Mobile ?? DBNull.Value
        );

        cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive);

        con.Open();

        return Convert.ToInt32(cmd.ExecuteScalar());
    }


    // =========================================================
    // UPDATE USER
    // =========================================================

    public void Update(User user)
    {
        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            UPDATE Users
            SET
                FullName = @FullName,
                Username = @Username,
                Email = @Email,
                Mobile = @Mobile,
                RoleId = @RoleId,
                IsActive = @IsActive
            WHERE UserId = @UserId", con);

        cmd.Parameters.AddWithValue("@FullName", user.FullName);
        cmd.Parameters.AddWithValue("@Username", user.Username);

        cmd.Parameters.AddWithValue(
            "@Email",
            (object?)user.Email ?? DBNull.Value
        );

        cmd.Parameters.AddWithValue(
            "@Mobile",
            (object?)user.Mobile ?? DBNull.Value
        );

        cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive);
        cmd.Parameters.AddWithValue("@UserId", user.UserId);

        con.Open();

        cmd.ExecuteNonQuery();
    }


    // =========================================================
    // UPDATE PASSWORD
    // =========================================================

    public void UpdatePassword(int userId, string passwordHash)
    {
        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            UPDATE Users
            SET PasswordHash = @PasswordHash
            WHERE UserId = @UserId", con);

        cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
        cmd.Parameters.AddWithValue("@UserId", userId);

        con.Open();

        cmd.ExecuteNonQuery();
    }


    // =========================================================
    // UPDATE USER STATUS
    // =========================================================

    public void UpdateStatus(int userId, bool isActive)
    {
        using var con = _db.GetConnection();

        using var cmd = new SqlCommand(@"
            UPDATE Users
            SET IsActive = @IsActive
            WHERE UserId = @UserId", con);

        cmd.Parameters.AddWithValue("@IsActive", isActive);
        cmd.Parameters.AddWithValue("@UserId", userId);

        con.Open();

        cmd.ExecuteNonQuery();
    }


    // =========================================================
    // CHECK USERNAME EXISTS
    // =========================================================

    public bool UsernameExists(string username, int? excludeUserId = null)
    {
        using var con = _db.GetConnection();

        var sql = @"
            SELECT COUNT(*)
            FROM Users
            WHERE Username = @Username";

        if (excludeUserId.HasValue)
        {
            sql += " AND UserId <> @UserId";
        }

        using var cmd = new SqlCommand(sql, con);

        cmd.Parameters.AddWithValue("@Username", username);

        if (excludeUserId.HasValue)
        {
            cmd.Parameters.AddWithValue(
                "@UserId",
                excludeUserId.Value
            );
        }

        con.Open();

        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }


    // =========================================================
    // MAP DATABASE ROW TO USER MODEL
    // =========================================================

    private static User Map(SqlDataReader reader)
    {
        return new User
        {
            UserId = (int)reader["UserId"],

            FullName = reader["FullName"].ToString()!,

            Username = reader["Username"].ToString()!,

            PasswordHash = reader["PasswordHash"].ToString()!,

            Email = reader["Email"] == DBNull.Value 
                ? null
                : reader["Email"].ToString(),

            Mobile = reader["Mobile"] == DBNull.Value
                ? null
                : reader["Mobile"].ToString(),

            RoleId = (int)reader["RoleId"],

            RoleName = reader["RoleName"] == DBNull.Value
                ? null
                : reader["RoleName"].ToString(),

            IsActive = (bool)reader["IsActive"],

            CreatedDate = (DateTime)reader["CreatedDate"]
        };
    }
}