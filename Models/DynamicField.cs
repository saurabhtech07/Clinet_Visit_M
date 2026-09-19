namespace ClientVisitManagement.Models;

public class DynamicField
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsNullable { get; set; }
}