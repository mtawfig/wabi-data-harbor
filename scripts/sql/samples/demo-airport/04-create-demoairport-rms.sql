-- ==============================================================================================================
-- DemoAirport_RMS — Resource Management System of Demo Airport (sample)
-- Planning and use of the airport's fixed resources, equipment and operational staff for every flight from
-- 1 January 2023 until the day the script runs: flight legs, stand, gate, check-in desk and baggage belt
-- allocations (planned against actual), allocation changes with reasons, resource closures, the shift roster of
-- the operational staff, ground support equipment and about 1.3 million ground handling tasks.
--
-- Run it LAST (as a sysadmin, in SSMS or sqlcmd), after 01 and 02: flights and resources come from
-- [DemoAirport_AODB]; staff and their shifts come from [DemoAirport_HCM], so a task is only given to someone who
-- was on shift. Equipment codes are the asset tags of [DemoAirport_Assets].
-- Re-running resets the data (about 3 minutes on SQL Server Express 2022). All times are airport local time.
-- ==============================================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
USE [master];
GO
IF DB_ID(N'DemoAirport_HCM') IS NULL OR DB_ID(N'DemoAirport_AODB') IS NULL
    THROW 50000, 'Run 01-create-demoairport-hcm.sql and 02-create-demoairport-aodb.sql first: RMS takes its staff and flights from them.', 1;
IF DB_ID(N'DemoAirport_RMS') IS NULL CREATE DATABASE [DemoAirport_RMS];
GO
ALTER DATABASE [DemoAirport_RMS] SET RECOVERY SIMPLE;
IF SUSER_ID(N'demoairport_reader') IS NULL
    CREATE LOGIN [demoairport_reader] WITH PASSWORD = N'DemoAirport#Reader#2026', CHECK_POLICY = ON;
GO
USE [DemoAirport_RMS];
GO
IF DATABASE_PRINCIPAL_ID(N'demoairport_reader') IS NULL CREATE USER [demoairport_reader] FOR LOGIN [demoairport_reader];
ALTER ROLE db_datareader ADD MEMBER [demoairport_reader];
GO
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
FROM sys.foreign_keys f JOIN sys.tables t ON t.object_id = f.parent_object_id;
SELECT @sql += N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N';' FROM sys.tables;
EXEC sys.sp_executesql @sql;
DROP FUNCTION IF EXISTS dbo.R;
IF SCHEMA_ID(N'ref') IS NULL EXEC (N'CREATE SCHEMA ref');
IF SCHEMA_ID(N'res') IS NULL EXEC (N'CREATE SCHEMA res');
IF SCHEMA_ID(N'plan') IS NULL EXEC (N'CREATE SCHEMA [plan]');
IF SCHEMA_ID(N'staff') IS NULL EXEC (N'CREATE SCHEMA staff');
IF SCHEMA_ID(N'ops') IS NULL EXEC (N'CREATE SCHEMA ops');
GO
CREATE FUNCTION dbo.R (@k bigint, @s int) RETURNS int WITH SCHEMABINDING AS
BEGIN
    RETURN CAST(HASHBYTES('MD5', CONCAT(@k, N':', @s)) AS int) & 2147483647;
END
GO
-- ---------------------------------------------------------------------------------------------------- tables
CREATE TABLE dbo.SampleInfo (DatabaseName sysname NOT NULL, DataFrom date NOT NULL, DataTo date NOT NULL, GeneratedAt datetime2(0) NOT NULL);

CREATE TABLE ref.Terminal (TerminalId int NOT NULL PRIMARY KEY, TerminalCode varchar(3) NOT NULL UNIQUE, TerminalName nvarchar(60) NOT NULL, TerminalType varchar(10) NOT NULL);
CREATE TABLE ref.Airline (AirlineId int NOT NULL PRIMARY KEY, IataCode char(2) NOT NULL UNIQUE, AirlineName nvarchar(80) NOT NULL, AirlineType varchar(12) NOT NULL, IsBaseCarrier bit NOT NULL,
    TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), GroundHandlerName nvarchar(80) NOT NULL);
CREATE TABLE ref.AircraftType (AircraftTypeId int NOT NULL PRIMARY KEY, IataCode char(3) NOT NULL UNIQUE, TypeName nvarchar(60) NOT NULL, BodyType varchar(6) NOT NULL, SizeCategory char(1) NOT NULL,
    Seats smallint NOT NULL, IsFreighter bit NOT NULL);
CREATE TABLE ref.ChangeReason (ChangeReasonId int NOT NULL PRIMARY KEY, ReasonName nvarchar(60) NOT NULL, ReasonGroup nvarchar(30) NOT NULL);
CREATE TABLE ref.StaffGroup (StaffGroupCode varchar(8) NOT NULL PRIMARY KEY, StaffGroupName nvarchar(60) NOT NULL);
CREATE TABLE ref.EquipmentType (EquipmentTypeId int NOT NULL PRIMARY KEY, EquipmentTypeCode varchar(8) NOT NULL UNIQUE, EquipmentTypeName nvarchar(60) NOT NULL, AssetCategoryCode varchar(8) NOT NULL);
CREATE TABLE ref.TaskType (TaskTypeId int NOT NULL PRIMARY KEY, TaskCode varchar(6) NOT NULL UNIQUE, TaskName nvarchar(60) NOT NULL, Direction char(1) NOT NULL, AppliesTo varchar(10) NOT NULL,
    StaffGroupCode varchar(8) NOT NULL REFERENCES ref.StaffGroup (StaffGroupCode), EquipmentTypeId int NULL REFERENCES ref.EquipmentType (EquipmentTypeId),
    StartOffsetMinutes smallint NOT NULL, StandardDurationMinutes smallint NOT NULL);

CREATE TABLE res.Stand (StandId int NOT NULL PRIMARY KEY, StandCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), StandType varchar(8) NOT NULL,
    Apron nvarchar(30) NOT NULL, MaxSizeCategory char(1) NOT NULL, HasBoardingBridge bit NOT NULL, StandGroup varchar(4) NOT NULL);
CREATE TABLE res.Gate (GateId int NOT NULL PRIMARY KEY, GateCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), GateType varchar(8) NOT NULL,
    StandId int NULL REFERENCES res.Stand (StandId));
CREATE TABLE res.CheckInDesk (CheckInDeskId int NOT NULL PRIMARY KEY, DeskCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), Island char(1) NOT NULL);
CREATE TABLE res.BaggageBelt (BaggageBeltId int NOT NULL PRIMARY KEY, BeltCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), BeltLengthMetres smallint NOT NULL);
CREATE TABLE res.Equipment (EquipmentId int IDENTITY(1,1) NOT NULL PRIMARY KEY, EquipmentCode varchar(16) NOT NULL UNIQUE, EquipmentTypeId int NOT NULL REFERENCES ref.EquipmentType (EquipmentTypeId),
    EquipmentStatus varchar(12) NOT NULL);

CREATE TABLE staff.Staff (StaffId int NOT NULL PRIMARY KEY, EmployeeNumber varchar(10) NOT NULL UNIQUE, FullName nvarchar(90) NOT NULL, JobTitle nvarchar(100) NOT NULL,
    DepartmentCode varchar(5) NOT NULL, SectionCode varchar(5) NOT NULL, StaffGroupCode varchar(8) NOT NULL REFERENCES ref.StaffGroup (StaffGroupCode), IsShiftWorker bit NOT NULL,
    HireDate date NOT NULL, SeparationDate date NULL, IsActive bit NOT NULL);
CREATE TABLE staff.ShiftAssignment (ShiftAssignmentId int IDENTITY(1,1) NOT NULL PRIMARY KEY, StaffId int NOT NULL REFERENCES staff.Staff (StaffId), ShiftDate date NOT NULL,
    ShiftCode varchar(5) NOT NULL, ShiftStart datetime2(0) NOT NULL, ShiftEnd datetime2(0) NOT NULL, AttendanceStatus varchar(10) NOT NULL, OvertimeHours decimal(4,2) NOT NULL,
    CONSTRAINT UQ_ShiftAssignment UNIQUE (StaffId, ShiftDate));

CREATE TABLE [plan].FlightLeg (FlightLegId int NOT NULL PRIMARY KEY, RotationId int NOT NULL, LinkedFlightLegId int NULL, FlightNumber varchar(8) NOT NULL,
    AirlineId int NOT NULL REFERENCES ref.Airline (AirlineId), Direction char(1) NOT NULL, FlightDate date NOT NULL, RouteAirportCode char(3) NOT NULL, RouteCity nvarchar(60) NOT NULL,
    FlightNature varchar(10) NOT NULL, AircraftTypeId int NOT NULL REFERENCES ref.AircraftType (AircraftTypeId), Registration varchar(10) NOT NULL,
    TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), ScheduledDateTime datetime2(0) NOT NULL, ActualDateTime datetime2(0) NULL, DelayMinutes smallint NULL,
    PassengersTotal smallint NOT NULL, BagsCount smallint NOT NULL, FlightStatus varchar(10) NOT NULL);
CREATE TABLE [plan].StandAllocation (StandAllocationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, ArrivalFlightLegId int NULL REFERENCES [plan].FlightLeg (FlightLegId),
    DepartureFlightLegId int NULL REFERENCES [plan].FlightLeg (FlightLegId), AirlineId int NOT NULL REFERENCES ref.Airline (AirlineId),
    AircraftTypeId int NOT NULL REFERENCES ref.AircraftType (AircraftTypeId), PlannedStandId int NOT NULL REFERENCES res.Stand (StandId), ActualStandId int NOT NULL REFERENCES res.Stand (StandId),
    PlannedStart datetime2(0) NOT NULL, PlannedEnd datetime2(0) NOT NULL, ActualStart datetime2(0) NULL, ActualEnd datetime2(0) NULL,
    OccupancyMinutes AS (DATEDIFF(minute, ActualStart, ActualEnd)) PERSISTED, AllocationStatus varchar(10) NOT NULL, WasChanged bit NOT NULL);
CREATE TABLE [plan].GateAllocation (GateAllocationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, FlightLegId int NOT NULL REFERENCES [plan].FlightLeg (FlightLegId),
    PlannedGateId int NOT NULL REFERENCES res.Gate (GateId), ActualGateId int NOT NULL REFERENCES res.Gate (GateId), PlannedOpen datetime2(0) NOT NULL, PlannedClose datetime2(0) NOT NULL,
    ActualOpen datetime2(0) NULL, ActualClose datetime2(0) NULL, AllocationStatus varchar(10) NOT NULL, WasChanged bit NOT NULL);
CREATE TABLE [plan].CheckInAllocation (CheckInAllocationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, FlightLegId int NOT NULL REFERENCES [plan].FlightLeg (FlightLegId),
    CheckInDeskId int NOT NULL REFERENCES res.CheckInDesk (CheckInDeskId), DeskClass varchar(10) NOT NULL, OpenDateTime datetime2(0) NOT NULL, CloseDateTime datetime2(0) NOT NULL,
    PassengersProcessed smallint NOT NULL, AllocationStatus varchar(10) NOT NULL);
CREATE TABLE [plan].BeltAllocation (BeltAllocationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, FlightLegId int NOT NULL REFERENCES [plan].FlightLeg (FlightLegId),
    PlannedBeltId int NOT NULL REFERENCES res.BaggageBelt (BaggageBeltId), ActualBeltId int NOT NULL REFERENCES res.BaggageBelt (BaggageBeltId), PlannedStart datetime2(0) NOT NULL,
    PlannedEnd datetime2(0) NOT NULL, FirstBagDateTime datetime2(0) NULL, LastBagDateTime datetime2(0) NULL, BagsDelivered smallint NOT NULL, AllocationStatus varchar(10) NOT NULL,
    WasChanged bit NOT NULL);
CREATE TABLE [plan].AllocationChange (AllocationChangeId int IDENTITY(1,1) NOT NULL PRIMARY KEY, ResourceType varchar(8) NOT NULL, FlightLegId int NOT NULL REFERENCES [plan].FlightLeg (FlightLegId),
    FromResourceCode varchar(5) NOT NULL, ToResourceCode varchar(5) NOT NULL, ChangedAt datetime2(0) NOT NULL, MinutesBeforeUse smallint NOT NULL,
    ChangeReasonId int NOT NULL REFERENCES ref.ChangeReason (ChangeReasonId), ChangedByStaffId int NULL REFERENCES staff.Staff (StaffId));
CREATE TABLE [plan].ResourceClosure (ResourceClosureId int IDENTITY(1,1) NOT NULL PRIMARY KEY, ResourceType varchar(8) NOT NULL, ResourceCode varchar(5) NOT NULL,
    ClosureStart datetime2(0) NOT NULL, ClosureEnd datetime2(0) NOT NULL, DurationMinutes AS (DATEDIFF(minute, ClosureStart, ClosureEnd)) PERSISTED, Reason nvarchar(40) NOT NULL, IsPlanned bit NOT NULL);
CREATE TABLE ops.Task (TaskId int IDENTITY(1,1) NOT NULL PRIMARY KEY, FlightLegId int NOT NULL REFERENCES [plan].FlightLeg (FlightLegId),
    TaskTypeId int NOT NULL REFERENCES ref.TaskType (TaskTypeId), PlannedStart datetime2(0) NOT NULL, PlannedEnd datetime2(0) NOT NULL, ActualStart datetime2(0) NULL, ActualEnd datetime2(0) NULL,
    StartDelayMinutes smallint NULL, StaffId int NULL REFERENCES staff.Staff (StaffId), EquipmentId int NULL REFERENCES res.Equipment (EquipmentId), TaskStatus varchar(10) NOT NULL);
GO
-- ------------------------------------------------------------------------------------------------ reference data
DECLARE @Start date = '2023-01-01', @Today date = CAST(GETDATE() AS date), @Now datetime2(0) = SYSDATETIME();
INSERT INTO dbo.SampleInfo VALUES (N'DemoAirport_RMS', @Start, @Today, @Now);

INSERT INTO ref.Terminal SELECT TerminalId, TerminalCode, TerminalName, TerminalType FROM DemoAirport_AODB.ref.Terminal;
INSERT INTO ref.Airline SELECT a.AirlineId, a.IataCode, a.AirlineName, a.AirlineType, a.IsBaseCarrier, a.TerminalId, h.HandlerName
FROM DemoAirport_AODB.ref.Airline a JOIN DemoAirport_AODB.ref.GroundHandler h ON h.GroundHandlerId = a.GroundHandlerId;
INSERT INTO ref.AircraftType SELECT AircraftTypeId, IataCode, TypeName, BodyType, SizeCategory, Seats, IsFreighter FROM DemoAirport_AODB.ref.AircraftType;
INSERT INTO res.Stand SELECT StandId, StandCode, TerminalId, StandType, Apron, MaxSizeCategory, HasBoardingBridge, StandGroup FROM DemoAirport_AODB.ref.Stand;
INSERT INTO res.Gate SELECT GateId, GateCode, TerminalId, GateType, StandId FROM DemoAirport_AODB.ref.Gate;
INSERT INTO res.CheckInDesk SELECT CheckInDeskId, DeskCode, TerminalId, Island FROM DemoAirport_AODB.ref.CheckInDesk;
INSERT INTO res.BaggageBelt SELECT BaggageBeltId, BeltCode, TerminalId, BeltLengthMetres FROM DemoAirport_AODB.ref.BaggageBelt;

INSERT INTO ref.ChangeReason VALUES (1,N'Late arrival of the previous aircraft',N'Flight disruption'),(2,N'Early arrival',N'Flight disruption'),(3,N'Aircraft type change',N'Airline request'),
(4,N'Airline request',N'Airline request'),(5,N'Resource fault',N'Infrastructure'),(6,N'Resource closed for maintenance',N'Infrastructure'),(7,N'Overlapping occupation',N'Planning'),
(8,N'Passenger connection',N'Planning'),(9,N'Security or authority request',N'Authorities'),(10,N'Weather',N'Flight disruption');
INSERT INTO ref.StaffGroup VALUES ('AIRSIDE',N'Airside operations and marshalling'),('RAMP',N'Ramp handling'),('GSEOPS',N'Equipment operators'),('PAXSVC',N'Passenger services'),
('LOADCTL',N'Load control'),('BAGGAGE',N'Baggage operations'),('PRM',N'Special assistance'),('TERMINAL',N'Terminal duty management'),('OCC',N'Operations control centre'),('CARGO',N'Cargo handling');
INSERT INTO ref.EquipmentType VALUES (1,'TUG',N'Pushback tractor','GSE-TUG'),(2,'BELTLDR',N'Belt loader','GSE-BLD'),(3,'HILOADER',N'Container high loader','GSE-HLD'),(4,'GPU',N'Mobile ground power unit','GSE-GPU'),
(5,'STAIRS',N'Passenger stairs','GSE-STR'),(6,'BUS',N'Apron passenger bus','GSE-BUS'),(7,'BAGTRAC',N'Baggage tractor','GSE-BTR');
INSERT INTO res.Equipment (EquipmentCode, EquipmentTypeId, EquipmentStatus)
SELECT CONCAT('DA-', t.AssetCategoryCode, '-', RIGHT(CONCAT('0000', n.value), 5)), t.EquipmentTypeId, CASE WHEN (t.EquipmentTypeId * 7 + n.value) % 23 = 0 THEN 'In workshop' ELSE 'Available' END
FROM ref.EquipmentType t CROSS APPLY GENERATE_SERIES(1, CHOOSE(t.EquipmentTypeId, 22, 44, 26, 34, 30, 28, 90)) n ORDER BY t.EquipmentTypeId, n.value;
INSERT INTO ref.TaskType VALUES
(1,'MARSH',N'Aircraft marshalling and chocks','A','All','AIRSIDE',NULL,-5,10),(2,'GPU',N'Ground power connection','A','All','GSEOPS',4,1,6),
(3,'DISEMB',N'Passenger disembarkation','A','Passenger','PAXSVC',NULL,2,18),(4,'BAGU',N'Baggage unloading','A','Passenger','RAMP',2,3,25),
(5,'BAGDEL',N'Baggage delivery to belt','A','Passenger','BAGGAGE',7,8,28),(6,'CLEAN',N'Cabin cleaning','A','Passenger','RAMP',NULL,22,20),
(7,'CGOU',N'Cargo unloading','A','Cargo','CARGO',3,10,60),
(8,'CKIN',N'Check-in service','D','Passenger','PAXSVC',NULL,-180,120),(9,'BOARD',N'Boarding and gate service','D','Passenger','PAXSVC',NULL,-45,35),
(10,'BAGL',N'Baggage loading','D','Passenger','RAMP',2,-50,35),(11,'CGOL',N'Cargo loading','D','Cargo','CARGO',3,-95,70),
(12,'LOADC',N'Load control, weight and balance','D','All','LOADCTL',NULL,-60,30),(13,'PUSH',N'Pushback','D','All','GSEOPS',1,-3,8),
(14,'PRMA',N'Reduced-mobility assistance, arrival','A','Passenger','PRM',NULL,5,30),(15,'PRMD',N'Reduced-mobility assistance, departure','D','Passenger','PRM',NULL,-75,40);

-- ---------------------------------------------------------------------------------------------- staff and roster
INSERT INTO staff.Staff (StaffId, EmployeeNumber, FullName, JobTitle, DepartmentCode, SectionCode, StaffGroupCode, IsShiftWorker, HireDate, SeparationDate, IsActive)
SELECT e.EmployeeId, e.EmployeeNumber, e.FullName, p.PositionTitle, d.DepartmentCode, s.SectionCode,
       CASE s.SectionCode WHEN 'RMP' THEN 'RAMP' WHEN 'GSE' THEN 'GSEOPS' WHEN 'CKI' THEN 'PAXSVC' WHEN 'PAX' THEN 'PAXSVC' WHEN 'LDC' THEN 'LOADCTL' WHEN 'BAG' THEN 'BAGGAGE' WHEN 'PRM' THEN 'PRM'
                          WHEN 'TDM' THEN 'TERMINAL' WHEN 'OCC' THEN 'OCC' WHEN 'AFO' THEN 'AIRSIDE' WHEN 'APM' THEN 'AIRSIDE' WHEN 'WAS' THEN 'AIRSIDE' ELSE 'CARGO' END,
       e.IsShiftWorker, e.HireDate, e.SeparationDate, CASE WHEN e.EmploymentStatus = 'Active' THEN 1 ELSE 0 END
FROM DemoAirport_HCM.hr.Employee e JOIN DemoAirport_HCM.org.Department d ON d.DepartmentId = e.DepartmentId JOIN DemoAirport_HCM.org.Section s ON s.SectionId = e.SectionId
JOIN DemoAirport_HCM.org.Position p ON p.PositionId = e.PositionId
WHERE d.DepartmentCode IN ('AOP', 'TOP', 'GHS', 'CGO');
INSERT INTO staff.ShiftAssignment WITH (TABLOCK) (StaffId, ShiftDate, ShiftCode, ShiftStart, ShiftEnd, AttendanceStatus, OvertimeHours)
SELECT a.EmployeeId, a.WorkDate, a.ShiftCode, a.ScheduledStart, a.ScheduledEnd, a.AttendanceStatus, a.OvertimeHours
FROM DemoAirport_HCM.ta.Attendance a JOIN staff.Staff s ON s.StaffId = a.EmployeeId ORDER BY a.WorkDate, a.EmployeeId;
-- Who was at work on each shift, per staff group, numbered for assignment.
CREATE TABLE #Roster (ShiftDate date, ShiftCode varchar(5), Grp varchar(8), Idx int, StaffId int, PRIMARY KEY (ShiftDate, ShiftCode, Grp, Idx));
INSERT INTO #Roster
SELECT a.ShiftDate, a.ShiftCode, s.StaffGroupCode, ROW_NUMBER() OVER (PARTITION BY a.ShiftDate, a.ShiftCode, s.StaffGroupCode ORDER BY a.StaffId) - 1, a.StaffId
FROM staff.ShiftAssignment a JOIN staff.Staff s ON s.StaffId = a.StaffId WHERE a.AttendanceStatus IN ('Present', 'Late') AND a.ShiftCode IN ('M', 'E', 'N');
CREATE TABLE #RC (ShiftDate date, ShiftCode varchar(5), Grp varchar(8), Cnt int, PRIMARY KEY (ShiftDate, ShiftCode, Grp));
INSERT INTO #RC SELECT ShiftDate, ShiftCode, Grp, COUNT(*) FROM #Roster GROUP BY ShiftDate, ShiftCode, Grp;

-- ----------------------------------------------------------------------------------------------------- flight legs
CREATE TABLE #F (FlightId int PRIMARY KEY, RotationId int, Direction char(1), IsBase bit, IsPax bit, IsWide bit, IsFullService bit, TerminalId int, AirlineId int, AircraftTypeId int,
    Sched datetime2(0), Best datetime2(0), Operated bit, Status varchar(10), StandId int, GateId int NULL, BeltId int NULL, DeskFrom int NULL, DeskTo int NULL,
    Pax int, PaxTransfer int, Prm int, Bags int, Rnd int);
INSERT INTO #F
SELECT f.FlightId, f.RotationId, f.Direction, al.IsBaseCarrier, CASE WHEN f.FlightNature = 'Passenger' THEN 1 ELSE 0 END, CASE WHEN t.BodyType = 'Wide' THEN 1 ELSE 0 END,
       CASE WHEN al.AirlineType = 'Full service' THEN 1 ELSE 0 END, f.TerminalId, f.AirlineId, f.AircraftTypeId, f.ScheduledDateTime, ISNULL(f.ActualBlockDateTime, f.ScheduledDateTime),
       CASE WHEN f.FlightStatus IN ('Arrived', 'Departed') THEN 1 ELSE 0 END, f.FlightStatus, f.StandId, f.GateId, f.BaggageBeltId, f.CheckInDeskFromId, f.CheckInDeskToId,
       ISNULL(l.PassengersTotal, 0), ISNULL(l.PassengersTransfer, 0), ISNULL(l.PassengersReducedMobility, 0), ISNULL(l.BagsCount, 0), dbo.R(f.FlightId, 1)
FROM DemoAirport_AODB.ops.Flight f JOIN DemoAirport_AODB.ref.Airline al ON al.AirlineId = f.AirlineId JOIN DemoAirport_AODB.ref.AircraftType t ON t.AircraftTypeId = f.AircraftTypeId
LEFT JOIN DemoAirport_AODB.ops.FlightLoad l ON l.FlightId = f.FlightId;

INSERT INTO [plan].FlightLeg (FlightLegId, RotationId, LinkedFlightLegId, FlightNumber, AirlineId, Direction, FlightDate, RouteAirportCode, RouteCity, FlightNature, AircraftTypeId, Registration,
    TerminalId, ScheduledDateTime, ActualDateTime, DelayMinutes, PassengersTotal, BagsCount, FlightStatus)
SELECT f.FlightId, f.RotationId, f.LinkedFlightId, f.FlightNumber, f.AirlineId, f.Direction, f.FlightDate, ap.IataCode, ap.City, f.FlightNature, f.AircraftTypeId, ac.Registration, f.TerminalId,
       f.ScheduledDateTime, f.ActualBlockDateTime, f.DelayMinutes, x.Pax, x.Bags, f.FlightStatus
FROM DemoAirport_AODB.ops.Flight f JOIN #F x ON x.FlightId = f.FlightId JOIN DemoAirport_AODB.ref.Airport ap ON ap.AirportId = f.RouteAirportId
JOIN DemoAirport_AODB.ref.Aircraft ac ON ac.AircraftId = f.AircraftId;
CREATE INDEX IX_FlightLeg_Date ON [plan].FlightLeg (FlightDate) INCLUDE (AirlineId, Direction, FlightStatus);

-- "The next resource of the same kind": the resource a changed allocation was first planned on.
CREATE TABLE #StandAlt (StandId int PRIMARY KEY, AltId int);
INSERT INTO #StandAlt SELECT StandId, ISNULL(LEAD(StandId) OVER (PARTITION BY StandGroup ORDER BY StandId), MIN(StandId) OVER (PARTITION BY StandGroup)) FROM res.Stand;
CREATE TABLE #GateAlt (GateId int PRIMARY KEY, AltId int);
INSERT INTO #GateAlt SELECT GateId, ISNULL(LEAD(GateId) OVER (PARTITION BY TerminalId, GateType ORDER BY GateId), MIN(GateId) OVER (PARTITION BY TerminalId, GateType)) FROM res.Gate;
CREATE TABLE #BeltAlt (BeltId int PRIMARY KEY, AltId int);
INSERT INTO #BeltAlt SELECT BaggageBeltId, ISNULL(LEAD(BaggageBeltId) OVER (PARTITION BY TerminalId ORDER BY BaggageBeltId), MIN(BaggageBeltId) OVER (PARTITION BY TerminalId)) FROM res.BaggageBelt;

-- -------------------------------------------------------------------------------------------------------- stands
-- One allocation per ground visit: a turn-round of another airline (arrival and departure), or one flight of the base carrier.
INSERT INTO [plan].StandAllocation WITH (TABLOCK) (ArrivalFlightLegId, DepartureFlightLegId, AirlineId, AircraftTypeId, PlannedStandId, ActualStandId, PlannedStart, PlannedEnd, ActualStart, ActualEnd,
    AllocationStatus, WasChanged)
SELECT v.ArrId, v.DepId, v.AirlineId, v.AircraftTypeId, CASE WHEN v.Rnd / 3 % 100 < 8 THEN alt.AltId ELSE v.StandId END, v.StandId, v.PlannedStart, v.PlannedEnd,
       CASE WHEN v.Operated = 1 THEN v.ActualStart END, CASE WHEN v.Operated = 1 THEN v.ActualEnd END,
       CASE WHEN v.Operated = 0 THEN 'Released' WHEN v.ActualEnd > @Now THEN 'Planned' ELSE 'Completed' END, CASE WHEN v.Rnd / 3 % 100 < 8 THEN 1 ELSE 0 END
FROM (
    SELECT a.FlightId AS ArrId, d.FlightId AS DepId, a.AirlineId, a.AircraftTypeId, a.StandId, a.Rnd, a.Sched AS PlannedStart, d.Sched AS PlannedEnd, a.Best AS ActualStart,
           CASE WHEN d.Best > a.Best THEN d.Best ELSE DATEADD(minute, 30, a.Best) END AS ActualEnd, CASE WHEN a.Operated = 1 AND d.Operated = 1 THEN 1 ELSE 0 END AS Operated
    FROM #F a JOIN #F d ON d.RotationId = a.RotationId AND d.Direction = 'D' WHERE a.Direction = 'A' AND a.IsBase = 0
    UNION ALL
    SELECT CASE WHEN f.Direction = 'A' THEN f.FlightId END, CASE WHEN f.Direction = 'D' THEN f.FlightId END, f.AirlineId, f.AircraftTypeId, f.StandId, f.Rnd,
           CASE WHEN f.Direction = 'A' THEN f.Sched ELSE DATEADD(minute, -70, f.Sched) END, CASE WHEN f.Direction = 'A' THEN DATEADD(minute, 60, f.Sched) ELSE f.Sched END,
           CASE WHEN f.Direction = 'A' THEN f.Best ELSE DATEADD(minute, -70, f.Sched) END, CASE WHEN f.Direction = 'A' THEN DATEADD(minute, 60, f.Best) ELSE f.Best END, f.Operated
    FROM #F f WHERE f.IsBase = 1
) v JOIN #StandAlt alt ON alt.StandId = v.StandId
ORDER BY v.PlannedStart, ISNULL(v.ArrId, v.DepId);
CREATE INDEX IX_StandAllocation_Stand ON [plan].StandAllocation (ActualStandId, ActualStart) INCLUDE (ActualEnd);

-- --------------------------------------------------------------------------------------------------------- gates
INSERT INTO [plan].GateAllocation WITH (TABLOCK) (FlightLegId, PlannedGateId, ActualGateId, PlannedOpen, PlannedClose, ActualOpen, ActualClose, AllocationStatus, WasChanged)
SELECT f.FlightId, CASE WHEN f.Rnd / 5 % 100 < 6 THEN alt.AltId ELSE f.GateId END, f.GateId, t.PlannedOpen, t.PlannedClose,
       CASE WHEN f.Operated = 1 THEN DATEADD(minute, DATEDIFF(minute, f.Sched, f.Best) / CASE WHEN f.Direction = 'D' THEN 2 ELSE 1 END, t.PlannedOpen) END,
       CASE WHEN f.Operated = 1 THEN DATEADD(minute, DATEDIFF(minute, f.Sched, f.Best), t.PlannedClose) END,
       CASE WHEN f.Operated = 0 THEN CASE WHEN f.Status = 'Scheduled' THEN 'Planned' ELSE 'Released' END ELSE 'Completed' END, CASE WHEN f.Rnd / 5 % 100 < 6 THEN 1 ELSE 0 END
FROM #F f JOIN #GateAlt alt ON alt.GateId = f.GateId
CROSS APPLY (SELECT CASE WHEN f.Direction = 'D' THEN DATEADD(minute, -60, f.Sched) ELSE f.Sched END AS PlannedOpen, CASE WHEN f.Direction = 'D' THEN DATEADD(minute, -10, f.Sched) ELSE DATEADD(minute, 25, f.Sched) END AS PlannedClose) t
WHERE f.GateId IS NOT NULL
ORDER BY t.PlannedOpen, f.FlightId;
CREATE INDEX IX_GateAllocation_Gate ON [plan].GateAllocation (ActualGateId, PlannedOpen) INCLUDE (PlannedClose, ActualOpen, ActualClose);

-- --------------------------------------------------------------------------------------------------- check-in desks
INSERT INTO [plan].CheckInAllocation WITH (TABLOCK) (FlightLegId, CheckInDeskId, DeskClass, OpenDateTime, CloseDateTime, PassengersProcessed, AllocationStatus)
SELECT f.FlightId, n.value, c.DeskClass, DATEADD(minute, CASE WHEN f.IsWide = 1 THEN -210 ELSE -180 END, f.Sched), DATEADD(minute, -60 + CASE WHEN f.Best > f.Sched THEN DATEDIFF(minute, f.Sched, f.Best) / 3 ELSE 0 END, f.Sched),
       CASE WHEN f.Operated = 0 THEN 0 ELSE (f.Pax - f.PaxTransfer) * 7 / 10 * CASE c.DeskClass WHEN 'Business' THEN 5 WHEN 'Bag drop' THEN 14 ELSE 10 END
                                            / (10 * (f.DeskTo - f.DeskFrom + 1)) + dbo.R(f.FlightId * 10 + n.value - f.DeskFrom, 2) % 5 END,
       CASE WHEN f.Operated = 0 THEN CASE WHEN f.Status = 'Scheduled' THEN 'Planned' ELSE 'Released' END ELSE 'Completed' END
FROM #F f CROSS APPLY GENERATE_SERIES(f.DeskFrom, f.DeskTo) n
CROSS APPLY (SELECT CASE WHEN f.IsFullService = 1 AND n.value = f.DeskFrom THEN 'Business' WHEN n.value = f.DeskTo THEN 'Bag drop' ELSE 'Economy' END AS DeskClass) c
WHERE f.DeskFrom IS NOT NULL
ORDER BY f.Sched, f.FlightId, n.value;
CREATE INDEX IX_CheckInAllocation_Desk ON [plan].CheckInAllocation (CheckInDeskId, OpenDateTime) INCLUDE (CloseDateTime);

-- ---------------------------------------------------------------------------------------------------- baggage belts
INSERT INTO [plan].BeltAllocation WITH (TABLOCK) (FlightLegId, PlannedBeltId, ActualBeltId, PlannedStart, PlannedEnd, FirstBagDateTime, LastBagDateTime, BagsDelivered, AllocationStatus, WasChanged)
SELECT f.FlightId, CASE WHEN f.Rnd / 7 % 100 < 4 THEN alt.AltId ELSE f.BeltId END, f.BeltId, DATEADD(minute, 10, f.Sched), DATEADD(minute, 45, f.Sched), fb.ActualDateTime, lb.ActualDateTime,
       CASE WHEN f.Operated = 1 THEN f.Bags - f.Bags * f.PaxTransfer / CASE WHEN f.Pax = 0 THEN 1 ELSE f.Pax END ELSE 0 END,
       CASE WHEN f.Operated = 0 THEN CASE WHEN f.Status = 'Scheduled' THEN 'Planned' ELSE 'Released' END ELSE 'Completed' END, CASE WHEN f.Rnd / 7 % 100 < 4 THEN 1 ELSE 0 END
FROM #F f JOIN #BeltAlt alt ON alt.BeltId = f.BeltId
LEFT JOIN DemoAirport_AODB.ops.FlightMilestone fb ON fb.FlightId = f.FlightId AND fb.MilestoneCode = 'FBAG'
LEFT JOIN DemoAirport_AODB.ops.FlightMilestone lb ON lb.FlightId = f.FlightId AND lb.MilestoneCode = 'LBAG'
WHERE f.BeltId IS NOT NULL
ORDER BY f.Sched, f.FlightId;
CREATE INDEX IX_BeltAllocation_Belt ON [plan].BeltAllocation (ActualBeltId, PlannedStart) INCLUDE (PlannedEnd, LastBagDateTime);

-- ------------------------------------------------------------------------------------------------ allocation changes
SELECT c.* INTO #Chg FROM (
    SELECT 'Stand' AS ResourceType, ISNULL(a.ArrivalFlightLegId, a.DepartureFlightLegId) AS FlightLegId, p.StandCode AS FromCode, s.StandCode AS ToCode, a.PlannedStart AS UseAt,
           dbo.R(a.StandAllocationId, 3) AS Rnd
    FROM [plan].StandAllocation a JOIN res.Stand p ON p.StandId = a.PlannedStandId JOIN res.Stand s ON s.StandId = a.ActualStandId WHERE a.WasChanged = 1
    UNION ALL
    SELECT 'Gate', a.FlightLegId, p.GateCode, s.GateCode, a.PlannedOpen, dbo.R(a.GateAllocationId, 4)
    FROM [plan].GateAllocation a JOIN res.Gate p ON p.GateId = a.PlannedGateId JOIN res.Gate s ON s.GateId = a.ActualGateId WHERE a.WasChanged = 1
    UNION ALL
    SELECT 'Belt', a.FlightLegId, p.BeltCode, s.BeltCode, a.PlannedStart, dbo.R(a.BeltAllocationId, 5)
    FROM [plan].BeltAllocation a JOIN res.BaggageBelt p ON p.BaggageBeltId = a.PlannedBeltId JOIN res.BaggageBelt s ON s.BaggageBeltId = a.ActualBeltId WHERE a.WasChanged = 1
) c;
INSERT INTO [plan].AllocationChange (ResourceType, FlightLegId, FromResourceCode, ToResourceCode, ChangedAt, MinutesBeforeUse, ChangeReasonId, ChangedByStaffId)
SELECT c.ResourceType, c.FlightLegId, c.FromCode, c.ToCode, DATEADD(minute, -m.Mins, c.UseAt), m.Mins,
       CHOOSE(c.Rnd / 11 % 20 + 1, 1, 1, 1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 7, 7, 8, 9, 10), r.StaffId
FROM #Chg c
CROSS APPLY (SELECT CASE WHEN c.Rnd % 100 < 55 THEN 5 + c.Rnd / 100 % 40 WHEN c.Rnd % 100 < 90 THEN 45 + c.Rnd / 100 % 120 ELSE 165 + c.Rnd / 100 % 500 END AS Mins) m
CROSS APPLY (SELECT DATEADD(minute, -m.Mins, c.UseAt) AS At) t
CROSS APPLY (SELECT CASE WHEN DATEPART(hour, t.At) < 6 THEN DATEADD(day, -1, CAST(t.At AS date)) ELSE CAST(t.At AS date) END AS ShiftDate,
                    CASE WHEN DATEPART(hour, t.At) BETWEEN 6 AND 13 THEN 'M' WHEN DATEPART(hour, t.At) BETWEEN 14 AND 21 THEN 'E' ELSE 'N' END AS ShiftCode) sh
LEFT JOIN #RC rc ON rc.ShiftDate = sh.ShiftDate AND rc.ShiftCode = sh.ShiftCode AND rc.Grp = 'OCC'
LEFT JOIN #Roster r ON r.ShiftDate = sh.ShiftDate AND r.ShiftCode = sh.ShiftCode AND r.Grp = 'OCC' AND r.Idx = c.Rnd / 13 % rc.Cnt
ORDER BY t.At, c.FlightLegId;

-- -------------------------------------------------------------------------------------------------- resource closures
-- Short closures (maintenance, cleaning, faults); a closure that would overlap an allocation of the resource is not kept.
CREATE TABLE #Closure (N int PRIMARY KEY, ResourceType varchar(8), ResourceId int, ResourceCode varchar(5), ClosureStart datetime2(0), ClosureEnd datetime2(0), Rnd int);
INSERT INTO #Closure
SELECT n.value, k.ResourceType, k.ResourceId, k.ResourceCode, t.ClosureStart, DATEADD(minute, 45 + x.Rnd / 7 % 20 * 15, t.ClosureStart), x.Rnd
FROM GENERATE_SERIES(1, DATEDIFF(day, @Start, @Today) * 16) n
CROSS APPLY (SELECT dbo.R(n.value, 6) AS Rnd, dbo.R(n.value, 7) AS Rnd2) x
CROSS APPLY (SELECT DATEADD(minute, x.Rnd2 % 96 * 15, CAST(DATEADD(day, (n.value - 1) / 16, @Start) AS datetime2(0))) AS ClosureStart) t
JOIN (SELECT 'Stand' AS ResourceType, StandId AS ResourceId, StandCode AS ResourceCode, ROW_NUMBER() OVER (ORDER BY StandId) - 1 AS Idx FROM res.Stand
      UNION ALL SELECT 'Gate', GateId, GateCode, 66 + ROW_NUMBER() OVER (ORDER BY GateId) - 1 FROM res.Gate
      UNION ALL SELECT 'Belt', BaggageBeltId, BeltCode, 108 + ROW_NUMBER() OVER (ORDER BY BaggageBeltId) - 1 FROM res.BaggageBelt
      UNION ALL SELECT 'Desk', CheckInDeskId, DeskCode, 120 + ROW_NUMBER() OVER (ORDER BY CheckInDeskId) - 1 FROM res.CheckInDesk) k ON k.Idx = x.Rnd % 264;
DELETE c FROM #Closure c WHERE c.ResourceType = 'Stand' AND EXISTS (SELECT 1 FROM [plan].StandAllocation a WHERE a.ActualStandId = c.ResourceId
    AND ISNULL(a.ActualStart, a.PlannedStart) < DATEADD(minute, 20, c.ClosureEnd) AND ISNULL(a.ActualEnd, a.PlannedEnd) > DATEADD(minute, -20, c.ClosureStart)
    AND ISNULL(a.ActualStart, a.PlannedStart) > DATEADD(day, -2, c.ClosureStart));
DELETE c FROM #Closure c WHERE c.ResourceType = 'Gate' AND EXISTS (SELECT 1 FROM [plan].GateAllocation a WHERE a.ActualGateId = c.ResourceId
    AND a.PlannedOpen < DATEADD(minute, 20, c.ClosureEnd) AND ISNULL(a.ActualClose, a.PlannedClose) > DATEADD(minute, -20, c.ClosureStart) AND a.PlannedOpen > DATEADD(day, -2, c.ClosureStart));
DELETE c FROM #Closure c WHERE c.ResourceType = 'Belt' AND EXISTS (SELECT 1 FROM [plan].BeltAllocation a WHERE a.ActualBeltId = c.ResourceId
    AND a.PlannedStart < DATEADD(minute, 20, c.ClosureEnd) AND ISNULL(a.LastBagDateTime, a.PlannedEnd) > DATEADD(minute, -20, c.ClosureStart) AND a.PlannedStart > DATEADD(day, -2, c.ClosureStart));
DELETE c FROM #Closure c WHERE c.ResourceType = 'Desk' AND EXISTS (SELECT 1 FROM [plan].CheckInAllocation a WHERE a.CheckInDeskId = c.ResourceId
    AND a.OpenDateTime < DATEADD(minute, 20, c.ClosureEnd) AND a.CloseDateTime > DATEADD(minute, -20, c.ClosureStart) AND a.OpenDateTime > DATEADD(day, -2, c.ClosureStart));
INSERT INTO [plan].ResourceClosure (ResourceType, ResourceCode, ClosureStart, ClosureEnd, Reason, IsPlanned)
SELECT c.ResourceType, c.ResourceCode, c.ClosureStart, c.ClosureEnd,
       CHOOSE(c.Rnd / 3 % 10 + 1, N'Planned maintenance', N'Planned maintenance', N'Planned maintenance', N'Cleaning', N'Cleaning', N'Equipment fault', N'Equipment fault', N'Inspection', N'Construction works', N'Spillage clean-up'),
       CASE WHEN c.Rnd / 3 % 10 IN (5, 6, 9) THEN 0 ELSE 1 END
FROM #Closure c WHERE c.ClosureStart < @Now ORDER BY c.ClosureStart, c.N;

-- --------------------------------------------------------------------------------------------------------- tasks
-- Ground handling tasks of every flight that was not cancelled, each given to a member of the right group who was on that shift.
CREATE TABLE #T (FlightId int, TaskTypeId int, Grp varchar(8), EquipmentTypeId int NULL, Dur int, Rnd int, Late int, Operated bit, PlannedStart datetime2(0), ActualStart datetime2(0),
    ShiftDate date, ShiftCode varchar(5), PRIMARY KEY (FlightId, TaskTypeId));
INSERT INTO #T WITH (TABLOCK)
SELECT q.FlightId, q.TaskTypeId, q.StaffGroupCode, q.EquipmentTypeId, q.Dur, q.Rnd, q.Late, q.Operated, q.PlannedStart, q.ActualStart,
       CASE WHEN DATEPART(hour, t.At) < 6 THEN DATEADD(day, -1, CAST(t.At AS date)) ELSE CAST(t.At AS date) END,
       CASE WHEN DATEPART(hour, t.At) BETWEEN 6 AND 13 THEN 'M' WHEN DATEPART(hour, t.At) BETWEEN 14 AND 21 THEN 'E' ELSE 'N' END
FROM (
    SELECT f.FlightId, f.Operated, tt.TaskTypeId, tt.StaffGroupCode, tt.EquipmentTypeId, tt.StandardDurationMinutes AS Dur, x.Rnd, l.Late,
           DATEADD(minute, tt.StartOffsetMinutes, f.Sched) AS PlannedStart,
           DATEADD(minute, tt.StartOffsetMinutes + CASE WHEN tt.StartOffsetMinutes < -30 THEN DATEDIFF(minute, f.Sched, f.Best) / 3 ELSE DATEDIFF(minute, f.Sched, f.Best) END + l.Late, f.Sched) AS ActualStart
    FROM #F f JOIN ref.TaskType tt ON tt.Direction = f.Direction AND (tt.AppliesTo = 'All' OR (tt.AppliesTo = 'Passenger' AND f.IsPax = 1) OR (tt.AppliesTo = 'Cargo' AND f.IsPax = 0))
    CROSS APPLY (SELECT dbo.R(CAST(f.FlightId AS bigint) * 100 + tt.TaskTypeId, 8) AS Rnd) x
    CROSS APPLY (SELECT CASE WHEN x.Rnd % 100 < 84 THEN x.Rnd / 100 % 4 - 1 WHEN x.Rnd % 100 < 97 THEN 4 + x.Rnd / 100 % 12 ELSE 16 + x.Rnd / 100 % 30 END AS Late) l
    WHERE f.Status NOT IN ('Cancelled', 'Diverted') AND (tt.TaskCode NOT IN ('PRMA', 'PRMD') OR f.Prm > 0)
) q CROSS APPLY (SELECT CASE WHEN q.Operated = 1 THEN q.ActualStart ELSE q.PlannedStart END AS At) t;

INSERT INTO ops.Task WITH (TABLOCK) (FlightLegId, TaskTypeId, PlannedStart, PlannedEnd, ActualStart, ActualEnd, StartDelayMinutes, StaffId, EquipmentId, TaskStatus)
SELECT q.FlightId, q.TaskTypeId, q.PlannedStart, DATEADD(minute, q.Dur, q.PlannedStart), CASE WHEN q.Operated = 1 THEN q.ActualStart END,
       CASE WHEN q.Operated = 1 THEN DATEADD(minute, q.Dur + q.Rnd / 17 % 9 - 3, q.ActualStart) END, CASE WHEN q.Operated = 1 THEN q.Late END, r.StaffId, e.EquipmentId,
       CASE WHEN q.Operated = 1 THEN 'Completed' ELSE 'Planned' END
FROM #T q
LEFT JOIN #RC rc ON rc.ShiftDate = q.ShiftDate AND rc.ShiftCode = q.ShiftCode AND rc.Grp = q.Grp
LEFT JOIN #Roster r ON r.ShiftDate = q.ShiftDate AND r.ShiftCode = q.ShiftCode AND r.Grp = q.Grp AND r.Idx = q.Rnd / 19 % rc.Cnt
LEFT JOIN (SELECT EquipmentId, EquipmentTypeId, ROW_NUMBER() OVER (PARTITION BY EquipmentTypeId ORDER BY EquipmentId) - 1 AS Idx, COUNT(*) OVER (PARTITION BY EquipmentTypeId) AS Cnt FROM res.Equipment) e
  ON e.EquipmentTypeId = q.EquipmentTypeId AND e.Idx = q.Rnd / 23 % e.Cnt
ORDER BY q.PlannedStart, q.FlightId, q.TaskTypeId;
CREATE INDEX IX_Task_FlightLeg ON ops.Task (FlightLegId) INCLUDE (TaskTypeId, StaffId, StartDelayMinutes);
CREATE INDEX IX_Task_Staff ON ops.Task (StaffId, PlannedStart);
CREATE INDEX IX_ShiftAssignment_Date ON staff.ShiftAssignment (ShiftDate) INCLUDE (StaffId, ShiftCode, AttendanceStatus);

-- ------------------------------------------------------------------------------------------------------- summary
SELECT 'Flight legs' AS Item, COUNT(*) AS Rows_ FROM [plan].FlightLeg
UNION ALL SELECT 'Stand allocations', COUNT(*) FROM [plan].StandAllocation
UNION ALL SELECT 'Gate allocations', COUNT(*) FROM [plan].GateAllocation
UNION ALL SELECT 'Check-in desk allocations', COUNT(*) FROM [plan].CheckInAllocation
UNION ALL SELECT 'Belt allocations', COUNT(*) FROM [plan].BeltAllocation
UNION ALL SELECT 'Allocation changes', COUNT(*) FROM [plan].AllocationChange
UNION ALL SELECT 'Resource closures', COUNT(*) FROM [plan].ResourceClosure
UNION ALL SELECT 'Operational staff', COUNT(*) FROM staff.Staff
UNION ALL SELECT 'Shift assignments', COUNT(*) FROM staff.ShiftAssignment
UNION ALL SELECT 'Tasks', COUNT(*) FROM ops.Task
UNION ALL SELECT 'Completed tasks without staff', COUNT(*) FROM ops.Task WHERE TaskStatus = 'Completed' AND StaffId IS NULL;
GO
