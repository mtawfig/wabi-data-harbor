"""WabiBankPersonnelDB: Wabi Bank's personnel, organization structure, attendance, leave and the identity directory.

Personnel is the system of record for divisions, departments (Head Office departments and banking branches), sections,
employees, their single UserId and which systems each user may use (UserSystemAccess). The other four Wabi Bank
databases reuse these codes unchanged. Planted findings: one leaver whose core-banking access was revoked three days
after termination; a Marketing user with core-banking access and a Recruitment user with finance buyer access.
"""
import datetime as dt
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bank_people as bp  # noqa: E402
from build_common import TALLY_CTE, drop_tables, epilogue, insert, prologue  # noqa: E402

DB = "WabiBankPersonnelDB"
TABLES = ["UserSystemAccess", "SystemUsers", "Systems", "AttendanceRecords", "LeaveRequests", "LeaveTypes", "EmployeeMovements", "Employees",
          "Shifts", "PublicHolidays", "Positions", "JobGrades", "Sections", "Departments", "Divisions", "Locations", "Countries"]

OBJECT_DOCS = [
    ("Divisions", "Divisions", "القطاعات", "The bank's divisions under the CEO (Retail, Wholesale, Operations and Technology, Risk, Finance, HR, Marketing, CEO Office), each headed by an executive."),
    ("Locations", "Locations", "المواقع", "Head Office (Dubai) and the six banking branches with emirate, address, opening date and number of counters."),
    ("Departments", "Departments and branches", "الإدارات والفروع", "Department-level units: the 15 Head Office departments and the 6 banking branches (DepartmentType Branch). Codes are shared by every Wabi Bank system and are the finance cost centres."),
    ("Sections", "Sections", "الأقسام", "Sub-units of departments and branches. Every branch has Customer Service, Teller and Cash, Personal Banking and Sales, and Branch Operations sections."),
    ("JobGrades", "Job grades", "الدرجات الوظيفية", "Grades G1 (Associate) to G10 (Executive) with the monthly salary band in AED."),
    ("Positions", "Positions", "المسميات الوظيفية", "Job titles with grade; branch positions are shared by all branches (DepartmentCode empty)."),
    ("Countries", "Countries and nationalities", "الدول والجنسيات", "Nationalities of employees."),
    ("PublicHolidays", "UAE public holidays 2025", "العطل الرسمية 2025", "Days when Head Office and branches are closed (the Contact Centre and security work)."),
    ("Shifts", "Shifts", "الورديات", "Working hours: Head Office, branches, Contact Centre (three shifts, seven days) and security rota."),
    ("Employees", "Employees", "الموظفون", "Everyone employed in 2025 (including leavers): names EN/AR, nationality, Emirates ID, hire and termination dates, division, department or branch, section, position, grade, line manager, shift, work e-mail and the UserId used in every system (empty for staff without system access)."),
    ("EmployeeMovements", "Transfers and promotions", "التنقلات والترقيات", "Movements in 2025 with effective date and the unit, position and grade before and after."),
    ("LeaveTypes", "Leave types", "أنواع الإجازات", "Annual, sick, emergency and maternity leave with entitlement."),
    ("LeaveRequests", "Leave requests", "طلبات الإجازات", "Leave requests with dates, working days, status and who decided (the line manager's UserId)."),
    ("AttendanceRecords", "Daily attendance", "الحضور اليومي", "One row per employee per rostered working day of 2025: Present, Late, Absent or OnLeave, check-in and check-out, late and overtime minutes, and the unit the employee belonged to that day."),
    ("Systems", "Systems", "الأنظمة", "The bank's systems that use the shared identities: CBS, HRMS, FIN, DIGITAL, ITSM."),
    ("SystemUsers", "System users", "مستخدمو الأنظمة", "One login (UserId) per employee with system access, used in every system, plus service accounts (batch, clearing, digital channel, monitoring)."),
    ("UserSystemAccess", "System access", "صلاحيات الأنظمة", "Which systems each UserId may use, with the role, approval limit, granted date and revoked date."),
]
COLUMN_DOCS = [
    ("Employees", "EmiratesIdNumber", "Emirates ID", "رقم الهوية الإماراتية", "National identity number.", "Restricted"),
    ("Employees", "PassportNumber", "Passport number", "رقم جواز السفر", None, "Restricted"),
    ("Employees", "DateOfBirth", "Date of birth", "تاريخ الميلاد", None, "Confidential"),
    ("Employees", "MobileNumber", "Mobile number", "رقم الهاتف المتحرك", None, "Confidential"),
    ("Employees", "UserId", "User ID", "معرف المستخدم", "The employee's login in every Wabi Bank system (SystemUsers).", None),
    ("Employees", "DepartmentCode", "Department or branch", "الإدارة أو الفرع", "Head Office department code or branch code (BR-...).", None),
    ("Employees", "PositionLevel", "Position level", "المستوى الوظيفي", "DepartmentHead, SectionHead or Staff.", None),
    ("JobGrades", "MinMonthlySalary", "Minimum monthly salary", "الحد الأدنى للراتب الشهري", "AED.", "Confidential"),
    ("JobGrades", "MaxMonthlySalary", "Maximum monthly salary", "الحد الأقصى للراتب الشهري", "AED.", "Confidential"),
    ("AttendanceRecords", "Status", "Status", "الحالة", "Present, Late, Absent or OnLeave.", None),
    ("AttendanceRecords", "LateMinutes", "Late minutes", "دقائق التأخير", None, None),
    ("AttendanceRecords", "OvertimeMinutes", "Overtime minutes", "دقائق العمل الإضافي", "Minutes after the shift end (more than 30).", None),
    ("LeaveRequests", "DecidedByUserId", "Decided by", "تم القرار بواسطة", "UserId of the line manager who approved or rejected (the employee for a cancellation).", None),
    ("UserSystemAccess", "RevokedDate", "Revoked on", "تاريخ الإلغاء", "Empty while access is active; normally the termination date.", None),
    ("UserSystemAccess", "ApprovalLimitAed", "Approval limit (AED)", "حد الموافقة (درهم)", None, None),
]
FIELD_ROLES = [
    ("Employees", "EmployeeNumber", 2), ("Employees", "UserId", 3), ("Employees", "HireDate", 5), ("Employees", "EmploymentStatus", 8), ("Employees", "LocationCode", 9),
    ("AttendanceRecords", "EmployeeNumber", 2), ("AttendanceRecords", "AttendanceDate", 5), ("AttendanceRecords", "Status", 8), ("AttendanceRecords", "LateMinutes", 7),
    ("LeaveRequests", "EmployeeNumber", 2), ("LeaveRequests", "StartDate", 5), ("LeaveRequests", "WorkingDays", 7), ("LeaveRequests", "Status", 8),
    ("SystemUsers", "UserId", 3), ("SystemUsers", "EmployeeNumber", 2), ("UserSystemAccess", "UserId", 3), ("UserSystemAccess", "GrantedDate", 5),
]
ROW_SCOPES = [
    ("Employees", "EmployeeNumber", "DepartmentCode", "SectionCode"), ("AttendanceRecords", "EmployeeNumber", "DepartmentCode", "SectionCode"),
    ("LeaveRequests", "EmployeeNumber", "DepartmentCode", "SectionCode"), ("EmployeeMovements", "EmployeeNumber", "ToDepartmentCode", "ToSectionCode"),
]


def _rows():
    bp.validate()
    emps = bp.employees()
    y1 = bp.Y1
    out = {}
    out["Countries"] = [(c[0], c[1], c[2], c[3]) for c in bp.COUNTRIES]
    out["Locations"] = [(x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7]) for x in bp.LOCATIONS]
    chief = {pos: next((e for e in emps if e["PositionCode"] == pos and e["TerminationDate"] is None), None) for _, _, _, pos, _ in bp.DIVISIONS}
    out["Divisions"] = [(c, en, ar, chief[pos]["EmployeeNumber"], rep) for c, en, ar, pos, rep in bp.DIVISIONS]
    out["Departments"] = [(x["DepartmentCode"], x["DepartmentName"], x["DepartmentNameAr"], x["DepartmentType"], x["DivisionCode"], x["LocationCode"],
                           bp.head_of(x["DepartmentCode"], y1)["EmployeeNumber"]) for x in bp.departments()]
    out["Sections"] = [(s["SectionCode"], s["DepartmentCode"], s["SectionName"], s["SectionNameAr"],
                        (bp.head_of(s["SectionCode"], y1) or {}).get("EmployeeNumber")) for s in bp.sections()]
    out["JobGrades"] = [g for g in bp.GRADES]
    out["Positions"] = [(p["PositionCode"], p["Title"], p["TitleAr"], p["GradeCode"], p["DepartmentCode"], p["SectionType"], p["Level"]) for p in bp.positions()]
    out["PublicHolidays"] = sorted((str(k), v[0], v[1]) for k, v in bp.HOLIDAYS.items())
    out["Shifts"] = [s for s in bp.SHIFTS]
    out["Employees"] = [(e["EmployeeNumber"], e["FirstName"], e["LastName"], e["FullName"], e["FullNameAr"], e["Gender"], e["DateOfBirth"], e["NationalityCode"],
                         e["EmiratesIdNumber"], e["PassportNumber"], e["MaritalStatus"], e["HireDate"], e["TerminationDate"], e["TerminationReason"], e["EmploymentStatus"],
                         e["DivisionCode"], e["DepartmentCode"], e["SectionCode"], e["LocationCode"], e["PositionCode"], e["GradeCode"], e["Level"],
                         (bp.manager_on(e, min(y1, e["TerminationDate"] or y1)) or {}).get("EmployeeNumber"),
                         e["ShiftCode"], e["WeeklyOffDays"], e["WorkEmail"], e["MobileNumber"], e["UserId"]) for e in emps]
    chro = chief["CHRO"]
    out["EmployeeMovements"] = [(m["MovementId"], m["EmployeeNumber"], m["MovementType"], m["EffectiveDate"], m["FromDepartmentCode"], m["FromSectionCode"], m["FromPositionCode"],
                                 m["FromGradeCode"], m["ToDepartmentCode"], m["ToSectionCode"], m["ToPositionCode"], m["ToGradeCode"], m["Reason"], chro["UserId"],
                                 dt.datetime.combine(m["EffectiveDate"] - dt.timedelta(days=14), dt.time(11, 0))) for m in bp.movements()]
    out["LeaveTypes"] = [("AL", "Annual leave", "إجازة سنوية", 1, 30), ("SL", "Sick leave", "إجازة مرضية", 1, 15), ("EL", "Emergency leave", "إجازة طارئة", 1, 5),
                         ("ML", "Maternity leave", "إجازة أمومة", 1, 60)]
    leave = []
    by_no = bp.by_number()
    hr_officers = [e for e in emps if e["SectionCode"] == "HRD-PSV" and e["Level"] == "Staff"]
    for q in bp.leave_requests():
        p = by_no[q["EmployeeNumber"]]
        dept, sect, _, _ = bp.unit_on(p, q["StartDate"])
        req_day = q["RequestedAt"].date()
        if p["UserId"] and bp.has_access(p["UserId"], "HRMS", req_day):
            submitted = p["UserId"]
        else:
            submitted = next(h["UserId"] for h in hr_officers if bp.employed(h, req_day) and bp.has_access(h["UserId"], "HRMS", req_day))
        if q["Status"] == "Cancelled":
            decided_by, decided_at = submitted, q["RequestedAt"] + dt.timedelta(days=2, hours=1)
        else:
            mgr = bp.manager_on(p, req_day) or chro
            day = req_day
            while not (bp.available(mgr, day) and bp.has_access(mgr["UserId"], "HRMS", day)):
                day += dt.timedelta(days=1)
                if day > q["StartDate"]:
                    mgr, day = chro, req_day
                    break
            decided_by, decided_at = mgr["UserId"], dt.datetime.combine(day, dt.time(13, 30)) if day > req_day else q["RequestedAt"] + dt.timedelta(hours=3)
            assert decided_by != p["UserId"], q
        leave.append((q["LeaveRequestId"], q["EmployeeNumber"], dept, sect, q["LeaveTypeCode"], q["StartDate"], q["EndDate"], q["WorkingDays"], q["Status"],
                      q["RequestedAt"], submitted, decided_by, decided_at))
    out["LeaveRequests"] = leave
    out["Systems"] = [(s[0], s[1], s[2], s[3], s[4]) for s in bp.SYSTEMS]
    out["SystemUsers"] = [(u["UserId"], u["EmployeeNumber"], u["AccountType"], u["DisplayName"], u["Email"], u["Status"], u["CreatedDate"], u["DisabledDate"])
                          for u in bp.system_users()]
    out["UserSystemAccess"] = [(a["AccessId"], a["UserId"], a["SystemCode"], a["RoleCode"], a["ApprovalLimitAed"], a["GrantedDate"], a["RevokedDate"])
                               for a in bp.access()]
    out["Absences"] = sorted((n, str(x)) for n, x in bp.absences())
    return out


SCHEMA = """CREATE TABLE dbo.Countries (CountryCode char(2) NOT NULL CONSTRAINT PK_Countries PRIMARY KEY, CountryName nvarchar(100) NOT NULL,
    CountryNameAr nvarchar(100) NOT NULL, Nationality nvarchar(50) NOT NULL);
CREATE TABLE dbo.Locations (LocationCode nvarchar(10) NOT NULL CONSTRAINT PK_Locations PRIMARY KEY, LocationName nvarchar(100) NOT NULL,
    LocationNameAr nvarchar(100) NOT NULL, LocationType nvarchar(20) NOT NULL, Emirate nvarchar(30) NOT NULL, Address nvarchar(200) NOT NULL,
    OpenedDate date NOT NULL, Counters tinyint NOT NULL);
CREATE TABLE dbo.Divisions (DivisionCode nvarchar(10) NOT NULL CONSTRAINT PK_Divisions PRIMARY KEY, DivisionName nvarchar(100) NOT NULL,
    DivisionNameAr nvarchar(100) NOT NULL, HeadEmployeeNumber nvarchar(10) NOT NULL, ReportsTo nvarchar(20) NOT NULL);
CREATE TABLE dbo.Departments (DepartmentCode nvarchar(10) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY, DepartmentName nvarchar(120) NOT NULL,
    DepartmentNameAr nvarchar(120) NOT NULL, DepartmentType nvarchar(20) NOT NULL, DivisionCode nvarchar(10) NOT NULL CONSTRAINT FK_Departments_Divisions REFERENCES dbo.Divisions,
    LocationCode nvarchar(10) NOT NULL CONSTRAINT FK_Departments_Locations REFERENCES dbo.Locations, HeadEmployeeNumber nvarchar(10) NULL);
CREATE TABLE dbo.Sections (SectionCode nvarchar(20) NOT NULL CONSTRAINT PK_Sections PRIMARY KEY,
    DepartmentCode nvarchar(10) NOT NULL CONSTRAINT FK_Sections_Departments REFERENCES dbo.Departments, SectionName nvarchar(120) NOT NULL,
    SectionNameAr nvarchar(120) NOT NULL, HeadEmployeeNumber nvarchar(10) NULL);
CREATE TABLE dbo.JobGrades (GradeCode nvarchar(4) NOT NULL CONSTRAINT PK_JobGrades PRIMARY KEY, GradeName nvarchar(60) NOT NULL, GradeNameAr nvarchar(60) NOT NULL,
    MinMonthlySalary decimal(18,2) NOT NULL, MaxMonthlySalary decimal(18,2) NOT NULL);
CREATE TABLE dbo.Positions (PositionCode nvarchar(20) NOT NULL CONSTRAINT PK_Positions PRIMARY KEY, PositionTitle nvarchar(100) NOT NULL, PositionTitleAr nvarchar(100) NOT NULL,
    GradeCode nvarchar(4) NOT NULL CONSTRAINT FK_Positions_JobGrades REFERENCES dbo.JobGrades, DepartmentCode nvarchar(10) NULL, SectionType nvarchar(20) NULL, PositionLevel nvarchar(20) NOT NULL);
CREATE TABLE dbo.PublicHolidays (HolidayDate date NOT NULL CONSTRAINT PK_PublicHolidays PRIMARY KEY, HolidayName nvarchar(100) NOT NULL, HolidayNameAr nvarchar(100) NOT NULL);
CREATE TABLE dbo.Shifts (ShiftCode nvarchar(4) NOT NULL CONSTRAINT PK_Shifts PRIMARY KEY, ShiftName nvarchar(60) NOT NULL, ShiftNameAr nvarchar(60) NOT NULL,
    StartTime time(0) NOT NULL, EndTime time(0) NOT NULL, WorksWeekends bit NOT NULL, WorksPublicHolidays bit NOT NULL);
CREATE TABLE dbo.Employees (EmployeeNumber nvarchar(10) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY, FirstName nvarchar(60) NOT NULL, LastName nvarchar(60) NOT NULL,
    FullName nvarchar(120) NOT NULL, FullNameAr nvarchar(120) NULL, Gender char(1) NOT NULL, DateOfBirth date NOT NULL,
    NationalityCode char(2) NOT NULL CONSTRAINT FK_Employees_Countries REFERENCES dbo.Countries, EmiratesIdNumber nvarchar(20) NOT NULL, PassportNumber nvarchar(20) NULL,
    MaritalStatus nvarchar(10) NOT NULL, HireDate date NOT NULL, TerminationDate date NULL, TerminationReason nvarchar(40) NULL, EmploymentStatus nvarchar(12) NOT NULL,
    DivisionCode nvarchar(10) NOT NULL CONSTRAINT FK_Employees_Divisions REFERENCES dbo.Divisions,
    DepartmentCode nvarchar(10) NOT NULL CONSTRAINT FK_Employees_Departments REFERENCES dbo.Departments,
    SectionCode nvarchar(20) NULL CONSTRAINT FK_Employees_Sections REFERENCES dbo.Sections,
    LocationCode nvarchar(10) NOT NULL CONSTRAINT FK_Employees_Locations REFERENCES dbo.Locations,
    PositionCode nvarchar(20) NOT NULL CONSTRAINT FK_Employees_Positions REFERENCES dbo.Positions, GradeCode nvarchar(4) NOT NULL CONSTRAINT FK_Employees_JobGrades REFERENCES dbo.JobGrades,
    PositionLevel nvarchar(20) NOT NULL, ManagerEmployeeNumber nvarchar(10) NULL, ShiftCode nvarchar(4) NOT NULL CONSTRAINT FK_Employees_Shifts REFERENCES dbo.Shifts,
    WeeklyOffDays nvarchar(7) NOT NULL, WorkEmail nvarchar(120) NULL, MobileNumber nvarchar(20) NOT NULL, UserId nvarchar(20) NULL);
CREATE TABLE dbo.EmployeeMovements (MovementId int NOT NULL CONSTRAINT PK_EmployeeMovements PRIMARY KEY,
    EmployeeNumber nvarchar(10) NOT NULL CONSTRAINT FK_EmployeeMovements_Employees REFERENCES dbo.Employees, MovementType nvarchar(20) NOT NULL, EffectiveDate date NOT NULL,
    FromDepartmentCode nvarchar(10) NOT NULL, FromSectionCode nvarchar(20) NULL, FromPositionCode nvarchar(20) NOT NULL, FromGradeCode nvarchar(4) NOT NULL,
    ToDepartmentCode nvarchar(10) NOT NULL, ToSectionCode nvarchar(20) NULL, ToPositionCode nvarchar(20) NOT NULL, ToGradeCode nvarchar(4) NOT NULL,
    Reason nvarchar(200) NULL, ApprovedByUserId nvarchar(20) NOT NULL, ApprovedAt datetime2(0) NOT NULL);
CREATE TABLE dbo.LeaveTypes (LeaveTypeCode nvarchar(4) NOT NULL CONSTRAINT PK_LeaveTypes PRIMARY KEY, LeaveTypeName nvarchar(60) NOT NULL, LeaveTypeNameAr nvarchar(60) NOT NULL,
    IsPaid bit NOT NULL, EntitlementDays smallint NOT NULL);
CREATE TABLE dbo.LeaveRequests (LeaveRequestId int NOT NULL CONSTRAINT PK_LeaveRequests PRIMARY KEY,
    EmployeeNumber nvarchar(10) NOT NULL CONSTRAINT FK_LeaveRequests_Employees REFERENCES dbo.Employees, DepartmentCode nvarchar(10) NOT NULL, SectionCode nvarchar(20) NULL,
    LeaveTypeCode nvarchar(4) NOT NULL CONSTRAINT FK_LeaveRequests_LeaveTypes REFERENCES dbo.LeaveTypes, StartDate date NOT NULL, EndDate date NOT NULL,
    WorkingDays smallint NOT NULL, Status nvarchar(12) NOT NULL, RequestedAt datetime2(0) NOT NULL, SubmittedByUserId nvarchar(20) NOT NULL,
    DecidedByUserId nvarchar(20) NULL, DecidedAt datetime2(0) NULL);
CREATE TABLE dbo.AttendanceRecords (AttendanceId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceRecords PRIMARY KEY,
    EmployeeNumber nvarchar(10) NOT NULL CONSTRAINT FK_AttendanceRecords_Employees REFERENCES dbo.Employees, AttendanceDate date NOT NULL,
    DepartmentCode nvarchar(10) NOT NULL, SectionCode nvarchar(20) NULL, LocationCode nvarchar(10) NOT NULL, ShiftCode nvarchar(4) NOT NULL,
    Status nvarchar(10) NOT NULL, CheckIn datetime2(0) NULL, CheckOut datetime2(0) NULL, LateMinutes smallint NOT NULL, OvertimeMinutes smallint NOT NULL,
    WorkedMinutes smallint NOT NULL, CONSTRAINT UQ_AttendanceRecords UNIQUE (EmployeeNumber, AttendanceDate));
CREATE TABLE dbo.Systems (SystemCode nvarchar(10) NOT NULL CONSTRAINT PK_Systems PRIMARY KEY, SystemName nvarchar(60) NOT NULL, SystemNameAr nvarchar(60) NOT NULL,
    GoLiveDate date NOT NULL, OwnerDepartmentCode nvarchar(10) NOT NULL);
CREATE TABLE dbo.SystemUsers (UserId nvarchar(20) NOT NULL CONSTRAINT PK_SystemUsers PRIMARY KEY, EmployeeNumber nvarchar(10) NULL CONSTRAINT FK_SystemUsers_Employees REFERENCES dbo.Employees,
    AccountType nvarchar(10) NOT NULL, DisplayName nvarchar(120) NOT NULL, Email nvarchar(120) NULL, Status nvarchar(10) NOT NULL, CreatedDate date NOT NULL, DisabledDate date NULL);
CREATE TABLE dbo.UserSystemAccess (AccessId int NOT NULL CONSTRAINT PK_UserSystemAccess PRIMARY KEY,
    UserId nvarchar(20) NOT NULL CONSTRAINT FK_UserSystemAccess_SystemUsers REFERENCES dbo.SystemUsers,
    SystemCode nvarchar(10) NOT NULL CONSTRAINT FK_UserSystemAccess_Systems REFERENCES dbo.Systems, RoleCode nvarchar(40) NOT NULL, ApprovalLimitAed decimal(18,2) NULL,
    GrantedDate date NOT NULL, RevokedDate date NULL);
GO
"""

COLUMNS = {
    "Countries": ["CountryCode", "CountryName", "CountryNameAr", "Nationality"],
    "Locations": ["LocationCode", "LocationName", "LocationNameAr", "LocationType", "Emirate", "Address", "OpenedDate", "Counters"],
    "Divisions": ["DivisionCode", "DivisionName", "DivisionNameAr", "HeadEmployeeNumber", "ReportsTo"],
    "Departments": ["DepartmentCode", "DepartmentName", "DepartmentNameAr", "DepartmentType", "DivisionCode", "LocationCode", "HeadEmployeeNumber"],
    "Sections": ["SectionCode", "DepartmentCode", "SectionName", "SectionNameAr", "HeadEmployeeNumber"],
    "JobGrades": ["GradeCode", "GradeName", "GradeNameAr", "MinMonthlySalary", "MaxMonthlySalary"],
    "Positions": ["PositionCode", "PositionTitle", "PositionTitleAr", "GradeCode", "DepartmentCode", "SectionType", "PositionLevel"],
    "PublicHolidays": ["HolidayDate", "HolidayName", "HolidayNameAr"],
    "Shifts": ["ShiftCode", "ShiftName", "ShiftNameAr", "StartTime", "EndTime", "WorksWeekends", "WorksPublicHolidays"],
    "Employees": ["EmployeeNumber", "FirstName", "LastName", "FullName", "FullNameAr", "Gender", "DateOfBirth", "NationalityCode", "EmiratesIdNumber", "PassportNumber",
                  "MaritalStatus", "HireDate", "TerminationDate", "TerminationReason", "EmploymentStatus", "DivisionCode", "DepartmentCode", "SectionCode", "LocationCode",
                  "PositionCode", "GradeCode", "PositionLevel", "ManagerEmployeeNumber", "ShiftCode", "WeeklyOffDays", "WorkEmail", "MobileNumber", "UserId"],
    "EmployeeMovements": ["MovementId", "EmployeeNumber", "MovementType", "EffectiveDate", "FromDepartmentCode", "FromSectionCode", "FromPositionCode", "FromGradeCode",
                          "ToDepartmentCode", "ToSectionCode", "ToPositionCode", "ToGradeCode", "Reason", "ApprovedByUserId", "ApprovedAt"],
    "LeaveTypes": ["LeaveTypeCode", "LeaveTypeName", "LeaveTypeNameAr", "IsPaid", "EntitlementDays"],
    "LeaveRequests": ["LeaveRequestId", "EmployeeNumber", "DepartmentCode", "SectionCode", "LeaveTypeCode", "StartDate", "EndDate", "WorkingDays", "Status",
                      "RequestedAt", "SubmittedByUserId", "DecidedByUserId", "DecidedAt"],
    "Systems": ["SystemCode", "SystemName", "SystemNameAr", "GoLiveDate", "OwnerDepartmentCode"],
    "SystemUsers": ["UserId", "EmployeeNumber", "AccountType", "DisplayName", "Email", "Status", "CreatedDate", "DisabledDate"],
    "UserSystemAccess": ["AccessId", "UserId", "SystemCode", "RoleCode", "ApprovalLimitAed", "GrantedDate", "RevokedDate"],
}
LOAD_ORDER = ["Countries", "Locations", "Divisions", "Departments", "Sections", "JobGrades", "Positions", "PublicHolidays", "Shifts", "Employees",
              "EmployeeMovements", "LeaveTypes", "LeaveRequests", "Systems", "SystemUsers", "UserSystemAccess"]

ATTENDANCE = f"""-- Daily attendance, set-based: every rostered working day inside the employment period. Branches and Head Office close on
-- UAE public holidays; Contact Centre and security staff work seven-day rotas with two weekly days off. Approved leave and
-- the unauthorised absences listed below are the same as in bank_people.py, so every system agrees on who was at work.
CREATE INDEX IX_LeaveRequests_Employee ON dbo.LeaveRequests (EmployeeNumber, Status) INCLUDE (StartDate, EndDate);
CREATE TABLE #Absences (EmployeeNumber nvarchar(10) NOT NULL, AbsenceDate date NOT NULL, PRIMARY KEY (EmployeeNumber, AbsenceDate));
{{absences}}
{TALLY_CTE},
Days AS (SELECT DATEADD(DAY, n - 1, CAST('2025-01-01' AS date)) AS D, ((DATEDIFF(DAY, CAST('1900-01-01' AS date), DATEADD(DAY, n - 1, CAST('2025-01-01' AS date))) % 7) + 1) AS Dow
         FROM Tally WHERE n <= 365)
INSERT INTO dbo.AttendanceRecords (EmployeeNumber, AttendanceDate, DepartmentCode, SectionCode, LocationCode, ShiftCode, Status, CheckIn, CheckOut, LateMinutes, OvertimeMinutes, WorkedMinutes)
SELECT e.EmployeeNumber, d.D, COALESCE(mv.FromDepartmentCode, e.DepartmentCode), CASE WHEN mv.MovementId IS NULL THEN e.SectionCode ELSE mv.FromSectionCode END,
       COALESCE(loc.LocationCode, e.LocationCode), e.ShiftCode, st.Status,
       CASE WHEN st.Status IN (N'Present', N'Late') THEN DATEADD(MINUTE, t.InOffset, DATEADD(MINUTE, DATEDIFF(MINUTE, CAST('00:00' AS time), s.StartTime), CAST(d.D AS datetime2(0)))) END,
       CASE WHEN st.Status IN (N'Present', N'Late') THEN DATEADD(MINUTE, t.OutOffset + CASE WHEN s.EndTime < s.StartTime THEN 1440 ELSE 0 END, DATEADD(MINUTE, DATEDIFF(MINUTE, CAST('00:00' AS time), s.EndTime), CAST(d.D AS datetime2(0)))) END,
       CASE WHEN st.Status = N'Late' THEN t.InOffset ELSE 0 END,
       CASE WHEN st.Status IN (N'Present', N'Late') AND t.OutOffset > 30 THEN t.OutOffset ELSE 0 END,
       CASE WHEN st.Status IN (N'Present', N'Late') THEN DATEDIFF(MINUTE, s.StartTime, s.EndTime) + CASE WHEN s.EndTime < s.StartTime THEN 1440 ELSE 0 END
            - t.InOffset + t.OutOffset ELSE 0 END
FROM dbo.Employees e
JOIN dbo.Shifts s ON s.ShiftCode = e.ShiftCode
JOIN Days d ON d.D >= e.HireDate AND (e.TerminationDate IS NULL OR d.D <= e.TerminationDate)
LEFT JOIN dbo.PublicHolidays h ON h.HolidayDate = d.D
OUTER APPLY (SELECT TOP (1) m.* FROM dbo.EmployeeMovements m WHERE m.EmployeeNumber = e.EmployeeNumber AND m.EffectiveDate > d.D ORDER BY m.EffectiveDate) mv
OUTER APPLY (SELECT CASE WHEN mv.FromDepartmentCode LIKE N'BR-%' THEN mv.FromDepartmentCode WHEN mv.MovementId IS NOT NULL THEN N'HO' END AS LocationCode) loc
CROSS APPLY gen.Rand(CAST(SUBSTRING(e.EmployeeNumber, 2, 9) AS bigint) * 1000 + DATEPART(DAYOFYEAR, d.D)) r
CROSS APPLY (SELECT CASE
        WHEN EXISTS (SELECT 1 FROM dbo.LeaveRequests lr WHERE lr.EmployeeNumber = e.EmployeeNumber AND lr.Status = N'Approved' AND d.D BETWEEN lr.StartDate AND lr.EndDate) THEN N'OnLeave'
        WHEN EXISTS (SELECT 1 FROM #Absences a WHERE a.EmployeeNumber = e.EmployeeNumber AND a.AbsenceDate = d.D) THEN N'Absent'
        WHEN r.R1 < 0.07 THEN N'Late' ELSE N'Present' END AS Status) st
CROSS APPLY (SELECT CASE WHEN st.Status = N'Late' THEN 5 + CAST(r.R2 * 55 AS int) ELSE -CAST(r.R2 * 15 AS int) END AS InOffset,
                    CASE WHEN r.R3 < 0.18 THEN 31 + CAST(r.R4 * 120 AS int) ELSE CAST(r.R4 * 20 AS int) END AS OutOffset) t
WHERE CHARINDEX(CAST(d.Dow AS nchar(1)), e.WeeklyOffDays) = 0
  AND NOT (e.ShiftCode IN (N'HO', N'BR') AND h.HolidayDate IS NOT NULL);
GO
"""


# Re-runs drop every foreign key first, so the order of DROP TABLE never matters (some keys are added after loading).
DROP_FOREIGN_KEYS = """DECLARE @DropFks nvarchar(max) = N'';
SELECT @DropFks += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';'
FROM sys.foreign_keys;
EXEC sys.sp_executesql @DropFks;
GO
"""


def build():
    rows = _rows()
    parts = [prologue(DB, "wbpersonnel_owner", "WbPersonnel#Owner#2026", "wbpersonnel_reader", "WbPersonnel#Reader#2026",
                      "WabiBankPersonnelDB — Wabi Bank personnel, structure, attendance, leave and identities (sample)",
                      "Divisions, Head Office departments and branches, sections, employees, transfers, leave, daily attendance, system users and access, 2025."),
             DROP_FOREIGN_KEYS, drop_tables(TABLES), SCHEMA]
    for t in LOAD_ORDER:
        parts.append(insert(f"dbo.{t}", COLUMNS[t], rows[t]) + "\nGO\n")
    absences = insert("#Absences", ["EmployeeNumber", "AbsenceDate"], rows["Absences"])
    parts.append(ATTENDANCE.replace("{absences}", absences))
    parts.append("""ALTER TABLE dbo.Departments ADD CONSTRAINT FK_Departments_Head FOREIGN KEY (HeadEmployeeNumber) REFERENCES dbo.Employees;
ALTER TABLE dbo.Sections ADD CONSTRAINT FK_Sections_Head FOREIGN KEY (HeadEmployeeNumber) REFERENCES dbo.Employees;
ALTER TABLE dbo.Divisions ADD CONSTRAINT FK_Divisions_Head FOREIGN KEY (HeadEmployeeNumber) REFERENCES dbo.Employees;
ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Manager FOREIGN KEY (ManagerEmployeeNumber) REFERENCES dbo.Employees;
ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_SystemUsers FOREIGN KEY (UserId) REFERENCES dbo.SystemUsers;
CREATE INDEX IX_AttendanceRecords_Date ON dbo.AttendanceRecords (AttendanceDate, DepartmentCode);
GO
""")
    summary = """SELECT N'Employees' AS TableName, COUNT(*) AS Rows FROM dbo.Employees UNION ALL SELECT N'Active at year end', COUNT(*) FROM dbo.Employees WHERE TerminationDate IS NULL
UNION ALL SELECT N'Departments and branches', COUNT(*) FROM dbo.Departments UNION ALL SELECT N'Sections', COUNT(*) FROM dbo.Sections
UNION ALL SELECT N'AttendanceRecords', COUNT(*) FROM dbo.AttendanceRecords UNION ALL SELECT N'LeaveRequests', COUNT(*) FROM dbo.LeaveRequests
UNION ALL SELECT N'SystemUsers', COUNT(*) FROM dbo.SystemUsers UNION ALL SELECT N'UserSystemAccess', COUNT(*) FROM dbo.UserSystemAccess;
GO"""
    parts.append(epilogue(DB, summary))
    stats = {k: len(v) for k, v in rows.items()}
    return "\n".join(parts), stats


if __name__ == "__main__":
    text, stats = build()
    path = os.path.join(HERE, "01-create-wabibankpersonneldb.sql")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(f"{os.path.basename(path)}: {len(text) // 1024} KB {stats}")
