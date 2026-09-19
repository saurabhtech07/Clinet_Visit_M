namespace ClientVisitManagement.Models;

public class StateMaster
{
    public int StateId { get; set; }

    public string StateName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedOn { get; set; }
}