-- ================= DML: Sample Data =================
USE ClientVisitManagementDB;
GO

SET IDENTITY_INSERT Client ON;
INSERT INTO Client (ClientId, ClientName, Address1, Address2, City, State, Pincode, LandlineNo, MobileNo, Email, CompanyType, NatureOfBusiness, Category, CustomerProfile) VALUES
(125, 'ABC Industries Pvt. Ltd.', '2390 Ground Floor', '', 'Delhi', 'Delhi', '110001', '011-2345678', '9876543210', 'abc@company.com', 'Proprietorship', 'Manufacturing', 'Interlining', '-'),
(126, 'XYZ Textiles', 'Sector 12', '', 'Noida', 'Uttar Pradesh', '201301', '', '9876543211', 'xyz@company.com', 'Pvt Ltd', 'Textile', 'Regular', '-'),
(127, 'PQR Solutions', 'MG Road', '', 'Noida', 'Uttar Pradesh', '201307', '', '9876543212', 'pqr@company.com', 'Pvt Ltd', 'IT Services', 'Regular', '-'),
(128, 'LMN Enterprises', 'Vijay Nagar', '', 'Ghaziabad', 'Uttar Pradesh', '201001', '', '9876543213', 'lmn@company.com', 'Partnership', 'Trading', 'Regular', '-'),
(129, 'RST Manufacturing', 'Industrial Area', '', 'Faridabad', 'Haryana', '121004', '', '9876543214', 'rst@company.com', 'Pvt Ltd', 'Manufacturing', 'Interlining', '-');
SET IDENTITY_INSERT Client OFF;
GO

INSERT INTO ClientVisit (ClientId, VisitDate, VisitTime, PersonMet, PersonName, Designation, ContactNo, Email, DiscussionRequirement, Remarks) VALUES
(125, '2026-09-04', '11:30 AM', 'Manager', 'Rahul Sharma', 'Purchase Manager', '9876543210', 'rahul@abc.com', 'Initial discussion done regarding product requirement and future collaboration.', 'Client showed positive interest.'),
(126, '2026-09-04', '10:00 AM', 'CEO', 'Amit Verma', 'CEO', '9876543211', 'amit@xyz.com', 'Discussed bulk order requirement.', 'Follow up next week.'),
(127, '2026-09-02', '02:00 PM', 'Owner', 'Sandeep Singh', 'Owner', '9876543212', 'sandeep@pqr.com', 'New requirement for software licenses.', 'Send quotation.'),
(128, '2026-09-01', '01:00 PM', 'Manager', 'Neha Gupta', 'Manager', '9876543213', 'neha@lmn.com', 'General relationship visit.', '-'),
(129, '2026-08-31', '12:00 PM', 'Purchase Manager', 'Vikram Patel', 'Purchase Manager', '9876543214', 'vikram@rst.com', 'Discussed raw material supply.', 'Pending confirmation.');
GO
