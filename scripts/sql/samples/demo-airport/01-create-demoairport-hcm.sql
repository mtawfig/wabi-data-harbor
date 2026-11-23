-- ==============================================================================================================
-- DemoAirport_HCM — Human Capital Management of Demo Airport (sample)
-- Organization structure, 1,864 employees (1,268 in service; 596 resigned, terminated, retired, end of contract,
-- deceased or absconded), compensation, monthly payroll, leave, daily attendance, performance, training,
-- recruitment, documents, dependents and disciplinary actions, from 1 January 2023 until the day the script runs.
--
-- Run it FIRST (as a sysadmin, in SSMS or sqlcmd): 03 (Assets) and 04 (RMS) take their staff from this database.
--   1. creates the database [DemoAirport_HCM] if it does not exist;
--   2. creates the read-only login [demoairport_reader] (db_datareader), the one to register in Wabi Insight;
--   3. drops and recreates every table and generates the data (about 4 minutes on SQL Server Express 2022).
-- Re-running resets the data. Change the password below before using this outside a sandbox.
-- The data is synthetic and deterministic for a given run date. People are invented; any resemblance is accidental.
-- ==============================================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
USE [master];
GO
IF DB_ID(N'DemoAirport_HCM') IS NULL CREATE DATABASE [DemoAirport_HCM];
GO
ALTER DATABASE [DemoAirport_HCM] SET RECOVERY SIMPLE;
IF SUSER_ID(N'demoairport_reader') IS NULL
    CREATE LOGIN [demoairport_reader] WITH PASSWORD = N'DemoAirport#Reader#2026', CHECK_POLICY = ON;
GO
USE [DemoAirport_HCM];
GO
IF DATABASE_PRINCIPAL_ID(N'demoairport_reader') IS NULL CREATE USER [demoairport_reader] FOR LOGIN [demoairport_reader];
ALTER ROLE db_datareader ADD MEMBER [demoairport_reader];
GO
-- Reset: drop every foreign key, table and helper function of a previous run.
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id;
SELECT @sql += N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' FROM sys.tables;
EXEC sys.sp_executesql @sql;
DROP FUNCTION IF EXISTS dbo.R;
IF SCHEMA_ID(N'org') IS NULL EXEC (N'CREATE SCHEMA org');
IF SCHEMA_ID(N'hr') IS NULL EXEC (N'CREATE SCHEMA hr');
IF SCHEMA_ID(N'pay') IS NULL EXEC (N'CREATE SCHEMA pay');
IF SCHEMA_ID(N'ta') IS NULL EXEC (N'CREATE SCHEMA ta');
IF SCHEMA_ID(N'talent') IS NULL EXEC (N'CREATE SCHEMA talent');
GO
-- Deterministic pseudo-random number (0 .. 2,147,483,647) from a key and a salt.
CREATE FUNCTION dbo.R (@k bigint, @s int) RETURNS int WITH SCHEMABINDING AS
BEGIN
    RETURN CAST(HASHBYTES('MD5', CONCAT(@k, N':', @s)) AS int) & 2147483647;
END
GO
-- ---------------------------------------------------------------------------------------------------- tables
CREATE TABLE dbo.SampleInfo (DatabaseName sysname NOT NULL, DataFrom date NOT NULL, DataTo date NOT NULL, GeneratedAt datetime2(0) NOT NULL);

CREATE TABLE org.Country (CountryCode char(2) NOT NULL PRIMARY KEY, CountryName nvarchar(60) NOT NULL, Region nvarchar(40) NOT NULL);
CREATE TABLE org.Grade (GradeLevel tinyint NOT NULL PRIMARY KEY, GradeName nvarchar(40) NOT NULL, Band nvarchar(30) NOT NULL,
    MinBasicSalary decimal(10,2) NOT NULL, MaxBasicSalary decimal(10,2) NOT NULL, AnnualLeaveDays tinyint NOT NULL);
CREATE TABLE org.Department (DepartmentId int NOT NULL PRIMARY KEY, DepartmentCode varchar(5) NOT NULL UNIQUE, DepartmentName nvarchar(80) NOT NULL,
    DepartmentNameAr nvarchar(80) NOT NULL, CostCenter varchar(10) NOT NULL, Division nvarchar(40) NOT NULL);
CREATE TABLE org.Section (SectionId int NOT NULL PRIMARY KEY, SectionCode varchar(5) NOT NULL UNIQUE, SectionName nvarchar(80) NOT NULL,
    DepartmentId int NOT NULL REFERENCES org.Department (DepartmentId));
CREATE TABLE org.Position (PositionId int IDENTITY(1,1) NOT NULL PRIMARY KEY, PositionCode varchar(12) NOT NULL UNIQUE, PositionTitle nvarchar(100) NOT NULL,
    DepartmentId int NOT NULL REFERENCES org.Department (DepartmentId), SectionId int NULL REFERENCES org.Section (SectionId),
    GradeLevel tinyint NOT NULL REFERENCES org.Grade (GradeLevel), JobLevel nvarchar(20) NOT NULL, IsShiftBased bit NOT NULL);

CREATE TABLE hr.Employee (
    EmployeeId int NOT NULL PRIMARY KEY, EmployeeNumber varchar(10) NOT NULL UNIQUE,
    FirstName nvarchar(40) NOT NULL, LastName nvarchar(40) NOT NULL, FullName nvarchar(90) NOT NULL,
    Gender char(1) NOT NULL, BirthDate date NOT NULL, NationalityCode char(2) NOT NULL REFERENCES org.Country (CountryCode),
    MaritalStatus varchar(10) NOT NULL, WorkEmail nvarchar(120) NOT NULL, MobileNumber varchar(20) NOT NULL,
    HireDate date NOT NULL, ProbationEndDate date NOT NULL,
    DepartmentId int NOT NULL REFERENCES org.Department (DepartmentId), SectionId int NOT NULL REFERENCES org.Section (SectionId),
    PositionId int NOT NULL REFERENCES org.Position (PositionId), GradeLevel tinyint NOT NULL REFERENCES org.Grade (GradeLevel),
    ManagerEmployeeId int NULL REFERENCES hr.Employee (EmployeeId),
    EmploymentType varchar(12) NOT NULL, ContractType varchar(12) NOT NULL, IsShiftWorker bit NOT NULL,
    EmploymentStatus varchar(15) NOT NULL, SeparationDate date NULL,
    BasicSalary decimal(10,2) NOT NULL, HousingAllowance decimal(10,2) NOT NULL, TransportAllowance decimal(10,2) NOT NULL,
    OtherAllowance decimal(10,2) NOT NULL, TotalMonthlySalary AS (BasicSalary + HousingAllowance + TransportAllowance + OtherAllowance) PERSISTED,
    BankCode varchar(10) NOT NULL);
CREATE TABLE hr.EmploymentHistory (EmploymentHistoryId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    EffectiveDate date NOT NULL, ActionType varchar(15) NOT NULL, DepartmentId int NOT NULL REFERENCES org.Department (DepartmentId),
    SectionId int NOT NULL REFERENCES org.Section (SectionId), PositionId int NOT NULL REFERENCES org.Position (PositionId),
    GradeLevel tinyint NOT NULL, Remarks nvarchar(200) NULL);
CREATE TABLE hr.Separation (SeparationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL UNIQUE REFERENCES hr.Employee (EmployeeId),
    SeparationType varchar(15) NOT NULL, Reason nvarchar(80) NOT NULL, NoticeDate date NOT NULL, LastWorkingDate date NOT NULL,
    ServiceYears decimal(5,2) NOT NULL, IsRegretted bit NOT NULL, EligibleForRehire bit NOT NULL, ExitInterviewScore tinyint NULL);
CREATE TABLE hr.Dependent (DependentId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    DependentName nvarchar(90) NOT NULL, Relationship varchar(10) NOT NULL, Gender char(1) NOT NULL, BirthDate date NOT NULL, IsMedicallyInsured bit NOT NULL);
CREATE TABLE hr.EmployeeDocument (DocumentId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    DocumentType varchar(25) NOT NULL, DocumentNumber varchar(20) NOT NULL, IssueDate date NOT NULL, ExpiryDate date NOT NULL,
    IssuingAuthority nvarchar(60) NOT NULL);
CREATE TABLE hr.DisciplinaryAction (DisciplinaryActionId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    ActionDate date NOT NULL, ActionType varchar(20) NOT NULL, Category nvarchar(40) NOT NULL, Outcome nvarchar(40) NOT NULL, IssuedByEmployeeId int NULL REFERENCES hr.Employee (EmployeeId));

CREATE TABLE pay.EmployeeCompensation (CompensationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    EffectiveDate date NOT NULL, ChangeReason varchar(20) NOT NULL, BasicSalary decimal(10,2) NOT NULL, HousingAllowance decimal(10,2) NOT NULL,
    TransportAllowance decimal(10,2) NOT NULL, OtherAllowance decimal(10,2) NOT NULL, IncreasePercent decimal(5,2) NULL);
CREATE TABLE pay.PayComponent (ComponentCode varchar(10) NOT NULL PRIMARY KEY, ComponentName nvarchar(60) NOT NULL, ComponentType varchar(10) NOT NULL);
CREATE TABLE pay.PayrollRun (PayrollRunId int NOT NULL PRIMARY KEY, PayYear smallint NOT NULL, PayMonth tinyint NOT NULL, PeriodStart date NOT NULL,
    PeriodEnd date NOT NULL, PaymentDate date NOT NULL, RunStatus varchar(10) NOT NULL, EmployeeCount int NOT NULL, TotalGross decimal(14,2) NOT NULL,
    TotalNet decimal(14,2) NOT NULL);
CREATE TABLE pay.Payslip (PayslipId int IDENTITY(1,1) NOT NULL PRIMARY KEY, PayrollRunId int NOT NULL REFERENCES pay.PayrollRun (PayrollRunId),
    EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId), DepartmentId int NOT NULL REFERENCES org.Department (DepartmentId),
    PaidDays tinyint NOT NULL, BasicPay decimal(10,2) NOT NULL, HousingAllowance decimal(10,2) NOT NULL, TransportAllowance decimal(10,2) NOT NULL,
    OtherAllowance decimal(10,2) NOT NULL, OvertimeHours decimal(6,2) NOT NULL, OvertimePay decimal(10,2) NOT NULL, Bonus decimal(10,2) NOT NULL,
    EndOfServiceGratuity decimal(12,2) NOT NULL, GrossPay decimal(12,2) NOT NULL, PensionDeduction decimal(10,2) NOT NULL,
    LoanDeduction decimal(10,2) NOT NULL, AbsenceDeduction decimal(10,2) NOT NULL, TotalDeductions decimal(10,2) NOT NULL, NetPay decimal(12,2) NOT NULL,
    CONSTRAINT UQ_Payslip UNIQUE (PayrollRunId, EmployeeId));
CREATE TABLE pay.PayslipLine (PayslipLineId int IDENTITY(1,1) NOT NULL PRIMARY KEY, PayslipId int NOT NULL REFERENCES pay.Payslip (PayslipId),
    ComponentCode varchar(10) NOT NULL REFERENCES pay.PayComponent (ComponentCode), Amount decimal(12,2) NOT NULL);

CREATE TABLE ta.Shift (ShiftCode varchar(5) NOT NULL PRIMARY KEY, ShiftName nvarchar(40) NOT NULL, StartTime time(0) NOT NULL, EndTime time(0) NOT NULL, PaidHours decimal(4,2) NOT NULL);
CREATE TABLE ta.PublicHoliday (HolidayDate date NOT NULL PRIMARY KEY, HolidayName nvarchar(60) NOT NULL);
CREATE TABLE ta.LeaveType (LeaveTypeCode varchar(5) NOT NULL PRIMARY KEY, LeaveTypeName nvarchar(40) NOT NULL, LeaveTypeNameAr nvarchar(40) NOT NULL, IsPaid bit NOT NULL);
CREATE TABLE ta.LeaveRequest (LeaveRequestId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    LeaveTypeCode varchar(5) NOT NULL REFERENCES ta.LeaveType (LeaveTypeCode), RequestDate date NOT NULL, StartDate date NOT NULL, EndDate date NOT NULL,
    LeaveDays AS (DATEDIFF(day, StartDate, EndDate) + 1) PERSISTED, RequestStatus varchar(10) NOT NULL, ApprovedByEmployeeId int NULL REFERENCES hr.Employee (EmployeeId));
CREATE TABLE ta.LeaveBalance (LeaveBalanceId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    LeaveYear smallint NOT NULL, EntitledDays decimal(5,1) NOT NULL, CarriedForwardDays decimal(5,1) NOT NULL, TakenDays decimal(5,1) NOT NULL,
    BalanceDays AS (EntitledDays + CarriedForwardDays - TakenDays) PERSISTED, CONSTRAINT UQ_LeaveBalance UNIQUE (EmployeeId, LeaveYear));
CREATE TABLE ta.Attendance (AttendanceId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    WorkDate date NOT NULL, ShiftCode varchar(5) NOT NULL REFERENCES ta.Shift (ShiftCode), ScheduledStart datetime2(0) NOT NULL, ScheduledEnd datetime2(0) NOT NULL,
    ClockIn datetime2(0) NULL, ClockOut datetime2(0) NULL, WorkedHours decimal(4,2) NOT NULL, LateMinutes smallint NOT NULL,
    OvertimeHours decimal(4,2) NOT NULL, AttendanceStatus varchar(10) NOT NULL, CONSTRAINT UQ_Attendance UNIQUE (EmployeeId, WorkDate));

CREATE TABLE talent.PerformanceReview (PerformanceReviewId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    ReviewYear smallint NOT NULL, ReviewDate date NOT NULL, ReviewerEmployeeId int NULL REFERENCES hr.Employee (EmployeeId), GoalsScore decimal(4,2) NOT NULL,
    CompetencyScore decimal(4,2) NOT NULL, OverallRating tinyint NOT NULL, RatingLabel nvarchar(30) NOT NULL, PromotionRecommended bit NOT NULL,
    CONSTRAINT UQ_Review UNIQUE (EmployeeId, ReviewYear));
CREATE TABLE talent.TrainingCourse (CourseId int NOT NULL PRIMARY KEY, CourseCode varchar(10) NOT NULL UNIQUE, CourseName nvarchar(100) NOT NULL,
    Category nvarchar(40) NOT NULL, DurationHours decimal(5,1) NOT NULL, ValidityMonths smallint NULL, IsMandatory bit NOT NULL,
    AudienceDepartmentCode varchar(5) NULL, CostPerSeat decimal(8,2) NOT NULL);
CREATE TABLE talent.TrainingRecord (TrainingRecordId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EmployeeId int NOT NULL REFERENCES hr.Employee (EmployeeId),
    CourseId int NOT NULL REFERENCES talent.TrainingCourse (CourseId), CompletionDate date NOT NULL, ExpiryDate date NULL, Score tinyint NOT NULL,
    Result varchar(10) NOT NULL, DeliveryMethod varchar(12) NOT NULL);
CREATE TABLE talent.JobRequisition (RequisitionId int IDENTITY(1,1) NOT NULL PRIMARY KEY, RequisitionNumber varchar(12) NOT NULL UNIQUE,
    DepartmentId int NOT NULL REFERENCES org.Department (DepartmentId), PositionId int NOT NULL REFERENCES org.Position (PositionId), OpenedDate date NOT NULL,
    ClosedDate date NULL, RequisitionStatus varchar(10) NOT NULL, Reason varchar(15) NOT NULL, HiredEmployeeId int NULL REFERENCES hr.Employee (EmployeeId));
CREATE TABLE talent.JobApplication (ApplicationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, RequisitionId int NOT NULL REFERENCES talent.JobRequisition (RequisitionId),
    CandidateName nvarchar(90) NOT NULL, NationalityCode char(2) NOT NULL REFERENCES org.Country (CountryCode), Source varchar(15) NOT NULL,
    AppliedDate date NOT NULL, Stage varchar(12) NOT NULL, InterviewScore tinyint NULL, IsInternal bit NOT NULL);
GO
-- ------------------------------------------------------------------------------------------------ reference data
DECLARE @Start date = '2023-01-01', @Today date = CAST(GETDATE() AS date);
INSERT INTO dbo.SampleInfo VALUES (N'DemoAirport_HCM', @Start, @Today, SYSDATETIME());

INSERT INTO org.Country VALUES
('AE',N'United Arab Emirates',N'GCC'),('IN',N'India',N'South Asia'),('PK',N'Pakistan',N'South Asia'),('PH',N'Philippines',N'South-East Asia'),
('EG',N'Egypt',N'Arab countries'),('JO',N'Jordan',N'Arab countries'),('LB',N'Lebanon',N'Arab countries'),('SY',N'Syria',N'Arab countries'),
('SD',N'Sudan',N'Arab countries'),('MA',N'Morocco',N'Arab countries'),('TN',N'Tunisia',N'Arab countries'),('BD',N'Bangladesh',N'South Asia'),
('NP',N'Nepal',N'South Asia'),('LK',N'Sri Lanka',N'South Asia'),('GB',N'United Kingdom',N'Europe'),('US',N'United States',N'Americas'),
('AU',N'Australia',N'Oceania'),('ZA',N'South Africa',N'Africa'),('FR',N'France',N'Europe'),('DE',N'Germany',N'Europe'),
('KE',N'Kenya',N'Africa'),('NG',N'Nigeria',N'Africa'),('UG',N'Uganda',N'Africa');

INSERT INTO org.Grade (GradeLevel, GradeName, Band, MinBasicSalary, MaxBasicSalary, AnnualLeaveDays)
SELECT g.value, CONCAT(N'Grade ', g.value),
       CASE WHEN g.value <= 4 THEN N'Operational' WHEN g.value <= 7 THEN N'Skilled' WHEN g.value <= 9 THEN N'Professional' WHEN g.value <= 11 THEN N'Management' ELSE N'Executive' END,
       ROUND(2200 * POWER(1.24, g.value - 1), -2), ROUND(2200 * POWER(1.24, g.value - 1) * 1.45, -2), CASE WHEN g.value >= 8 THEN 30 ELSE 22 END
FROM GENERATE_SERIES(1, 13) g;

INSERT INTO org.Department VALUES
(1,'AOP',N'Airside Operations',N'عمليات ساحة المطار','CC-1100',N'Operations'),
(2,'TOP',N'Terminal Operations and Passenger Services',N'عمليات المبنى وخدمات المسافرين','CC-1200',N'Operations'),
(3,'SEC',N'Aviation Security',N'أمن الطيران','CC-1300',N'Operations'),
(4,'RFF',N'Rescue and Fire Fighting',N'الإنقاذ ومكافحة الحرائق','CC-1400',N'Operations'),
(5,'ENG',N'Engineering and Maintenance',N'الهندسة والصيانة','CC-2100',N'Technical'),
(6,'GHS',N'Ground Handling Services',N'خدمات المناولة الأرضية','CC-1500',N'Operations'),
(7,'CGO',N'Cargo Operations',N'عمليات الشحن','CC-1600',N'Operations'),
(8,'ITD',N'Information Technology',N'تقنية المعلومات','CC-2200',N'Technical'),
(9,'FIN',N'Finance and Procurement',N'المالية والمشتريات','CC-3100',N'Corporate'),
(10,'HRA',N'Human Resources and Administration',N'الموارد البشرية والشؤون الإدارية','CC-3200',N'Corporate'),
(11,'COM',N'Commercial and Business Development',N'الشؤون التجارية وتطوير الأعمال','CC-3300',N'Corporate'),
(12,'SHE',N'Safety, Quality and Environment',N'السلامة والجودة والبيئة','CC-2300',N'Technical'),
(13,'CEO',N'Executive Office, Strategy and Legal',N'المكتب التنفيذي والاستراتيجية والشؤون القانونية','CC-3000',N'Corporate'),
(14,'CSC',N'Customer Experience and Call Centre',N'تجربة المتعاملين ومركز الاتصال','CC-1700',N'Operations');

INSERT INTO org.Section (SectionId, SectionCode, SectionName, DepartmentId)
SELECT ROW_NUMBER() OVER (ORDER BY d.DepartmentId, v.Ord), v.Code, v.Name, d.DepartmentId
FROM (VALUES
('AOP',1,'AFO',N'Airfield Operations'),('AOP',2,'APM',N'Apron Management'),('AOP',3,'OCC',N'Operations Control Centre'),('AOP',4,'WAS',N'Wildlife and Airfield Safety'),
('TOP',1,'TDM',N'Terminal Duty Management'),('TOP',2,'PAX',N'Passenger Services'),('TOP',3,'BAG',N'Baggage Operations'),('TOP',4,'PRM',N'Facilitation and Special Assistance'),
('SEC',1,'PSC',N'Passenger Screening'),('SEC',2,'ACP',N'Access Control and Patrol'),('SEC',3,'HBS',N'Hold Baggage Screening'),('SEC',4,'SCR',N'Security Control Room and Training'),
('RFF',1,'FSA',N'Fire Station A'),('RFF',2,'FSB',N'Fire Station B'),('RFF',3,'FTP',N'Fire Training and Prevention'),
('ENG',1,'ELE',N'Electrical and Airfield Lighting'),('ENG',2,'MEC',N'Mechanical and HVAC'),('ENG',3,'BHS',N'Baggage Handling Systems'),('ENG',4,'CIV',N'Civil and Facilities'),('ENG',5,'MPL',N'Maintenance Planning'),
('GHS',1,'RMP',N'Ramp Services'),('GHS',2,'CKI',N'Passenger Check-in'),('GHS',3,'LDC',N'Load Control'),('GHS',4,'GSE',N'Ground Support Equipment Operations'),
('CGO',1,'IMP',N'Cargo Import'),('CGO',2,'EXP',N'Cargo Export'),('CGO',3,'WHS',N'Warehouse and Special Cargo'),
('ITD',1,'INF',N'Infrastructure and Networks'),('ITD',2,'APS',N'Airport Systems'),('ITD',3,'SDK',N'Service Desk'),
('FIN',1,'ACC',N'Financial Accounting'),('FIN',2,'PRC',N'Procurement and Contracts'),('FIN',3,'TRP',N'Treasury and Payroll'),
('HRA',1,'TAC',N'Talent Acquisition'),('HRA',2,'HRO',N'HR Operations'),('HRA',3,'LND',N'Learning and Development'),('HRA',4,'ADM',N'Administration'),
('COM',1,'RET',N'Retail and Concessions'),('COM',2,'AVM',N'Aviation Marketing'),('COM',3,'PRP',N'Property and Advertising'),
('SHE',1,'SMS',N'Safety Management'),('SHE',2,'QAC',N'Quality and Compliance'),('SHE',3,'ENV',N'Environment'),
('CEO',1,'EXO',N'Executive Office'),('CEO',2,'STR',N'Strategy and Planning'),('CEO',3,'LIA',N'Legal and Internal Audit'),
('CSC',1,'CCT',N'Call Centre'),('CSC',2,'CFB',N'Customer Feedback')) v (Dept, Ord, Code, Name)
JOIN org.Department d ON d.DepartmentCode = v.Dept;

-- Staff positions with the share of the department's staff that holds each (Weight).
CREATE TABLE #Pos (Dept varchar(5), Ord int, Title nvarchar(100), Grade tinyint, Weight int, IsShift bit);
INSERT INTO #Pos VALUES
('AOP',1,N'Airside Operations Officer',6,40,1),('AOP',2,N'Senior Airside Operations Officer',8,15,1),('AOP',3,N'Apron Controller',7,20,1),('AOP',4,N'OCC Duty Officer',8,15,1),('AOP',5,N'Airside Operations Supervisor',9,10,1),
('TOP',1,N'Passenger Service Agent',4,45,1),('TOP',2,N'Senior Passenger Service Agent',5,20,1),('TOP',3,N'Terminal Duty Officer',7,12,1),('TOP',4,N'Baggage Services Agent',4,15,1),('TOP',5,N'Terminal Supervisor',9,8,1),
('SEC',1,N'Security Officer',4,55,1),('SEC',2,N'Senior Security Officer',5,20,1),('SEC',3,N'Screening Supervisor',7,12,1),('SEC',4,N'Security Control Room Operator',6,8,1),('SEC',5,N'Aviation Security Instructor',8,5,0),
('RFF',1,N'Firefighter',5,55,1),('RFF',2,N'Senior Firefighter',6,20,1),('RFF',3,N'Crew Commander',8,15,1),('RFF',4,N'Watch Commander',9,6,1),('RFF',5,N'Fire Safety Inspector',7,4,0),
('ENG',1,N'Electrical Technician',5,22,1),('ENG',2,N'Mechanical Technician',5,22,1),('ENG',3,N'Baggage Handling System Technician',5,14,1),('ENG',4,N'HVAC Technician',5,10,1),('ENG',5,N'Civil Works Technician',4,8,0),('ENG',6,N'Maintenance Engineer',9,10,0),('ENG',7,N'Maintenance Planner',7,6,0),('ENG',8,N'Maintenance Supervisor',8,8,1),
('GHS',1,N'Ramp Agent',3,40,1),('GHS',2,N'Equipment Operator',4,20,1),('GHS',3,N'Check-in Agent',4,22,1),('GHS',4,N'Load Controller',7,6,1),('GHS',5,N'Ramp Supervisor',8,8,1),('GHS',6,N'Turnaround Coordinator',7,4,1),
('CGO',1,N'Cargo Agent',4,45,1),('CGO',2,N'Cargo Handler',3,30,1),('CGO',3,N'Cargo Documentation Officer',6,15,0),('CGO',4,N'Cargo Supervisor',8,10,1),
('ITD',1,N'Systems Engineer',9,25,0),('ITD',2,N'Network Engineer',9,20,0),('ITD',3,N'Service Desk Analyst',6,30,1),('ITD',4,N'Application Support Specialist',8,25,0),
('FIN',1,N'Accountant',7,35,0),('FIN',2,N'Senior Accountant',9,15,0),('FIN',3,N'Procurement Officer',7,30,0),('FIN',4,N'Payroll Officer',7,10,0),('FIN',5,N'Contracts Specialist',8,10,0),
('HRA',1,N'HR Officer',7,35,0),('HRA',2,N'Recruitment Specialist',8,15,0),('HRA',3,N'Training Coordinator',6,15,0),('HRA',4,N'Administration Officer',5,25,0),('HRA',5,N'Government Relations Officer',5,10,0),
('COM',1,N'Commercial Officer',7,40,0),('COM',2,N'Concessions Executive',8,25,0),('COM',3,N'Marketing Specialist',8,20,0),('COM',4,N'Leasing Officer',7,15,0),
('SHE',1,N'Safety Officer',8,45,0),('SHE',2,N'Quality Auditor',8,30,0),('SHE',3,N'Environment Officer',7,25,0),
('CEO',1,N'Executive Assistant',7,30,0),('CEO',2,N'Strategy Analyst',9,30,0),('CEO',3,N'Legal Counsel',10,20,0),('CEO',4,N'Internal Auditor',9,20,0),
('CSC',1,N'Call Centre Agent',4,60,1),('CSC',2,N'Customer Experience Officer',6,25,0),('CSC',3,N'Call Centre Supervisor',8,15,1);

INSERT INTO org.Position (PositionCode, PositionTitle, DepartmentId, SectionId, GradeLevel, JobLevel, IsShiftBased)
SELECT CONCAT(p.Dept, '-S', RIGHT(CONCAT('0', p.Ord), 2)), p.Title, d.DepartmentId, NULL, p.Grade, CASE WHEN p.Title LIKE N'%Supervisor%' OR p.Title LIKE N'%Commander%' THEN N'Supervisor' ELSE N'Staff' END, p.IsShift
FROM #Pos p JOIN org.Department d ON d.DepartmentCode = p.Dept ORDER BY d.DepartmentId, p.Ord;
INSERT INTO org.Position (PositionCode, PositionTitle, DepartmentId, SectionId, GradeLevel, JobLevel, IsShiftBased)
SELECT CONCAT(s.SectionCode, '-MGR'), CONCAT(N'Manager, ', s.SectionName), s.DepartmentId, s.SectionId, 10, N'Manager', 0 FROM org.Section s ORDER BY s.SectionId;
INSERT INTO org.Position (PositionCode, PositionTitle, DepartmentId, SectionId, GradeLevel, JobLevel, IsShiftBased)
SELECT CONCAT(d.DepartmentCode, '-DIR'), CASE WHEN d.DepartmentCode = 'CEO' THEN N'Chief Executive Officer' ELSE CONCAT(N'Director, ', d.DepartmentName) END,
       d.DepartmentId, NULL, CASE WHEN d.DepartmentCode = 'CEO' THEN 13 ELSE 12 END, N'Executive', 0 FROM org.Department d ORDER BY d.DepartmentId;

INSERT INTO ta.Shift VALUES ('DAY',N'Office hours','08:00','16:00',8),('M',N'Morning shift','06:00','14:00',8),('E',N'Evening shift','14:00','22:00',8),('N',N'Night shift','22:00','06:00',8);
INSERT INTO ta.LeaveType VALUES ('ANN',N'Annual leave',N'إجازة سنوية',1),('SCK',N'Sick leave',N'إجازة مرضية',1),('EMR',N'Emergency leave',N'إجازة طارئة',1),
('UNP',N'Unpaid leave',N'إجازة بدون راتب',0),('MAT',N'Maternity leave',N'إجازة وضع',1),('HAJ',N'Hajj leave',N'إجازة حج',1),('STD',N'Study leave',N'إجازة دراسية',1),('CMP',N'Compassionate leave',N'إجازة حداد',1);
-- Public holidays of the United Arab Emirates (dates of the lunar holidays are approximate).
INSERT INTO ta.PublicHoliday VALUES
('2023-01-01',N'New Year''s Day'),('2023-04-21',N'Eid Al Fitr'),('2023-04-22',N'Eid Al Fitr'),('2023-04-23',N'Eid Al Fitr'),('2023-06-27',N'Arafat Day'),('2023-06-28',N'Eid Al Adha'),('2023-06-29',N'Eid Al Adha'),('2023-06-30',N'Eid Al Adha'),('2023-07-21',N'Islamic New Year'),('2023-09-29',N'Prophet''s Birthday'),('2023-12-01',N'Commemoration Day'),('2023-12-02',N'National Day'),('2023-12-03',N'National Day'),
('2024-01-01',N'New Year''s Day'),('2024-04-09',N'Eid Al Fitr'),('2024-04-10',N'Eid Al Fitr'),('2024-04-11',N'Eid Al Fitr'),('2024-04-12',N'Eid Al Fitr'),('2024-06-15',N'Arafat Day'),('2024-06-16',N'Eid Al Adha'),('2024-06-17',N'Eid Al Adha'),('2024-06-18',N'Eid Al Adha'),('2024-07-07',N'Islamic New Year'),('2024-09-15',N'Prophet''s Birthday'),('2024-12-02',N'National Day'),('2024-12-03',N'National Day'),
('2025-01-01',N'New Year''s Day'),('2025-03-30',N'Eid Al Fitr'),('2025-03-31',N'Eid Al Fitr'),('2025-04-01',N'Eid Al Fitr'),('2025-06-05',N'Arafat Day'),('2025-06-06',N'Eid Al Adha'),('2025-06-07',N'Eid Al Adha'),('2025-06-08',N'Eid Al Adha'),('2025-06-26',N'Islamic New Year'),('2025-09-05',N'Prophet''s Birthday'),('2025-12-01',N'Commemoration Day'),('2025-12-02',N'National Day'),('2025-12-03',N'National Day'),
('2026-01-01',N'New Year''s Day'),('2026-03-20',N'Eid Al Fitr'),('2026-03-21',N'Eid Al Fitr'),('2026-03-22',N'Eid Al Fitr'),('2026-05-26',N'Arafat Day'),('2026-05-27',N'Eid Al Adha'),('2026-05-28',N'Eid Al Adha'),('2026-05-29',N'Eid Al Adha'),('2026-06-16',N'Islamic New Year'),('2026-08-25',N'Prophet''s Birthday'),('2026-12-01',N'Commemoration Day'),('2026-12-02',N'National Day'),('2026-12-03',N'National Day');
INSERT INTO pay.PayComponent VALUES ('BASIC',N'Basic salary','Earning'),('HOUSING',N'Housing allowance','Earning'),('TRANSPORT',N'Transport allowance','Earning'),
('OTHER',N'Other allowances','Earning'),('OVERTIME',N'Overtime','Earning'),('BONUS',N'Performance bonus','Earning'),('EOSB',N'End-of-service gratuity','Earning'),
('PENSION',N'Pension contribution','Deduction'),('LOAN',N'Staff loan instalment','Deduction'),('ABSENCE',N'Unpaid absence','Deduction');

-- --------------------------------------------------------------------------------------------------- employees
-- Name pools by region of origin.
CREATE TABLE #Name (Grp varchar(3), Kind char(1), Idx int, Name nvarchar(40));
INSERT INTO #Name SELECT v.Grp, v.Kind, s.ordinal - 1, LTRIM(s.value) FROM (VALUES
('ARB','M',N'Ahmed,Mohammed,Khalid,Omar,Yousef,Hassan,Ali,Saeed,Abdullah,Tariq,Majid,Rashid,Hamad,Sultan,Faisal,Ibrahim,Mahmoud,Karim,Nasser,Walid,Ziad,Sami,Adel,Hani'),
('ARB','F',N'Fatima,Aisha,Mariam,Noura,Layla,Hind,Salma,Reem,Amna,Dana,Huda,Rania,Sara,Lina,Maha,Noor,Shaikha,Mona,Yasmin,Ghada'),
('ARB','L',N'Al Mansoori,Al Zaabi,Al Shamsi,Al Nuaimi,Al Ketbi,Al Hammadi,Al Mazrouei,Al Suwaidi,Al Marzouqi,Al Ali,Hassan,Ibrahim,Khalil,Haddad,Saleh,Mostafa,Abdelrahman,Nasser,Yousef,Darwish,Qasim,Hamdan,Barakat,Fahmy'),
('SAS','M',N'Rahul,Amit,Sanjay,Vijay,Arjun,Imran,Bilal,Usman,Farhan,Naveen,Suresh,Rajesh,Anil,Kiran,Deepak,Asif,Kamal,Rohan,Prakash,Zeeshan,Manoj,Nikhil,Tanvir,Dinesh'),
('SAS','F',N'Priya,Anjali,Sunita,Kavita,Neha,Ayesha,Sana,Divya,Pooja,Meera,Nisha,Rekha,Shreya,Zainab,Lakshmi,Farah,Anita,Ritu,Hina,Swati'),
('SAS','L',N'Sharma,Patel,Kumar,Singh,Nair,Menon,Khan,Ahmed,Hussain,Malik,Reddy,Iyer,Pillai,Chowdhury,Rahman,Thapa,Gurung,Perera,Fernando,Joshi,Verma,Das,Qureshi,Shaikh'),
('FIL','M',N'Jose,Mark,John,Michael,Ramon,Carlo,Paolo,Jerome,Rodel,Dennis,Arnel,Jayson,Noel,Rommel,Ariel,Edwin,Marlon,Ronald,Allan,Joel'),
('FIL','F',N'Maria,Grace,Joy,Angelica,Kristine,Rowena,Liza,Jennifer,Marites,Cherry,Lorna,Aileen,Janet,Rhea,Mylene,Divina,Analyn,Jocelyn,Maricel,Hazel'),
('FIL','L',N'Santos,Reyes,Cruz,Bautista,Garcia,Mendoza,Torres,Flores,Ramos,Aquino,Villanueva,Castillo,Navarro,Domingo,Salazar,Mercado,Pascual,Aguilar,De Leon,Dela Cruz'),
('WST','M',N'James,David,Robert,Andrew,Peter,Thomas,Daniel,Stephen,Paul,Richard,Mark,Simon,Chris,Matthew,Neil,Gareth,Ian,Martin,Scott,Adam'),
('WST','F',N'Sarah,Emma,Claire,Laura,Helen,Rachel,Kate,Louise,Julie,Nicola,Fiona,Karen,Anna,Sophie,Jane,Lucy,Amy,Rebecca,Hannah,Victoria'),
('WST','L',N'Smith,Jones,Taylor,Brown,Wilson,Evans,Walker,Roberts,Clarke,Hughes,Edwards,Turner,Cooper,Morris,Bennett,Murphy,Kelly,Fischer,Dubois,Van der Merwe'),
('AFR','M',N'Samuel,Joseph,Peter,Daniel,Emmanuel,Moses,Brian,Kevin,Collins,Dennis,Patrick,Isaac,Victor,Stephen,Francis,George'),
('AFR','F',N'Grace,Mercy,Faith,Esther,Joyce,Ruth,Agnes,Lilian,Purity,Winnie,Beatrice,Gloria,Irene,Naomi,Stella,Dorcas'),
('AFR','L',N'Mwangi,Otieno,Kamau,Njoroge,Ochieng,Wanjiru,Adeyemi,Okafor,Okello,Mugisha,Kiprono,Mutua,Onyango,Nwosu,Ssempa,Chege')) v (Grp, Kind, List)
CROSS APPLY STRING_SPLIT(v.List, ',', 1) s;
CREATE TABLE #Nat (Code char(2), Grp varchar(3), LoBand int, HiBand int);
INSERT INTO #Nat SELECT v.Code, v.Grp, SUM(v.W) OVER (ORDER BY v.Ord) - v.W, SUM(v.W) OVER (ORDER BY v.Ord) FROM (VALUES
(1,'AE','ARB',200),(2,'IN','SAS',230),(3,'PK','SAS',100),(4,'PH','FIL',120),(5,'EG','ARB',70),(6,'JO','ARB',35),(7,'LB','ARB',15),(8,'SY','ARB',15),(9,'SD','ARB',20),
(10,'MA','ARB',10),(11,'TN','ARB',8),(12,'BD','SAS',30),(13,'NP','SAS',30),(14,'LK','SAS',20),(15,'GB','WST',35),(16,'US','WST',8),(17,'AU','WST',5),(18,'ZA','WST',12),
(19,'FR','WST',5),(20,'DE','WST',4),(21,'KE','AFR',15),(22,'NG','AFR',8),(23,'UG','AFR',5)) v (Ord, Code, Grp, W);
CREATE TABLE #DeptBand (DepartmentId int, LoBand int, HiBand int);
INSERT INTO #DeptBand SELECT v.Id, SUM(v.W) OVER (ORDER BY v.Id) - v.W, SUM(v.W) OVER (ORDER BY v.Id)
FROM (VALUES (1,11),(2,14),(3,17),(4,7),(5,13),(6,14),(7,5),(8,4),(9,4),(10,3),(11,3),(12,2),(13,1),(14,2)) v (Id, W);

DECLARE @Days int = DATEDIFF(day, @Start, @Today);
-- 1,180 people were in service on 1 January 2023; 684 joined since; 596 left since: 1,268 are in service today.
CREATE TABLE #E (Seq int PRIMARY KEY, HireDate date, DepartmentId int, SectionId int, Gender char(1), Nat char(2), Grp varchar(3),
    SepRank int NULL, SepType varchar(15) NULL, SepDate date NULL, IsDirector bit DEFAULT 0, IsManager bit DEFAULT 0, PositionId int NULL, GradeLevel tinyint NULL,
    IsShift bit NULL, ManagerSeq int NULL, FirstName nvarchar(40), LastName nvarchar(40), BirthDate date, Basic decimal(10,2));
INSERT INTO #E (Seq, HireDate, DepartmentId, Gender, Nat, Grp)
SELECT s.value,
       CASE WHEN s.value <= 1180 THEN DATEADD(day, CAST(5112 * POWER(s.value / 1180.0, 0.75) AS int), '2009-01-01')
            ELSE DATEADD(day, CAST((s.value - 1180 - 0.5) * @Days / 684.0 AS int), @Start) END,
       db.DepartmentId, CASE WHEN dbo.R(s.value, 3) % 100 < 72 THEN 'M' ELSE 'F' END, n.Code, n.Grp
FROM GENERATE_SERIES(1, 1864) s
JOIN #DeptBand db ON dbo.R(s.value, 2) % 100 >= db.LoBand AND dbo.R(s.value, 2) % 100 < db.HiBand
JOIN #Nat n ON dbo.R(s.value, 4) % 1000 >= n.LoBand AND dbo.R(s.value, 4) % 1000 < n.HiBand;

UPDATE e SET SectionId = s.SectionId
FROM #E e JOIN (SELECT SectionId, DepartmentId, ROW_NUMBER() OVER (PARTITION BY DepartmentId ORDER BY SectionId) - 1 AS Idx, COUNT(*) OVER (PARTITION BY DepartmentId) AS Cnt FROM org.Section) s
  ON s.DepartmentId = e.DepartmentId AND s.Idx = dbo.R(e.Seq, 5) % s.Cnt;

-- Leavers: 596 of the people hired at least 120 days ago.
WITH x AS (SELECT Seq, ROW_NUMBER() OVER (ORDER BY dbo.R(Seq, 7), Seq) AS rn FROM #E WHERE HireDate <= DATEADD(day, -120, @Today))
UPDATE e SET SepRank = x.rn,
    SepType = CASE WHEN x.rn <= 352 THEN 'Resigned' WHEN x.rn <= 448 THEN 'Terminated' WHEN x.rn <= 532 THEN 'EndOfContract' WHEN x.rn <= 584 THEN 'Retired' WHEN x.rn <= 588 THEN 'Deceased' ELSE 'Absconded' END
FROM #E e JOIN x ON x.Seq = e.Seq WHERE x.rn <= 596;
UPDATE e SET SepDate = DATEADD(day, dbo.R(e.Seq, 8) % (DATEDIFF(day, lo.d, DATEADD(day, -7, @Today)) + 1), lo.d)
FROM #E e CROSS APPLY (SELECT CASE WHEN DATEADD(day, 90, e.HireDate) > @Start THEN DATEADD(day, 90, e.HireDate) ELSE @Start END AS d) lo
WHERE e.SepType IS NOT NULL;

-- Heads: the longest-serving person in service leads the department; the next one in each section manages it.
WITH x AS (SELECT Seq, ROW_NUMBER() OVER (PARTITION BY DepartmentId ORDER BY Seq) AS rn FROM #E WHERE SepType IS NULL)
UPDATE e SET IsDirector = 1 FROM #E e JOIN x ON x.Seq = e.Seq WHERE x.rn = 1;
WITH x AS (SELECT Seq, ROW_NUMBER() OVER (PARTITION BY SectionId ORDER BY Seq) AS rn FROM #E WHERE SepType IS NULL AND IsDirector = 0)
UPDATE e SET IsManager = 1 FROM #E e JOIN x ON x.Seq = e.Seq WHERE x.rn = 1;

UPDATE e SET PositionId = p.PositionId, GradeLevel = p.GradeLevel, IsShift = p.IsShiftBased
FROM #E e JOIN org.Position p ON p.DepartmentId = e.DepartmentId AND p.JobLevel = N'Executive' WHERE e.IsDirector = 1;
UPDATE e SET PositionId = p.PositionId, GradeLevel = p.GradeLevel, IsShift = p.IsShiftBased
FROM #E e JOIN org.Position p ON p.SectionId = e.SectionId AND p.JobLevel = N'Manager' WHERE e.IsManager = 1;
WITH w AS (SELECT p.Dept, p.Ord, SUM(p.Weight) OVER (PARTITION BY p.Dept ORDER BY p.Ord) - p.Weight AS Lo, SUM(p.Weight) OVER (PARTITION BY p.Dept ORDER BY p.Ord) AS Hi,
                  SUM(p.Weight) OVER (PARTITION BY p.Dept) AS Total FROM #Pos p)
UPDATE e SET PositionId = p.PositionId, GradeLevel = p.GradeLevel, IsShift = p.IsShiftBased
FROM #E e JOIN org.Department d ON d.DepartmentId = e.DepartmentId
JOIN w ON w.Dept = d.DepartmentCode AND dbo.R(e.Seq, 6) % w.Total >= w.Lo AND dbo.R(e.Seq, 6) % w.Total < w.Hi
JOIN org.Position p ON p.PositionCode = CONCAT(w.Dept, '-S', RIGHT(CONCAT('0', w.Ord), 2))
WHERE e.PositionId IS NULL;

UPDATE e SET ManagerSeq = CASE WHEN e.IsDirector = 1 THEN CASE WHEN e.DepartmentId = 13 THEN NULL ELSE ceo.Seq END
                               WHEN e.IsManager = 1 THEN dir.Seq ELSE ISNULL(mgr.Seq, dir.Seq) END
FROM #E e
LEFT JOIN #E dir ON dir.DepartmentId = e.DepartmentId AND dir.IsDirector = 1
LEFT JOIN #E mgr ON mgr.SectionId = e.SectionId AND mgr.IsManager = 1
LEFT JOIN #E ceo ON ceo.DepartmentId = 13 AND ceo.IsDirector = 1;

UPDATE e SET FirstName = f.Name, LastName = l.Name,
    BirthDate = CASE WHEN e.SepType = 'Retired' THEN DATEADD(day, -(dbo.R(e.Seq, 11) % 300), DATEADD(year, -60, e.SepDate))
                     ELSE DATEADD(day, -(dbo.R(e.Seq, 11) % 365), DATEADD(year, -(22 + dbo.R(e.Seq, 12) % 21 + CASE WHEN e.GradeLevel >= 10 THEN 8 ELSE 0 END), e.HireDate)) END,
    Basic = ROUND(g.MinBasicSalary + (dbo.R(e.Seq, 13) % 1000) / 1000.0 * (g.MaxBasicSalary - g.MinBasicSalary), -1)
FROM #E e
JOIN org.Grade g ON g.GradeLevel = e.GradeLevel
CROSS APPLY (SELECT COUNT(*) AS c FROM #Name n WHERE n.Grp = e.Grp AND n.Kind = e.Gender) fc
JOIN #Name f ON f.Grp = e.Grp AND f.Kind = e.Gender AND f.Idx = dbo.R(e.Seq, 9) % fc.c
CROSS APPLY (SELECT COUNT(*) AS c FROM #Name n WHERE n.Grp = e.Grp AND n.Kind = 'L') lc
JOIN #Name l ON l.Grp = e.Grp AND l.Kind = 'L' AND l.Idx = dbo.R(e.Seq, 10) % lc.c;

INSERT INTO hr.Employee (EmployeeId, EmployeeNumber, FirstName, LastName, FullName, Gender, BirthDate, NationalityCode, MaritalStatus, WorkEmail, MobileNumber,
    HireDate, ProbationEndDate, DepartmentId, SectionId, PositionId, GradeLevel, ManagerEmployeeId, EmploymentType, ContractType, IsShiftWorker, EmploymentStatus,
    SeparationDate, BasicSalary, HousingAllowance, TransportAllowance, OtherAllowance, BankCode)
SELECT e.Seq, CONCAT('DA', RIGHT(CONCAT('0000', e.Seq), 5)), e.FirstName, e.LastName, CONCAT(e.FirstName, N' ', e.LastName), e.Gender, e.BirthDate, e.Nat,
    CASE WHEN dbo.R(e.Seq, 14) % 100 < 58 THEN 'Married' WHEN dbo.R(e.Seq, 14) % 100 < 96 THEN 'Single' ELSE 'Divorced' END,
    CONCAT(LOWER(REPLACE(e.FirstName, N' ', N'')), N'.', LOWER(REPLACE(e.LastName, N' ', N'')),
           CASE WHEN ROW_NUMBER() OVER (PARTITION BY e.FirstName, e.LastName ORDER BY e.Seq) > 1 THEN CAST(ROW_NUMBER() OVER (PARTITION BY e.FirstName, e.LastName ORDER BY e.Seq) AS varchar(5)) ELSE '' END,
           N'@demoairport.example'),
    CONCAT('+9715', dbo.R(e.Seq, 15) % 9, RIGHT(CONCAT('000000', dbo.R(e.Seq, 16) % 10000000), 7)),
    e.HireDate, DATEADD(month, 6, e.HireDate), e.DepartmentId, e.SectionId, e.PositionId, e.GradeLevel, NULL,
    CASE WHEN dbo.R(e.Seq, 17) % 100 < 3 THEN 'Part-time' ELSE 'Full-time' END, CASE WHEN e.Nat = 'AE' OR dbo.R(e.Seq, 18) % 100 < 35 THEN 'Unlimited' ELSE 'Fixed-term' END,
    e.IsShift, ISNULL(e.SepType, 'Active'), e.SepDate, e.Basic, ROUND(e.Basic * 0.35, -1),
    CASE WHEN e.GradeLevel <= 4 THEN 600 WHEN e.GradeLevel <= 8 THEN 1000 WHEN e.GradeLevel <= 10 THEN 1500 ELSE 2500 END,
    CASE WHEN e.IsShift = 1 THEN 400 ELSE 0 END + CASE WHEN e.GradeLevel >= 9 THEN 1000 ELSE 0 END,
    CHOOSE(dbo.R(e.Seq, 19) % 5 + 1, 'ENBD', 'FAB', 'ADCB', 'DIB', 'MASHREQ')
FROM #E e;
UPDATE emp SET ManagerEmployeeId = e.ManagerSeq FROM hr.Employee emp JOIN #E e ON e.Seq = emp.EmployeeId;

INSERT INTO hr.Separation (EmployeeId, SeparationType, Reason, NoticeDate, LastWorkingDate, ServiceYears, IsRegretted, EligibleForRehire, ExitInterviewScore)
SELECT e.Seq, e.SepType,
    CASE e.SepType WHEN 'Resigned' THEN CHOOSE(dbo.R(e.Seq, 20) % 7 + 1, N'Better opportunity', N'Relocation', N'Compensation', N'Career growth', N'Family reasons', N'Work-life balance', N'Further studies')
                   WHEN 'Terminated' THEN CHOOSE(dbo.R(e.Seq, 20) % 4 + 1, N'Poor performance', N'Misconduct', N'Redundancy', N'Failed probation')
                   WHEN 'EndOfContract' THEN N'Contract not renewed' WHEN 'Retired' THEN N'Reached retirement age' WHEN 'Deceased' THEN N'Death in service' ELSE N'Unauthorised absence' END,
    DATEADD(day, -CASE WHEN e.SepType IN ('Resigned', 'EndOfContract', 'Retired') THEN 30 + dbo.R(e.Seq, 21) % 60 ELSE dbo.R(e.Seq, 21) % 10 END, e.SepDate), e.SepDate,
    ROUND(DATEDIFF(day, e.HireDate, e.SepDate) / 365.25, 2),
    CASE WHEN e.SepType = 'Resigned' AND dbo.R(e.Seq, 22) % 100 < 45 THEN 1 ELSE 0 END,
    CASE WHEN e.SepType IN ('Resigned', 'EndOfContract') AND dbo.R(e.Seq, 23) % 100 < 80 THEN 1 ELSE 0 END,
    CASE WHEN e.SepType IN ('Resigned', 'EndOfContract', 'Retired') AND dbo.R(e.Seq, 24) % 100 < 70 THEN 1 + dbo.R(e.Seq, 25) % 5 END
FROM #E e WHERE e.SepType IS NOT NULL;

-- Employment history: hiring, transfers between sections, promotions and separation.
INSERT INTO hr.EmploymentHistory (EmployeeId, EffectiveDate, ActionType, DepartmentId, SectionId, PositionId, GradeLevel, Remarks)
SELECT e.Seq, e.HireDate, 'Hire', e.DepartmentId, e.SectionId, e.PositionId, e.GradeLevel, NULL FROM #E e
UNION ALL
SELECT e.Seq, DATEADD(day, 200 + dbo.R(e.Seq, 26) % (DATEDIFF(day, e.HireDate, ISNULL(e.SepDate, @Today)) - 300), e.HireDate),
       CASE WHEN dbo.R(e.Seq, 27) % 2 = 0 THEN 'Promotion' ELSE 'Transfer' END, e.DepartmentId, e.SectionId, e.PositionId, e.GradeLevel,
       CASE WHEN dbo.R(e.Seq, 27) % 2 = 0 THEN N'Promoted to current grade' ELSE N'Moved to current section' END
FROM #E e WHERE DATEDIFF(day, e.HireDate, ISNULL(e.SepDate, @Today)) > 700 AND dbo.R(e.Seq, 28) % 100 < 30
UNION ALL
SELECT e.Seq, e.SepDate, 'Separation', e.DepartmentId, e.SectionId, e.PositionId, e.GradeLevel, e.SepType FROM #E e WHERE e.SepType IS NOT NULL
ORDER BY 2, 1;

INSERT INTO hr.Dependent (EmployeeId, DependentName, Relationship, Gender, BirthDate, IsMedicallyInsured)
SELECT e.Seq, CONCAT(f.Name, N' ', e.LastName), CASE WHEN k.value = 0 THEN 'Spouse' ELSE 'Child' END, g.G,
       CASE WHEN k.value = 0 THEN DATEADD(day, dbo.R(e.Seq, 31) % 1800 - 600, e.BirthDate)
            ELSE DATEADD(day, dbo.R(e.Seq * 10 + k.value, 32) % 700, DATEADD(year, 24 + k.value * 3, e.BirthDate)) END,
       CASE WHEN e.GradeLevel >= 5 OR e.Nat = 'AE' THEN 1 ELSE 0 END
FROM #E e JOIN hr.Employee emp ON emp.EmployeeId = e.Seq AND emp.MaritalStatus = 'Married'
CROSS JOIN GENERATE_SERIES(0, 3) k
CROSS APPLY (SELECT CASE WHEN k.value = 0 THEN CASE e.Gender WHEN 'M' THEN 'F' ELSE 'M' END WHEN dbo.R(e.Seq * 10 + k.value, 33) % 2 = 0 THEN 'M' ELSE 'F' END AS G) g
CROSS APPLY (SELECT COUNT(*) AS c FROM #Name n WHERE n.Grp = e.Grp AND n.Kind = g.G) fc
JOIN #Name f ON f.Grp = e.Grp AND f.Kind = g.G AND f.Idx = dbo.R(e.Seq * 10 + k.value, 34) % fc.c
WHERE k.value <= dbo.R(e.Seq, 30) % 4
  AND (k.value = 0 OR DATEADD(year, 24 + k.value * 3, e.BirthDate) < @Today);

INSERT INTO hr.EmployeeDocument (EmployeeId, DocumentType, DocumentNumber, IssueDate, ExpiryDate, IssuingAuthority)
SELECT e.Seq, t.DocType, CONCAT(t.Prefix, RIGHT(CONCAT('0000000', dbo.R(e.Seq, 40 + t.Ord) % 10000000), 7)), DATEADD(month, -t.ValidMonths, x.Expiry), x.Expiry, t.Authority
FROM #E e
CROSS JOIN (VALUES (1, 'Passport', 'P', 120, N'Country of nationality'), (2, 'Emirates ID', '784', 36, N'Federal Authority for Identity'), (3, 'Residence Visa', 'V', 24, N'General Directorate of Residency'),
                   (4, 'Airport Security Pass', 'ASP', 24, N'Demo Airport Security'), (5, 'Airside Driving Permit', 'ADP', 12, N'Demo Airport Airside Operations')) t (Ord, DocType, Prefix, ValidMonths, Authority)
CROSS APPLY (SELECT DATEADD(day, dbo.R(e.Seq, 50 + t.Ord) % (t.ValidMonths * 30) - 45, ISNULL(e.SepDate, @Today)) AS Expiry) x
WHERE (t.Ord <> 3 OR e.Nat <> 'AE') AND (t.Ord <> 5 OR (e.DepartmentId IN (1, 4, 5, 6, 7) AND dbo.R(e.Seq, 56) % 100 < 60));

INSERT INTO hr.DisciplinaryAction (EmployeeId, ActionDate, ActionType, Category, Outcome, IssuedByEmployeeId)
SELECT e.Seq, DATEADD(day, dbo.R(s.value, 61) % (DATEDIFF(day, lo.d, ISNULL(e.SepDate, @Today)) + 1), lo.d),
       CHOOSE(dbo.R(s.value, 62) % 10 + 1, 'Verbal warning', 'Verbal warning', 'Verbal warning', 'Verbal warning', 'Written warning', 'Written warning', 'Written warning', 'Final warning', 'Final warning', 'Suspension'),
       CHOOSE(dbo.R(s.value, 63) % 7 + 1, N'Attendance and punctuality', N'Safety violation', N'Security procedure breach', N'Customer complaint', N'Misuse of equipment', N'Insubordination', N'Airside driving offence'),
       CHOOSE(dbo.R(s.value, 64) % 4 + 1, N'Closed', N'Closed', N'Under appeal', N'Training assigned'), e.ManagerSeq
FROM GENERATE_SERIES(1, 380) s JOIN #E e ON e.Seq = 1 + dbo.R(s.value, 60) % 1864
CROSS APPLY (SELECT CASE WHEN e.HireDate > @Start THEN e.HireDate ELSE @Start END AS d) lo;

-- ------------------------------------------------------------------------------------------------ compensation
-- Yearly increments of 2-6 % each January; the salary on the employee record is the latest one.
CREATE TABLE #Comp (Seq int, Yr int, Basic decimal(10,2), Pct decimal(5,2), PRIMARY KEY (Seq, Yr));
INSERT INTO #Comp
SELECT e.Seq, y.value,
       ROUND(e.Basic / ISNULL((SELECT EXP(SUM(LOG(f.F))) FROM (SELECT 1 + (20 + dbo.R(e.Seq, 100 + y2.value) % 41) / 1000.0 AS F FROM GENERATE_SERIES(y.value + 1, YEAR(ISNULL(e.SepDate, @Today))) y2) f), 1), -1),
       CASE WHEN y.value > YEAR(e.HireDate) AND y.value > 2023 THEN (20 + dbo.R(e.Seq, 100 + y.value) % 41) / 10.0 END
FROM #E e CROSS APPLY GENERATE_SERIES(CASE WHEN YEAR(e.HireDate) > 2023 THEN YEAR(e.HireDate) ELSE 2023 END, YEAR(ISNULL(e.SepDate, @Today))) y;
INSERT INTO pay.EmployeeCompensation (EmployeeId, EffectiveDate, ChangeReason, BasicSalary, HousingAllowance, TransportAllowance, OtherAllowance, IncreasePercent)
SELECT c.Seq, CASE WHEN c.Yr = YEAR(e.HireDate) THEN e.HireDate ELSE DATEFROMPARTS(c.Yr, 1, 1) END,
       CASE WHEN c.Yr = YEAR(e.HireDate) THEN 'Hire' WHEN c.Pct IS NULL THEN 'Opening salary' ELSE 'Annual increment' END,
       c.Basic, ROUND(c.Basic * 0.35, -1), emp.TransportAllowance, emp.OtherAllowance, c.Pct
FROM #Comp c JOIN #E e ON e.Seq = c.Seq JOIN hr.Employee emp ON emp.EmployeeId = c.Seq ORDER BY 2, 1;

-- ------------------------------------------------------------------------------------------------------- leave
-- Up to four requests per person and year, each in its own quarter of the time the person was employed that year.
INSERT INTO ta.LeaveRequest (EmployeeId, LeaveTypeCode, RequestDate, StartDate, EndDate, RequestStatus, ApprovedByEmployeeId)
SELECT q.Seq, q.LeaveType, DATEADD(day, -(3 + dbo.R(q.K, 74) % 40), q.StartDate), q.StartDate, DATEADD(day, q.Len - 1, q.StartDate),
       CASE WHEN q.StartDate > @Today THEN CASE WHEN dbo.R(q.K, 75) % 100 < 40 THEN 'Pending' ELSE 'Approved' END
            WHEN dbo.R(q.K, 75) % 100 < 90 THEN 'Approved' WHEN dbo.R(q.K, 75) % 100 < 95 THEN 'Rejected' ELSE 'Cancelled' END,
       q.ManagerSeq
FROM (
    SELECT e.Seq, e.ManagerSeq, k.K, t.LeaveType, t.Len,
           DATEADD(day, (s.value - 1) * seg.Seg + dbo.R(k.K, 73) % CASE WHEN seg.Seg >= t.Len THEN seg.Seg - t.Len + 1 ELSE 1 END, span.Lo) AS StartDate
    FROM #E e
    CROSS APPLY GENERATE_SERIES(CASE WHEN YEAR(e.HireDate) > 2023 THEN YEAR(e.HireDate) ELSE 2023 END, YEAR(ISNULL(e.SepDate, @Today))) y
    CROSS APPLY (SELECT CASE WHEN e.HireDate > DATEFROMPARTS(y.value, 1, 1) THEN DATEADD(day, 30, e.HireDate) ELSE DATEFROMPARTS(y.value, 1, 1) END AS Lo,
                        CASE WHEN e.SepDate < DATEFROMPARTS(y.value, 12, 31) THEN e.SepDate ELSE DATEFROMPARTS(y.value, 12, 31) END AS Hi) span
    CROSS APPLY (SELECT DATEDIFF(day, span.Lo, span.Hi) / 4 AS Seg) seg
    CROSS JOIN GENERATE_SERIES(1, 4) s
    CROSS APPLY (SELECT CAST(e.Seq AS bigint) * 100000 + y.value * 10 + s.value AS K) k
    CROSS APPLY (SELECT CASE s.value WHEN 1 THEN CASE WHEN dbo.R(k.K, 70) % 100 < 55 THEN 'SCK' END
                                     WHEN 2 THEN CASE WHEN dbo.R(k.K, 70) % 100 < 85 THEN 'ANN' END
                                     WHEN 3 THEN CASE WHEN dbo.R(k.K, 70) % 100 < 14 THEN CHOOSE(dbo.R(k.K, 71) % 6 + 1, 'EMR', 'EMR', 'UNP', 'CMP', 'HAJ', 'STD') WHEN dbo.R(k.K, 70) % 100 < 50 THEN 'SCK' END
                                     ELSE CASE WHEN dbo.R(k.K, 70) % 100 < 45 THEN 'ANN' WHEN dbo.R(k.K, 70) % 100 < 48 AND e.Gender = 'F' THEN 'MAT' END END AS LeaveType) lt
    CROSS APPLY (SELECT lt.LeaveType, CASE lt.LeaveType WHEN 'ANN' THEN CASE WHEN s.value = 2 THEN 8 + dbo.R(k.K, 72) % 14 ELSE 3 + dbo.R(k.K, 72) % 6 END
                                                        WHEN 'SCK' THEN 1 + dbo.R(k.K, 72) % 3 WHEN 'MAT' THEN 60 WHEN 'HAJ' THEN 15 WHEN 'STD' THEN 10
                                                        WHEN 'UNP' THEN 5 + dbo.R(k.K, 72) % 10 ELSE 2 + dbo.R(k.K, 72) % 3 END AS Len) t
    WHERE t.LeaveType IS NOT NULL AND seg.Seg >= t.Len AND span.Hi > span.Lo
) q
WHERE q.StartDate <= DATEADD(day, 75, @Today)
ORDER BY q.StartDate, q.Seq;
CREATE INDEX IX_LeaveRequest_Employee ON ta.LeaveRequest (EmployeeId, StartDate) INCLUDE (EndDate, RequestStatus, LeaveTypeCode);

INSERT INTO ta.LeaveBalance (EmployeeId, LeaveYear, EntitledDays, CarriedForwardDays, TakenDays)
SELECT e.Seq, y.value, g.AnnualLeaveDays, CASE WHEN y.value > YEAR(e.HireDate) THEN dbo.R(e.Seq, 80 + y.value) % 6 ELSE 0 END,
       ISNULL((SELECT SUM(l.LeaveDays) FROM ta.LeaveRequest l WHERE l.EmployeeId = e.Seq AND l.LeaveTypeCode = 'ANN' AND l.RequestStatus = 'Approved' AND YEAR(l.StartDate) = y.value AND l.StartDate <= @Today), 0)
FROM #E e JOIN org.Grade g ON g.GradeLevel = e.GradeLevel
CROSS APPLY GENERATE_SERIES(CASE WHEN YEAR(e.HireDate) > 2023 THEN YEAR(e.HireDate) ELSE 2023 END, YEAR(ISNULL(e.SepDate, @Today))) y;

-- -------------------------------------------------------------------------------------------------- attendance
-- Shift workers: four days on, two off, rotating morning / evening / night. Office staff: Monday to Friday except
-- public holidays. One row per rostered day up to yesterday.
INSERT INTO ta.Attendance WITH (TABLOCK) (EmployeeId, WorkDate, ShiftCode, ScheduledStart, ScheduledEnd, ClockIn, ClockOut, WorkedHours, LateMinutes, OvertimeHours, AttendanceStatus)
SELECT a.Seq, a.WorkDate, a.ShiftCode, a.SchedStart, DATEADD(hour, 8, a.SchedStart),
       CASE WHEN a.St <> 'Absent' THEN DATEADD(minute, a.Late - CASE WHEN a.Late = 0 THEN a.Rnd % 12 ELSE 0 END, a.SchedStart) END,
       CASE WHEN a.St <> 'Absent' THEN DATEADD(minute, 480 + a.OT * 60 + a.Rnd % 9, a.SchedStart) END,
       CASE WHEN a.St = 'Absent' THEN 0 ELSE 8 + a.OT - ROUND(a.Late / 60.0, 2) END, a.Late, a.OT, a.St
FROM (
    SELECT e.Seq, d.WorkDate, sh.ShiftCode, r.Rnd,
           DATEADD(hour, CASE sh.ShiftCode WHEN 'DAY' THEN 8 WHEN 'M' THEN 6 WHEN 'E' THEN 14 ELSE 22 END, CAST(d.WorkDate AS datetime2(0))) AS SchedStart,
           CASE WHEN r.Rnd % 1000 < 12 THEN 'Absent' WHEN r.Rnd % 1000 < 85 THEN 'Late' ELSE 'Present' END AS St,
           CASE WHEN r.Rnd % 1000 >= 12 AND r.Rnd % 1000 < 85 THEN 5 + r.Rnd / 1000 % 56 ELSE 0 END AS Late,
           CASE WHEN r.Rnd % 1000 >= 85 AND r.Rnd / 7 % 100 < CASE WHEN e.IsShift = 1 THEN 12 ELSE 5 END THEN 1 + r.Rnd / 700 % 4 ELSE 0 END AS OT
    FROM #E e
    JOIN (SELECT DATEADD(day, g.value, @Start) AS WorkDate, g.value AS DayNo FROM GENERATE_SERIES(0, @Days - 1) g) d
      ON d.WorkDate >= e.HireDate AND (e.SepDate IS NULL OR d.WorkDate <= e.SepDate)
    CROSS APPLY (SELECT dbo.R(CAST(e.Seq AS bigint) * 10000 + d.DayNo, 90) AS Rnd, (d.DayNo + e.Seq) % 6 AS Phase, (d.DayNo + e.Seq) / 6 % 3 AS Rot) r
    CROSS APPLY (SELECT CASE WHEN e.IsShift = 0 THEN 'DAY' ELSE CHOOSE(r.Rot + 1, 'M', 'E', 'N') END AS ShiftCode) sh
    WHERE (e.IsShift = 1 AND r.Phase < 4)
       OR (e.IsShift = 0 AND DATEDIFF(day, '2023-01-02', d.WorkDate) % 7 < 5 AND NOT EXISTS (SELECT 1 FROM ta.PublicHoliday h WHERE h.HolidayDate = d.WorkDate))
) a;
UPDATE a SET AttendanceStatus = 'On leave', ClockIn = NULL, ClockOut = NULL, WorkedHours = 0, LateMinutes = 0, OvertimeHours = 0
FROM ta.Attendance a
WHERE EXISTS (SELECT 1 FROM ta.LeaveRequest l WHERE l.EmployeeId = a.EmployeeId AND l.RequestStatus = 'Approved' AND a.WorkDate BETWEEN l.StartDate AND l.EndDate);
CREATE INDEX IX_Attendance_WorkDate ON ta.Attendance (WorkDate) INCLUDE (EmployeeId, AttendanceStatus, OvertimeHours);

-- ------------------------------------------------------------------------------------------------- performance
INSERT INTO talent.PerformanceReview (EmployeeId, ReviewYear, ReviewDate, ReviewerEmployeeId, GoalsScore, CompetencyScore, OverallRating, RatingLabel, PromotionRecommended)
SELECT e.Seq, y.value, DATEADD(day, 10 + dbo.R(e.Seq, 120 + y.value) % 40, DATEFROMPARTS(y.value + 1, 1, 1)), e.ManagerSeq,
       ROUND(r.Rating - 0.4 + (dbo.R(e.Seq, 130 + y.value) % 90) / 100.0, 2), ROUND(r.Rating - 0.4 + (dbo.R(e.Seq, 140 + y.value) % 90) / 100.0, 2), r.Rating,
       CHOOSE(r.Rating, N'Unsatisfactory', N'Needs improvement', N'Meets expectations', N'Exceeds expectations', N'Outstanding'),
       CASE WHEN r.Rating >= 4 AND dbo.R(e.Seq, 150 + y.value) % 100 < 25 THEN 1 ELSE 0 END
FROM #E e CROSS JOIN GENERATE_SERIES(2023, YEAR(@Today) - 1) y
CROSS APPLY (SELECT CASE WHEN x.p < 2 THEN 1 WHEN x.p < 10 THEN 2 WHEN x.p < 60 THEN 3 WHEN x.p < 90 THEN 4 ELSE 5 END AS Rating FROM (SELECT dbo.R(e.Seq, 110 + y.value) % 100 AS p) x) r
WHERE e.HireDate <= DATEFROMPARTS(y.value, 9, 30) AND (e.SepDate IS NULL OR e.SepDate > DATEADD(day, 60, DATEFROMPARTS(y.value, 12, 31)))
  AND DATEADD(day, 50, DATEFROMPARTS(y.value + 1, 1, 1)) <= @Today
ORDER BY y.value, e.Seq;

-- ----------------------------------------------------------------------------------------------------- payroll
-- One run per completed month. Overtime and unpaid absence come from the attendance of the month.
CREATE TABLE #AttM (Seq int, MonthStart date, OTHours decimal(8,2), AbsentDays int, PRIMARY KEY (Seq, MonthStart));
INSERT INTO #AttM SELECT EmployeeId, DATEFROMPARTS(YEAR(WorkDate), MONTH(WorkDate), 1), SUM(OvertimeHours), SUM(CASE WHEN AttendanceStatus = 'Absent' THEN 1 ELSE 0 END)
FROM ta.Attendance GROUP BY EmployeeId, DATEFROMPARTS(YEAR(WorkDate), MONTH(WorkDate), 1);

INSERT INTO pay.PayrollRun (PayrollRunId, PayYear, PayMonth, PeriodStart, PeriodEnd, PaymentDate, RunStatus, EmployeeCount, TotalGross, TotalNet)
SELECT YEAR(m.d) * 100 + MONTH(m.d), YEAR(m.d), MONTH(m.d), m.d, EOMONTH(m.d), DATEADD(day, -3, EOMONTH(m.d)), 'Posted', 0, 0, 0
FROM (SELECT DATEADD(month, g.value, @Start) AS d FROM GENERATE_SERIES(0, DATEDIFF(month, @Start, @Today) - 1) g) m;

INSERT INTO pay.Payslip (PayrollRunId, EmployeeId, DepartmentId, PaidDays, BasicPay, HousingAllowance, TransportAllowance, OtherAllowance, OvertimeHours, OvertimePay, Bonus,
    EndOfServiceGratuity, GrossPay, PensionDeduction, LoanDeduction, AbsenceDeduction, TotalDeductions, NetPay)
SELECT p.PayrollRunId, p.Seq, p.DepartmentId, p.PaidDays, p.BasicPay, p.Housing, p.Transport, p.Other, p.OTHours, p.OTPay, p.Bonus, p.EOSB,
       p.BasicPay + p.Housing + p.Transport + p.Other + p.OTPay + p.Bonus + p.EOSB, p.Pension, p.Loan, p.Absence, p.Pension + p.Loan + p.Absence,
       p.BasicPay + p.Housing + p.Transport + p.Other + p.OTPay + p.Bonus + p.EOSB - p.Pension - p.Loan - p.Absence
FROM (
    SELECT r.PayrollRunId, e.Seq, e.DepartmentId, f.PaidDays,
           CAST(ROUND(c.Basic * f.Ratio, 2) AS decimal(10,2)) AS BasicPay, CAST(ROUND(ROUND(c.Basic * 0.35, -1) * f.Ratio, 2) AS decimal(10,2)) AS Housing,
           CAST(ROUND(emp.TransportAllowance * f.Ratio, 2) AS decimal(10,2)) AS Transport, CAST(ROUND(emp.OtherAllowance * f.Ratio, 2) AS decimal(10,2)) AS Other,
           ISNULL(a.OTHours, 0) AS OTHours, CAST(ROUND(ISNULL(a.OTHours, 0) * c.Basic / 240 * 1.25, 2) AS decimal(10,2)) AS OTPay,
           CAST(CASE WHEN r.PayMonth = 3 AND pr.OverallRating >= 4 THEN ROUND(c.Basic * (pr.OverallRating - 3) * 0.5, -1) ELSE 0 END AS decimal(10,2)) AS Bonus,
           CAST(CASE WHEN e.SepDate BETWEEN r.PeriodStart AND r.PeriodEnd AND e.SepType NOT IN ('Absconded') AND e.Nat <> 'AE' AND sv.Yrs >= 1
                     THEN ROUND(c.Basic / 30 * (21 * CASE WHEN sv.Yrs > 5 THEN 5 ELSE sv.Yrs END + 30 * CASE WHEN sv.Yrs > 5 THEN sv.Yrs - 5 ELSE 0 END), -1) ELSE 0 END AS decimal(12,2)) AS EOSB,
           CAST(CASE WHEN e.Nat = 'AE' THEN ROUND((c.Basic + ROUND(c.Basic * 0.35, -1)) * f.Ratio * 0.05, 2) ELSE 0 END AS decimal(10,2)) AS Pension,
           CAST(CASE WHEN dbo.R(e.Seq, 160) % 100 < 7 THEN 500 + dbo.R(e.Seq, 161) % 11 * 100 ELSE 0 END AS decimal(10,2)) AS Loan,
           CAST(ROUND(ISNULL(a.AbsentDays, 0) * c.Basic / 30, 2) AS decimal(10,2)) AS Absence
    FROM pay.PayrollRun r
    JOIN #E e ON e.HireDate <= r.PeriodEnd AND (e.SepDate IS NULL OR e.SepDate >= r.PeriodStart)
    JOIN hr.Employee emp ON emp.EmployeeId = e.Seq
    JOIN #Comp c ON c.Seq = e.Seq AND c.Yr = r.PayYear
    CROSS APPLY (SELECT DATEDIFF(day, CASE WHEN e.HireDate > r.PeriodStart THEN e.HireDate ELSE r.PeriodStart END,
                                      CASE WHEN e.SepDate < r.PeriodEnd THEN e.SepDate ELSE r.PeriodEnd END) + 1 AS PaidDays) pd
    CROSS APPLY (SELECT pd.PaidDays, CAST(pd.PaidDays AS decimal(9,6)) / DAY(r.PeriodEnd) AS Ratio) f
    CROSS APPLY (SELECT DATEDIFF(day, e.HireDate, ISNULL(e.SepDate, @Today)) / 365.25 AS Yrs) sv
    LEFT JOIN #AttM a ON a.Seq = e.Seq AND a.MonthStart = r.PeriodStart
    LEFT JOIN talent.PerformanceReview pr ON pr.EmployeeId = e.Seq AND pr.ReviewYear = r.PayYear - 1
) p
ORDER BY p.PayrollRunId, p.Seq;

INSERT INTO pay.PayslipLine (PayslipId, ComponentCode, Amount)
SELECT s.PayslipId, v.Code, v.Amount
FROM pay.Payslip s CROSS APPLY (VALUES (1, 'BASIC', s.BasicPay), (2, 'HOUSING', s.HousingAllowance), (3, 'TRANSPORT', s.TransportAllowance), (4, 'OTHER', s.OtherAllowance),
    (5, 'OVERTIME', s.OvertimePay), (6, 'BONUS', s.Bonus), (7, 'EOSB', s.EndOfServiceGratuity), (8, 'PENSION', s.PensionDeduction), (9, 'LOAN', s.LoanDeduction),
    (10, 'ABSENCE', s.AbsenceDeduction)) v (Ord, Code, Amount)
WHERE v.Amount <> 0 ORDER BY s.PayslipId, v.Ord;
UPDATE r SET EmployeeCount = t.Cnt, TotalGross = t.Gross, TotalNet = t.Net
FROM pay.PayrollRun r JOIN (SELECT PayrollRunId, COUNT(*) AS Cnt, SUM(GrossPay) AS Gross, SUM(NetPay) AS Net FROM pay.Payslip GROUP BY PayrollRunId) t ON t.PayrollRunId = r.PayrollRunId;
CREATE INDEX IX_Payslip_Employee ON pay.Payslip (EmployeeId) INCLUDE (PayrollRunId, GrossPay, NetPay);

-- ---------------------------------------------------------------------------------------------------- training
INSERT INTO talent.TrainingCourse VALUES
(1,'AVSEC-AW',N'Aviation Security Awareness',N'Security',4,24,1,NULL,250),(2,'SMS-GEN',N'Safety Management System Awareness',N'Safety',4,36,1,NULL,200),
(3,'FIRE-BAS',N'Basic Fire Safety and Evacuation',N'Safety',3,24,1,NULL,150),(4,'INDUCT',N'Corporate Induction',N'Corporate',8,NULL,1,NULL,0),
(5,'ADP-TH',N'Airside Driving Permit',N'Airside',8,12,1,'AOP',600),(6,'ASA',N'Airside Safety Awareness',N'Airside',4,24,1,'AOP',300),
(7,'AVSEC-SCR',N'Screener Certification',N'Security',40,12,1,'SEC',1800),(8,'XRAY',N'X-ray Image Interpretation',N'Security',16,12,1,'SEC',900),
(9,'RFF-REC',N'Aircraft Rescue and Fire Fighting Recurrent',N'Fire',40,12,1,'RFF',2500),(10,'BA-WEAR',N'Breathing Apparatus Wearer',N'Fire',16,24,1,'RFF',1200),
(11,'HV-ELEC',N'High-Voltage Electrical Safety',N'Technical',16,36,1,'ENG',1400),(12,'LOTO',N'Lock-out and Tag-out',N'Technical',6,24,1,'ENG',400),
(13,'DGR-7',N'Dangerous Goods Regulations',N'Cargo',24,24,1,'CGO',1600),(14,'RAMP-SAF',N'Ramp Safety and Human Factors',N'Ground handling',8,24,1,'GHS',450),
(15,'GSE-OPS',N'Ground Support Equipment Operation',N'Ground handling',16,24,1,'GHS',800),(16,'PRM-ASST',N'Assisting Passengers with Reduced Mobility',N'Customer service',6,24,1,'TOP',350),
(17,'CX-EXC',N'Customer Service Excellence',N'Customer service',8,NULL,0,'TOP',500),(18,'CYBER',N'Cyber Security Awareness',N'Corporate',2,12,1,NULL,80),
(19,'LEAD-1',N'Supervisory Leadership Programme',N'Leadership',24,NULL,0,NULL,3200),(20,'FIRST-AID',N'First Aid and CPR',N'Safety',8,24,0,NULL,400),
(21,'ITIL-F',N'ITIL Foundation',N'Technical',24,NULL,0,'ITD',2800),(22,'EMS-AW',N'Environmental Management Awareness',N'Safety',3,36,0,NULL,150);

INSERT INTO talent.TrainingRecord (EmployeeId, CourseId, CompletionDate, ExpiryDate, Score, Result, DeliveryMethod)
SELECT e.Seq, c.CourseId, x.Done, CASE WHEN c.ValidityMonths IS NOT NULL THEN DATEADD(month, c.ValidityMonths, x.Done) END,
       CASE WHEN r.Rnd % 100 < 4 THEN 40 + r.Rnd / 100 % 30 ELSE 70 + r.Rnd / 100 % 31 END, CASE WHEN r.Rnd % 100 < 4 THEN 'Failed' ELSE 'Passed' END,
       CASE WHEN c.DurationHours <= 4 THEN 'E-learning' WHEN r.Rnd / 3 % 4 = 0 THEN 'On the job' ELSE 'Classroom' END
FROM #E e JOIN org.Department d ON d.DepartmentId = e.DepartmentId
JOIN talent.TrainingCourse c ON (c.AudienceDepartmentCode IS NULL OR c.AudienceDepartmentCode = d.DepartmentCode
                                 OR (c.CourseCode IN ('ADP-TH', 'ASA') AND d.DepartmentCode IN ('RFF', 'ENG', 'GHS', 'CGO')))
CROSS JOIN GENERATE_SERIES(0, 18) n
CROSS APPLY (SELECT DATEADD(day, n.value * 3, DATEADD(month, n.value * ISNULL(c.ValidityMonths, 0), DATEADD(day, 5 + dbo.R(e.Seq * 100 + c.CourseId, 170) % 50, e.HireDate))) AS Done) x
CROSS APPLY (SELECT dbo.R(CAST(e.Seq AS bigint) * 10000 + c.CourseId * 100 + n.value, 171) AS Rnd) r
WHERE (c.ValidityMonths IS NOT NULL OR n.value = 0) AND x.Done >= '2022-01-01' AND x.Done <= ISNULL(e.SepDate, @Today)
  AND (c.IsMandatory = 1 OR dbo.R(e.Seq * 100 + c.CourseId, 172) % 100 < CASE WHEN c.CourseCode = 'LEAD-1' AND e.GradeLevel < 8 THEN 0 ELSE 22 END)
ORDER BY x.Done, e.Seq;

-- ------------------------------------------------------------------------------------------------- recruitment
INSERT INTO talent.JobRequisition (RequisitionNumber, DepartmentId, PositionId, OpenedDate, ClosedDate, RequisitionStatus, Reason, HiredEmployeeId)
SELECT CONCAT('REQ-', RIGHT(CONCAT('00000', ROW_NUMBER() OVER (ORDER BY q.Opened, q.Seq)), 5)), q.DepartmentId, q.PositionId, q.Opened, q.Closed, q.St, q.Reason, q.Hired
FROM (
    SELECT e.Seq, e.DepartmentId, e.PositionId, DATEADD(day, -(45 + dbo.R(e.Seq, 180) % 80), e.HireDate) AS Opened, DATEADD(day, -(7 + dbo.R(e.Seq, 181) % 20), e.HireDate) AS Closed,
           'Filled' AS St, CASE WHEN dbo.R(e.Seq, 182) % 100 < 70 THEN 'Replacement' ELSE 'New position' END AS Reason, e.Seq AS Hired
    FROM #E e WHERE e.Seq > 1180
    UNION ALL
    SELECT 10000 + s.value, e.DepartmentId, e.PositionId, DATEADD(day, -(dbo.R(s.value, 183) % CASE WHEN s.value <= 34 THEN 70 ELSE 900 END), @Today),
           CASE WHEN s.value > 34 THEN DATEADD(day, -(dbo.R(s.value, 183) % 900) + 40, @Today) END, CASE WHEN s.value <= 34 THEN 'Open' ELSE 'Cancelled' END,
           CASE WHEN s.value % 3 = 0 THEN 'New position' ELSE 'Replacement' END, NULL
    FROM GENERATE_SERIES(1, 52) s JOIN #E e ON e.Seq = 1 + dbo.R(s.value, 184) % 1864 WHERE e.IsDirector = 0 AND e.IsManager = 0
) q;

INSERT INTO talent.JobApplication (RequisitionId, CandidateName, NationalityCode, Source, AppliedDate, Stage, InterviewScore, IsInternal)
SELECT q.RequisitionId,
       CASE WHEN n.value = 0 AND q.HiredEmployeeId IS NOT NULL THEN emp.FullName ELSE CONCAT(f.Name, N' ', l.Name) END,
       CASE WHEN n.value = 0 AND q.HiredEmployeeId IS NOT NULL THEN emp.NationalityCode ELSE nat.Code END,
       CHOOSE(r.Rnd % 10 + 1, 'Careers site', 'Careers site', 'Careers site', 'LinkedIn', 'LinkedIn', 'Referral', 'Referral', 'Agency', 'Job fair', 'Internal'),
       DATEADD(day, 1 + r.Rnd / 10 % 25, q.OpenedDate), st.Stage,
       CASE WHEN st.Stage IN ('Interviewed', 'Offered', 'Hired', 'Declined') THEN CASE WHEN st.Stage = 'Interviewed' THEN 40 + r.Rnd / 250 % 40 ELSE 72 + r.Rnd / 250 % 27 END END,
       CASE WHEN r.Rnd % 10 = 9 THEN 1 ELSE 0 END
FROM talent.JobRequisition q
CROSS JOIN GENERATE_SERIES(0, 13) n
CROSS APPLY (SELECT dbo.R(q.RequisitionId * 100 + n.value, 190) AS Rnd) r
LEFT JOIN hr.Employee emp ON emp.EmployeeId = q.HiredEmployeeId
JOIN #Nat nat ON dbo.R(q.RequisitionId * 100 + n.value, 191) % 1000 >= nat.LoBand AND dbo.R(q.RequisitionId * 100 + n.value, 191) % 1000 < nat.HiBand
CROSS APPLY (SELECT CASE WHEN r.Rnd / 7 % 100 < 70 THEN 'M' ELSE 'F' END AS G) g
CROSS APPLY (SELECT COUNT(*) AS c FROM #Name x WHERE x.Grp = nat.Grp AND x.Kind = g.G) fc
JOIN #Name f ON f.Grp = nat.Grp AND f.Kind = g.G AND f.Idx = dbo.R(q.RequisitionId * 100 + n.value, 192) % fc.c
CROSS APPLY (SELECT COUNT(*) AS c FROM #Name x WHERE x.Grp = nat.Grp AND x.Kind = 'L') lc
JOIN #Name l ON l.Grp = nat.Grp AND l.Kind = 'L' AND l.Idx = dbo.R(q.RequisitionId * 100 + n.value, 193) % lc.c
CROSS APPLY (SELECT CASE WHEN n.value = 0 AND q.HiredEmployeeId IS NOT NULL THEN 'Hired'
                         WHEN q.RequisitionStatus = 'Open' THEN CHOOSE(r.Rnd / 31 % 6 + 1, 'Applied', 'Applied', 'Screened', 'Screened', 'Interviewed', 'Offered')
                         ELSE CHOOSE(r.Rnd / 31 % 10 + 1, 'Rejected', 'Rejected', 'Rejected', 'Rejected', 'Screened', 'Screened', 'Interviewed', 'Interviewed', 'Interviewed', 'Declined') END AS Stage) st
WHERE n.value <= 4 + dbo.R(q.RequisitionId, 194) % 10
ORDER BY q.RequisitionId, n.value;

-- ----------------------------------------------------------------------------------------------------- summary
SELECT 'Employees' AS Item, COUNT(*) AS Rows_ FROM hr.Employee
UNION ALL SELECT 'Employees in service', COUNT(*) FROM hr.Employee WHERE EmploymentStatus = 'Active'
UNION ALL SELECT 'Separations', COUNT(*) FROM hr.Separation
UNION ALL SELECT 'Attendance rows', COUNT(*) FROM ta.Attendance
UNION ALL SELECT 'Leave requests', COUNT(*) FROM ta.LeaveRequest
UNION ALL SELECT 'Payslips', COUNT(*) FROM pay.Payslip
UNION ALL SELECT 'Payslip lines', COUNT(*) FROM pay.PayslipLine
UNION ALL SELECT 'Training records', COUNT(*) FROM talent.TrainingRecord
UNION ALL SELECT 'Job applications', COUNT(*) FROM talent.JobApplication;
GO
