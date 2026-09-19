-- ================= DDL: Database aur Tables =================
CREATE DATABASE ClientVisitManagementDB;
GO
USE ClientVisitManagementDB;
GO

CREATE TABLE Client (
    ClientId          INT IDENTITY(125,1) PRIMARY KEY,
    ClientName        VARCHAR(150) NOT NULL,
    Address1          VARCHAR(200) NOT NULL,
    Address2          VARCHAR(200) NULL,
    City              VARCHAR(100) NOT NULL,
    State             VARCHAR(100) NOT NULL,
    Pincode           VARCHAR(10)  NOT NULL,
    LandlineNo        VARCHAR(20)  NULL,
    MobileNo          VARCHAR(15)  NULL,
    Email             VARCHAR(100) NULL,
    CompanyType       VARCHAR(50)  NULL,
    NatureOfBusiness  VARCHAR(50)  NULL,
    Category          VARCHAR(50)  NULL,
    CustomerProfile   VARCHAR(500) NULL,
    CreatedOn         DATETIME DEFAULT GETDATE()
);
GO

CREATE TABLE ClientVisit (
    VisitId                 INT IDENTITY(1,1) PRIMARY KEY,
    ClientId                INT NOT NULL FOREIGN KEY REFERENCES Client(ClientId) ON DELETE CASCADE,
    VisitDate               DATE NOT NULL,
    VisitTime               VARCHAR(10) NULL,
    PersonMet               VARCHAR(50) NULL,      -- Owner / Manager / CEO / Purchase Manager etc
    PersonName              VARCHAR(100) NULL,
    Designation             VARCHAR(100) NULL,
    ContactNo               VARCHAR(15) NULL,
    Email                   VARCHAR(100) NULL,
    DiscussionRequirement   VARCHAR(1000) NULL,
    Remarks                 VARCHAR(1000) NULL
);
GO
