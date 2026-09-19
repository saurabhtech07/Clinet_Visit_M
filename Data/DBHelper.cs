using Microsoft.Data.SqlClient;

namespace ClientVisitManagement.Data;

public class DBHelper
{
    private readonly string _connectionString;
    public DBHelper(IConfiguration config) => _connectionString = config.GetConnectionString("DefaultConnection")!;
    public SqlConnection GetConnection() => new SqlConnection(_connectionString);
}
