-- ==============================================================================================================
-- WabiInsightDB — sample organization over the three sample source databases
--
-- Run AFTER:
--   * the Wabi Insight migrations (dotnet WabiInsight.Api.dll migrate) against WabiInsightDB, and
--   * 01-create-flightoperationsdb.sql, 02-create-humanresourcesdb.sql and 03-create-procurementstoresdb.sql
--     on the SAME SQL Server instance (this script reads their departments, employees and table catalogs).
-- Run as a login that can read all four databases (sysadmin in a sandbox). Safe to re-run: existing rows are kept.
--
-- What it creates in WabiInsightDB:
--   * organization "Gulf Aviation Services" (code GAS) with the three standard roles and the built-in Platform Insights application;
--   * the departments and sections of HumanResourcesDB (codes match, so DEPARTMENT / SECTION / SELF row scopes work);
--   * four users (employee numbers match HumanResourcesDB):
--       E1001 Faisal Al Shamsi   IT Manager            roles OrganizationAdmin  (manages the organization, no data access by itself)
--                                                            and CyberSecurity      (employee-portal sign-ins only)
--       E1002 Khalid Al Suwaidi  CEO                   role Management          (reads every approved object, all rows)
--       E1003 Arjun Nair         Operations Controller role OperationsAnalyst   (flights + procurement all rows; own attendance, leave and payslips)
--       E1004 Sara Khoury        Payroll Specialist    role HRPayrollOfficer    (HR all rows without Restricted columns; own department's purchases and store issues)
--     Initial password for all four: Wabi#Sample#2026 — every user must change it at first sign-in. Edit the e-mail
--     variables below to real mailboxes if the organization should receive Wabi Insight notifications.
--   * three applications (FLIGHT-OPS, HR-PAYROLL, PROCUREMENT) with every table catalogued, approved, described (EN/AR)
--     and classified (Internal / Confidential / Restricted), foreign-key relationships, row-scope mappings and role permissions;
--   * field roles on key columns (account, device, date/time, amount ...) so Monitoring Rule templates are offered (Phase 14);
--   * the Monitoring Rule "Same account, several devices (employee portal)": owned by E1002 (runs with the CEO's read access),
--     daily at 06:00, High, sent to the Cyber Security role. It reads vw_EmployeePortalSignInsLive, whose last day is always
--     today, so its first run finds the four planted accounts.
--
-- One step stays in the application, by design: connection strings are encrypted with the deployment's Data Protection
-- key ring and can never be written by SQL. After this script, sign in as the organization administrator and, for each
-- application under Organization Administration > Applications: "Replace connection" with the read-only login
-- (examples are printed at the end), "Test connection", then "Approve".
-- ==============================================================================================================
USE [WabiInsightDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @OrgCode       nvarchar(32)  = N'GAS',
        @OrgName       nvarchar(200) = N'Gulf Aviation Services',
        @OrgNameAr     nvarchar(200) = N'خدمات الطيران الخليجية',
        @FlightDb      sysname       = N'FlightOperationsDB',
        @HrDb          sysname       = N'HumanResourcesDB',
        @ProcDb        sysname       = N'ProcurementStoresDB',
        @AdminEmail    nvarchar(256) = N'faisal.alshamsi@gulfaviation.example',
        @MgmtEmail     nvarchar(256) = N'khalid.alsuwaidi@gulfaviation.example',
        @OpsEmail      nvarchar(256) = N'arjun.nair@gulfaviation.example',
        @HrEmail       nvarchar(256) = N'sara.khoury@gulfaviation.example',
        -- ASP.NET Core Identity v3 hash (PBKDF2-HMAC-SHA512, 100,000 iterations) of the initial password Wabi#Sample#2026.
        @InitialPasswordHash nvarchar(512) = N'AQAAAAIAAYagAAAAEETy/99eckKEanTcWITTTOPP6JGVZqBqe69qf5tkJ4xaB63ZZbNF3r2unaowrHc7tA==';
DECLARE @Now datetimeoffset(7) = SYSDATETIMEOFFSET(), @OrgId uniqueidentifier, @AdminUserId uniqueidentifier, @Sql nvarchar(max);

IF OBJECT_ID(N'dbo.MonitoringRules', N'U') IS NULL
    THROW 50001, N'WabiInsightDB is not migrated to the current version: run "dotnet WabiInsight.Api.dll migrate" first.', 1;
IF DB_ID(@FlightDb) IS NULL OR DB_ID(@HrDb) IS NULL OR DB_ID(@ProcDb) IS NULL
    THROW 50002, N'Run 01-, 02- and 03-create-*.sql on this server first: FlightOperationsDB, HumanResourcesDB and ProcurementStoresDB are required.', 1;

-- ---------------------------------------------------------------------------------------------- staging tables
CREATE TABLE #Users (EmployeeNumber nvarchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, Email nvarchar(256) COLLATE DATABASE_DEFAULT, RoleCode nvarchar(64) COLLATE DATABASE_DEFAULT,
                     PreferredLanguage nvarchar(2) COLLATE DATABASE_DEFAULT, FullName nvarchar(200) COLLATE DATABASE_DEFAULT NULL, DepartmentCode nvarchar(32) COLLATE DATABASE_DEFAULT NULL,
                     SectionCode nvarchar(32) COLLATE DATABASE_DEFAULT NULL, MobileNumber nvarchar(50) COLLATE DATABASE_DEFAULT NULL, UserId uniqueidentifier NULL);
INSERT INTO #Users (EmployeeNumber, Email, RoleCode, PreferredLanguage) VALUES
    (N'E1001', @AdminEmail, N'OrganizationAdmin', N'en'), (N'E1002', @MgmtEmail, N'Management', N'ar'),
    (N'E1003', @OpsEmail, N'OperationsAnalyst', N'en'), (N'E1004', @HrEmail, N'HRPayrollOfficer', N'en');
CREATE TABLE #Depts (DepartmentCode nvarchar(32) COLLATE DATABASE_DEFAULT PRIMARY KEY, NameEn nvarchar(200) COLLATE DATABASE_DEFAULT, NameAr nvarchar(200) COLLATE DATABASE_DEFAULT);
CREATE TABLE #Sections (SectionCode nvarchar(32) COLLATE DATABASE_DEFAULT PRIMARY KEY, DepartmentCode nvarchar(32) COLLATE DATABASE_DEFAULT, NameEn nvarchar(200) COLLATE DATABASE_DEFAULT, NameAr nvarchar(200) COLLATE DATABASE_DEFAULT);
CREATE TABLE #Apps (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT PRIMARY KEY, DbName sysname, NameEn nvarchar(200) COLLATE DATABASE_DEFAULT, NameAr nvarchar(200) COLLATE DATABASE_DEFAULT,
                    Description nvarchar(1000) COLLATE DATABASE_DEFAULT, ReaderLogin sysname, ReaderPassword nvarchar(64), ApplicationId uniqueidentifier NULL);
INSERT INTO #Apps (AppCode, DbName, NameEn, NameAr, Description, ReaderLogin, ReaderPassword) VALUES
    (N'FLIGHT-OPS', @FlightDb, N'Flight Operations', N'العمليات الجوية', N'Sharjah International Airport flight operations for 2025: schedules, every departure and arrival, delays, passengers, aircraft, airlines and airports.', N'flightops_reader', N'FlightOps#Reader#2026'),
    (N'HR-PAYROLL', @HrDb, N'HR, Payroll and Attendance', N'الموارد البشرية والرواتب والحضور', N'Employees, organization structure, contracts and salaries, monthly payroll, daily attendance, leave and performance reviews for 2025.', N'hr_reader', N'HumanRes#Reader#2026'),
    (N'PROCUREMENT', @ProcDb, N'Procurement and Stores', N'المشتريات والمخازن', N'Suppliers, items, purchase requisitions and orders, goods receipts, supplier invoices, store issues and stock for 2025.', N'procurement_reader', N'Procure#Reader#2026');
CREATE TABLE #Catalog (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, SchemaName nvarchar(128) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT,
                       ObjectType nvarchar(10) COLLATE DATABASE_DEFAULT, ColumnName nvarchar(128) COLLATE DATABASE_DEFAULT, DataType nvarchar(128) COLLATE DATABASE_DEFAULT,
                       MaxLength int NULL, Precision int, Scale int, IsNullable bit, OrdinalPosition int, IsPrimaryKey bit);
CREATE TABLE #Relations (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ConstraintName nvarchar(128) COLLATE DATABASE_DEFAULT, FromSchema nvarchar(128) COLLATE DATABASE_DEFAULT,
                         FromObject nvarchar(128) COLLATE DATABASE_DEFAULT, FromColumn nvarchar(128) COLLATE DATABASE_DEFAULT, ToSchema nvarchar(128) COLLATE DATABASE_DEFAULT,
                         ToObject nvarchar(128) COLLATE DATABASE_DEFAULT, ToColumn nvarchar(128) COLLATE DATABASE_DEFAULT);

-- Departments, sections and the four people come from HumanResourcesDB so the codes always match.
SET @Sql = N'INSERT INTO #Depts SELECT DepartmentCode, DepartmentName, DepartmentNameAr FROM ' + QUOTENAME(@HrDb) + N'.dbo.Departments;
INSERT INTO #Sections SELECT SectionCode, DepartmentCode, SectionName, SectionNameAr FROM ' + QUOTENAME(@HrDb) + N'.dbo.Sections;
UPDATE u SET FullName = e.FullName, DepartmentCode = e.DepartmentCode, SectionCode = e.SectionCode, MobileNumber = e.MobileNumber
FROM #Users u JOIN ' + QUOTENAME(@HrDb) + N'.dbo.Employees e ON e.EmployeeNumber = u.EmployeeNumber COLLATE DATABASE_DEFAULT;';
EXEC sys.sp_executesql @Sql;
IF EXISTS (SELECT 1 FROM #Users WHERE FullName IS NULL)
    THROW 50003, N'Employees E1001-E1004 are missing from HumanResourcesDB: re-run 02-create-humanresourcesdb.sql.', 1;

-- Catalog of every user table and view, read exactly as the metadata refresh of the SQL Server adapter reads it.
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
WHERE o.type IN (''U'', ''V'') AND o.is_ms_shipped = 0 AND s.name NOT IN (''sys'', ''INFORMATION_SCHEMA'');
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
-- Flight operations
(N'FLIGHT-OPS', N'Calendar', N'Calendar 2025', N'التقويم 2025', N'One row per day of 2025: month, ISO week, weekday number (1 = Monday ... 7 = Sunday), UAE weekend flag (Saturday and Sunday) and UAE public holidays.'),
(N'FLIGHT-OPS', N'Countries', N'Countries', N'الدول', N'Countries of the airports and airlines in the network (ISO code) with region (GCC, South Asia, Middle East & North Africa, Europe, Caucasus & Central Asia, Africa, Asia Pacific) and continent.'),
(N'FLIGHT-OPS', N'Cities', N'Cities', N'المدن', N'Cities served from Sharjah, with Arabic names and country.'),
(N'FLIGHT-OPS', N'Airports', N'Airports', N'المطارات', N'Airports of the Sharjah route network by IATA code with city, country, region and typical block time from Sharjah. SHJ (Sharjah International) is the home airport.'),
(N'FLIGHT-OPS', N'Airlines', N'Airlines', N'شركات الطيران', N'Airlines operating at Sharjah by IATA designator, with country and carrier type (Low-cost or Full-service). Air Arabia (G9) is based at Sharjah and operates most flights.'),
(N'FLIGHT-OPS', N'AircraftTypes', N'Aircraft types', N'أنواع الطائرات', N'Aircraft types by ICAO designator: manufacturer, model, body type (Narrow-body, Wide-body, Regional), typical seats and range.'),
(N'FLIGHT-OPS', N'Aircraft', N'Aircraft', N'الطائرات', N'Individual aircraft by registration with airline, type, cabin seats in this airline''s configuration, fleet sequence and year built.'),
(N'FLIGHT-OPS', N'Terminals', N'Terminals', N'مباني الركاب', N'Passenger terminals: T1 for Air Arabia, T2 for the other airlines.'),
(N'FLIGHT-OPS', N'Gates', N'Gates and stands', N'البوابات والمواقف', N'Gates per terminal and stand type (Contact = air bridge, Remote = bus boarding).'),
(N'FLIGHT-OPS', N'DelayReasons', N'Delay reasons', N'أسباب التأخير', N'IATA delay codes with description and category (Passenger and baggage, Technical, Crew, Weather, Air traffic control, Airport, Reactionary, Airline operations).'),
(N'FLIGHT-OPS', N'FlightSchedules', N'Flight schedules (flights master)', N'جداول الرحلات', N'Planned flights: flight number, airline, direction (Departure from SHJ or Arrival to SHJ), route airport, local scheduled time, operating weekdays (digits, 1 = Monday), aircraft type, block minutes, terminal and validity period. Season is Year-round, Summer peak, Year-end peak, Eid al-Fitr or Eid al-Adha.'),
(N'FLIGHT-OPS', N'Flights', N'Flights 2025', N'الرحلات 2025', N'Every flight at Sharjah in 2025: one row per departure or arrival with scheduled and actual local time, delay minutes (NULL when cancelled), status (On Time, Delayed = more than 15 minutes, Cancelled, Diverted), IATA delay code, aircraft registration and type, seats, passengers (adult, child, infant), load factor, baggage pieces, cargo kg, terminal and gate, and calendar helpers (hour of day, weekday, month, quarter). Use FlightDate for time series.'),
-- HR, payroll and attendance
(N'HR-PAYROLL', N'Calendar', N'Calendar 2025', N'التقويم 2025', N'One row per day of 2025: month, ISO week, weekday number (1 = Monday), UAE weekend (Saturday and Sunday) and UAE public holidays.'),
(N'HR-PAYROLL', N'Countries', N'Countries and nationalities', N'الدول والجنسيات', N'Country codes with country name (English and Arabic) and nationality, used for employee nationality.'),
(N'HR-PAYROLL', N'Departments', N'Departments', N'الإدارات', N'Departments with cost centre, whether staff work rotating shifts (24/7 airport operations) and the department manager''s employee number.'),
(N'HR-PAYROLL', N'Sections', N'Sections', N'الأقسام', N'Sections inside each department.'),
(N'HR-PAYROLL', N'JobGrades', N'Job grades', N'الدرجات الوظيفية', N'Grades G1 (entry) to G10 (executive) with the monthly basic salary band in AED and annual leave entitlement.'),
(N'HR-PAYROLL', N'Positions', N'Positions', N'المناصب', N'Job positions with title (English and Arabic), department, section and grade.'),
(N'HR-PAYROLL', N'Shifts', N'Shifts', N'الورديات', N'Working shifts: DAY 07:00-15:00, EVE 15:00-23:00, NGT 23:00-07:00 and OFF (office hours 08:00-17:00).'),
(N'HR-PAYROLL', N'Employees', N'Employees', N'الموظفون', N'One row per employee (current and those who left in 2025): personal data, nationality, hire and termination dates, employment status as of 31 December 2025 (Active, Resigned, Terminated), department, section, position, grade, manager, shift and weekly off days.'),
(N'HR-PAYROLL', N'EmployeeContracts', N'Employment contracts', N'عقود العمل', N'Contracts and monthly salary packages in AED (basic, housing, transport, other allowances). Employees with an annual increment have a previous contract ending 31 March 2025 and a current one from 1 April 2025 (IsCurrent = 1).'),
(N'HR-PAYROLL', N'EmployeeBankAccounts', N'Salary bank accounts', N'الحسابات البنكية للرواتب', N'Bank and IBAN used to pay each employee''s salary through the UAE Wage Protection System (WPS).'),
(N'HR-PAYROLL', N'PerformanceReviews', N'Performance reviews', N'تقييمات الأداء', N'Annual performance review for 2024 (rated in February 2025): rating 1 (unsatisfactory) to 5 (outstanding), label and reviewer.'),
(N'HR-PAYROLL', N'LeaveTypes', N'Leave types', N'أنواع الإجازات', N'Leave types: annual, sick, emergency, maternity and unpaid, with paid flag and entitlement.'),
(N'HR-PAYROLL', N'LeaveRequests', N'Leave requests', N'طلبات الإجازة', N'Leave requests in 2025 with type, start and end dates, calendar days, request date, status (Approved, Rejected, Pending), approver and decision date. Approved leave appears as On Leave in attendance.'),
(N'HR-PAYROLL', N'AttendanceRecords', N'Daily attendance', N'الحضور اليومي', N'One row per employee per scheduled working day in 2025: shift, scheduled start and end, check-in and check-out, worked, late and overtime minutes, status (Present, Late, Absent, On Leave), leave type and whether a public holiday was worked. Office staff work Monday to Friday; shift staff work five days around their two weekly off days.'),
(N'HR-PAYROLL', N'PayElements', N'Pay elements', N'عناصر الراتب', N'Earnings and deductions that appear on payslip lines.'),
(N'HR-PAYROLL', N'PayrollPeriods', N'Payroll periods', N'فترات الرواتب', N'Monthly payroll periods of 2025 (YYYY-MM) with pay date, number of employees paid and total gross, deductions and net pay in AED.'),
(N'HR-PAYROLL', N'Payslips', N'Payslips', N'قسائم الرواتب', N'Monthly payslip per employee in 2025 (AED): prorated basic and allowances, overtime hours and pay (125 % of the hourly basic), absence deduction, pension (UAE nationals), salary-advance repayment, gross, total deductions and net pay.'),
(N'HR-PAYROLL', N'PayslipLines', N'Payslip lines', N'بنود قسائم الرواتب', N'Payslip broken down by pay element (one row per non-zero earning or deduction).'),
(N'HR-PAYROLL', N'EmployeePortalSignIns', N'Employee portal sign-ins', N'عمليات الدخول إلى بوابة الموظفين', N'Every sign-in attempt to the employee self-service portal by office staff in December 2025: account, device (LT = work laptop, MB = registered phone, UNK = unregistered device), device type, channel, IP address, city, local time and whether it succeeded.'),
(N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'Employee portal sign-ins (live)', N'عمليات الدخول إلى بوابة الموظفين (مباشر)', N'The same sign-ins with dates moved forward so that 31 December 2025 is today. Used by Monitoring Rules that look at the last 24 hours.'),
-- Procurement and stores
(N'PROCUREMENT', N'Calendar', N'Calendar 2025', N'التقويم 2025', N'One row per day of 2025 with month, week, weekday (1 = Monday), UAE weekend and public holidays.'),
(N'PROCUREMENT', N'CostCenters', N'Cost centres', N'مراكز التكلفة', N'Cost centre of each department (department codes match the HR application).'),
(N'PROCUREMENT', N'ItemCategories', N'Item categories', N'فئات الأصناف', N'Purchasing categories (aircraft spares and consumables, ground equipment parts, fuels and lubricants, uniforms, catering, IT, office, cleaning, safety, facilities) with default warehouse and lead time.'),
(N'PROCUREMENT', N'UnitsOfMeasure', N'Units of measure', N'وحدات القياس', N'Units in which items are bought and issued.'),
(N'PROCUREMENT', N'Warehouses', N'Warehouses (stores)', N'المستودعات (المخازن)', N'Stores: main store, line-maintenance store, ground-equipment store, uniform store and IT store.'),
(N'PROCUREMENT', N'Suppliers', N'Suppliers', N'الموردون', N'Suppliers with main category, country, payment terms in days and rating (A preferred, B, C under review). Ratings reflect delivery performance.'),
(N'PROCUREMENT', N'Items', N'Items', N'الأصناف', N'Catalogue items with category, unit, standard cost in AED, reorder level and quantity, lead time, default warehouse, preferred supplier and whether the item is a rotable aircraft component.'),
(N'PROCUREMENT', N'PurchaseRequisitions', N'Purchase requisitions', N'طلبات الشراء', N'Requests to buy raised by departments in 2025: requester, department and section, category, priority (Routine, Urgent, AOG = aircraft on ground), need-by date, status (Submitted, Approved, Rejected, Converted to a purchase order) and estimated amount in AED.'),
(N'PROCUREMENT', N'PurchaseRequisitionLines', N'Purchase requisition lines', N'بنود طلبات الشراء', N'Items and quantities requested on each requisition with estimated unit cost.'),
(N'PROCUREMENT', N'PurchaseOrders', N'Purchase orders', N'أوامر الشراء', N'Purchase orders issued in 2025 (one per converted requisition): supplier, buyer, priority, expected delivery date, status (Open, Partially received, Received, Closed, Cancelled), subtotal, 5 % VAT and total in AED.'),
(N'PROCUREMENT', N'PurchaseOrderLines', N'Purchase order lines', N'بنود أوامر الشراء', N'Ordered items with quantity, unit price, line amount, quantity received (accepted) and receiving warehouse.'),
(N'PROCUREMENT', N'GoodsReceipts', N'Goods receipts', N'استلام البضائع', N'Deliveries received against purchase orders: receipt date, warehouse, receiver, delivery sequence (2 = balance of a partial delivery) and days late against the expected delivery date.'),
(N'PROCUREMENT', N'GoodsReceiptLines', N'Goods receipt lines', N'بنود استلام البضائع', N'Received, accepted and rejected quantities per item with the rejection reason.'),
(N'PROCUREMENT', N'SupplierInvoices', N'Supplier invoices', N'فواتير الموردين', N'One invoice per purchase order after delivery: invoice and due dates, net, VAT and total in AED, status (Pending, Approved, Paid, Disputed), paid date and days to pay.'),
(N'PROCUREMENT', N'StoreIssues', N'Store issues', N'صرف المخازن', N'Stock issued from a warehouse to a department in 2025 with requester, storekeeper, department, section and cost centre.'),
(N'PROCUREMENT', N'StoreIssueLines', N'Store issue lines', N'بنود صرف المخازن', N'Items, quantities and value (at standard cost, AED) on each store issue.'),
(N'PROCUREMENT', N'StockMovements', N'Stock movements', N'حركات المخزون', N'Stock ledger for 2025: opening balance on 1 January, goods receipts (+) and store issues (-) per warehouse and item with value in AED and the consuming department.'),
(N'PROCUREMENT', N'StockBalances', N'Stock balances', N'أرصدة المخزون', N'Quantity on hand and value per warehouse and item as of 31 December 2025, reorder level, below-reorder flag, last receipt and issue dates and bin location.');

CREATE TABLE #ColumnDocs (AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT, ColumnName nvarchar(128) COLLATE DATABASE_DEFAULT,
                          DisplayEn nvarchar(200) COLLATE DATABASE_DEFAULT NULL, DisplayAr nvarchar(200) COLLATE DATABASE_DEFAULT NULL, Description nvarchar(2000) COLLATE DATABASE_DEFAULT NULL,
                          Classification nvarchar(20) COLLATE DATABASE_DEFAULT NULL);
INSERT INTO #ColumnDocs VALUES
(N'FLIGHT-OPS', N'Flights', N'Direction', N'Direction', N'الاتجاه', N'Departure = leaving Sharjah; Arrival = landing at Sharjah.', NULL),
(N'FLIGHT-OPS', N'Flights', N'RouteAirportCode', N'Route airport', N'مطار الوجهة أو المنشأ', N'The airport at the other end: destination of a departure, origin of an arrival.', NULL),
(N'FLIGHT-OPS', N'Flights', N'ScheduledTime', N'Scheduled time', N'الموعد المجدول', N'Scheduled departure or arrival, local Sharjah time.', NULL),
(N'FLIGHT-OPS', N'Flights', N'ActualTime', N'Actual time', N'الموعد الفعلي', N'Actual departure or arrival, local time; NULL when cancelled.', NULL),
(N'FLIGHT-OPS', N'Flights', N'DelayMinutes', N'Delay (minutes)', N'التأخير (دقائق)', N'Minutes after the scheduled time; NULL when cancelled. A flight is Delayed when this exceeds 15.', NULL),
(N'FLIGHT-OPS', N'Flights', N'Status', N'Status', N'الحالة', N'On Time, Delayed (more than 15 minutes late), Cancelled or Diverted.', NULL),
(N'FLIGHT-OPS', N'Flights', N'DelayCode', N'Delay code', N'رمز التأخير', N'IATA delay code of the main cause (see Delay reasons); NULL for on-time flights.', NULL),
(N'FLIGHT-OPS', N'Flights', N'Passengers', N'Passengers', N'الركاب', N'Total passengers on board (adults + children + infants); 0 when cancelled.', NULL),
(N'FLIGHT-OPS', N'Flights', N'LoadFactorPercent', N'Load factor (%)', N'معامل الإشغال (%)', N'Passengers as a percentage of seats.', NULL),
(N'FLIGHT-OPS', N'Flights', N'CargoKg', N'Cargo (kg)', N'الشحن (كغ)', N'Belly cargo carried, kilograms.', NULL),
(N'FLIGHT-OPS', N'Flights', N'HourOfDay', N'Hour of day', N'ساعة اليوم', N'Scheduled hour, local time (0-23).', NULL),
(N'FLIGHT-OPS', N'Flights', N'DayOfWeekNumber', N'Weekday number', N'رقم يوم الأسبوع', N'1 = Monday ... 7 = Sunday.', NULL),
(N'FLIGHT-OPS', N'FlightSchedules', N'OperatingDays', N'Operating days', N'أيام التشغيل', N'Digits of the weekdays the flight operates, 1 = Monday ... 7 = Sunday (1234567 = daily).', NULL),
(N'FLIGHT-OPS', N'FlightSchedules', N'Season', N'Season', N'الموسم', N'Year-round, Summer peak, Year-end peak, Eid al-Fitr or Eid al-Adha extra flights.', NULL),
(N'FLIGHT-OPS', N'Calendar', N'DayOfWeekNumber', N'Weekday number', N'رقم يوم الأسبوع', N'1 = Monday ... 7 = Sunday.', NULL),
(N'HR-PAYROLL', N'Employees', N'EmployeeNumber', N'Employee number', N'الرقم الوظيفي', N'Unique employee number, also used by the Wabi Insight user accounts.', NULL),
(N'HR-PAYROLL', N'Employees', N'EmploymentStatus', N'Employment status', N'حالة التوظيف', N'Active, Resigned or Terminated, as of 31 December 2025.', NULL),
(N'HR-PAYROLL', N'Employees', N'DateOfBirth', N'Date of birth', N'تاريخ الميلاد', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Employees', N'NationalIdNumber', N'Emirates ID', N'رقم الهوية الإماراتية', NULL, N'Restricted'),
(N'HR-PAYROLL', N'Employees', N'PassportNumber', N'Passport number', N'رقم جواز السفر', NULL, N'Restricted'),
(N'HR-PAYROLL', N'Employees', N'MobileNumber', N'Mobile number', N'رقم الهاتف المتحرك', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Employees', N'WeeklyOffDays', N'Weekly off days', N'أيام الراحة الأسبوعية', N'Two weekday digits, 1 = Monday ... 7 = Sunday.', NULL),
(N'HR-PAYROLL', N'EmployeeBankAccounts', N'IBAN', N'IBAN', N'رقم الحساب الدولي', NULL, N'Restricted'),
(N'HR-PAYROLL', N'EmployeeContracts', N'BasicSalary', N'Basic salary (AED/month)', N'الراتب الأساسي (درهم/شهر)', NULL, N'Confidential'),
(N'HR-PAYROLL', N'EmployeeContracts', N'HousingAllowance', N'Housing allowance', N'بدل السكن', NULL, N'Confidential'),
(N'HR-PAYROLL', N'EmployeeContracts', N'TransportAllowance', N'Transport allowance', N'بدل المواصلات', NULL, N'Confidential'),
(N'HR-PAYROLL', N'EmployeeContracts', N'OtherAllowance', N'Other allowances', N'بدلات أخرى', NULL, N'Confidential'),
(N'HR-PAYROLL', N'EmployeeContracts', N'TotalMonthlyPackage', N'Total monthly package', N'إجمالي الحزمة الشهرية', N'Basic + housing + transport + other, AED per month.', N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'BasicSalary', N'Basic paid', N'الأساسي المدفوع', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'HousingAllowance', N'Housing paid', N'السكن المدفوع', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'TransportAllowance', N'Transport paid', N'المواصلات المدفوعة', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'OtherAllowance', N'Other allowances paid', N'البدلات الأخرى المدفوعة', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'OvertimePay', N'Overtime pay', N'أجر العمل الإضافي', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'GrossPay', N'Gross pay', N'إجمالي الراتب', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'AbsenceDeduction', N'Absence deduction', N'خصم الغياب', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'PensionDeduction', N'Pension deduction', N'خصم التقاعد', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'LoanDeduction', N'Salary advance repayment', N'سداد السلفة', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'TotalDeductions', N'Total deductions', N'إجمالي الخصومات', NULL, N'Confidential'),
(N'HR-PAYROLL', N'Payslips', N'NetPay', N'Net pay', N'صافي الراتب', NULL, N'Confidential'),
(N'HR-PAYROLL', N'PayslipLines', N'Amount', N'Amount (AED)', N'المبلغ (درهم)', N'Always positive; PayElements.ElementType tells whether it is an earning or a deduction.', N'Confidential'),
(N'HR-PAYROLL', N'PayrollPeriods', N'TotalGrossPay', N'Total gross pay', N'إجمالي الرواتب', NULL, N'Confidential'),
(N'HR-PAYROLL', N'PayrollPeriods', N'TotalDeductions', N'Total deductions', N'إجمالي الخصومات', NULL, N'Confidential'),
(N'HR-PAYROLL', N'PayrollPeriods', N'TotalNetPay', N'Total net pay', N'صافي الرواتب', NULL, N'Confidential'),
(N'HR-PAYROLL', N'PerformanceReviews', N'Rating', N'Rating', N'التقييم', N'1 = unsatisfactory ... 5 = outstanding.', N'Confidential'),
(N'HR-PAYROLL', N'PerformanceReviews', N'RatingLabel', N'Rating label', N'وصف التقييم', NULL, N'Confidential'),
(N'HR-PAYROLL', N'AttendanceRecords', N'Status', N'Attendance status', N'حالة الحضور', N'Present, Late (checked in after the scheduled start), Absent (no show, unpaid) or On Leave (approved leave).', NULL),
(N'HR-PAYROLL', N'AttendanceRecords', N'OvertimeMinutes', N'Overtime (minutes)', N'العمل الإضافي (دقائق)', N'Minutes worked after the scheduled end of the shift.', NULL),
(N'HR-PAYROLL', N'AttendanceRecords', N'LateMinutes', N'Late (minutes)', N'التأخير (دقائق)', N'Minutes after the scheduled start at check-in.', NULL),
(N'HR-PAYROLL', N'EmployeePortalSignIns', N'AccountName', N'Portal account', N'حساب البوابة', N'Sign-in account, GAS\<employee number>.', NULL),
(N'HR-PAYROLL', N'EmployeePortalSignIns', N'DeviceId', N'Device', N'الجهاز', N'LT-… work laptop, MB-… registered phone, UNK-… a device the company does not know.', NULL),
(N'HR-PAYROLL', N'EmployeePortalSignIns', N'IpAddress', N'IP address', N'عنوان IP', NULL, N'Confidential'),
(N'HR-PAYROLL', N'EmployeePortalSignIns', N'LoginAt', N'Signed in at', N'وقت الدخول', N'Local time (Asia/Dubai).', NULL),
(N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'AccountName', N'Portal account', N'حساب البوابة', N'Sign-in account, GAS\<employee number>.', NULL),
(N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'DeviceId', N'Device', N'الجهاز', N'LT-… work laptop, MB-… registered phone, UNK-… a device the company does not know.', NULL),
(N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'IpAddress', N'IP address', N'عنوان IP', NULL, N'Confidential'),
(N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'LoginAt', N'Signed in at', N'وقت الدخول', N'Local time, shifted so the last day is today.', NULL),
(N'PROCUREMENT', N'PurchaseRequisitions', N'Priority', N'Priority', N'الأولوية', N'Routine, Urgent or AOG (aircraft on ground: an aircraft cannot fly until the part arrives).', NULL),
(N'PROCUREMENT', N'PurchaseRequisitions', N'Status', N'Status', N'الحالة', N'Submitted, Approved, Rejected or Converted (a purchase order was issued).', NULL),
(N'PROCUREMENT', N'PurchaseOrders', N'Status', N'Status', N'الحالة', N'Open (nothing received), Partially received, Received, Closed (received and invoice paid) or Cancelled.', NULL),
(N'PROCUREMENT', N'GoodsReceipts', N'DaysLate', N'Days late', N'أيام التأخير', N'Days after the expected delivery date of the purchase order; 0 when on time or early.', NULL),
(N'PROCUREMENT', N'Suppliers', N'Rating', N'Supplier rating', N'تصنيف المورد', N'A = preferred, B = acceptable, C = under review.', NULL),
(N'PROCUREMENT', N'StockMovements', N'Quantity', N'Quantity', N'الكمية', N'Positive for opening balance and goods receipts, negative for store issues.', NULL),
(N'PROCUREMENT', N'StockBalances', N'BelowReorderLevel', N'Below reorder level', N'أقل من حد إعادة الطلب', N'1 when the quantity on hand is below the item''s reorder level.', NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    -- ------------------------------------------------------------------------------------------ organization, roles, built-in app
    SELECT @OrgId = OrganizationId FROM dbo.Organizations WHERE OrganizationCode = @OrgCode;
    IF @OrgId IS NULL
    BEGIN
        SET @OrgId = NEWID();
        INSERT INTO dbo.Organizations (OrganizationId, OrganizationCode, OrganizationName, OrganizationNameAr, Description, ContactEmail, ContactPhone, CountryCode, TimeZone,
                                       DefaultLanguage, Status, CreatedAt, RequireSecondAdminApprovalForSupportAccess)
        VALUES (@OrgId, @OrgCode, @OrgName, @OrgNameAr, N'Sample organization (fictional): airport services company at Sharjah International Airport with flight operations, HR and procurement sources.',
                @AdminEmail, N'+971 6 000 0000', N'AE', N'Asia/Dubai', N'en', N'Active', @Now, 0);
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
    SELECT NEWID(), @OrgId, v.RoleCode, v.NameEn, v.NameAr, v.Description, 0, 1, NULL, @Now
    FROM (VALUES
        (N'OperationsAnalyst', N'Operations Analyst', N'محلل العمليات', N'Flight operations and procurement data (all rows); own attendance, leave and payslips.'),
        (N'HRPayrollOfficer', N'HR and Payroll Officer', N'مسؤول الموارد البشرية والرواتب', N'HR, payroll and attendance (all rows, without Restricted personal identifiers); own department''s purchases and store issues.'),
        (N'CyberSecurity', N'Cyber Security', N'الأمن السيبراني', N'Employee-portal sign-ins (all rows); receives the alerts of the sign-in Monitoring Rules.')) v(RoleCode, NameEn, NameAr, Description)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Roles r WHERE r.OrganizationId = @OrgId AND r.RoleCode = v.RoleCode);

    INSERT INTO dbo.Applications (ApplicationId, OrganizationId, ApplicationName, ApplicationNameAr, ApplicationCode, Description, DatabaseType, IsBuiltIn, BuiltInCode, IsActive, IsApproved, CreatedAt)
    SELECT NEWID(), @OrgId, N'Platform Insights', N'تحليلات المنصة', N'WI-INSIGHTS', N'Built-in, tenant-scoped usage analytics of Wabi Insight for this organization.', N'PlatformInsights', 1, N'WI-INSIGHTS', 1, 1, @Now
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Applications a WHERE a.OrganizationId = @OrgId AND a.BuiltInCode = N'WI-INSIGHTS');

    -- ------------------------------------------------------------------------------------------ departments and sections
    INSERT INTO dbo.Departments (DepartmentId, OrganizationId, DepartmentCode, DepartmentNameEn, DepartmentNameAr, IsActive, CreatedAt)
    SELECT NEWID(), @OrgId, d.DepartmentCode, d.NameEn, d.NameAr, 1, @Now FROM #Depts d
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Departments x WHERE x.OrganizationId = @OrgId AND x.DepartmentCode = d.DepartmentCode);
    INSERT INTO dbo.Sections (SectionId, OrganizationId, DepartmentId, SectionCode, SectionNameEn, SectionNameAr, IsActive, CreatedAt)
    SELECT NEWID(), @OrgId, d.DepartmentId, s.SectionCode, s.NameEn, s.NameAr, 1, @Now
    FROM #Sections s JOIN dbo.Departments d ON d.OrganizationId = @OrgId AND d.DepartmentCode = s.DepartmentCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Sections x WHERE x.OrganizationId = @OrgId AND x.DepartmentId = d.DepartmentId AND x.SectionCode = s.SectionCode);

    -- ------------------------------------------------------------------------------------------ users
    IF EXISTS (SELECT 1 FROM #Users u JOIN dbo.LoginIdentities li ON li.NormalizedEmail = UPPER(LTRIM(RTRIM(u.Email)))
               WHERE li.OrganizationId IS NULL OR li.OrganizationId <> @OrgId)
        THROW 50004, N'One of the sample e-mail addresses already belongs to another account; change the e-mail variables at the top of this script.', 1;
    UPDATE u SET UserId = ou.UserId FROM #Users u JOIN dbo.OrganizationUsers ou ON ou.OrganizationId = @OrgId AND ou.NormalizedEmail = UPPER(LTRIM(RTRIM(u.Email)));
    UPDATE #Users SET UserId = NEWID() WHERE UserId IS NULL AND NOT EXISTS (SELECT 1 FROM dbo.OrganizationUsers ou WHERE ou.OrganizationId = @OrgId AND ou.EmployeeNumber = #Users.EmployeeNumber);
    UPDATE u SET UserId = ou.UserId FROM #Users u JOIN dbo.OrganizationUsers ou ON ou.OrganizationId = @OrgId AND ou.EmployeeNumber = u.EmployeeNumber WHERE u.UserId IS NULL;
    INSERT INTO dbo.OrganizationUsers (UserId, OrganizationId, Email, NormalizedEmail, FullName, EmployeeNumber, PhoneNumber, DepartmentId, SectionId, PreferredLanguage, Status,
                                       MustChangePassword, PasswordHash, SecurityStamp, FailedLoginCount, CreatedAt)
    SELECT u.UserId, @OrgId, LTRIM(RTRIM(u.Email)), UPPER(LTRIM(RTRIM(u.Email))), u.FullName, u.EmployeeNumber, u.MobileNumber, d.DepartmentId, s.SectionId, u.PreferredLanguage, N'Active',
           1, @InitialPasswordHash, CONVERT(nvarchar(64), CRYPT_GEN_RANDOM(20), 2), 0, @Now
    FROM #Users u
    JOIN dbo.Departments d ON d.OrganizationId = @OrgId AND d.DepartmentCode = u.DepartmentCode
    JOIN dbo.Sections s ON s.OrganizationId = @OrgId AND s.DepartmentId = d.DepartmentId AND s.SectionCode = u.SectionCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.OrganizationUsers ou WHERE ou.UserId = u.UserId);
    INSERT INTO dbo.LoginIdentities (NormalizedEmail, PrincipalType, PrincipalId, OrganizationId, CreatedAt)
    SELECT UPPER(LTRIM(RTRIM(u.Email))), N'OrganizationUser', u.UserId, @OrgId, @Now FROM #Users u
    WHERE NOT EXISTS (SELECT 1 FROM dbo.LoginIdentities li WHERE li.NormalizedEmail = UPPER(LTRIM(RTRIM(u.Email))));
    INSERT INTO dbo.UserRoles (UserId, RoleId, OrganizationId, AssignedAt)
    SELECT u.UserId, r.RoleId, @OrgId, @Now FROM #Users u JOIN dbo.Roles r ON r.OrganizationId = @OrgId AND r.RoleCode = u.RoleCode
    WHERE NOT EXISTS (SELECT 1 FROM dbo.UserRoles x WHERE x.UserId = u.UserId AND x.RoleId = r.RoleId);
    SELECT @AdminUserId = UserId FROM #Users WHERE RoleCode = N'OrganizationAdmin';
    -- The IT manager also belongs to Cyber Security (sign-in data only), so the sign-in alerts reach a person who may read them.
    INSERT INTO dbo.UserRoles (UserId, RoleId, OrganizationId, AssignedAt)
    SELECT @AdminUserId, r.RoleId, @OrgId, @Now FROM dbo.Roles r
    WHERE r.OrganizationId = @OrgId AND r.RoleCode = N'CyberSecurity' AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles x WHERE x.UserId = @AdminUserId AND x.RoleId = r.RoleId);

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

    -- Field roles (Phase 14): what Monitoring Rule templates bind to. Values follow ColumnSemanticRole (1 Identifier, 2 Person,
    -- 3 Account, 4 Device, 5 Date/time, 6 Amount, 7 Quantity, 8 Status, 9 Location). An administrator's later choice is kept.
    UPDATE c SET SemanticRole = v.SemanticRole, UpdatedAt = @Now
    FROM (VALUES
        (N'HR-PAYROLL', N'EmployeePortalSignIns', N'AccountName', 3), (N'HR-PAYROLL', N'EmployeePortalSignIns', N'EmployeeNumber', 2),
        (N'HR-PAYROLL', N'EmployeePortalSignIns', N'DeviceId', 4), (N'HR-PAYROLL', N'EmployeePortalSignIns', N'IpAddress', 4),
        (N'HR-PAYROLL', N'EmployeePortalSignIns', N'City', 9), (N'HR-PAYROLL', N'EmployeePortalSignIns', N'LoginAt', 5),
        (N'HR-PAYROLL', N'EmployeePortalSignIns', N'Succeeded', 8),
        (N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'AccountName', 3), (N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'EmployeeNumber', 2),
        (N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'DeviceId', 4), (N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'IpAddress', 4),
        (N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'City', 9), (N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'LoginAt', 5),
        (N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'Succeeded', 8),
        (N'HR-PAYROLL', N'Employees', N'EmployeeNumber', 2), (N'HR-PAYROLL', N'EmployeeBankAccounts', N'IBAN', 3),
        (N'HR-PAYROLL', N'Payslips', N'EmployeeNumber', 2), (N'HR-PAYROLL', N'Payslips', N'NetPay', 6), (N'HR-PAYROLL', N'Payslips', N'GrossPay', 6),
        (N'HR-PAYROLL', N'AttendanceRecords', N'EmployeeNumber', 2), (N'HR-PAYROLL', N'AttendanceRecords', N'Status', 8),
        (N'FLIGHT-OPS', N'Flights', N'ScheduledTime', 5), (N'FLIGHT-OPS', N'Flights', N'Passengers', 7), (N'FLIGHT-OPS', N'Flights', N'Status', 8),
        (N'PROCUREMENT', N'SupplierInvoices', N'TotalAmount', 6), (N'PROCUREMENT', N'SupplierInvoices', N'InvoiceDate', 5)) v(AppCode, ObjectName, ColumnName, SemanticRole)
    JOIN #Apps a ON a.AppCode = v.AppCode
    JOIN dbo.DatabaseObjects o ON o.ApplicationId = a.ApplicationId AND o.SchemaName = N'dbo' AND o.ObjectName = v.ObjectName
    JOIN dbo.DatabaseColumns c ON c.ObjectId = o.ObjectId AND c.ColumnName = v.ColumnName
    WHERE c.SemanticRole IS NULL;

    -- ------------------------------------------------------------------------------------------ row-scope mappings
    -- SELF compares the employee column with the user's employee number, DEPARTMENT / SECTION with the user's department / section code.
    INSERT INTO dbo.RowScopeColumnMappings (ObjectId, OrganizationId, EmployeeColumnId, DepartmentColumnId, SectionColumnId, UpdatedAt, UpdatedByUserId)
    SELECT o.ObjectId, @OrgId,
           (SELECT c.ColumnId FROM dbo.DatabaseColumns c WHERE c.ObjectId = o.ObjectId AND c.ColumnName = m.EmployeeColumn),
           (SELECT c.ColumnId FROM dbo.DatabaseColumns c WHERE c.ObjectId = o.ObjectId AND c.ColumnName = m.DepartmentColumn),
           (SELECT c.ColumnId FROM dbo.DatabaseColumns c WHERE c.ObjectId = o.ObjectId AND c.ColumnName = m.SectionColumn), @Now, @AdminUserId
    FROM (VALUES
        (N'HR-PAYROLL', N'Employees', N'EmployeeNumber', N'DepartmentCode', N'SectionCode'), (N'HR-PAYROLL', N'EmployeeContracts', N'EmployeeNumber', NULL, NULL),
        (N'HR-PAYROLL', N'EmployeeBankAccounts', N'EmployeeNumber', NULL, NULL), (N'HR-PAYROLL', N'PerformanceReviews', N'EmployeeNumber', N'DepartmentCode', NULL),
        (N'HR-PAYROLL', N'LeaveRequests', N'EmployeeNumber', N'DepartmentCode', N'SectionCode'), (N'HR-PAYROLL', N'AttendanceRecords', N'EmployeeNumber', N'DepartmentCode', N'SectionCode'),
        (N'HR-PAYROLL', N'Payslips', N'EmployeeNumber', N'DepartmentCode', N'SectionCode'), (N'HR-PAYROLL', N'PayslipLines', N'EmployeeNumber', NULL, NULL),
        (N'PROCUREMENT', N'PurchaseRequisitions', N'RequestedByEmployeeNumber', N'DepartmentCode', N'SectionCode'),
        (N'PROCUREMENT', N'StoreIssues', N'RequestedByEmployeeNumber', N'DepartmentCode', N'SectionCode'),
        (N'PROCUREMENT', N'PurchaseOrders', NULL, N'DepartmentCode', NULL), (N'PROCUREMENT', N'StockMovements', NULL, N'DepartmentCode', NULL),
        (N'PROCUREMENT', N'CostCenters', NULL, N'DepartmentCode', NULL)) m(AppCode, ObjectName, EmployeeColumn, DepartmentColumn, SectionColumn)
    JOIN #Apps a ON a.AppCode = m.AppCode
    JOIN dbo.DatabaseObjects o ON o.ApplicationId = a.ApplicationId AND o.SchemaName = N'dbo' AND o.ObjectName = m.ObjectName
    WHERE NOT EXISTS (SELECT 1 FROM dbo.RowScopeColumnMappings x WHERE x.ObjectId = o.ObjectId);

    -- ------------------------------------------------------------------------------------------ role permissions
    CREATE TABLE #Grants (RoleCode nvarchar(64) COLLATE DATABASE_DEFAULT, AppCode nvarchar(64) COLLATE DATABASE_DEFAULT, ObjectName nvarchar(128) COLLATE DATABASE_DEFAULT NULL,
                          Scope nvarchar(20) COLLATE DATABASE_DEFAULT, ExcludeRestricted bit);
    INSERT INTO #Grants VALUES
        (N'OperationsAnalyst', N'FLIGHT-OPS', NULL, N'All', 0),              -- every flight-operations table
        (N'OperationsAnalyst', N'PROCUREMENT', NULL, N'All', 0),             -- every procurement table
        (N'OperationsAnalyst', N'HR-PAYROLL', N'AttendanceRecords', N'Self', 0), (N'OperationsAnalyst', N'HR-PAYROLL', N'LeaveRequests', N'Self', 0),
        (N'OperationsAnalyst', N'HR-PAYROLL', N'Payslips', N'Self', 0), (N'OperationsAnalyst', N'HR-PAYROLL', N'PayslipLines', N'Self', 0),
        (N'OperationsAnalyst', N'HR-PAYROLL', N'LeaveTypes', N'All', 0), (N'OperationsAnalyst', N'HR-PAYROLL', N'PayElements', N'All', 0),
        (N'OperationsAnalyst', N'HR-PAYROLL', N'Shifts', N'All', 0), (N'OperationsAnalyst', N'HR-PAYROLL', N'PayrollPeriods', N'All', 0),
        (N'HRPayrollOfficer', N'HR-PAYROLL', NULL, N'All', 1),               -- every HR table, Restricted columns withheld
        (N'HRPayrollOfficer', N'PROCUREMENT', N'PurchaseRequisitions', N'Department', 0), (N'HRPayrollOfficer', N'PROCUREMENT', N'StoreIssues', N'Department', 0),
        (N'HRPayrollOfficer', N'PROCUREMENT', N'Items', N'All', 0), (N'HRPayrollOfficer', N'PROCUREMENT', N'ItemCategories', N'All', 0),
        (N'HRPayrollOfficer', N'PROCUREMENT', N'Warehouses', N'All', 0), (N'HRPayrollOfficer', N'PROCUREMENT', N'CostCenters', N'All', 0),
        (N'CyberSecurity', N'HR-PAYROLL', N'EmployeePortalSignIns', N'All', 0), (N'CyberSecurity', N'HR-PAYROLL', N'vw_EmployeePortalSignInsLive', N'All', 0);
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

    -- ------------------------------------------------------------------------------------------ monitoring rule (Phase 14)
    -- The owner's example: the same portal account used from more than one device within 24 hours, daily at 06:00 (organization
    -- time), High, to the Cyber Security role. It runs with the owner's (E1002, Management) permissions; the definition binds the
    -- catalogued columns by id, exactly as the rule editor saves it.
    DECLARE @SignIns uniqueidentifier, @Account uniqueidentifier, @Device uniqueidentifier, @LoginAt uniqueidentifier, @RuleId uniqueidentifier,
            @OwnerId uniqueidentifier = (SELECT UserId FROM #Users WHERE EmployeeNumber = N'E1002'),
            @CyberRoleId uniqueidentifier = (SELECT RoleId FROM dbo.Roles WHERE OrganizationId = @OrgId AND RoleCode = N'CyberSecurity');
    SELECT @SignIns = o.ObjectId FROM dbo.DatabaseObjects o JOIN #Apps a ON a.ApplicationId = o.ApplicationId
    WHERE a.AppCode = N'HR-PAYROLL' AND o.SchemaName = N'dbo' AND o.ObjectName = N'vw_EmployeePortalSignInsLive';
    SELECT @Account = ColumnId FROM dbo.DatabaseColumns WHERE ObjectId = @SignIns AND ColumnName = N'AccountName';
    SELECT @Device = ColumnId FROM dbo.DatabaseColumns WHERE ObjectId = @SignIns AND ColumnName = N'DeviceId';
    SELECT @LoginAt = ColumnId FROM dbo.DatabaseColumns WHERE ObjectId = @SignIns AND ColumnName = N'LoginAt';
    IF @SignIns IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.MonitoringRules WHERE OrganizationId = @OrgId AND Name = N'Same account, several devices (employee portal)')
    BEGIN
        SET @RuleId = NEWID();
        INSERT INTO dbo.MonitoringRules (RuleId, OrganizationId, OwnerUserId, CreatedByUserId, Name, Description, Kind, TemplateCode, DefinitionJson, Severity,
                                         RecurrenceKind, IntervalMinutes, TimeOfDay, DayOfWeek, CooldownHours, IsActive, CreatedAt)
        VALUES (@RuleId, @OrgId, @OwnerId, @AdminUserId, N'Same account, several devices (employee portal)',
                N'A portal account signed in from more than one device within 24 hours. For the Cyber Security team to review; nobody is suspended automatically.',
                N'Data', N'multi_device_same_account',
                CONCAT(N'{"data":{"objectId":"', LOWER(CONVERT(nvarchar(36), @SignIns)), N'","timeColumnId":"', LOWER(CONVERT(nvarchar(36), @LoginAt)),
                       N'","windowHours":24,"groupByColumnIds":["', LOWER(CONVERT(nvarchar(36), @Account)), N'"],"measure":{"aggregate":"count_distinct","columnId":"',
                       LOWER(CONVERT(nvarchar(36), @Device)), N'"},"filters":[],"condition":{"operator":"gt","value":1},"comparison":"none","maxGroups":200}}'),
                N'High', N'Daily', 0, '06:00', NULL, 24, 1, @Now);
        INSERT INTO dbo.MonitoringRuleRecipients (RecipientId, OrganizationId, RuleId, UserId, RoleId) VALUES (NEWID(), @OrgId, @RuleId, NULL, @CyberRoleId);
        -- The API's scheduler creates the rule's job on its next tick (one minute at most).
    END;

    -- ------------------------------------------------------------------------------------------ audit trail
    INSERT INTO dbo.AuditLogs (AuditLogId, OrganizationId, ActorType, ActorId, ActorDisplayName, EventType, EntityType, EntityId, DetailsJson, CreatedAt)
    VALUES (NEWID(), @OrgId, N'PlatformAdministrator', NULL, N'Sample data seed (04-seed-wabiinsightdb.sql)', N'SampleData.Seeded', N'Organization', CONVERT(nvarchar(64), @OrgId),
            CONCAT(N'{"organization":"', @OrgCode, N'","applications":["FLIGHT-OPS","HR-PAYROLL","PROCUREMENT"],"users":4,"monitoringRules":1}'), @Now);

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
SELECT u.EmployeeNumber, ou.FullName, ou.Email, r.RoleCode, d.DepartmentCode, s.SectionCode, CASE WHEN ou.MustChangePassword = 1 THEN N'Wabi#Sample#2026 (change at first sign-in)' ELSE N'(already changed)' END AS Password
FROM #Users u JOIN dbo.OrganizationUsers ou ON ou.UserId = u.UserId
JOIN dbo.UserRoles ur ON ur.UserId = ou.UserId JOIN dbo.Roles r ON r.RoleId = ur.RoleId
LEFT JOIN dbo.Departments d ON d.DepartmentId = ou.DepartmentId LEFT JOIN dbo.Sections s ON s.SectionId = ou.SectionId
ORDER BY u.EmployeeNumber;
DECLARE @Server nvarchar(128) = CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));
PRINT N'';
PRINT N'Next: sign in as ' + @AdminEmail + N' (password Wabi#Sample#2026, change it), open Organization Administration > Applications and for each application';
PRINT N'"Replace connection", "Test connection", then "Approve". Read-only connection strings (use the address the API reaches this server at, and the passwords you set):';
SELECT a.AppCode AS ApplicationCode,
       CONCAT(N'Server=', @Server, N';Database=', a.DbName, N';User Id=', a.ReaderLogin, N';Password=', a.ReaderPassword, N';Encrypt=True;TrustServerCertificate=True;') AS ReadOnlyConnectionString
FROM #Apps a ORDER BY a.AppCode;
PRINT N'';
PRINT N'Monitoring: the rule "Same account, several devices (employee portal)" runs daily at 06:00 (Asia/Dubai) once HR-PAYROLL is connected and approved.';
PRINT N'Open Monitoring Rules and choose "Test now" to see the four planted accounts at once; alerts reach E1002 and the Cyber Security role (E1001).';
GO
