"""HumanResourcesDB: employees, contracts, payroll, attendance, leave and performance for Gulf Aviation Services, 2025."""
import datetime as dt
from build_common import insert, prologue, epilogue, drop_tables, calendar_table, UAE_HOLIDAYS_2025, rng
from build_people import DEPARTMENTS, SECTIONS, GRADES, POSITIONS, NATIONALITIES, SHIFTS, staff
from build_signins import portal_signins

DB, OWNER, READER = "HumanResourcesDB", "hr_owner", "hr_reader"

LEAVE_TYPES = [("ANNUAL", "Annual leave", "إجازة سنوية", 1, 30), ("SICK", "Sick leave", "إجازة مرضية", 1, 15),
               ("EMERGENCY", "Emergency leave", "إجازة طارئة", 1, 5), ("MATERNITY", "Maternity leave", "إجازة أمومة", 1, 60),
               ("UNPAID", "Unpaid leave", "إجازة بدون راتب", 0, 0)]
PAY_ELEMENTS = [("BASIC", "Basic salary", "الراتب الأساسي", "Earning"), ("HOUSING", "Housing allowance", "بدل السكن", "Earning"),
                ("TRANSPORT", "Transport allowance", "بدل المواصلات", "Earning"), ("OTHER", "Other allowances (incl. shift)", "بدلات أخرى (تشمل بدل الورديات)", "Earning"),
                ("OVERTIME", "Overtime pay", "أجر العمل الإضافي", "Earning"), ("ABSENCE", "Unpaid absence deduction", "خصم الغياب", "Deduction"),
                ("PENSION", "Pension contribution (UAE nationals)", "اشتراك التقاعد (للمواطنين)", "Deduction"), ("LOAN", "Salary advance repayment", "سداد سلفة الراتب", "Deduction")]


def pay_date(year, month):
    d = dt.date(year, month, 25)
    while d.weekday() >= 5:  # pay on the last working day before the 25th falls on a weekend
        d -= dt.timedelta(days=1)
    return d


def build():
    r = rng(77)
    people, heads = staff()
    shift_based = {d[0]: d[4] for d in DEPARTMENTS}
    contracts, banks, reviews = [], [], []
    cn = 1
    for p in people:
        basic = p["BasicSalary"]
        housing = round(basic * 0.4 / 50) * 50
        transport = min(2000, round(basic * 0.1 / 50) * 50)
        other = round(basic * (0.10 if p["ShiftCode"] != "OFF" else r.random() * 0.06) / 50) * 50
        ctype = "Limited" if p["EmploymentType"] == "Fixed-term" else "Unlimited"
        end = p["TerminationDate"]
        if p["HireDate"] < "2025-01-01" and r.random() < 0.6:
            raise_pct = r.choice([0.03, 0.04, 0.05, 0.06, 0.08])
            prev = round(basic / (1 + raise_pct) / 50) * 50
            contracts.append((f"CT-{cn:05d}", p["EmployeeNumber"], ctype, p["HireDate"], "2025-03-31", prev, round(prev * 0.4 / 50) * 50,
                              min(2000, round(prev * 0.1 / 50) * 50), other, None, "AED", 0, "Initial or previous contract"))
            cn += 1
            contracts.append((f"CT-{cn:05d}", p["EmployeeNumber"], ctype, "2025-04-01", end, basic, housing, transport, other, None, "AED", 1,
                              f"Annual increment {int(raise_pct * 100)}%"))
        else:
            contracts.append((f"CT-{cn:05d}", p["EmployeeNumber"], ctype, p["HireDate"], end, basic, housing, transport, other, None, "AED", 1,
                              "New hire" if p["HireDate"] >= "2025-01-01" else "Initial contract"))
        cn += 1
        banks.append((p["EmployeeNumber"], p["BankName"], p["IBAN"], p["FullName"], "WPS bank transfer"))
        if p["HireDate"] < "2024-07-01" and p["EmploymentStatus"] == "Active" or (p["HireDate"] < "2024-07-01" and p["TerminationDate"] and p["TerminationDate"] > "2025-03-01"):
            rating = 5 if (x := r.random()) < 0.10 else 4 if x < 0.40 else 3 if x < 0.85 else 2 if x < 0.97 else 1
            label = {5: "Outstanding", 4: "Exceeds expectations", 3: "Meets expectations", 2: "Needs improvement", 1: "Unsatisfactory"}[rating]
            reviews.append((len(reviews) + 1, p["EmployeeNumber"], 2024, rating, label, p["ManagerEmployeeNumber"] or p["EmployeeNumber"],
                            f"2025-02-{r.randint(3, 27):02d}", p["DepartmentCode"]))
    for c in contracts:
        pass
    contracts = [c[:9] + (c[5] + c[6] + c[7] + c[8],) + c[10:] for c in contracts]
    periods = [(f"2025-{m:02d}", f"2025-{m:02d}-01", (dt.date(2025, m + 1, 1) - dt.timedelta(days=1)).isoformat() if m < 12 else "2025-12-31",
                pay_date(2025, m).isoformat(), "Closed") for m in range(1, 13)]

    sql = [prologue(DB, OWNER, "HumanRes#Owner#2026", READER, "HumanRes#Reader#2026",
                    "HumanResourcesDB — employees, payroll, attendance and leave of Gulf Aviation Services, 2025 (sample)",
                    "Departments, sections, grades, positions, employees, contracts, bank accounts, shifts, leave, attendance, payroll, reviews.")]
    sql.append(drop_tables(["PayslipLines", "Payslips", "PayrollPeriods", "PayElements", "AttendanceRecords", "LeaveRequests", "LeaveTypes",
                            "PerformanceReviews", "EmployeeBankAccounts", "EmployeeContracts", "Departments", "Employees", "Positions", "Sections",
                            "Shifts", "JobGrades", "Countries", "Calendar"]).replace("IF OBJECT_ID(N'dbo.Departments'",
                            "IF OBJECT_ID(N'FK_Departments_Manager', N'F') IS NOT NULL ALTER TABLE dbo.Departments DROP CONSTRAINT FK_Departments_Manager;\nIF OBJECT_ID(N'dbo.Departments'"))
    sql.append("""CREATE TABLE dbo.Countries (
    CountryCode   char(2)      NOT NULL CONSTRAINT PK_Countries PRIMARY KEY,
    CountryName   nvarchar(80) NOT NULL,
    CountryNameAr nvarchar(80) NOT NULL,
    Nationality   nvarchar(40) NOT NULL
);
CREATE TABLE dbo.Departments (
    DepartmentCode        nvarchar(10)  NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    DepartmentName        nvarchar(80)  NOT NULL,
    DepartmentNameAr      nvarchar(80)  NOT NULL,
    CostCenterCode        nvarchar(10)  NOT NULL,
    IsShiftBased          bit           NOT NULL,   -- 24/7 airport operations work rotating shifts
    ManagerEmployeeNumber nvarchar(10)  NULL
);
CREATE TABLE dbo.Sections (
    SectionCode    nvarchar(10) NOT NULL CONSTRAINT PK_Sections PRIMARY KEY,
    DepartmentCode nvarchar(10) NOT NULL CONSTRAINT FK_Sections_Departments REFERENCES dbo.Departments (DepartmentCode),
    SectionName    nvarchar(80) NOT NULL,
    SectionNameAr  nvarchar(80) NOT NULL
);
CREATE TABLE dbo.JobGrades (
    GradeCode       nvarchar(4)   NOT NULL CONSTRAINT PK_JobGrades PRIMARY KEY,
    GradeName       nvarchar(60)  NOT NULL,
    MinBasicSalary  decimal(10,2) NOT NULL,   -- AED per month
    MaxBasicSalary  decimal(10,2) NOT NULL,
    AnnualLeaveDays int           NOT NULL
);
CREATE TABLE dbo.Positions (
    PositionCode    nvarchar(6)  NOT NULL CONSTRAINT PK_Positions PRIMARY KEY,
    PositionTitle   nvarchar(80) NOT NULL,
    PositionTitleAr nvarchar(80) NOT NULL,
    DepartmentCode  nvarchar(10) NOT NULL CONSTRAINT FK_Positions_Departments REFERENCES dbo.Departments (DepartmentCode),
    SectionCode     nvarchar(10) NOT NULL CONSTRAINT FK_Positions_Sections REFERENCES dbo.Sections (SectionCode),
    GradeCode       nvarchar(4)  NOT NULL CONSTRAINT FK_Positions_JobGrades REFERENCES dbo.JobGrades (GradeCode)
);
CREATE TABLE dbo.Shifts (
    ShiftCode      nvarchar(4)  NOT NULL CONSTRAINT PK_Shifts PRIMARY KEY,
    ShiftName      nvarchar(40) NOT NULL,
    ShiftNameAr    nvarchar(40) NOT NULL,
    StartTime      time(0)      NOT NULL,
    EndTime        time(0)      NOT NULL,
    ScheduledHours decimal(4,1) NOT NULL
);
CREATE TABLE dbo.Employees (
    EmployeeNumber        nvarchar(10)  NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    FirstName             nvarchar(40)  NOT NULL,
    LastName              nvarchar(40)  NOT NULL,
    FullName              nvarchar(90)  NOT NULL,
    FullNameAr            nvarchar(90)  NULL,
    Gender                char(1)       NOT NULL,   -- M | F
    DateOfBirth           date          NOT NULL,
    NationalityCode       char(2)       NOT NULL CONSTRAINT FK_Employees_Countries REFERENCES dbo.Countries (CountryCode),
    NationalIdNumber      nvarchar(20)  NOT NULL,   -- Emirates ID
    PassportNumber        nvarchar(12)  NOT NULL,
    MaritalStatus         nvarchar(10)  NOT NULL,
    HireDate              date          NOT NULL,
    TerminationDate       date          NULL,
    TerminationReason     nvarchar(30)  NULL,
    EmploymentStatus      nvarchar(12)  NOT NULL,   -- Active | Resigned | Terminated (as of 31 Dec 2025)
    EmploymentType        nvarchar(12)  NOT NULL,   -- Permanent | Fixed-term
    DepartmentCode        nvarchar(10)  NOT NULL CONSTRAINT FK_Employees_Departments REFERENCES dbo.Departments (DepartmentCode),
    SectionCode           nvarchar(10)  NOT NULL CONSTRAINT FK_Employees_Sections REFERENCES dbo.Sections (SectionCode),
    PositionCode          nvarchar(6)   NOT NULL CONSTRAINT FK_Employees_Positions REFERENCES dbo.Positions (PositionCode),
    GradeCode             nvarchar(4)   NOT NULL CONSTRAINT FK_Employees_JobGrades REFERENCES dbo.JobGrades (GradeCode),
    ManagerEmployeeNumber nvarchar(10)  NULL,
    ShiftCode             nvarchar(4)   NOT NULL CONSTRAINT FK_Employees_Shifts REFERENCES dbo.Shifts (ShiftCode),
    WeeklyOffDays         varchar(2)    NOT NULL,   -- two weekday digits, 1 = Monday ... 7 = Sunday
    WorkEmail             nvarchar(120) NOT NULL,
    MobileNumber          nvarchar(20)  NOT NULL,
    WorkLocation          nvarchar(20)  NOT NULL
);
CREATE TABLE dbo.EmployeeContracts (
    ContractNumber      nvarchar(10)  NOT NULL CONSTRAINT PK_EmployeeContracts PRIMARY KEY,
    EmployeeNumber      nvarchar(10)  NOT NULL CONSTRAINT FK_EmployeeContracts_Employees REFERENCES dbo.Employees (EmployeeNumber),
    ContractType        nvarchar(10)  NOT NULL,   -- Unlimited | Limited (UAE labour law)
    StartDate           date          NOT NULL,
    EndDate             date          NULL,
    BasicSalary         decimal(10,2) NOT NULL,   -- AED per month
    HousingAllowance    decimal(10,2) NOT NULL,
    TransportAllowance  decimal(10,2) NOT NULL,
    OtherAllowance      decimal(10,2) NOT NULL,
    TotalMonthlyPackage decimal(10,2) NOT NULL,
    Currency            char(3)       NOT NULL,
    IsCurrent           bit           NOT NULL,
    ChangeReason        nvarchar(60)  NOT NULL
);
CREATE TABLE dbo.EmployeeBankAccounts (
    EmployeeNumber    nvarchar(10) NOT NULL CONSTRAINT PK_EmployeeBankAccounts PRIMARY KEY CONSTRAINT FK_EmployeeBankAccounts_Employees REFERENCES dbo.Employees (EmployeeNumber),
    BankName          nvarchar(60) NOT NULL,
    IBAN              nvarchar(34) NOT NULL,
    AccountHolderName nvarchar(90) NOT NULL,
    PaymentMethod     nvarchar(30) NOT NULL
);
CREATE TABLE dbo.PerformanceReviews (
    ReviewId               int          NOT NULL CONSTRAINT PK_PerformanceReviews PRIMARY KEY,
    EmployeeNumber         nvarchar(10) NOT NULL CONSTRAINT FK_PerformanceReviews_Employees REFERENCES dbo.Employees (EmployeeNumber),
    ReviewYear             smallint     NOT NULL,
    Rating                 tinyint      NOT NULL,   -- 1 (unsatisfactory) ... 5 (outstanding)
    RatingLabel            nvarchar(30) NOT NULL,
    ReviewerEmployeeNumber nvarchar(10) NOT NULL,
    ReviewDate             date         NOT NULL,
    DepartmentCode         nvarchar(10) NOT NULL
);
CREATE TABLE dbo.LeaveTypes (
    LeaveTypeCode         nvarchar(10) NOT NULL CONSTRAINT PK_LeaveTypes PRIMARY KEY,
    LeaveTypeName         nvarchar(40) NOT NULL,
    LeaveTypeNameAr       nvarchar(40) NOT NULL,
    IsPaid                bit          NOT NULL,
    AnnualEntitlementDays int          NOT NULL
);
CREATE TABLE dbo.PayElements (
    ElementCode   nvarchar(10) NOT NULL CONSTRAINT PK_PayElements PRIMARY KEY,
    ElementName   nvarchar(60) NOT NULL,
    ElementNameAr nvarchar(60) NOT NULL,
    ElementType   nvarchar(10) NOT NULL   -- Earning | Deduction
);
CREATE TABLE dbo.PayrollPeriods (
    PeriodCode      char(7)        NOT NULL CONSTRAINT PK_PayrollPeriods PRIMARY KEY,   -- YYYY-MM
    PeriodStart     date           NOT NULL,
    PeriodEnd       date           NOT NULL,
    PayDate         date           NOT NULL,
    Status          nvarchar(10)   NOT NULL,
    EmployeesPaid   int            NULL,
    TotalGrossPay   decimal(14,2)  NULL,
    TotalDeductions decimal(14,2)  NULL,
    TotalNetPay     decimal(14,2)  NULL
);
GO
""")
    sql.append(calendar_table(UAE_HOLIDAYS_2025))
    sql.append(insert("dbo.Countries", ["CountryCode", "CountryName", "CountryNameAr", "Nationality"], [(n[0], n[1], n[2], n[3]) for n in NATIONALITIES]))
    sql.append(insert("dbo.Departments", ["DepartmentCode", "DepartmentName", "DepartmentNameAr", "CostCenterCode", "IsShiftBased", "ManagerEmployeeNumber"],
                      [(d[0], d[1], d[2], d[3], d[4], heads.get(d[0])) for d in DEPARTMENTS]))
    sql.append(insert("dbo.Sections", ["SectionCode", "DepartmentCode", "SectionName", "SectionNameAr"], SECTIONS))
    sql.append(insert("dbo.JobGrades", ["GradeCode", "GradeName", "MinBasicSalary", "MaxBasicSalary", "AnnualLeaveDays"], GRADES))
    sql.append(insert("dbo.Positions", ["PositionCode", "PositionTitle", "PositionTitleAr", "DepartmentCode", "SectionCode", "GradeCode"], [p[:6] for p in POSITIONS]))
    sql.append(insert("dbo.Shifts", ["ShiftCode", "ShiftName", "ShiftNameAr", "StartTime", "EndTime", "ScheduledHours"],
                      [(s[0], s[1], s[2], s[3], s[4], 9 if s[0] == "OFF" else 8) for s in SHIFTS]))
    cols = ["EmployeeNumber", "FirstName", "LastName", "FullName", "FullNameAr", "Gender", "DateOfBirth", "NationalityCode", "NationalIdNumber",
            "PassportNumber", "MaritalStatus", "HireDate", "TerminationDate", "TerminationReason", "EmploymentStatus", "EmploymentType", "DepartmentCode",
            "SectionCode", "PositionCode", "GradeCode", "ManagerEmployeeNumber", "ShiftCode", "WeeklyOffDays", "WorkEmail", "MobileNumber", "WorkLocation"]
    sql.append(insert("dbo.Employees", cols, [tuple(p[c] for c in cols) for p in people]))
    sql.append(insert("dbo.EmployeeContracts", ["ContractNumber", "EmployeeNumber", "ContractType", "StartDate", "EndDate", "BasicSalary", "HousingAllowance",
                                                "TransportAllowance", "OtherAllowance", "TotalMonthlyPackage", "Currency", "IsCurrent", "ChangeReason"], contracts))
    sql.append(insert("dbo.EmployeeBankAccounts", ["EmployeeNumber", "BankName", "IBAN", "AccountHolderName", "PaymentMethod"], banks))
    sql.append(insert("dbo.PerformanceReviews", ["ReviewId", "EmployeeNumber", "ReviewYear", "Rating", "RatingLabel", "ReviewerEmployeeNumber", "ReviewDate", "DepartmentCode"], reviews))
    sql.append(insert("dbo.LeaveTypes", ["LeaveTypeCode", "LeaveTypeName", "LeaveTypeNameAr", "IsPaid", "AnnualEntitlementDays"], LEAVE_TYPES))
    sql.append(insert("dbo.PayElements", ["ElementCode", "ElementName", "ElementNameAr", "ElementType"], PAY_ELEMENTS))
    sql.append(insert("dbo.PayrollPeriods", ["PeriodCode", "PeriodStart", "PeriodEnd", "PayDate", "Status"], periods))
    sql.append("""GO
ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Manager FOREIGN KEY (ManagerEmployeeNumber) REFERENCES dbo.Employees (EmployeeNumber);
ALTER TABLE dbo.Departments ADD CONSTRAINT FK_Departments_Manager FOREIGN KEY (ManagerEmployeeNumber) REFERENCES dbo.Employees (EmployeeNumber);
CREATE INDEX IX_Employees_Department ON dbo.Employees (DepartmentCode, SectionCode);
CREATE INDEX IX_EmployeeContracts_Employee ON dbo.EmployeeContracts (EmployeeNumber, StartDate);
GO
""")
    sql.append(GENERATED_SQL)
    signins_sql, signins = portal_signins(people)
    sql.append(signins_sql)
    sql.append(epilogue(DB, """SELECT N'Employees' AS TableName, COUNT(*) AS RowsLoaded FROM dbo.Employees
UNION ALL SELECT N'AttendanceRecords', COUNT(*) FROM dbo.AttendanceRecords
UNION ALL SELECT N'LeaveRequests', COUNT(*) FROM dbo.LeaveRequests
UNION ALL SELECT N'Payslips', COUNT(*) FROM dbo.Payslips
UNION ALL SELECT N'PayslipLines', COUNT(*) FROM dbo.PayslipLines
UNION ALL SELECT N'EmployeePortalSignIns', COUNT(*) FROM dbo.EmployeePortalSignIns;"""))
    return "\n".join(sql), len(people), len(contracts), signins


GENERATED_SQL = r"""-- Leave requests: two to three annual-leave requests (summer-heavy), sick days, and occasional emergency, maternity or unpaid leave.
CREATE TABLE dbo.LeaveRequests (
    LeaveRequestId           int          NOT NULL CONSTRAINT PK_LeaveRequests PRIMARY KEY,
    EmployeeNumber           nvarchar(10) NOT NULL CONSTRAINT FK_LeaveRequests_Employees REFERENCES dbo.Employees (EmployeeNumber),
    DepartmentCode           nvarchar(10) NOT NULL,
    SectionCode              nvarchar(10) NOT NULL,
    LeaveTypeCode            nvarchar(10) NOT NULL CONSTRAINT FK_LeaveRequests_LeaveTypes REFERENCES dbo.LeaveTypes (LeaveTypeCode),
    StartDate                date         NOT NULL,
    EndDate                  date         NOT NULL,
    CalendarDays             int          NOT NULL,
    RequestedOn              date         NOT NULL,
    Status                   nvarchar(10) NOT NULL,   -- Approved | Rejected | Pending
    ApproverEmployeeNumber   nvarchar(10) NULL,
    DecisionDate             date         NULL
);
WITH L AS (
    SELECT e.EmployeeNumber, e.DepartmentCode, e.SectionCode, e.ManagerEmployeeNumber, e.HireDate, e.TerminationDate, n.n, r.*,
           CASE WHEN n.n <= 2 THEN N'ANNUAL'
                WHEN n.n = 3 THEN CASE WHEN r.R9 < 0.5 THEN N'ANNUAL' END
                WHEN n.n IN (4, 5) THEN CASE WHEN r.R9 < 0.55 THEN N'SICK' END
                ELSE CASE WHEN r.R9 < 0.15 THEN N'EMERGENCY' WHEN r.R9 < 0.18 AND e.Gender = 'F' THEN N'MATERNITY' WHEN r.R9 < 0.20 THEN N'UNPAID' END END AS LeaveType
    FROM dbo.Employees e
    CROSS JOIN (VALUES (1), (2), (3), (4), (5), (6)) n(n)
    CROSS APPLY gen.Rand(3000000000 + CAST(SUBSTRING(e.EmployeeNumber, 2, 9) AS bigint) * 10 + n.n) r
), D AS (
    SELECT L.*, DATEADD(DAY, CASE WHEN L.LeaveType = N'ANNUAL' AND L.R1 < 0.45 THEN 181 + CAST(L.R2 * 61 AS int) ELSE CAST(L.R2 * 358 AS int) END, CAST('2025-01-01' AS date)) AS StartDate,
           CASE L.LeaveType WHEN N'ANNUAL' THEN 5 + CAST(L.R3 * 12 AS int) WHEN N'SICK' THEN 1 + CAST(L.R3 * 3 AS int) WHEN N'EMERGENCY' THEN 2 + CAST(L.R3 * 4 AS int)
                            WHEN N'MATERNITY' THEN 60 ELSE 3 + CAST(L.R3 * 8 AS int) END AS Days
    FROM L WHERE L.LeaveType IS NOT NULL
)
INSERT INTO dbo.LeaveRequests
SELECT ROW_NUMBER() OVER (ORDER BY D.StartDate, D.EmployeeNumber, D.n), D.EmployeeNumber, D.DepartmentCode, D.SectionCode, D.LeaveType, D.StartDate, x.EndDate,
       DATEDIFF(DAY, D.StartDate, x.EndDate) + 1, req.RequestedOn, st.Status, D.ManagerEmployeeNumber,
       CASE WHEN st.Status = N'Pending' THEN NULL ELSE DATEADD(DAY, 1 + CAST(D.R6 * 3 AS int), req.RequestedOn) END
FROM D
CROSS APPLY (SELECT CASE WHEN DATEADD(DAY, D.Days - 1, D.StartDate) > '2025-12-31' THEN CAST('2025-12-31' AS date) ELSE DATEADD(DAY, D.Days - 1, D.StartDate) END AS EndDate) x
CROSS APPLY (SELECT DATEADD(DAY, -CASE D.LeaveType WHEN N'ANNUAL' THEN 10 + CAST(D.R5 * 40 AS int) WHEN N'SICK' THEN 0 WHEN N'MATERNITY' THEN 45 ELSE 1 + CAST(D.R5 * 3 AS int) END, D.StartDate) AS RequestedOn) req
CROSS APPLY (SELECT CASE WHEN D.StartDate > '2025-12-08' AND D.LeaveType = N'ANNUAL' AND D.R4 < 0.6 THEN N'Pending'
                         WHEN D.LeaveType <> N'SICK' AND D.R4 < 0.05 THEN N'Rejected' ELSE N'Approved' END AS Status) st
WHERE D.StartDate >= D.HireDate AND (D.TerminationDate IS NULL OR D.StartDate <= D.TerminationDate);
CREATE INDEX IX_LeaveRequests_Employee ON dbo.LeaveRequests (EmployeeNumber, StartDate, EndDate) INCLUDE (Status, LeaveTypeCode);
GO
-- Daily attendance: office staff Monday-Friday except public holidays; shift staff five days a week around their two weekly off days, holidays included.
CREATE TABLE dbo.AttendanceRecords (
    AttendanceId      int           NOT NULL CONSTRAINT PK_AttendanceRecords PRIMARY KEY,
    EmployeeNumber    nvarchar(10)  NOT NULL CONSTRAINT FK_AttendanceRecords_Employees REFERENCES dbo.Employees (EmployeeNumber),
    AttendanceDate    date          NOT NULL CONSTRAINT FK_AttendanceRecords_Calendar REFERENCES dbo.Calendar (CalendarDate),
    DepartmentCode    nvarchar(10)  NOT NULL,
    SectionCode       nvarchar(10)  NOT NULL,
    ShiftCode         nvarchar(4)   NOT NULL CONSTRAINT FK_AttendanceRecords_Shifts REFERENCES dbo.Shifts (ShiftCode),
    ScheduledStart    datetime2(0)  NOT NULL,
    ScheduledEnd      datetime2(0)  NOT NULL,
    CheckIn           datetime2(0)  NULL,
    CheckOut          datetime2(0)  NULL,
    WorkedMinutes     int           NOT NULL,
    LateMinutes       int           NOT NULL,
    OvertimeMinutes   int           NOT NULL,
    Status            nvarchar(10)  NOT NULL,   -- Present | Late | Absent | On Leave
    LeaveTypeCode     nvarchar(10)  NULL CONSTRAINT FK_AttendanceRecords_LeaveTypes REFERENCES dbo.LeaveTypes (LeaveTypeCode),
    WorkedOnHoliday   bit           NOT NULL
);
WITH A AS (
    SELECT e.EmployeeNumber, e.DepartmentCode, e.SectionCode, e.ShiftCode, c.CalendarDate, c.MonthNumber, c.IsPublicHoliday, d.IsShiftBased,
           DATEADD(MINUTE, DATEDIFF(MINUTE, CAST('00:00' AS time(0)), s.StartTime), CAST(c.CalendarDate AS datetime2(0))) AS SchedStart,
           CAST(s.ScheduledHours * 60 AS int) AS SchedMinutes,
           2000000000 + CAST(SUBSTRING(e.EmployeeNumber, 2, 9) AS bigint) * 1000 + c.DayOfYear AS K
    FROM dbo.Employees e
    JOIN dbo.Departments d ON d.DepartmentCode = e.DepartmentCode
    JOIN dbo.Shifts s ON s.ShiftCode = e.ShiftCode
    JOIN dbo.Calendar c ON c.CalendarDate >= e.HireDate AND (e.TerminationDate IS NULL OR c.CalendarDate <= e.TerminationDate)
    WHERE (e.ShiftCode = N'OFF' AND c.IsWeekend = 0 AND c.IsPublicHoliday = 0)
       OR (e.ShiftCode <> N'OFF' AND CHARINDEX(CAST(c.DayOfWeekNumber AS char(1)), e.WeeklyOffDays) = 0)
)
INSERT INTO dbo.AttendanceRecords
SELECT ROW_NUMBER() OVER (ORDER BY A.CalendarDate, A.EmployeeNumber), A.EmployeeNumber, A.CalendarDate, A.DepartmentCode, A.SectionCode, A.ShiftCode,
       A.SchedStart, DATEADD(MINUTE, A.SchedMinutes, A.SchedStart),
       t.CheckIn, t.CheckOut,
       CASE WHEN t.CheckIn IS NULL THEN 0 ELSE DATEDIFF(MINUTE, t.CheckIn, t.CheckOut) END,
       CASE WHEN st.Status = N'Late' THEN m.Late ELSE 0 END,
       CASE WHEN st.Status IN (N'Present', N'Late') THEN m.Overtime ELSE 0 END,
       st.Status, l.LeaveTypeCode,
       CASE WHEN A.IsPublicHoliday = 1 AND st.Status IN (N'Present', N'Late') THEN 1 ELSE 0 END
FROM A
CROSS APPLY gen.Rand(A.K) r
OUTER APPLY (SELECT TOP (1) lr.LeaveTypeCode FROM dbo.LeaveRequests lr
             WHERE lr.EmployeeNumber = A.EmployeeNumber AND lr.Status = N'Approved' AND A.CalendarDate BETWEEN lr.StartDate AND lr.EndDate
             ORDER BY lr.StartDate) l
CROSS APPLY (SELECT CASE WHEN l.LeaveTypeCode IS NOT NULL THEN N'On Leave' WHEN r.R1 < 0.012 THEN N'Absent' WHEN r.R2 < 0.09 THEN N'Late' ELSE N'Present' END AS Status) st
-- Overtime is more frequent for shift departments in the peak months (July, August, December).
CROSS APPLY (SELECT 5 + CAST(r.R3 * 70 AS int) AS Late,
                    CASE WHEN r.R5 < CASE WHEN A.IsShiftBased = 1 AND A.MonthNumber IN (7, 8, 12) THEN 0.26 WHEN A.IsShiftBased = 1 THEN 0.13 ELSE 0.07 END
                         THEN (30 + CAST(r.R6 * 210 AS int)) / 15 * 15 ELSE 0 END AS Overtime) m
CROSS APPLY (SELECT CASE WHEN st.Status IN (N'Absent', N'On Leave') THEN NULL
                         WHEN st.Status = N'Late' THEN DATEADD(MINUTE, m.Late, A.SchedStart)
                         ELSE DATEADD(MINUTE, -CAST(r.R4 * 15 AS int), A.SchedStart) END AS CheckIn,
                    CASE WHEN st.Status IN (N'Absent', N'On Leave') THEN NULL
                         ELSE DATEADD(MINUTE, A.SchedMinutes + m.Overtime + CAST(r.R7 * 10 AS int), A.SchedStart) END AS CheckOut) t
OPTION (MAXDOP 4);
CREATE INDEX IX_AttendanceRecords_Employee ON dbo.AttendanceRecords (EmployeeNumber, AttendanceDate);
CREATE INDEX IX_AttendanceRecords_Department ON dbo.AttendanceRecords (DepartmentCode, AttendanceDate) INCLUDE (Status, LateMinutes, OvertimeMinutes);
GO
-- Monthly payroll: the contract in force at the end of the month, prorated for joiners and leavers, overtime at 125 % of the hourly basic,
-- unpaid absence deducted at the daily basic, pension for UAE nationals (11 % of basic + housing), salary-advance repayments.
CREATE TABLE dbo.Payslips (
    PayslipId          int           NOT NULL CONSTRAINT PK_Payslips PRIMARY KEY,
    PeriodCode         char(7)       NOT NULL CONSTRAINT FK_Payslips_PayrollPeriods REFERENCES dbo.PayrollPeriods (PeriodCode),
    EmployeeNumber     nvarchar(10)  NOT NULL CONSTRAINT FK_Payslips_Employees REFERENCES dbo.Employees (EmployeeNumber),
    DepartmentCode     nvarchar(10)  NOT NULL,
    SectionCode        nvarchar(10)  NOT NULL,
    GradeCode          nvarchar(4)   NOT NULL,
    ContractNumber     nvarchar(10)  NOT NULL CONSTRAINT FK_Payslips_EmployeeContracts REFERENCES dbo.EmployeeContracts (ContractNumber),
    DaysPaid           int           NOT NULL,
    BasicSalary        decimal(12,2) NOT NULL,
    HousingAllowance   decimal(12,2) NOT NULL,
    TransportAllowance decimal(12,2) NOT NULL,
    OtherAllowance     decimal(12,2) NOT NULL,
    OvertimeHours      decimal(8,2)  NOT NULL,
    OvertimePay        decimal(12,2) NOT NULL,
    GrossPay           decimal(12,2) NOT NULL,
    AbsenceDays        int           NOT NULL,
    AbsenceDeduction   decimal(12,2) NOT NULL,
    PensionDeduction   decimal(12,2) NOT NULL,
    LoanDeduction      decimal(12,2) NOT NULL,
    TotalDeductions    decimal(12,2) NOT NULL,
    NetPay             decimal(12,2) NOT NULL,
    PaymentDate        date          NOT NULL,
    PaymentMethod      nvarchar(30)  NOT NULL
);
WITH E AS (
    SELECT e.EmployeeNumber, e.DepartmentCode, e.SectionCode, e.GradeCode, e.NationalityCode, p.PeriodCode, p.PayDate, p.PeriodEnd,
           CASE WHEN e.HireDate > p.PeriodStart THEN e.HireDate ELSE p.PeriodStart END AS FromDate,
           CASE WHEN e.TerminationDate < p.PeriodEnd THEN e.TerminationDate ELSE p.PeriodEnd END AS ToDate,
           DAY(p.PeriodEnd) AS MonthDays,
           CAST(SUBSTRING(e.EmployeeNumber, 2, 9) AS bigint) AS EmpNo
    FROM dbo.Employees e
    JOIN dbo.PayrollPeriods p ON e.HireDate <= p.PeriodEnd AND (e.TerminationDate IS NULL OR e.TerminationDate >= p.PeriodStart)
), C AS (
    SELECT E.*, ct.ContractNumber, ct.BasicSalary AS CBasic, ct.HousingAllowance AS CHousing, ct.TransportAllowance AS CTransport, ct.OtherAllowance AS COther,
           (DATEDIFF(DAY, E.FromDate, E.ToDate) + 1) * 1.0 / E.MonthDays AS Factor, DATEDIFF(DAY, E.FromDate, E.ToDate) + 1 AS DaysPaid,
           ISNULL(a.OvertimeMinutes, 0) AS OvertimeMinutes, ISNULL(a.AbsentDays, 0) AS AbsentDays, ISNULL(a.UnpaidDays, 0) AS UnpaidDays
    FROM E
    CROSS APPLY (SELECT TOP (1) * FROM dbo.EmployeeContracts ct WHERE ct.EmployeeNumber = E.EmployeeNumber AND ct.StartDate <= E.ToDate ORDER BY ct.StartDate DESC) ct
    OUTER APPLY (SELECT SUM(ar.OvertimeMinutes) AS OvertimeMinutes, SUM(CASE WHEN ar.Status = N'Absent' THEN 1 ELSE 0 END) AS AbsentDays,
                        SUM(CASE WHEN ar.LeaveTypeCode = N'UNPAID' THEN 1 ELSE 0 END) AS UnpaidDays
                 FROM dbo.AttendanceRecords ar WHERE ar.EmployeeNumber = E.EmployeeNumber AND ar.AttendanceDate BETWEEN E.FromDate AND E.ToDate) a
), M AS (
    SELECT C.*, CAST(ROUND(C.CBasic * C.Factor, 2) AS decimal(12,2)) AS Basic, CAST(ROUND(C.CHousing * C.Factor, 2) AS decimal(12,2)) AS Housing,
           CAST(ROUND(C.CTransport * C.Factor, 2) AS decimal(12,2)) AS Transport, CAST(ROUND(C.COther * C.Factor, 2) AS decimal(12,2)) AS Other,
           CAST(ROUND(C.OvertimeMinutes / 60.0, 2) AS decimal(8,2)) AS OtHours,
           CAST(ROUND(C.OvertimeMinutes / 60.0 * C.CBasic / 30 / 8 * 1.25, 2) AS decimal(12,2)) AS OtPay,
           CAST(ROUND((C.AbsentDays + C.UnpaidDays) * C.CBasic / 30, 2) AS decimal(12,2)) AS Absence,
           CASE WHEN C.NationalityCode = 'AE' THEN CAST(ROUND((C.CBasic + C.CHousing) * C.Factor * 0.11, 2) AS decimal(12,2)) ELSE 0 END AS Pension,
           CASE WHEN r.R1 < 0.08 THEN CAST(CHOOSE(1 + CAST(r.R2 * 3 AS int), 750, 1000, 1500) AS decimal(12,2)) ELSE 0 END AS Loan
    FROM C CROSS APPLY gen.Rand(4000000000 + C.EmpNo) r
)
INSERT INTO dbo.Payslips
SELECT ROW_NUMBER() OVER (ORDER BY M.PeriodCode, M.EmployeeNumber), M.PeriodCode, M.EmployeeNumber, M.DepartmentCode, M.SectionCode, M.GradeCode, M.ContractNumber,
       M.DaysPaid, M.Basic, M.Housing, M.Transport, M.Other, M.OtHours, M.OtPay,
       M.Basic + M.Housing + M.Transport + M.Other + M.OtPay,
       M.AbsentDays + M.UnpaidDays, M.Absence, M.Pension, M.Loan, M.Absence + M.Pension + M.Loan,
       M.Basic + M.Housing + M.Transport + M.Other + M.OtPay - (M.Absence + M.Pension + M.Loan),
       M.PayDate, N'WPS bank transfer'
FROM M;
CREATE INDEX IX_Payslips_Employee ON dbo.Payslips (EmployeeNumber, PeriodCode);
CREATE INDEX IX_Payslips_Department ON dbo.Payslips (DepartmentCode, PeriodCode);
GO
CREATE TABLE dbo.PayslipLines (
    PayslipLineId  int           NOT NULL CONSTRAINT PK_PayslipLines PRIMARY KEY,
    PayslipId      int           NOT NULL CONSTRAINT FK_PayslipLines_Payslips REFERENCES dbo.Payslips (PayslipId),
    PeriodCode     char(7)       NOT NULL,
    EmployeeNumber nvarchar(10)  NOT NULL,
    ElementCode    nvarchar(10)  NOT NULL CONSTRAINT FK_PayslipLines_PayElements REFERENCES dbo.PayElements (ElementCode),
    Amount         decimal(12,2) NOT NULL   -- positive for earnings and for deductions; see PayElements.ElementType
);
INSERT INTO dbo.PayslipLines
SELECT ROW_NUMBER() OVER (ORDER BY u.PayslipId, u.Seq), u.PayslipId, u.PeriodCode, u.EmployeeNumber, u.ElementCode, u.Amount
FROM (
    SELECT p.PayslipId, p.PeriodCode, p.EmployeeNumber, v.Seq, v.ElementCode, v.Amount
    FROM dbo.Payslips p
    CROSS APPLY (VALUES (1, N'BASIC', p.BasicSalary), (2, N'HOUSING', p.HousingAllowance), (3, N'TRANSPORT', p.TransportAllowance), (4, N'OTHER', p.OtherAllowance),
                        (5, N'OVERTIME', p.OvertimePay), (6, N'ABSENCE', p.AbsenceDeduction), (7, N'PENSION', p.PensionDeduction), (8, N'LOAN', p.LoanDeduction)) v(Seq, ElementCode, Amount)
    WHERE v.Amount > 0
) u;
CREATE INDEX IX_PayslipLines_Payslip ON dbo.PayslipLines (PayslipId);
UPDATE pp SET EmployeesPaid = s.N, TotalGrossPay = s.Gross, TotalDeductions = s.Ded, TotalNetPay = s.Net
FROM dbo.PayrollPeriods pp
JOIN (SELECT PeriodCode, COUNT(*) AS N, SUM(GrossPay) AS Gross, SUM(TotalDeductions) AS Ded, SUM(NetPay) AS Net FROM dbo.Payslips GROUP BY PeriodCode) s ON s.PeriodCode = pp.PeriodCode;
GO
"""
