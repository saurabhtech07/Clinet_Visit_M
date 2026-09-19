# Client Visit Management

ASP.NET Core MVC project (.NET 8) — screenshot ke design ke hisaab se banaya gaya.
ADO.NET (raw SQL, DDL/DML) with Microsoft.Data.SqlClient. Excel export ClosedXML se hota hai.

## Screens
1. **Create Client** — `/Client/Create` — new client add karta hai, phir "Save & Continue" seedhe uski Visit Details form pe le jaata hai.
2. **Client Visit Details** — `/ClientVisit/Create?clientId=125` — visit record add karna.
3. **Client Visit List** — `/ClientVisit` — search (Client Name/Mobile/Client ID), From/To date filter, Export to Excel, pagination, view/edit/delete actions.
4. **View Client Visit Details (Modal)** — list me eye icon click karne par AJAX se popup khulta hai.

## Setup
1. `Database/DDL.sql` SSMS me run karo — database + Client & ClientVisit tables banega.
2. `Database/DML.sql` run karo — sample data (screenshot jaisa hi) insert hoga.
3. `appsettings.json` me connection string check karo.
4. Terminal me:
   ```
   dotnet restore
   dotnet run
   ```
5. Browser me `/ClientVisit` kholo — list dikhega. Naya client add karne ke liye sidebar se "Create Client" pe jao.

## Notes
- Client ID auto-generate hota hai format `CL-000125` (database ke ClientId column se).
- Excel export `.xlsx` file deta hai (ClosedXML library, NuGet se automatic restore ho jayega).
