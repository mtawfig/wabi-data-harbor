"""06-seed-wabiinsightdb.sql: the Wabi Bank organization (WBK) in Wabi Insight over the five Wabi Bank databases.

The catalogue documentation, classifications, field roles and row-scope mappings come from the generator modules, so they
always match the tables the database scripts create. Departments, sections and the eight users' names, units and e-mails are
read from WabiBankPersonnelDB when the script runs.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bank_people as bp  # noqa: E402
import build_corebanking  # noqa: E402
import build_digital  # noqa: E402
import build_finance  # noqa: E402
import build_it  # noqa: E402
import build_personnel  # noqa: E402

APPS = [  # code, database, module, name, Arabic, description, reader login, reader password
    ("PERSONNEL", "WabiBankPersonnelDB", build_personnel, "Personnel and Attendance", "شؤون الموظفين والحضور",
     "Wabi Bank HRMS: divisions, departments, branches and sections, positions and grades, employees, movements, leave, daily attendance for 2025, "
     "and the bank's systems with each employee's user id and access (the source of every identifier the other systems reuse).",
     "wbpersonnel_reader", "WbPersonnel#Reader#2026"),
    ("CORE-BANKING", "WabiBankCoreBankingDB", build_corebanking, "Core Banking", "النظام المصرفي الأساسي",
     "Customers (CIF), KYC, accounts and every posting of 2025, cheque clearing (outward and inward), remittances, teller tills, fixed deposits, "
     "loans with schedules and repayments, and month-end balances. Postings carry the staff UserId and the posting unit.",
     "wbcore_reader", "WbCore#Reader#2026"),
    ("FINANCE", "WabiBankFinanceDB", build_finance, "Finance", "المالية",
     "Chart of accounts, cost centres (departments and sections), FY2025 original and revised budgets, budget transfers, purchase requisitions and orders, "
     "goods receipts, vendor invoices, payments, fixed assets and the general ledger.",
     "wbfinance_reader", "WbFinance#Reader#2026"),
    ("DIGITAL-BANKING", "WabiBankDigitalDB", build_digital, "Digital Banking", "الخدمات المصرفية الرقمية",
     "Internet and mobile banking for Core Banking customers: profiles, devices, sign-ins, beneficiaries, channel transfers (posted in Core Banking), "
     "Contact Centre service requests and profile actions, 2025.",
     "wbdigital_reader", "WbDigital#Reader#2026"),
    ("IT-HELPDESK", "WabiBankITDB", build_it, "IT Help Desk", "مكتب خدمات تقنية المعلومات",
     "IT service management with two modules, Requests (service catalogue, approvals, tasks) and Incidents (priority, SLA, activities), "
     "plus services, configuration items and assignment groups, 2025.",
     "wbit_reader", "WbIt#Reader#2026"),
]

USER_ROLES = {  # employee number -> Wabi Insight roles (every user also gets Employee: own IT tickets)
    "W1001": ["OrganizationAdmin", "CyberSecurity"], "W1002": ["Management"], "W1003": ["BranchManager"], "W1004": ["Teller"],
    "W1005": ["HROfficer"], "W1006": ["BudgetController"], "W1007": ["ComplianceOfficer"], "W1008": ["ServiceDeskManager"],
}

CUSTOM_ROLES = [  # code, name, Arabic, description
    ("Employee", "Employee self-service", "الخدمة الذاتية للموظف", "Own IT Help Desk requests and incidents (SELF). Every Wabi Bank user holds it."),
    ("CyberSecurity", "Cyber Security", "الأمن السيبراني",
     "Digital Banking sign-ins and devices, Personnel system users and access, IT access requests (all rows); receives the sign-in alerts."),
    ("BranchManager", "Branch Manager", "مدير الفرع", "Core Banking, Digital Banking and IT Help Desk rows of the manager's own branch (DEPARTMENT), without Restricted columns."),
    ("Teller", "Teller", "صرّاف", "Own postings only (SELF, through the staff views of Core Banking)."),
    ("HROfficer", "HR Officer", "موظف الموارد البشرية", "Personnel and Attendance, all rows, without Restricted columns."),
    ("BudgetController", "Budget Controller", "مراقب الميزانية", "Finance, all rows; Personnel structure and headcount tables. Restricted columns (vendor IBAN, Emirates ID, passport) withheld."),
    ("ComplianceOfficer", "Compliance and AML Officer", "مسؤول الامتثال ومكافحة غسل الأموال",
     "Core Banking and Digital Banking, all rows including KYC documents; no Personnel data. Receives the AML alerts."),
    ("ServiceDeskManager", "IT Service Desk Manager", "مدير مكتب خدمات تقنية المعلومات",
     "IT Help Desk, all rows; Personnel employee directory without Restricted columns; no banking or finance data."),
]

PERSONNEL_STRUCTURE = ["Divisions", "Departments", "Sections", "Locations", "Positions", "JobGrades"]
CBS_REFERENCE = ["Branches", "Units", "Products", "TransactionCodes", "Currencies", "ChequeReturnReasons", "LoanProducts", "BusinessCalendar"]


def _grants():
    rows = [  # role, app, object (None = every object), scope, exclude Restricted
        ("Employee", "IT-HELPDESK", "ServiceRequests", "Self", 0), ("Employee", "IT-HELPDESK", "Incidents", "Self", 0),
        ("Employee", "IT-HELPDESK", "vw_IncidentsLive", "Self", 0),
        ("CyberSecurity", "DIGITAL-BANKING", "SignIns", "All", 0), ("CyberSecurity", "DIGITAL-BANKING", "vw_SignInsLive", "All", 0),
        ("CyberSecurity", "DIGITAL-BANKING", "Devices", "All", 0), ("CyberSecurity", "DIGITAL-BANKING", "DigitalProfiles", "All", 0),
        ("CyberSecurity", "PERSONNEL", "Systems", "All", 0), ("CyberSecurity", "PERSONNEL", "SystemUsers", "All", 0),
        ("CyberSecurity", "PERSONNEL", "UserSystemAccess", "All", 0), ("CyberSecurity", "PERSONNEL", "Employees", "All", 1),
        ("CyberSecurity", "IT-HELPDESK", "ServiceRequests", "All", 0), ("CyberSecurity", "IT-HELPDESK", "RequestTasks", "All", 0),
        ("CyberSecurity", "IT-HELPDESK", "ServiceCatalogItems", "All", 0),
        ("HROfficer", "PERSONNEL", None, "All", 1),
        ("BudgetController", "FINANCE", None, "All", 1),
        ("BudgetController", "PERSONNEL", "Employees", "All", 1),
        ("ComplianceOfficer", "CORE-BANKING", None, "All", 0), ("ComplianceOfficer", "DIGITAL-BANKING", None, "All", 0),
        ("ServiceDeskManager", "IT-HELPDESK", None, "All", 0), ("ServiceDeskManager", "PERSONNEL", "Employees", "All", 1),
        ("Teller", "CORE-BANKING", "vw_TransactionsByStaff", "Self", 0), ("Teller", "CORE-BANKING", "vw_OutwardChequesByStaff", "Self", 0),
        ("Teller", "CORE-BANKING", "TransactionCodes", "All", 0), ("Teller", "CORE-BANKING", "Products", "All", 0),
    ]
    rows += [(role, "PERSONNEL", o, "All", 0) for role in ("BudgetController", "ServiceDeskManager") for o in PERSONNEL_STRUCTURE]
    rows += [("BranchManager", "CORE-BANKING", o, "All", 1) for o in CBS_REFERENCE]
    rows += [("BranchManager", "DIGITAL-BANKING", "Branches", "All", 1)]
    # Branch-scoped objects: every object of the three systems whose row-scope mapping has a department column.
    for app, module in (("CORE-BANKING", build_corebanking), ("DIGITAL-BANKING", build_digital), ("IT-HELPDESK", build_it)):
        rows += [("BranchManager", app, o, "Department", 1) for o, _, dept, _ in module.ROW_SCOPES if dept]
    scopes = {(a[0], s[0]): s for a in APPS for s in a[2].ROW_SCOPES}
    for role, app, obj, scope, _ in rows:  # DEPARTMENT / SELF need the mapped column, or the object would return nothing
        if scope == "Department":
            assert scopes[(app, obj)][2], (app, obj)
        if scope == "Self":
            assert scopes[(app, obj)][1], (app, obj)
    return rows


def _q(v):
    return "NULL" if v is None else "N'" + str(v).replace("'", "''") + "'"


def _values(rows):
    return ",\n".join("(" + ", ".join(_q(v) if not isinstance(v, int) else str(v) for v in r) + ")" for r in rows)


HEADER = """-- ==============================================================================================================
-- WabiInsightDB — Wabi Bank (WBK) sample organization over the five Wabi Bank databases
-- Generated by scripts/sql/samples/wabi-bank/build_seed.py — edit the generator, not this file.
--
-- Run AFTER:
--   * the Wabi Insight migrations (dotnet WabiInsight.Api.dll migrate) against WabiInsightDB, and
--   * 01- to 05-create-*.sql of this folder on the SAME SQL Server instance (this script reads their structure and catalogs).
-- Run as a login that can read all six databases (sysadmin in a sandbox). Safe to re-run: existing rows are kept, documentation refreshed.
--
-- What it creates in WabiInsightDB:
--   * organization "Wabi Bank" (code WBK, Asia/Dubai) with the standard roles, eight custom roles and Platform Insights;
--   * the 15 Head Office departments and 6 branches with all their sections (codes and EN/AR names from WabiBankPersonnelDB);
--   * eight users, each a Personnel employee with the same EmployeeNumber, department and section:
--       W1001 Head of IT Security       OrganizationAdmin + Cyber Security     W1005 HR officer          HR Officer
--       W1002 Chief Executive Officer   Management (every object, all rows)    W1006 Budget controller   Budget Controller
--       W1003 Branch Manager, Deira     Branch Manager (own branch)            W1007 AML officer         Compliance and AML Officer
--       W1004 Teller, Deira             Teller (own postings)                  W1008 Service desk head   IT Service Desk Manager
--     Everyone also holds "Employee" (own IT tickets). Initial password for all eight: Wabi#Sample#2026, changed at first sign-in.
--   * five applications with every table and view catalogued, approved, described (EN/AR) and classified, relationships,
--     field roles and row-scope mappings, and the role permissions above;
--   * five Monitoring Rules on the planted findings (four data rules on the live views, one access rule on KYC look-ups).
--
-- Connection strings stay in the application, by design (Data Protection key ring): after this script, sign in as W1001 and,
-- for each application under Organization Administration > Applications, "Replace connection" (read-only logins printed
-- at the end), "Test connection", then "Approve".
-- ==============================================================================================================
USE [WabiInsightDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @OrgCode nvarchar(32) = N'WBK', @OrgName nvarchar(200) = N'Wabi Bank', @OrgNameAr nvarchar(200) = N'بنك وابي',
        @PersonnelDb sysname = N'WabiBankPersonnelDB',
        -- ASP.NET Core Identity v3 hash (PBKDF2-HMAC-SHA512, 100,000 iterations) of the initial password Wabi#Sample#2026.
        @InitialPasswordHash nvarchar(512) = N'AQAAAAIAAYagAAAAEETy/99eckKEanTcWITTTOPP6JGVZqBqe69qf5tkJ4xaB63ZZbNF3r2unaowrHc7tA==';
DECLARE @Now datetimeoffset(7) = SYSDATETIMEOFFSET(), @OrgId uniqueidentifier, @AdminUserId uniqueidentifier, @Sql nvarchar(max);

IF OBJECT_ID(N'dbo.MonitoringRules', N'U') IS NULL
    THROW 50001, N'WabiInsightDB is not migrated to the current version: run "dotnet WabiInsight.Api.dll migrate" first.', 1;
IF DB_ID(N'WabiBankPersonnelDB') IS NULL OR DB_ID(N'WabiBankCoreBankingDB') IS NULL OR DB_ID(N'WabiBankFinanceDB') IS NULL
   OR DB_ID(N'WabiBankDigitalDB') IS NULL OR DB_ID(N'WabiBankITDB') IS NULL
    THROW 50002, N'Run 01- to 05-create-*.sql of scripts/sql/samples/wabi-bank on this server first.', 1;

-- ---------------------------------------------------------------------------------------------- staging tables
-- E-mail addresses come from Personnel (WorkEmail). To use real mailboxes, set Email here before running.
CREATE TABLE #Users (EmployeeNumber nvarchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, PreferredLanguage nvarchar(2) COLLATE DATABASE_DEFAULT,
                     Email nvarchar(256) COLLATE DATABASE_DEFAULT NULL, FullName nvarchar(200) COLLATE DATABASE_DEFAULT NULL, DepartmentCode nvarchar(32) COLLATE DATABASE_DEFAULT NULL,
                     SectionCode nvarchar(32) COLLATE DATABASE_DEFAULT NULL, MobileNumber nvarchar(50) COLLATE DATABASE_DEFAULT NULL, UserId uniqueidentifier NULL);
INSERT INTO #Users (EmployeeNumber, PreferredLanguage) VALUES
{users};
CREATE TABLE #UserRoles (EmployeeNumber nvarchar(10) COLLATE DATABASE_DEFAULT, RoleCode nvarchar(64) COLLATE DATABASE_DEFAULT);
INSERT INTO #UserRoles VALUES
{user_roles};
CREATE TABLE #Depts (DepartmentCode nvarchar(32) COLLATE DATABASE_DEFAULT PRIMARY KEY, NameEn nvarchar(200) COLLATE DATABASE_DEFAULT, NameAr nvarchar(200) COLLATE DATABASE_DEFAULT);
CREATE TABLE #Sections (SectionCode nvarchar(32) COLLATE DATABASE_DEFAULT PRIMARY KEY, DepartmentCode nvarchar(32) COLLATE DATABASE_DEFAULT, NameEn nvarchar(200) COLLATE DATABASE_DEFAULT, NameAr nvarchar(200) COLLATE DATABASE_DEFAULT);
CREATE TABLE #Apps (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT PRIMARY KEY, DbName sysname, NameEn nvarchar(200) COLLATE DATABASE_DEFAULT, NameAr nvarchar(200) COLLATE DATABASE_DEFAULT,
                    Description nvarchar(1000) COLLATE DATABASE_DEFAULT, ReaderLogin sysname, ReaderPassword nvarchar(64), ApplicationId uniqueidentifier NULL);
INSERT INTO #Apps (AppCode, DbName, NameEn, NameAr, Description, ReaderLogin, ReaderPassword) VALUES
{apps};
CREATE TABLE #Catalog (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, SchemaName nvarchar(128) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT,
                       ObjectType nvarchar(10) COLLATE DATABASE_DEFAULT, ColumnName nvarchar(128) COLLATE DATABASE_DEFAULT, DataType nvarchar(128) COLLATE DATABASE_DEFAULT,
                       MaxLength int NULL, Precision int, Scale int, IsNullable bit, OrdinalPosition int, IsPrimaryKey bit);
CREATE TABLE #Relations (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ConstraintName nvarchar(128) COLLATE DATABASE_DEFAULT, FromSchema nvarchar(128) COLLATE DATABASE_DEFAULT,
                         FromObject nvarchar(128) COLLATE DATABASE_DEFAULT, FromColumn nvarchar(128) COLLATE DATABASE_DEFAULT, ToSchema nvarchar(128) COLLATE DATABASE_DEFAULT,
                         ToObject nvarchar(128) COLLATE DATABASE_DEFAULT, ToColumn nvarchar(128) COLLATE DATABASE_DEFAULT);

-- Departments (Head Office departments and branches), sections and the eight people come from Personnel so the codes always match.
SET @Sql = N'INSERT INTO #Depts SELECT DepartmentCode, DepartmentName, DepartmentNameAr FROM ' + QUOTENAME(@PersonnelDb) + N'.dbo.Departments;
INSERT INTO #Sections SELECT SectionCode, DepartmentCode, SectionName, SectionNameAr FROM ' + QUOTENAME(@PersonnelDb) + N'.dbo.Sections;
UPDATE u SET FullName = e.FullName, DepartmentCode = e.DepartmentCode, SectionCode = e.SectionCode, MobileNumber = e.MobileNumber, Email = ISNULL(u.Email, e.WorkEmail)
FROM #Users u JOIN ' + QUOTENAME(@PersonnelDb) + N'.dbo.Employees e ON e.EmployeeNumber = u.EmployeeNumber COLLATE DATABASE_DEFAULT;';
EXEC sys.sp_executesql @Sql;
IF EXISTS (SELECT 1 FROM #Users WHERE FullName IS NULL OR Email IS NULL)
    THROW 50003, N'Employees W1001-W1008 are missing from WabiBankPersonnelDB: re-run 01-create-wabibankpersonneldb.sql.', 1;
"""

CATALOG = """-- Catalog of every user table and view, read exactly as the metadata refresh of the SQL Server adapter reads it.
DECLARE @App nvarchar(64), @Db sysname;
DECLARE apps CURSOR LOCAL FAST_FORWARD FOR SELECT AppCode, DbName FROM #Apps;
OPEN apps;
FETCH NEXT FROM apps INTO @App, @Db;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Sql = N'INSERT INTO #Catalog
SELECT @App, s.name, o.name, CASE WHEN o.type = ''V'' THEN N''View'' ELSE N''Table'' END, c.name, t.name,
       CASE WHEN c.max_length < 0 THEN NULL ELSE c.max_length END, c.precision, c.scale, c.is_nullable, c.column_id,
       CASE WHEN EXISTS (SELECT 1 FROM DB.sys.index_columns ic JOIN DB.sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                         WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id) THEN 1 ELSE 0 END
FROM DB.sys.objects o
JOIN DB.sys.schemas s ON s.schema_id = o.schema_id
JOIN DB.sys.columns c ON c.object_id = o.object_id
JOIN DB.sys.types t ON t.user_type_id = c.user_type_id
WHERE o.type IN (''U'', ''V'') AND o.is_ms_shipped = 0 AND s.name = ''dbo'';
INSERT INTO #Relations
SELECT @App, fk.name, ps.name, pt.name, pc.name, rs.name, rt.name, rc.name
FROM DB.sys.foreign_key_columns fkc
JOIN DB.sys.foreign_keys fk ON fk.object_id = fkc.constraint_object_id
JOIN DB.sys.tables pt ON pt.object_id = fkc.parent_object_id
JOIN DB.sys.schemas ps ON ps.schema_id = pt.schema_id
JOIN DB.sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
JOIN DB.sys.tables rt ON rt.object_id = fkc.referenced_object_id
JOIN DB.sys.schemas rs ON rs.schema_id = rt.schema_id
JOIN DB.sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id;';
    SET @Sql = REPLACE(@Sql, N'DB.sys.', QUOTENAME(@Db) + N'.sys.');
    EXEC sys.sp_executesql @Sql, N'@App nvarchar(64)', @App = @App;
    FETCH NEXT FROM apps INTO @App, @Db;
END;
CLOSE apps;
DEALLOCATE apps;

-- ---------------------------------------------------------------------------------------------- documentation for the AI planner
CREATE TABLE #ObjectDocs (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT, DisplayEn nvarchar(200) COLLATE DATABASE_DEFAULT,
                          DisplayAr nvarchar(200) COLLATE DATABASE_DEFAULT, Description nvarchar(2000) COLLATE DATABASE_DEFAULT);
INSERT INTO #ObjectDocs VALUES
{object_docs};
CREATE TABLE #ColumnDocs (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT, ColumnName nvarchar(128) COLLATE DATABASE_DEFAULT,
                          DisplayEn nvarchar(200) COLLATE DATABASE_DEFAULT NULL, DisplayAr nvarchar(200) COLLATE DATABASE_DEFAULT NULL, Description nvarchar(2000) COLLATE DATABASE_DEFAULT NULL,
                          Classification nvarchar(20) COLLATE DATABASE_DEFAULT NULL);
INSERT INTO #ColumnDocs VALUES
{column_docs};
CREATE TABLE #FieldRoles (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT, ColumnName nvarchar(128) COLLATE DATABASE_DEFAULT, SemanticRole int);
INSERT INTO #FieldRoles VALUES
{field_roles};
CREATE TABLE #RowScopes (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT, EmployeeColumn nvarchar(128) COLLATE DATABASE_DEFAULT NULL,
                         DepartmentColumn nvarchar(128) COLLATE DATABASE_DEFAULT NULL, SectionColumn nvarchar(128) COLLATE DATABASE_DEFAULT NULL);
INSERT INTO #RowScopes VALUES
{row_scopes};
CREATE TABLE #Grants (RoleCode nvarchar(64) COLLATE DATABASE_DEFAULT, AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT NULL,
                      Scope nvarchar(20) COLLATE DATABASE_DEFAULT, ExcludeRestricted bit);
INSERT INTO #Grants VALUES
{grants};
CREATE TABLE #CustomRoles (RoleCode nvarchar(64) COLLATE DATABASE_DEFAULT, NameEn nvarchar(200) COLLATE DATABASE_DEFAULT, NameAr nvarchar(200) COLLATE DATABASE_DEFAULT,
                           Description nvarchar(1000) COLLATE DATABASE_DEFAULT);
INSERT INTO #CustomRoles VALUES
{custom_roles};
"""

BODY = """
BEGIN TRY
    BEGIN TRANSACTION;

    -- ------------------------------------------------------------------------------------------ organization, roles, built-in app
    SELECT @OrgId = OrganizationId FROM dbo.Organizations WHERE OrganizationCode = @OrgCode;
    IF @OrgId IS NULL
    BEGIN
        SET @OrgId = NEWID();
        INSERT INTO dbo.Organizations (OrganizationId, OrganizationCode, OrganizationName, OrganizationNameAr, Description, ContactEmail, ContactPhone, CountryCode, TimeZone,
                                       DefaultLanguage, Status, CreatedAt, RequireSecondAdminApprovalForSupportAccess)
        VALUES (@OrgId, @OrgCode, @OrgName, @OrgNameAr, N'Sample organization (fictional): a UAE retail and commercial bank with Personnel, Core Banking, Finance, Digital Banking and IT Help Desk sources.',
                (SELECT Email FROM #Users WHERE EmployeeNumber = N'W1001'), N'+971 4 000 0000', N'AE', N'Asia/Dubai', N'en', N'Active', @Now, 0);
    END;

    INSERT INTO dbo.Roles (RoleId, OrganizationId, RoleCode, NameEn, NameAr, Description, IsSystem, IsActive, TemplateVersion, CreatedAt)
    SELECT NEWID(), @OrgId, t.RoleCode, t.NameEn, t.NameAr, t.Description, t.IsSystem, 1, t.Version, @Now
    FROM (SELECT RoleCode, NameEn, NameAr, Description, IsSystem, Version FROM dbo.DefaultRoleTemplates WHERE IsActive = 1 OR IsSystem = 1
          UNION ALL
          SELECT v.RoleCode, v.NameEn, v.NameAr, v.Description, v.IsSystem, 1 FROM (VALUES
              (N'OrganizationAdmin', N'Organization Administrator', N'مدير المؤسسة', N'Manages the organization''s users, structure, roles, applications, metadata and settings.', CAST(1 AS bit)),
              (N'Management', N'Management', N'الإدارة', N'Read-only access to all approved registered objects of the organization.', CAST(1 AS bit)),
              (N'Analyst', N'Analyst', N'محلّل', N'Default business-data role; permissions are granted explicitly.', CAST(0 AS bit))) v(RoleCode, NameEn, NameAr, Description, IsSystem)
          WHERE NOT EXISTS (SELECT 1 FROM dbo.DefaultRoleTemplates)) t
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles r WHERE r.OrganizationId = @OrgId AND r.RoleCode = t.RoleCode);
    INSERT INTO dbo.Roles (RoleId, OrganizationId, RoleCode, NameEn, NameAr, Description, IsSystem, IsActive, TemplateVersion, CreatedAt)
    SELECT NEWID(), @OrgId, v.RoleCode, v.NameEn, v.NameAr, v.Description, 0, 1, NULL, @Now FROM #CustomRoles v
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles r WHERE r.OrganizationId = @OrgId AND r.RoleCode = v.RoleCode);

    INSERT INTO dbo.Applications (ApplicationId, OrganizationId, ApplicationName, ApplicationNameAr, ApplicationCode, Description, DatabaseType, IsBuiltIn, BuiltInCode, IsActive, IsApproved, CreatedAt)
    SELECT NEWID(), @OrgId, N'Platform Insights', N'تحليلات المنصة', N'WI-INSIGHTS', N'Built-in, tenant-scoped usage analytics of Wabi Insight for this organization.', N'PlatformInsights', 1, N'WI-INSIGHTS', 1, 1, @Now
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Applications a WHERE a.OrganizationId = @OrgId AND a.BuiltInCode = N'WI-INSIGHTS');

    -- ------------------------------------------------------------------------------------------ departments (Head Office and branches) and sections
    INSERT INTO dbo.Departments (DepartmentId, OrganizationId, DepartmentCode, DepartmentNameEn, DepartmentNameAr, IsActive, CreatedAt)
    SELECT NEWID(), @OrgId, d.DepartmentCode, d.NameEn, d.NameAr, 1, @Now FROM #Depts d
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Departments x WHERE x.OrganizationId = @OrgId AND x.DepartmentCode = d.DepartmentCode);
    INSERT INTO dbo.Sections (SectionId, OrganizationId, DepartmentId, SectionCode, SectionNameEn, SectionNameAr, IsActive, CreatedAt)
    SELECT NEWID(), @OrgId, d.DepartmentId, s.SectionCode, s.NameEn, s.NameAr, 1, @Now
    FROM #Sections s JOIN dbo.Departments d ON d.OrganizationId = @OrgId AND d.DepartmentCode = s.DepartmentCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Sections x WHERE x.OrganizationId = @OrgId AND x.DepartmentId = d.DepartmentId AND x.SectionCode = s.SectionCode);

    -- ------------------------------------------------------------------------------------------ users (the CEO and the branch manager head a unit, so they have no section)
    IF EXISTS (SELECT 1 FROM #Users u JOIN dbo.LoginIdentities li ON li.NormalizedEmail = UPPER(LTRIM(RTRIM(u.Email)))
               WHERE li.OrganizationId IS NULL OR li.OrganizationId <> @OrgId)
        THROW 50004, N'One of the sample e-mail addresses already belongs to another account; set Email in #Users at the top of this script.', 1;
    UPDATE u SET UserId = ou.UserId FROM #Users u JOIN dbo.OrganizationUsers ou ON ou.OrganizationId = @OrgId AND ou.NormalizedEmail = UPPER(LTRIM(RTRIM(u.Email)));
    UPDATE u SET UserId = ou.UserId FROM #Users u JOIN dbo.OrganizationUsers ou ON ou.OrganizationId = @OrgId AND ou.EmployeeNumber = u.EmployeeNumber WHERE u.UserId IS NULL;
    UPDATE #Users SET UserId = NEWID() WHERE UserId IS NULL;
    INSERT INTO dbo.OrganizationUsers (UserId, OrganizationId, Email, NormalizedEmail, FullName, EmployeeNumber, PhoneNumber, DepartmentId, SectionId, PreferredLanguage, Status,
                                       MustChangePassword, PasswordHash, SecurityStamp, FailedLoginCount, CreatedAt)
    SELECT u.UserId, @OrgId, LTRIM(RTRIM(u.Email)), UPPER(LTRIM(RTRIM(u.Email))), u.FullName, u.EmployeeNumber, u.MobileNumber, d.DepartmentId, s.SectionId, u.PreferredLanguage, N'Active',
           1, @InitialPasswordHash, CONVERT(nvarchar(64), CRYPT_GEN_RANDOM(20), 2), 0, @Now
    FROM #Users u
    JOIN dbo.Departments d ON d.OrganizationId = @OrgId AND d.DepartmentCode = u.DepartmentCode
    LEFT JOIN dbo.Sections s ON s.OrganizationId = @OrgId AND s.DepartmentId = d.DepartmentId AND s.SectionCode = u.SectionCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.OrganizationUsers ou WHERE ou.UserId = u.UserId);
    INSERT INTO dbo.LoginIdentities (NormalizedEmail, PrincipalType, PrincipalId, OrganizationId, CreatedAt)
    SELECT UPPER(LTRIM(RTRIM(u.Email))), N'OrganizationUser', u.UserId, @OrgId, @Now FROM #Users u
    WHERE NOT EXISTS (SELECT 1 FROM dbo.LoginIdentities li WHERE li.NormalizedEmail = UPPER(LTRIM(RTRIM(u.Email))));
    INSERT INTO dbo.UserRoles (UserId, RoleId, OrganizationId, AssignedAt)
    SELECT u.UserId, r.RoleId, @OrgId, @Now FROM #UserRoles ur JOIN #Users u ON u.EmployeeNumber = ur.EmployeeNumber
    JOIN dbo.Roles r ON r.OrganizationId = @OrgId AND r.RoleCode = ur.RoleCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.UserRoles x WHERE x.UserId = u.UserId AND x.RoleId = r.RoleId);
    SELECT @AdminUserId = UserId FROM #Users WHERE EmployeeNumber = N'W1001';

    -- ------------------------------------------------------------------------------------------ applications and catalogued metadata
    INSERT INTO dbo.Applications (ApplicationId, OrganizationId, ApplicationName, ApplicationNameAr, ApplicationCode, Description, DatabaseType, IsBuiltIn, IsActive, IsApproved,
                                  CreatedByUserId, CreatedAt, ConnectionHint, LastMetadataRefreshAt)
    SELECT NEWID(), @OrgId, a.NameEn, a.NameAr, a.AppCode, a.Description, N'SqlServer', 0, 1, 0, @AdminUserId, @Now, CONCAT(a.DbName, N' (login ', a.ReaderLogin, N')'), @Now
    FROM #Apps a WHERE NOT EXISTS (SELECT 1 FROM dbo.Applications x WHERE x.OrganizationId = @OrgId AND x.ApplicationCode = a.AppCode);
    UPDATE a SET ApplicationId = x.ApplicationId FROM #Apps a JOIN dbo.Applications x ON x.OrganizationId = @OrgId AND x.ApplicationCode = a.AppCode;

    INSERT INTO dbo.DatabaseObjects (ObjectId, OrganizationId, ApplicationId, SchemaName, ObjectName, ObjectType, IsApproved, IsPresent, DiscoveredAt, LastSeenAt, ApprovedByUserId, ApprovedAt, CreatedAt)
    SELECT NEWID(), @OrgId, a.ApplicationId, c.SchemaName, c.ObjectName, MIN(c.ObjectType), 1, 1, @Now, @Now, @AdminUserId, @Now, @Now
    FROM #Catalog c JOIN #Apps a ON a.AppCode = c.AppCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DatabaseObjects o WHERE o.ApplicationId = a.ApplicationId AND o.SchemaName = c.SchemaName AND o.ObjectName = c.ObjectName)
    GROUP BY a.ApplicationId, c.SchemaName, c.ObjectName;
    INSERT INTO dbo.DatabaseColumns (ColumnId, OrganizationId, ObjectId, ColumnName, DataType, MaxLength, Precision, Scale, IsNullable, IsPrimaryKey, OrdinalPosition, Classification,
                                     IsApproved, IsPresent, DiscoveredAt, LastSeenAt, CreatedAt)
    SELECT NEWID(), @OrgId, o.ObjectId, c.ColumnName, c.DataType, c.MaxLength, c.Precision, c.Scale, c.IsNullable, c.IsPrimaryKey, c.OrdinalPosition, N'Internal', 1, 1, @Now, @Now, @Now
    FROM #Catalog c JOIN #Apps a ON a.AppCode = c.AppCode
    JOIN dbo.DatabaseObjects o ON o.ApplicationId = a.ApplicationId AND o.SchemaName = c.SchemaName AND o.ObjectName = c.ObjectName
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DatabaseColumns x WHERE x.ObjectId = o.ObjectId AND x.ColumnName = c.ColumnName);
    INSERT INTO dbo.DatabaseRelationships (RelationshipId, OrganizationId, ApplicationId, ConstraintName, FromObjectId, FromColumnId, ToObjectId, ToColumnId, IsPresent, DiscoveredAt, LastSeenAt)
    SELECT NEWID(), @OrgId, a.ApplicationId, r.ConstraintName, fo.ObjectId, fc.ColumnId, tobj.ObjectId, tc.ColumnId, 1, @Now, @Now
    FROM #Relations r JOIN #Apps a ON a.AppCode = r.AppCode
    JOIN dbo.DatabaseObjects fo ON fo.ApplicationId = a.ApplicationId AND fo.SchemaName = r.FromSchema AND fo.ObjectName = r.FromObject
    JOIN dbo.DatabaseColumns fc ON fc.ObjectId = fo.ObjectId AND fc.ColumnName = r.FromColumn
    JOIN dbo.DatabaseObjects tobj ON tobj.ApplicationId = a.ApplicationId AND tobj.SchemaName = r.ToSchema AND tobj.ObjectName = r.ToObject
    JOIN dbo.DatabaseColumns tc ON tc.ObjectId = tobj.ObjectId AND tc.ColumnName = r.ToColumn
    WHERE NOT EXISTS (SELECT 1 FROM dbo.DatabaseRelationships x WHERE x.ApplicationId = a.ApplicationId AND x.ConstraintName = r.ConstraintName AND x.FromColumnId = fc.ColumnId);

    -- Documentation and classification (refreshed on every run).
    UPDATE o SET DisplayNameEn = d.DisplayEn, DisplayNameAr = d.DisplayAr, BusinessDescription = d.Description, UpdatedAt = @Now
    FROM dbo.DatabaseObjects o JOIN #Apps a ON a.ApplicationId = o.ApplicationId JOIN #ObjectDocs d ON d.AppCode = a.AppCode AND d.ObjectName = o.ObjectName;
    UPDATE c SET DisplayNameEn = ISNULL(d.DisplayEn, c.DisplayNameEn), DisplayNameAr = ISNULL(d.DisplayAr, c.DisplayNameAr), BusinessDescription = ISNULL(d.Description, c.BusinessDescription),
                 Classification = ISNULL(d.Classification, c.Classification), UpdatedAt = @Now
    FROM dbo.DatabaseColumns c JOIN dbo.DatabaseObjects o ON o.ObjectId = c.ObjectId JOIN #Apps a ON a.ApplicationId = o.ApplicationId
    JOIN #ColumnDocs d ON d.AppCode = a.AppCode AND d.ObjectName = o.ObjectName AND d.ColumnName = c.ColumnName;

    -- Field roles (Phase 14): 1 Identifier, 2 Person, 3 Account, 4 Device, 5 Date/time, 6 Amount, 7 Quantity, 8 Status, 9 Location.
    -- An administrator's later choice is kept.
    UPDATE c SET SemanticRole = v.SemanticRole, UpdatedAt = @Now
    FROM #FieldRoles v
    JOIN #Apps a ON a.AppCode = v.AppCode
    JOIN dbo.DatabaseObjects o ON o.ApplicationId = a.ApplicationId AND o.SchemaName = N'dbo' AND o.ObjectName = v.ObjectName
    JOIN dbo.DatabaseColumns c ON c.ObjectId = o.ObjectId AND c.ColumnName = v.ColumnName
    WHERE c.SemanticRole IS NULL;

    -- ------------------------------------------------------------------------------------------ row-scope mappings
    -- SELF compares the employee column with the user's employee number; DEPARTMENT the department column (BranchCode,
    -- PostingUnitCode, DepartmentCode ...) with the user's department code, which for branch staff is the branch code.
    INSERT INTO dbo.RowScopeColumnMappings (ObjectId, OrganizationId, EmployeeColumnId, DepartmentColumnId, SectionColumnId, UpdatedAt, UpdatedByUserId)
    SELECT o.ObjectId, @OrgId,
           (SELECT c.ColumnId FROM dbo.DatabaseColumns c WHERE c.ObjectId = o.ObjectId AND c.ColumnName = m.EmployeeColumn),
           (SELECT c.ColumnId FROM dbo.DatabaseColumns c WHERE c.ObjectId = o.ObjectId AND c.ColumnName = m.DepartmentColumn),
           (SELECT c.ColumnId FROM dbo.DatabaseColumns c WHERE c.ObjectId = o.ObjectId AND c.ColumnName = m.SectionColumn), @Now, @AdminUserId
    FROM #RowScopes m
    JOIN #Apps a ON a.AppCode = m.AppCode
    JOIN dbo.DatabaseObjects o ON o.ApplicationId = a.ApplicationId AND o.SchemaName = N'dbo' AND o.ObjectName = m.ObjectName
    WHERE NOT EXISTS (SELECT 1 FROM dbo.RowScopeColumnMappings x WHERE x.ObjectId = o.ObjectId);

    -- ------------------------------------------------------------------------------------------ role permissions
    SELECT g.RoleCode, r.RoleId, o.ObjectId, g.Scope, g.ExcludeRestricted
    INTO #ObjectGrants
    FROM #Grants g
    JOIN dbo.Roles r ON r.OrganizationId = @OrgId AND r.RoleCode = g.RoleCode
    JOIN #Apps a ON a.AppCode = g.AppCode
    JOIN dbo.DatabaseObjects o ON o.ApplicationId = a.ApplicationId AND (g.ObjectName IS NULL OR o.ObjectName = g.ObjectName);
    INSERT INTO dbo.RoleTablePermissions (PermissionId, OrganizationId, RoleId, ObjectId, AllColumns, GrantedAt, GrantedByUserId)
    SELECT NEWID(), @OrgId, og.RoleId, og.ObjectId,
           CASE WHEN og.ExcludeRestricted = 1 AND EXISTS (SELECT 1 FROM dbo.DatabaseColumns c WHERE c.ObjectId = og.ObjectId AND c.Classification = N'Restricted') THEN 0 ELSE 1 END,
           @Now, @AdminUserId
    FROM #ObjectGrants og WHERE NOT EXISTS (SELECT 1 FROM dbo.RoleTablePermissions x WHERE x.RoleId = og.RoleId AND x.ObjectId = og.ObjectId);
    INSERT INTO dbo.RoleColumnPermissions (RoleId, ColumnId, OrganizationId, ObjectId)
    SELECT p.RoleId, c.ColumnId, @OrgId, p.ObjectId
    FROM dbo.RoleTablePermissions p JOIN #ObjectGrants og ON og.RoleId = p.RoleId AND og.ObjectId = p.ObjectId
    JOIN dbo.DatabaseColumns c ON c.ObjectId = p.ObjectId AND c.Classification <> N'Restricted'
    WHERE p.AllColumns = 0 AND NOT EXISTS (SELECT 1 FROM dbo.RoleColumnPermissions x WHERE x.RoleId = p.RoleId AND x.ColumnId = c.ColumnId);
    INSERT INTO dbo.RoleRowPermissions (RoleId, ObjectId, OrganizationId, Scope)
    SELECT og.RoleId, og.ObjectId, @OrgId, og.Scope FROM #ObjectGrants og
    WHERE NOT EXISTS (SELECT 1 FROM dbo.RoleRowPermissions x WHERE x.RoleId = og.RoleId AND x.ObjectId = og.ObjectId);

    -- ------------------------------------------------------------------------------------------ monitoring rules (Phase 14)
    -- Data rules run with their owner's read permissions and bind catalogued columns by id, exactly as the rule editor saves
    -- them. The live views move December 2025 so that 31 December is today, so each rule finds its planted case at once.
    CREATE TABLE #Rules (Name nvarchar(200) COLLATE DATABASE_DEFAULT, Description nvarchar(1000) COLLATE DATABASE_DEFAULT, Kind nvarchar(20) COLLATE DATABASE_DEFAULT,
                         TemplateCode nvarchar(64) COLLATE DATABASE_DEFAULT NULL, Owner nvarchar(10) COLLATE DATABASE_DEFAULT, Severity nvarchar(20) COLLATE DATABASE_DEFAULT,
                         RecurrenceKind nvarchar(20) COLLATE DATABASE_DEFAULT, IntervalMinutes int, TimeOfDay time, AppCode nvarchar(64) COLLATE DATABASE_DEFAULT NULL,
                         ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT NULL, DefinitionJson nvarchar(max) COLLATE DATABASE_DEFAULT, RecipientRole nvarchar(64) COLLATE DATABASE_DEFAULT);
    INSERT INTO #Rules VALUES
{rules};
    -- Replace {{col:Name}} with the catalogued column id of the rule's object and {{obj:App/Object}} with an object id.
    DECLARE @Name nvarchar(200), @Def nvarchar(max), @App2 nvarchar(64), @Obj nvarchar(128), @ObjId uniqueidentifier, @Token nvarchar(300), @Col nvarchar(128), @Id uniqueidentifier, @P int;
    DECLARE rules CURSOR LOCAL FAST_FORWARD FOR SELECT Name, DefinitionJson, AppCode, ObjectName FROM #Rules;
    OPEN rules;
    FETCH NEXT FROM rules INTO @Name, @Def, @App2, @Obj;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @ObjId = (SELECT o.ObjectId FROM dbo.DatabaseObjects o JOIN #Apps a ON a.ApplicationId = o.ApplicationId WHERE a.AppCode = @App2 AND o.SchemaName = N'dbo' AND o.ObjectName = @Obj);
        IF @ObjId IS NOT NULL SET @Def = REPLACE(@Def, N'{{object}}', LOWER(CONVERT(nvarchar(36), @ObjId)));
        ELSE IF @App2 IS NOT NULL THROW 50007, N'A monitoring rule refers to an object that is not catalogued.', 1;
        SET @P = CHARINDEX(N'{{col:', @Def);
        WHILE @P > 0
        BEGIN
            SET @Token = SUBSTRING(@Def, @P, CHARINDEX(N'}}', @Def, @P) - @P + 2);
            SET @Col = SUBSTRING(@Token, 7, LEN(@Token) - 8);
            SET @Id = (SELECT ColumnId FROM dbo.DatabaseColumns WHERE ObjectId = @ObjId AND ColumnName = @Col);
            IF @Id IS NULL THROW 50005, N'A monitoring rule refers to a column that is not catalogued.', 1;
            SET @Def = REPLACE(@Def, @Token, LOWER(CONVERT(nvarchar(36), @Id)));
            SET @P = CHARINDEX(N'{{col:', @Def);
        END;
        SET @P = CHARINDEX(N'{{obj:', @Def);
        WHILE @P > 0
        BEGIN
            SET @Token = SUBSTRING(@Def, @P, CHARINDEX(N'}}', @Def, @P) - @P + 2);
            SET @Col = SUBSTRING(@Token, 7, LEN(@Token) - 8);
            SET @Id = (SELECT o.ObjectId FROM dbo.DatabaseObjects o JOIN #Apps a ON a.ApplicationId = o.ApplicationId
                       WHERE a.AppCode = LEFT(@Col, CHARINDEX(N'/', @Col) - 1) AND o.SchemaName = N'dbo' AND o.ObjectName = SUBSTRING(@Col, CHARINDEX(N'/', @Col) + 1, 128));
            IF @Id IS NULL THROW 50006, N'A monitoring rule refers to an object that is not catalogued.', 1;
            SET @Def = REPLACE(@Def, @Token, LOWER(CONVERT(nvarchar(36), @Id)));
            SET @P = CHARINDEX(N'{{obj:', @Def);
        END;
        UPDATE #Rules SET DefinitionJson = @Def WHERE Name = @Name;
        FETCH NEXT FROM rules INTO @Name, @Def, @App2, @Obj;
    END;
    CLOSE rules;
    DEALLOCATE rules;
    SELECT NEWID() AS RuleId, r.* INTO #NewRules FROM #Rules r
    WHERE NOT EXISTS (SELECT 1 FROM dbo.MonitoringRules m WHERE m.OrganizationId = @OrgId AND m.Name = r.Name);
    INSERT INTO dbo.MonitoringRules (RuleId, OrganizationId, OwnerUserId, CreatedByUserId, Name, Description, Kind, TemplateCode, DefinitionJson, Severity,
                                     RecurrenceKind, IntervalMinutes, TimeOfDay, DayOfWeek, CooldownHours, IsActive, CreatedAt)
    SELECT n.RuleId, @OrgId, u.UserId, @AdminUserId, n.Name, n.Description, n.Kind, n.TemplateCode, n.DefinitionJson, n.Severity,
           n.RecurrenceKind, n.IntervalMinutes, n.TimeOfDay, NULL, 24, 1, @Now
    FROM #NewRules n JOIN #Users u ON u.EmployeeNumber = n.Owner;
    INSERT INTO dbo.MonitoringRuleRecipients (RecipientId, OrganizationId, RuleId, UserId, RoleId)
    SELECT NEWID(), @OrgId, n.RuleId, NULL, r.RoleId FROM #NewRules n JOIN dbo.Roles r ON r.OrganizationId = @OrgId AND r.RoleCode = n.RecipientRole;
    -- The API's scheduler creates each rule's job on its next tick (one minute at most).

    -- ------------------------------------------------------------------------------------------ audit trail
    INSERT INTO dbo.AuditLogs (AuditLogId, OrganizationId, ActorType, ActorId, ActorDisplayName, EventType, EntityType, EntityId, DetailsJson, CreatedAt)
    VALUES (NEWID(), @OrgId, N'PlatformAdministrator', NULL, N'Sample data seed (wabi-bank/06-seed-wabiinsightdb.sql)', N'SampleData.Seeded', N'Organization', CONVERT(nvarchar(64), @OrgId),
            CONCAT(N'{"organization":"', @OrgCode, N'","applications":[{app_codes}],"users":{user_count},"monitoringRules":{rule_count}}'), @Now);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

-- ---------------------------------------------------------------------------------------------- summary and next steps
SELECT a.ApplicationCode, a.ApplicationName, COUNT(DISTINCT o.ObjectId) AS ApprovedObjects, COUNT(c.ColumnId) AS ApprovedColumns,
       SUM(CASE WHEN c.Classification = N'Confidential' THEN 1 ELSE 0 END) AS ConfidentialColumns, SUM(CASE WHEN c.Classification = N'Restricted' THEN 1 ELSE 0 END) AS RestrictedColumns,
       CASE WHEN a.EncryptedConnectionString IS NULL THEN N'connection string to be entered in the application' ELSE N'connected' END AS Connection
FROM dbo.Applications a
JOIN dbo.DatabaseObjects o ON o.ApplicationId = a.ApplicationId AND o.IsApproved = 1
JOIN dbo.DatabaseColumns c ON c.ObjectId = o.ObjectId AND c.IsApproved = 1
WHERE a.OrganizationId = @OrgId
GROUP BY a.ApplicationCode, a.ApplicationName, a.EncryptedConnectionString;
SELECT u.EmployeeNumber, ou.FullName, ou.Email, STRING_AGG(r.RoleCode, N', ') WITHIN GROUP (ORDER BY r.RoleCode) AS Roles, d.DepartmentCode, s.SectionCode,
       CASE WHEN ou.MustChangePassword = 1 THEN N'Wabi#Sample#2026 (change at first sign-in)' ELSE N'(already changed)' END AS Password
FROM #Users u JOIN dbo.OrganizationUsers ou ON ou.UserId = u.UserId
JOIN dbo.UserRoles ur ON ur.UserId = ou.UserId JOIN dbo.Roles r ON r.RoleId = ur.RoleId
LEFT JOIN dbo.Departments d ON d.DepartmentId = ou.DepartmentId LEFT JOIN dbo.Sections s ON s.SectionId = ou.SectionId
GROUP BY u.EmployeeNumber, ou.FullName, ou.Email, d.DepartmentCode, s.SectionCode, ou.MustChangePassword
ORDER BY u.EmployeeNumber;
SELECT m.Name AS MonitoringRule, m.Kind, m.Severity, m.RecurrenceKind, o.EmployeeNumber AS Owner FROM dbo.MonitoringRules m JOIN dbo.OrganizationUsers o ON o.UserId = m.OwnerUserId
WHERE m.OrganizationId = @OrgId ORDER BY m.Name;
DECLARE @Server nvarchar(128) = CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)), @AdminEmail nvarchar(256) = (SELECT Email FROM #Users WHERE EmployeeNumber = N'W1001');
PRINT N'';
PRINT N'Next: sign in as ' + @AdminEmail + N' (password Wabi#Sample#2026, change it), open Organization Administration > Applications';
PRINT N'and for each application "Replace connection", "Test connection", then "Approve". Read-only connection strings (use the address the API reaches this server at):';
SELECT a.AppCode AS ApplicationCode,
       CONCAT(N'Server=', @Server, N';Database=', a.DbName, N';User Id=', a.ReaderLogin, N';Password=', a.ReaderPassword, N';Encrypt=True;TrustServerCertificate=True;') AS ReadOnlyConnectionString
FROM #Apps a ORDER BY a.AppCode;
GO
"""


def _rules():
    def data(obj, time_col, window, group_cols, aggregate, measure_col, filters, op, value):
        f = ",".join('{"columnId":"{{col:%s}}","operator":"%s","values":[%s]}' % (c, o, ",".join('"%s"' % v for v in vals)) for c, o, vals in filters)
        groups = ",".join('"{{col:%s}}"' % c for c in group_cols)
        measure = '"aggregate":"%s"' % aggregate + (',"columnId":"{{col:%s}}"' % measure_col if measure_col else "")
        return ('{"data":{"objectId":"{{object}}","timeColumnId":"{{col:%s}}","windowHours":%d,"groupByColumnIds":[%s],"measure":{%s},"filters":[%s],'
                '"condition":{"operator":"%s","value":%s},"comparison":"none","maxGroups":200}}') % (time_col, window, groups, measure, f, op, value)

    others = ["OrganizationAdmin", "Management", "Analyst", "CyberSecurity", "BranchManager", "Teller", "HROfficer", "BudgetController", "ServiceDeskManager", "Employee"]
    kyc = ('{"access":{"signal":"accesses","windowHours":24,"objectIds":["{{obj:CORE-BANKING/KycDocuments}}"],"roleCodes":[%s],'
           '"threshold":{"mode":"absolute","value":0.5}}}') % ",".join('"%s"' % r for r in others)
    return [
        ("Same digital profile, several devices in a day", "A digital-banking username signed in from more than two devices within 24 hours (possible account takeover). "
         "For the Cyber Security team to review; nobody is blocked automatically.", "Data", "multi_device_same_account", "W1001", "High", "Daily", 0, "06:00",
         "DIGITAL-BANKING", "vw_SignInsLive", data("vw_SignInsLive", "SignInAt", 24, ["Username"], "count_distinct", "DeviceId", [], "gt", 2), "CyberSecurity"),
        ("Cash deposits of AED 55,000 or more in a day (individuals)", "An individual customer's cash deposits within 24 hours reach the AED 55,000 cash reporting "
         "threshold, often in several smaller deposits (possible structuring). For the AML team.", "Data", "amount_above_threshold", "W1007", "High", "Daily", 0, "06:30",
         "CORE-BANKING", "vw_TransactionsLive", data("vw_TransactionsLive", "PostedAt", 24, ["CIF"], "sum", "Amount",
                                                     [("TransactionCode", "eq", ["CDP"]), ("CustomerType", "eq", ["Individual"])], "gte", 55000), "ComplianceOfficer"),
        ("More than two returned cheques per account in 30 days", "Cheques drawn on one account were returned more than twice in 30 days. For the AML team "
         "and the account's branch to follow up.", "Data", "many_events_per_entity", "W1007", "Medium", "Daily", 0, "06:30",
         "CORE-BANKING", "vw_InwardChequesLive", data("vw_InwardChequesLive", "PresentedDate", 720, ["AccountNumber"], "count", None,
                                                      [("Status", "eq", ["Returned"])], "gt", 2), "ComplianceOfficer"),
        ("More than three incidents on one configuration item in 7 days", "The same configuration item had more than three incidents within a week; "
         "consider a problem record.", "Data", "many_events_per_entity", "W1008", "Medium", "Daily", 0, "07:00",
         "IT-HELPDESK", "vw_IncidentsLive", data("vw_IncidentsLive", "OpenedAt", 168, ["CiCode"], "count", None,
                                                 [("CiCode", "not_null", [])], "gt", 3), "ServiceDeskManager"),
        ("KYC documents looked up outside Compliance", "Someone outside the Compliance role read KYC documents (Restricted identity data) through Wabi Insight.",
         "Access", None, "W1001", "High", "Interval", 60, "00:00", None, None, kyc, "ComplianceOfficer"),
    ]


def build():
    docs, cols, roles, scopes = [], [], [], []
    for code, _, module, *_ in APPS:
        docs += [(code,) + tuple(d) for d in module.OBJECT_DOCS]
        cols += [(code,) + tuple(c) for c in module.COLUMN_DOCS]
        roles += [(code,) + tuple(f) for f in module.FIELD_ROLES]
        scopes += [(code,) + tuple(s) for s in module.ROW_SCOPES]
    users = [(s[0], s[12]) for s in bp.SEED_PEOPLE]
    assert [u[0] for u in users] == sorted(USER_ROLES)
    user_roles = [(n, r) for n, rs in sorted(USER_ROLES.items()) for r in rs + ["Employee"]]
    rules = _rules()
    grants = _grants()
    text = HEADER.format(users=_values(users), user_roles=_values(user_roles),
                         apps=_values([(a[0], a[1], a[3], a[4], a[5], a[6], a[7]) for a in APPS]))
    text += CATALOG.format(object_docs=_values(docs), column_docs=_values(cols), field_roles=_values(roles), row_scopes=_values(scopes),
                           grants=_values(grants), custom_roles=_values(CUSTOM_ROLES))
    body = BODY.replace("{rules}", _values(rules)).replace("{app_codes}", ",".join(f'"{a[0]}"' for a in APPS))
    body = body.replace("{user_count}", str(len(users))).replace("{rule_count}", str(len(rules)))
    text += body
    return text, {"objects": len(docs), "columns": len(cols), "field_roles": len(roles), "row_scopes": len(scopes), "grants": len(grants), "rules": len(rules)}


if __name__ == "__main__":
    text, stats = build()
    path = os.path.join(HERE, "06-seed-wabiinsightdb.sql")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(f"{os.path.basename(path)}: {len(text) // 1024} KB {stats}")
