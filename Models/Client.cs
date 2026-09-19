namespace ClientVisitManagement.Models
{
    public class Client
    {
        public int ClientId { get; set; }
        public string? ClientName { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public int? StateId { get; set; }
        public string? StateName { get; set; }
        public string? Pincode { get; set; }
        public string? LandlineNo { get; set; }
        public string? MobileNo { get; set; }
        public string? Email { get; set; }
        
        // NAYA - Sirf 3 types
        public int? CompanyTypeId { get; set; }
        public string? CompanyTypeName { get; set; }
    }
}