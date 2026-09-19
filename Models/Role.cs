namespace ClientVisitManagement.Models;

public class Role
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
}