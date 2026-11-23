-- ==============================================================================================================
-- DemoAirport_Assets — Enterprise Asset Management of Demo Airport (sample)
-- About 18,400 airport assets in 62 categories (airfield, baggage handling, boarding bridges, lifts, HVAC, power,
-- fire, security, IT, ground support equipment, buildings, water), their locations, manufacturers and vendors;
-- preventive-maintenance plans, about 240,000 work orders with labour and parts, spare-part stock, purchase orders,
-- statutory inspections, meter readings, service contracts and monthly depreciation, from 1 January 2023 until the
-- day the script runs.
--
-- Run it THIRD (as a sysadmin, in SSMS or sqlcmd), after 01: the technicians are the engineering and IT staff of
-- [DemoAirport_HCM], and a work order is only given to someone employed on that day.
-- Re-running resets the data (about 1 minute on SQL Server Express 2022).
-- The data is synthetic. Manufacturers are real companies named for illustration; vendors are invented.
-- ==============================================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
USE [master];
GO
IF DB_ID(N'DemoAirport_HCM') IS NULL THROW 50000, 'Run 01-create-demoairport-hcm.sql first: the technicians come from DemoAirport_HCM.', 1;
IF DB_ID(N'DemoAirport_Assets') IS NULL CREATE DATABASE [DemoAirport_Assets];
GO
ALTER DATABASE [DemoAirport_Assets] SET RECOVERY SIMPLE;
IF SUSER_ID(N'demoairport_reader') IS NULL
    CREATE LOGIN [demoairport_reader] WITH PASSWORD = N'DemoAirport#Reader#2026', CHECK_POLICY = ON;
GO
USE [DemoAirport_Assets];
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
IF SCHEMA_ID(N'eam') IS NULL EXEC (N'CREATE SCHEMA eam');
IF SCHEMA_ID(N'mnt') IS NULL EXEC (N'CREATE SCHEMA mnt');
IF SCHEMA_ID(N'inv') IS NULL EXEC (N'CREATE SCHEMA inv');
IF SCHEMA_ID(N'fin') IS NULL EXEC (N'CREATE SCHEMA fin');
GO
CREATE FUNCTION dbo.R (@k bigint, @s int) RETURNS int WITH SCHEMABINDING AS
BEGIN
    RETURN CAST(HASHBYTES('MD5', CONCAT(@k, N':', @s)) AS int) & 2147483647;
END
GO
-- ---------------------------------------------------------------------------------------------------- tables
CREATE TABLE dbo.SampleInfo (DatabaseName sysname NOT NULL, DataFrom date NOT NULL, DataTo date NOT NULL, GeneratedAt datetime2(0) NOT NULL);

CREATE TABLE ref.AssetCategory (CategoryId int IDENTITY(1,1) NOT NULL PRIMARY KEY, CategoryCode varchar(8) NOT NULL UNIQUE, CategoryName nvarchar(80) NOT NULL,
    ParentCategoryId int NULL REFERENCES ref.AssetCategory (CategoryId), UsefulLifeYears tinyint NULL, PMFrequencyDays smallint NULL, MeterType varchar(6) NULL,
    DefaultCriticality char(1) NULL, IsStatutoryInspection bit NOT NULL DEFAULT 0);
CREATE TABLE ref.Location (LocationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, LocationCode varchar(8) NOT NULL UNIQUE, LocationName nvarchar(80) NOT NULL,
    LocationType varchar(10) NOT NULL, ParentLocationId int NULL REFERENCES ref.Location (LocationId), Zone varchar(4) NULL);
CREATE TABLE ref.Manufacturer (ManufacturerId int IDENTITY(1,1) NOT NULL PRIMARY KEY, ManufacturerName nvarchar(60) NOT NULL UNIQUE, Country nvarchar(40) NOT NULL, Specialty varchar(4) NOT NULL);
CREATE TABLE ref.Vendor (VendorId int IDENTITY(1,1) NOT NULL PRIMARY KEY, VendorCode varchar(8) NOT NULL UNIQUE, VendorName nvarchar(80) NOT NULL, Specialty varchar(4) NOT NULL,
    City nvarchar(30) NOT NULL, PaymentTermsDays tinyint NOT NULL, Rating tinyint NOT NULL, IsActive bit NOT NULL);
CREATE TABLE ref.FailureCode (FailureCodeId int NOT NULL PRIMARY KEY, FailureCode varchar(6) NOT NULL UNIQUE, Description nvarchar(60) NOT NULL, FailureClass nvarchar(30) NOT NULL);
CREATE TABLE ref.MaintenanceTeam (TeamId int NOT NULL PRIMARY KEY, TeamCode varchar(4) NOT NULL UNIQUE, TeamName nvarchar(60) NOT NULL);

CREATE TABLE mnt.Technician (TechnicianId int NOT NULL PRIMARY KEY, EmployeeNumber varchar(10) NOT NULL UNIQUE, FullName nvarchar(90) NOT NULL, Trade nvarchar(100) NOT NULL,
    TeamId int NOT NULL REFERENCES ref.MaintenanceTeam (TeamId), HourlyRate decimal(8,2) NOT NULL, HireDate date NOT NULL, SeparationDate date NULL, IsActive bit NOT NULL);
CREATE TABLE eam.Asset (AssetId int IDENTITY(1,1) NOT NULL PRIMARY KEY, AssetTag varchar(16) NOT NULL UNIQUE, AssetName nvarchar(120) NOT NULL,
    CategoryId int NOT NULL REFERENCES ref.AssetCategory (CategoryId), LocationId int NOT NULL REFERENCES ref.Location (LocationId),
    ManufacturerId int NOT NULL REFERENCES ref.Manufacturer (ManufacturerId), Model varchar(20) NOT NULL, SerialNumber varchar(20) NOT NULL,
    InstallDate date NOT NULL, WarrantyEndDate date NOT NULL, PurchaseCost decimal(14,2) NOT NULL, SalvageValue decimal(14,2) NOT NULL, UsefulLifeYears tinyint NOT NULL,
    Criticality char(1) NOT NULL, ConditionScore tinyint NOT NULL, AssetStatus varchar(15) NOT NULL, DisposalDate date NULL,
    ResponsibleTeamId int NOT NULL REFERENCES ref.MaintenanceTeam (TeamId), OwnerDepartmentCode varchar(5) NOT NULL);
CREATE TABLE eam.MeterReading (MeterReadingId int IDENTITY(1,1) NOT NULL PRIMARY KEY, AssetId int NOT NULL REFERENCES eam.Asset (AssetId), ReadingDate date NOT NULL,
    MeterType varchar(6) NOT NULL, ReadingValue decimal(12,1) NOT NULL, UsageSinceLast decimal(10,1) NOT NULL);
CREATE TABLE eam.Inspection (InspectionId int IDENTITY(1,1) NOT NULL PRIMARY KEY, AssetId int NOT NULL REFERENCES eam.Asset (AssetId), InspectionDate date NOT NULL,
    InspectionType nvarchar(60) NOT NULL, VendorId int NOT NULL REFERENCES ref.Vendor (VendorId), Result varchar(12) NOT NULL, FindingsCount tinyint NOT NULL,
    CertificateNumber varchar(16) NULL, NextDueDate date NOT NULL);
CREATE TABLE mnt.ServiceContract (ContractId int IDENTITY(1,1) NOT NULL PRIMARY KEY, ContractNumber varchar(12) NOT NULL UNIQUE, VendorId int NOT NULL REFERENCES ref.Vendor (VendorId),
    CategoryId int NOT NULL REFERENCES ref.AssetCategory (CategoryId), StartDate date NOT NULL, EndDate date NOT NULL, AnnualValue decimal(12,2) NOT NULL,
    ResponseTimeHours tinyint NOT NULL, ContractStatus varchar(10) NOT NULL);
CREATE TABLE mnt.PMPlan (PMPlanId int IDENTITY(1,1) NOT NULL PRIMARY KEY, AssetId int NOT NULL REFERENCES eam.Asset (AssetId), PlanName nvarchar(120) NOT NULL,
    FrequencyDays smallint NOT NULL, EstimatedHours decimal(5,1) NOT NULL, TeamId int NOT NULL REFERENCES ref.MaintenanceTeam (TeamId), IsStatutory bit NOT NULL,
    IsActive bit NOT NULL, LastCompletedDate date NULL, NextDueDate date NULL);
CREATE TABLE mnt.WorkOrder (WorkOrderId int IDENTITY(1,1) NOT NULL PRIMARY KEY, WorkOrderNumber varchar(12) NOT NULL UNIQUE, AssetId int NOT NULL REFERENCES eam.Asset (AssetId),
    WorkType varchar(12) NOT NULL, Priority tinyint NOT NULL, WorkOrderStatus varchar(15) NOT NULL, PMPlanId int NULL REFERENCES mnt.PMPlan (PMPlanId),
    FailureCodeId int NULL REFERENCES ref.FailureCode (FailureCodeId), Description nvarchar(160) NOT NULL, ReportedDateTime datetime2(0) NOT NULL, ScheduledDate date NOT NULL,
    StartDateTime datetime2(0) NULL, CompletedDateTime datetime2(0) NULL, TargetResponseHours smallint NOT NULL, ResponseHours decimal(7,1) NULL, IsResponseOnTime bit NULL,
    DowntimeHours decimal(8,1) NOT NULL, LabourHours decimal(7,1) NOT NULL, LabourCost decimal(12,2) NOT NULL, MaterialCost decimal(12,2) NOT NULL,
    ContractorCost decimal(12,2) NOT NULL, TotalCost AS (LabourCost + MaterialCost + ContractorCost) PERSISTED,
    TeamId int NOT NULL REFERENCES ref.MaintenanceTeam (TeamId), AssignedTechnicianId int NULL REFERENCES mnt.Technician (TechnicianId),
    VendorId int NULL REFERENCES ref.Vendor (VendorId), RequestedByDepartmentCode varchar(5) NOT NULL);
CREATE TABLE mnt.WorkOrderLabour (WorkOrderLabourId int IDENTITY(1,1) NOT NULL PRIMARY KEY, WorkOrderId int NOT NULL REFERENCES mnt.WorkOrder (WorkOrderId),
    TechnicianId int NOT NULL REFERENCES mnt.Technician (TechnicianId), WorkDate date NOT NULL, RegularHours decimal(6,1) NOT NULL, OvertimeHours decimal(6,1) NOT NULL,
    LabourCost decimal(10,2) NOT NULL);
CREATE TABLE inv.Warehouse (WarehouseId int NOT NULL PRIMARY KEY, WarehouseCode varchar(8) NOT NULL UNIQUE, WarehouseName nvarchar(60) NOT NULL);
CREATE TABLE inv.SparePart (PartId int IDENTITY(1,1) NOT NULL PRIMARY KEY, PartNumber varchar(16) NOT NULL UNIQUE, PartName nvarchar(120) NOT NULL,
    CategoryId int NOT NULL REFERENCES ref.AssetCategory (CategoryId), ManufacturerId int NOT NULL REFERENCES ref.Manufacturer (ManufacturerId),
    WarehouseId int NOT NULL REFERENCES inv.Warehouse (WarehouseId), UnitOfMeasure varchar(6) NOT NULL, UnitCost decimal(10,2) NOT NULL, QuantityOnHand int NOT NULL,
    ReorderLevel int NOT NULL, LeadTimeDays smallint NOT NULL, IsCritical bit NOT NULL, StockValue AS (UnitCost * QuantityOnHand) PERSISTED);
CREATE TABLE mnt.WorkOrderPart (WorkOrderPartId int IDENTITY(1,1) NOT NULL PRIMARY KEY, WorkOrderId int NOT NULL REFERENCES mnt.WorkOrder (WorkOrderId),
    PartId int NOT NULL REFERENCES inv.SparePart (PartId), Quantity smallint NOT NULL, UnitCost decimal(10,2) NOT NULL, LineCost AS (Quantity * UnitCost) PERSISTED);
CREATE TABLE inv.PurchaseOrder (PurchaseOrderId int IDENTITY(1,1) NOT NULL PRIMARY KEY, PurchaseOrderNumber varchar(12) NOT NULL UNIQUE, VendorId int NOT NULL REFERENCES ref.Vendor (VendorId),
    OrderType varchar(12) NOT NULL, OrderDate date NOT NULL, ExpectedDate date NOT NULL, ReceivedDate date NULL, OrderStatus varchar(15) NOT NULL, TotalAmount decimal(14,2) NOT NULL,
    RequestedByDepartmentCode varchar(5) NOT NULL);
CREATE TABLE inv.PurchaseOrderLine (PurchaseOrderLineId int IDENTITY(1,1) NOT NULL PRIMARY KEY, PurchaseOrderId int NOT NULL REFERENCES inv.PurchaseOrder (PurchaseOrderId),
    PartId int NULL REFERENCES inv.SparePart (PartId), Description nvarchar(120) NOT NULL, OrderedQuantity int NOT NULL, ReceivedQuantity int NOT NULL, UnitPrice decimal(12,2) NOT NULL,
    LineAmount AS (OrderedQuantity * UnitPrice) PERSISTED);
CREATE TABLE fin.AssetDepreciation (AssetDepreciationId int IDENTITY(1,1) NOT NULL PRIMARY KEY, AssetId int NOT NULL REFERENCES eam.Asset (AssetId), PeriodStart date NOT NULL,
    DepreciationAmount decimal(12,2) NOT NULL, AccumulatedDepreciation decimal(14,2) NOT NULL, NetBookValue decimal(14,2) NOT NULL);
GO
-- ------------------------------------------------------------------------------------------------ reference data
DECLARE @Start date = '2023-01-01', @Today date = CAST(GETDATE() AS date), @Now datetime2(0) = SYSDATETIME();
DECLARE @Days int = DATEDIFF(day, @Start, @Today) + 1;
INSERT INTO dbo.SampleInfo VALUES (N'DemoAirport_Assets', @Start, @Today, @Now);

INSERT INTO ref.MaintenanceTeam VALUES (1,'ELE',N'Electrical and Airfield Lighting'),(2,'MEC',N'Mechanical and HVAC'),(3,'BHS',N'Baggage Handling Systems'),(4,'CIV',N'Civil and Facilities'),(5,'ICT',N'Airport Systems and IT');
INSERT INTO inv.Warehouse VALUES (1,'WH-MAIN',N'Main Engineering Store'),(2,'WH-AIR',N'Airside Store'),(3,'WH-GSE',N'GSE Workshop Store');

-- Categories: parent, code, name, number of assets, cost range, life, PM frequency, failures per year, team, meter, location zone, criticality, statutory inspection days.
CREATE TABLE #Cat (Ord int IDENTITY(1,1), Parent varchar(4), Code varchar(8), Name nvarchar(80), Cnt int, CostMin int, CostMax int, Life int, PMDays int, FailRate decimal(4,2), Team varchar(4),
    Meter varchar(6) NULL, Zone varchar(4), Crit char(1), InspDays int NULL, Dept varchar(5));
INSERT INTO #Cat (Parent, Code, Name, Cnt, CostMin, CostMax, Life, PMDays, FailRate, Team, Meter, Zone, Crit, InspDays, Dept) VALUES
('AFL','AFL-LGT',N'Airfield ground lighting fixture',2600,1200,4000,15,180,0.25,'ELE',NULL,'AIR','A',NULL,'AOP'),
('AFL','AFL-CCR',N'Constant current regulator',80,60000,120000,20,90,0.50,'ELE','Hours','UTL','A',NULL,'AOP'),
('AFL','AFL-SGN',N'Airfield guidance sign',420,6000,15000,15,365,0.15,'ELE',NULL,'AIR','B',NULL,'AOP'),
('AFL','AFL-NAV',N'Navigation aid',24,900000,2500000,15,30,0.60,'ELE','Hours','AIR','A',180,'AOP'),
('AFL','AFL-MET',N'Meteorological sensor',30,80000,250000,12,90,0.40,'ELE',NULL,'AIR','B',NULL,'AOP'),
('AFL','AFL-PAV',N'Pavement section',160,2000000,15000000,30,365,0.30,'CIV',NULL,'AIR','A',NULL,'AOP'),
('BHS','BHS-CNV',N'Baggage conveyor section',1100,25000,90000,15,30,1.60,'BHS','Hours','BAG','B',NULL,'TOP'),
('BHS','BHS-SRT',N'Baggage sorter',24,1500000,4000000,15,30,3.00,'BHS','Hours','BAG','A',NULL,'TOP'),
('BHS','BHS-CAR',N'Baggage reclaim carousel',12,600000,1200000,20,30,2.00,'BHS','Hours','BAG','A',NULL,'TOP'),
('BHS','BHS-EDS',N'Hold baggage screening machine',18,3000000,5000000,12,30,2.50,'BHS','Hours','BAG','A',NULL,'SEC'),
('BHS','BHS-CKC',N'Check-in conveyor and scale',144,18000,35000,12,90,1.00,'BHS',NULL,'TRM','B',NULL,'TOP'),
('PAX','PAX-PBB',N'Passenger boarding bridge',20,2200000,3500000,20,30,2.20,'MEC','Cycles','APR','A',180,'AOP'),
('PAX','PAX-VDG',N'Visual docking guidance system',30,250000,400000,15,90,0.80,'ELE',NULL,'APR','A',NULL,'AOP'),
('PAX','PAX-GPU',N'Fixed ground power unit',30,300000,500000,15,90,1.00,'ELE','Hours','APR','B',NULL,'AOP'),
('PAX','PAX-PCA',N'Pre-conditioned air unit',20,350000,600000,15,90,1.20,'MEC','Hours','APR','B',NULL,'AOP'),
('PAX','PAX-FID',N'Flight information display',900,3000,12000,7,365,0.30,'ICT',NULL,'TRM','C',NULL,'ITD'),
('PAX','PAX-KSK',N'Self-service kiosk and bag drop',160,40000,120000,8,90,1.40,'ICT',NULL,'TRM','B',NULL,'TOP'),
('PAX','PAX-EGT',N'Electronic gate',120,60000,150000,8,90,1.20,'ICT',NULL,'TRM','B',NULL,'TOP'),
('VTS','VTS-ELV',N'Elevator',64,350000,700000,25,30,1.80,'MEC','Cycles','TRM','A',180,'TOP'),
('VTS','VTS-ESC',N'Escalator',48,500000,900000,25,30,2.40,'MEC','Hours','TRM','A',180,'TOP'),
('VTS','VTS-TRV',N'Moving walkway',16,700000,1200000,25,30,2.00,'MEC','Hours','TRM','B',180,'TOP'),
('HVC','HVC-CHL',N'Chiller',28,1200000,3000000,20,30,1.50,'MEC','Hours','UTL','A',365,'ENG'),
('HVC','HVC-AHU',N'Air handling unit',420,60000,200000,18,90,0.80,'MEC','Hours','TRM','B',NULL,'ENG'),
('HVC','HVC-FCU',N'Fan coil unit',1800,4000,9000,15,180,0.35,'MEC',NULL,'TRM','C',NULL,'ENG'),
('HVC','HVC-CTW',N'Cooling tower',16,400000,800000,20,30,1.00,'MEC','Hours','UTL','A',NULL,'ENG'),
('HVC','HVC-PMP',N'Chilled water pump',120,30000,90000,15,90,0.70,'MEC','Hours','UTL','B',NULL,'ENG'),
('ELC','ELC-TRF',N'Transformer',60,300000,900000,30,180,0.20,'ELE',NULL,'UTL','A',NULL,'ENG'),
('ELC','ELC-SWG',N'Switchgear panel',240,80000,300000,25,180,0.25,'ELE',NULL,'UTL','A',NULL,'ENG'),
('ELC','ELC-GEN',N'Standby generator',26,700000,2000000,20,30,1.00,'ELE','Hours','UTL','A',365,'ENG'),
('ELC','ELC-UPS',N'Uninterruptible power supply',90,60000,400000,10,90,0.60,'ELE',NULL,'UTL','A',NULL,'ENG'),
('ELC','ELC-DBD',N'Distribution board',900,6000,25000,25,365,0.10,'ELE',NULL,'TRM','C',NULL,'ENG'),
('ELC','ELC-LGT',N'Terminal lighting zone',700,5000,30000,12,365,0.50,'ELE',NULL,'TRM','C',NULL,'ENG'),
('FLS','FLS-FAP',N'Fire alarm panel',70,40000,150000,15,90,0.50,'ELE',NULL,'TRM','A',180,'SHE'),
('FLS','FLS-FPM',N'Fire pump',24,120000,300000,20,30,0.60,'MEC','Hours','UTL','A',180,'SHE'),
('FLS','FLS-SPK',N'Sprinkler zone valve set',380,8000,20000,25,180,0.15,'MEC',NULL,'TRM','A',365,'SHE'),
('FLS','FLS-EXT',N'Fire extinguisher and hose reel point',1500,300,1500,10,180,0.05,'CIV',NULL,'TRM','B',365,'SHE'),
('SEC','SEC-XRY',N'Cabin baggage X-ray machine',46,250000,600000,10,30,2.00,'ELE','Hours','TRM','A',NULL,'SEC'),
('SEC','SEC-WTM',N'Walk-through metal detector',60,25000,50000,10,90,0.60,'ELE',NULL,'TRM','B',NULL,'SEC'),
('SEC','SEC-BSC',N'Security body scanner',14,600000,900000,10,30,1.50,'ELE',NULL,'TRM','A',NULL,'SEC'),
('SEC','SEC-CAM',N'CCTV camera',2400,2500,12000,8,365,0.30,'ICT',NULL,'ALL','C',NULL,'SEC'),
('SEC','SEC-ACS',N'Access control door',650,8000,20000,10,180,0.60,'ICT',NULL,'ALL','B',NULL,'SEC'),
('ICT','ICT-NET',N'Network switch',520,8000,80000,7,365,0.25,'ICT',NULL,'ALL','B',NULL,'ITD'),
('ICT','ICT-SRV',N'Server and storage unit',180,30000,250000,6,180,0.40,'ICT',NULL,'DC','A',NULL,'ITD'),
('ICT','ICT-RAD',N'Radio communication unit',300,3000,90000,8,365,0.30,'ICT',NULL,'ALL','B',NULL,'ITD'),
('GSE','GSE-TUG',N'Pushback tractor',22,600000,1600000,15,30,2.50,'MEC','Hours','GSE','A',NULL,'GHS'),
('GSE','GSE-BTR',N'Baggage tractor',90,90000,140000,10,30,2.00,'MEC','Hours','GSE','B',NULL,'GHS'),
('GSE','GSE-BLD',N'Belt loader',44,150000,220000,12,30,2.00,'MEC','Hours','GSE','B',NULL,'GHS'),
('GSE','GSE-HLD',N'Container high loader',26,700000,1100000,15,30,2.40,'MEC','Hours','GSE','A',NULL,'GHS'),
('GSE','GSE-STR',N'Passenger stairs',30,180000,300000,15,90,1.20,'MEC',NULL,'GSE','B',NULL,'GHS'),
('GSE','GSE-BUS',N'Apron passenger bus',28,900000,1600000,12,30,2.60,'MEC','Km','GSE','A',365,'GHS'),
('GSE','GSE-GPU',N'Mobile ground power unit',34,250000,400000,12,30,1.80,'MEC','Hours','GSE','B',NULL,'GHS'),
('GSE','GSE-ACU',N'Air start and air-conditioning unit',18,400000,700000,12,90,1.50,'MEC','Hours','GSE','B',NULL,'GHS'),
('GSE','GSE-DLY',N'Baggage dolly and cart',600,8000,20000,12,365,0.30,'MEC',NULL,'GSE','C',NULL,'GHS'),
('GSE','GSE-RFF',N'Fire fighting vehicle',14,3000000,5500000,15,30,1.50,'MEC','Km','FIRE','A',180,'RFF'),
('GSE','GSE-VEH',N'Service vehicle',210,70000,450000,8,90,1.30,'MEC','Km','GSE','C',365,'AOP'),
('CIV','CIV-DOR',N'Automatic door',340,15000,45000,12,180,0.90,'CIV',NULL,'TRM','B',NULL,'ENG'),
('CIV','CIV-ROF',N'Roof and façade section',90,500000,4000000,30,365,0.30,'CIV',NULL,'TRM','B',NULL,'ENG'),
('CIV','CIV-WSH',N'Washroom block',120,150000,400000,15,90,2.00,'CIV',NULL,'TRM','C',NULL,'TOP'),
('WTR','WTR-PMP',N'Water and sewage pump',110,20000,80000,15,90,0.80,'MEC','Hours','UTL','B',NULL,'ENG'),
('WTR','WTR-TNK',N'Water tank and treatment unit',40,100000,600000,25,180,0.30,'MEC',NULL,'UTL','B',NULL,'ENG');

INSERT INTO ref.AssetCategory (CategoryCode, CategoryName) VALUES ('AFL',N'Airfield'),('BHS',N'Baggage handling'),('PAX',N'Passenger and aircraft stand equipment'),('VTS',N'Vertical transport'),
('HVC',N'Heating, ventilation and air conditioning'),('ELC',N'Electrical and power'),('FLS',N'Fire and life safety'),('SEC',N'Security systems'),('ICT',N'IT and communications'),
('GSE',N'Ground support equipment and vehicles'),('CIV',N'Buildings and civil'),('WTR',N'Water and utilities');
INSERT INTO ref.AssetCategory (CategoryCode, CategoryName, ParentCategoryId, UsefulLifeYears, PMFrequencyDays, MeterType, DefaultCriticality, IsStatutoryInspection)
SELECT c.Code, c.Name, p.CategoryId, c.Life, c.PMDays, c.Meter, c.Crit, CASE WHEN c.InspDays IS NOT NULL THEN 1 ELSE 0 END
FROM #Cat c JOIN ref.AssetCategory p ON p.CategoryCode = c.Parent ORDER BY c.Ord;

INSERT INTO ref.Location (LocationCode, LocationName, LocationType, Zone) VALUES ('AFLD',N'Airfield','Site',NULL),('T1',N'Terminal 1','Building',NULL),('T2',N'Terminal 2','Building',NULL),
('CT',N'Cargo Terminal','Building',NULL),('UTIL',N'Utilities Complex','Site',NULL),('SUPP',N'Support Facilities','Site',NULL);
INSERT INTO ref.Location (LocationCode, LocationName, LocationType, ParentLocationId, Zone)
SELECT v.Code, v.Name, 'Area', p.LocationId, v.Zone FROM (VALUES
('AFLD','RWY1',N'Runway 12L/30R','AIR'),('AFLD','RWY2',N'Runway 12R/30L','AIR'),('AFLD','TWYA',N'Taxiway Alpha','AIR'),('AFLD','TWYB',N'Taxiway Bravo','AIR'),('AFLD','TWYC',N'Taxiway Charlie','AIR'),
('AFLD','APR1',N'Terminal 1 Apron','APR'),('AFLD','APR2',N'Terminal 2 Apron','APR'),('AFLD','APRR',N'Remote Apron','APR'),('AFLD','APRC',N'Cargo Apron','APR'),
('T1','T1-CKI',N'Terminal 1 Check-in Hall','TRM'),('T1','T1-DEP',N'Terminal 1 Departures Concourse','TRM'),('T1','T1-ARR',N'Terminal 1 Arrivals Hall','TRM'),('T1','T1-SEC',N'Terminal 1 Security Screening','TRM'),
('T1','T1-BAG',N'Terminal 1 Baggage Hall','BAG'),('T1','T1-PLT',N'Terminal 1 Plant Rooms','TRM'),
('T2','T2-CKI',N'Terminal 2 Check-in Hall','TRM'),('T2','T2-DEP',N'Terminal 2 Departures Concourse','TRM'),('T2','T2-ARR',N'Terminal 2 Arrivals Hall','TRM'),('T2','T2-BAG',N'Terminal 2 Baggage Hall','BAG'),
('CT','CT-WHS',N'Cargo Warehouse','TRM'),('CT','CT-OFF',N'Cargo Offices','TRM'),
('UTIL','SUB1',N'Main Substation','UTL'),('UTIL','SUB2',N'Airfield Substation','UTL'),('UTIL','CHLP',N'Central Chiller Plant','UTL'),('UTIL','WTRP',N'Water and Sewage Plant','UTL'),
('SUPP','FSTA',N'Fire Station A','FIRE'),('SUPP','FSTB',N'Fire Station B','FIRE'),('SUPP','GSEW',N'GSE Workshop and Staging','GSE'),('SUPP','DATA',N'Data Centre','DC'),
('SUPP','ADMN',N'Administration Building','TRM'),('SUPP','CARP',N'Car Parks','TRM')) v (Parent, Code, Name, Zone)
JOIN ref.Location p ON p.LocationCode = v.Parent;

INSERT INTO ref.Manufacturer (ManufacturerName, Country, Specialty) VALUES
(N'ADB SAFEGATE',N'Belgium','AFL'),(N'Thales',N'France','AFL'),(N'Indra',N'Spain','AFL'),(N'Vaisala',N'Finland','AFL'),
(N'Vanderlande',N'Netherlands','BHS'),(N'BEUMER Group',N'Germany','BHS'),(N'Siemens Logistics',N'Germany','BHS'),(N'Leidos',N'United States','BHS'),
(N'TK Airport Solutions',N'Spain','PAX'),(N'JBT AeroTech',N'United States','PAX'),(N'ADELTE',N'Spain','PAX'),(N'Cavotec',N'Switzerland','PAX'),(N'SITA',N'Switzerland','PAX'),
(N'Otis',N'United States','VTS'),(N'Schindler',N'Switzerland','VTS'),(N'KONE',N'Finland','VTS'),(N'TK Elevator',N'Germany','VTS'),
(N'Carrier',N'United States','HVC'),(N'Trane',N'Ireland','HVC'),(N'Daikin',N'Japan','HVC'),(N'Johnson Controls',N'Ireland','HVC'),
(N'Schneider Electric',N'France','ELC'),(N'ABB',N'Switzerland','ELC'),(N'Siemens',N'Germany','ELC'),(N'Caterpillar',N'United States','ELC'),(N'Cummins',N'United States','ELC'),(N'Eaton',N'Ireland','ELC'),
(N'Kidde',N'United States','FLS'),(N'Notifier',N'United States','FLS'),(N'NAFFCO',N'United Arab Emirates','FLS'),
(N'Smiths Detection',N'United Kingdom','SEC'),(N'Rapiscan Systems',N'United States','SEC'),(N'CEIA',N'Italy','SEC'),(N'Bosch Security',N'Germany','SEC'),(N'Axis Communications',N'Sweden','SEC'),(N'HID Global',N'United States','SEC'),
(N'Cisco',N'United States','ICT'),(N'Dell Technologies',N'United States','ICT'),(N'Hewlett Packard Enterprise',N'United States','ICT'),(N'Motorola Solutions',N'United States','ICT'),
(N'TLD',N'France','GSE'),(N'Goldhofer',N'Germany','GSE'),(N'Mallaghan',N'United Kingdom','GSE'),(N'Cobus Industries',N'Germany','GSE'),(N'Rosenbauer',N'Austria','GSE'),(N'Toyota',N'Japan','GSE'),(N'Mulag',N'Germany','GSE'),
(N'ASSA ABLOY',N'Sweden','CIV'),(N'dormakaba',N'Switzerland','CIV'),(N'Geberit',N'Switzerland','CIV'),
(N'Grundfos',N'Denmark','WTR'),(N'Xylem',N'United States','WTR'),(N'KSB',N'Germany','WTR');

INSERT INTO ref.Vendor (VendorCode, VendorName, Specialty, City, PaymentTermsDays, Rating, IsActive)
SELECT CONCAT('V', RIGHT(CONCAT('000', ROW_NUMBER() OVER (ORDER BY s.Ord, n.Ord)), 4)), CONCAT(n.Name, N' ', s.Name, N' LLC'), s.Spec,
       CHOOSE((s.Ord + n.Ord) % 4 + 1, N'Demo City', N'Dubai', N'Abu Dhabi', N'Sharjah'), CHOOSE((s.Ord * 3 + n.Ord) % 3 + 1, 30, 45, 60), 2 + (s.Ord * 7 + n.Ord * 3) % 4,
       CASE WHEN (s.Ord * 5 + n.Ord) % 17 = 0 THEN 0 ELSE 1 END
FROM (VALUES (1,'AFL',N'Airfield Solutions'),(2,'BHS',N'Baggage Systems Services'),(3,'PAX',N'Airport Equipment Services'),(4,'VTS',N'Lifts and Escalators'),(5,'HVC',N'Air Conditioning'),
             (6,'ELC',N'Electromechanical'),(7,'FLS',N'Fire Protection'),(8,'SEC',N'Security Systems'),(9,'ICT',N'IT Solutions'),(10,'GSE',N'Vehicle and Equipment Maintenance'),
             (11,'CIV',N'Facilities Management'),(12,'WTR',N'Water Technology')) s (Ord, Spec, Name)
CROSS JOIN (VALUES (1,N'Al Noor'),(2,N'Falcon'),(3,N'Oasis'),(4,N'Horizon')) n (Ord, Name);

INSERT INTO ref.FailureCode VALUES
(1,'ELE-01',N'Power supply failure',N'Electrical'),(2,'ELE-02',N'Short circuit or earth fault',N'Electrical'),(3,'ELE-03',N'Lamp or LED failure',N'Electrical'),(4,'ELE-04',N'Control board fault',N'Electrical'),
(5,'MEC-01',N'Bearing failure',N'Mechanical'),(6,'MEC-02',N'Belt worn or broken',N'Mechanical'),(7,'MEC-03',N'Motor or gearbox failure',N'Mechanical'),(8,'MEC-04',N'Leak',N'Mechanical'),
(9,'MEC-05',N'Jam or blockage',N'Mechanical'),(10,'MEC-06',N'Excessive vibration or noise',N'Mechanical'),(11,'CTL-01',N'Sensor fault',N'Control and software'),
(12,'CTL-02',N'PLC or software error',N'Control and software'),(13,'CTL-03',N'Communication loss',N'Control and software'),(14,'CTL-04',N'Calibration out of tolerance',N'Control and software'),
(15,'EXT-01',N'Damage by vehicle or equipment',N'External'),(16,'EXT-02',N'Misuse or operator error',N'External'),(17,'EXT-03',N'Weather or dust ingress',N'External'),
(18,'EXT-04',N'Vandalism',N'External'),(19,'WAR-01',N'Normal wear and tear',N'Wear'),(20,'WAR-02',N'Corrosion',N'Wear'),(21,'UNK-01',N'No fault found',N'Other');

-- Technicians: the engineering sections and the IT department of the HCM database.
INSERT INTO mnt.Technician (TechnicianId, EmployeeNumber, FullName, Trade, TeamId, HourlyRate, HireDate, SeparationDate, IsActive)
SELECT e.EmployeeId, e.EmployeeNumber, e.FullName, p.PositionTitle, t.TeamId, ROUND(e.BasicSalary / 173 * 1.4, 0), e.HireDate, e.SeparationDate, CASE WHEN e.EmploymentStatus = 'Active' THEN 1 ELSE 0 END
FROM DemoAirport_HCM.hr.Employee e JOIN DemoAirport_HCM.org.Section s ON s.SectionId = e.SectionId JOIN DemoAirport_HCM.org.Position p ON p.PositionId = e.PositionId
JOIN ref.MaintenanceTeam t ON t.TeamCode = CASE WHEN s.SectionCode IN ('ELE', 'MEC', 'BHS', 'CIV') THEN s.SectionCode WHEN s.SectionCode IN ('INF', 'APS', 'SDK') THEN 'ICT' END
WHERE p.JobLevel IN (N'Staff', N'Supervisor');
-- The technicians of each team who were employed for the whole of each month.
CREATE TABLE #TM (TeamId int, MonthStart date, Idx int, TechnicianId int, Cnt int, PRIMARY KEY (TeamId, MonthStart, Idx));
INSERT INTO #TM
SELECT t.TeamId, m.MonthStart, ROW_NUMBER() OVER (PARTITION BY t.TeamId, m.MonthStart ORDER BY t.TechnicianId) - 1, t.TechnicianId, COUNT(*) OVER (PARTITION BY t.TeamId, m.MonthStart)
FROM mnt.Technician t
JOIN (SELECT DATEADD(month, g.value, @Start) AS MonthStart FROM GENERATE_SERIES(0, DATEDIFF(month, @Start, @Today) + 2) g) m
  ON t.HireDate <= m.MonthStart AND (t.SeparationDate IS NULL OR t.SeparationDate >= EOMONTH(m.MonthStart));

-- ------------------------------------------------------------------------------------------------------- assets
-- Ages run from new to a little beyond the useful life, so some assets are due for replacement; about 3 % were disposed of.
INSERT INTO eam.Asset (AssetTag, AssetName, CategoryId, LocationId, ManufacturerId, Model, SerialNumber, InstallDate, WarrantyEndDate, PurchaseCost, SalvageValue, UsefulLifeYears,
    Criticality, ConditionScore, AssetStatus, DisposalDate, ResponsibleTeamId, OwnerDepartmentCode)
SELECT CONCAT('DA-', c.Code, '-', RIGHT(CONCAT('0000', n.value), 5)), CONCAT(c.Name, N' ', l.LocationCode, N'-', n.value), cat.CategoryId, l.LocationId, m.ManufacturerId,
       CONCAT(LEFT(c.Code, 3), '-', 100 + x.Rnd / 3 % 9 * 100, CHAR(65 + x.Rnd / 5 % 6)), CONCAT('SN', RIGHT(CONCAT('00000000', x.Rnd / 7 % 100000000), 8)),
       a.InstallDate, DATEADD(year, 2, a.InstallDate), cost.Cost, ROUND(cost.Cost * 0.05, -1), c.Life,
       CASE WHEN x.Rnd / 11 % 10 = 0 AND c.Crit <> 'A' THEN CHAR(ASCII(c.Crit) - 1) ELSE c.Crit END,
       CASE WHEN a.AgeRatio < 0.25 THEN 5 WHEN a.AgeRatio < 0.55 THEN 4 WHEN a.AgeRatio < 0.85 THEN 3 WHEN a.AgeRatio < 1.0 THEN 2 ELSE 1 END
           - CASE WHEN x.Rnd / 13 % 6 = 0 AND a.AgeRatio >= 0.25 AND a.AgeRatio < 1.0 THEN 1 ELSE 0 END,
       CASE WHEN d.DisposalDate IS NOT NULL THEN 'Disposed' WHEN x.Rnd / 17 % 100 < 2 THEN 'Under repair' WHEN x.Rnd / 17 % 100 < 5 THEN 'Standby' ELSE 'In service' END,
       d.DisposalDate, t.TeamId, c.Dept
FROM #Cat c JOIN ref.AssetCategory cat ON cat.CategoryCode = c.Code JOIN ref.MaintenanceTeam t ON t.TeamCode = c.Team
CROSS APPLY GENERATE_SERIES(1, c.Cnt) n
CROSS APPLY (SELECT dbo.R(c.Ord * 100000 + n.value, 1) AS Rnd, dbo.R(c.Ord * 100000 + n.value, 2) AS Rnd2) x
CROSS APPLY (SELECT CASE WHEN ins.d < '2009-01-01' THEN DATEADD(day, x.Rnd2 / 9 % 1500, '2009-01-01') ELSE ins.d END AS InstallDate, 1.0 * (x.Rnd2 % (c.Life * 420)) / (c.Life * 365) AS AgeRatio
             FROM (SELECT DATEADD(day, -(x.Rnd2 % (c.Life * 420)), @Today) AS d) ins) a
CROSS APPLY (SELECT CASE WHEN a.AgeRatio > 0.9 AND x.Rnd / 19 % 100 < 22 AND DATEADD(day, 400, a.InstallDate) < @Today
                         THEN DATEADD(day, -(x.Rnd / 23 % (CASE WHEN DATEDIFF(day, a.InstallDate, @Today) - 400 < @Days THEN DATEDIFF(day, a.InstallDate, @Today) - 400 ELSE @Days END + 1)), @Today) END AS DisposalDate) d
CROSS APPLY (SELECT CAST(ROUND(c.CostMin + (x.Rnd / 29 % 1000) / 1000.0 * (c.CostMax - c.CostMin), -1) AS decimal(14,2)) AS Cost) cost
CROSS APPLY (SELECT COUNT(*) AS Cnt FROM ref.Location l WHERE l.Zone = c.Zone OR (c.Zone = 'ALL' AND l.Zone IS NOT NULL)) lc
JOIN (SELECT LocationId, LocationCode, Zone, ROW_NUMBER() OVER (PARTITION BY Zone ORDER BY LocationId) - 1 AS Idx, ROW_NUMBER() OVER (ORDER BY LocationId) - 1 AS AllIdx FROM ref.Location WHERE Zone IS NOT NULL) l
  ON (c.Zone <> 'ALL' AND l.Zone = c.Zone AND l.Idx = x.Rnd / 31 % lc.Cnt) OR (c.Zone = 'ALL' AND l.AllIdx = x.Rnd / 31 % lc.Cnt)
CROSS APPLY (SELECT COUNT(*) AS Cnt FROM ref.Manufacturer m WHERE m.Specialty = c.Parent) mc
JOIN (SELECT ManufacturerId, Specialty, ROW_NUMBER() OVER (PARTITION BY Specialty ORDER BY ManufacturerId) - 1 AS Idx FROM ref.Manufacturer) m ON m.Specialty = c.Parent AND m.Idx = x.Rnd / 37 % mc.Cnt
ORDER BY c.Ord, n.value;
CREATE INDEX IX_Asset_Category ON eam.Asset (CategoryId) INCLUDE (LocationId, AssetStatus, InstallDate);

-- Working copy of the assets with the category figures used below.
CREATE TABLE #A (AssetId int PRIMARY KEY, Ord int, Code varchar(8), Name nvarchar(80), Parent varchar(4), PMDays int, FailRate decimal(4,2), TeamId int, Crit char(1), Dept varchar(5),
    Cost decimal(14,2), Life int, InstallDate date, EndDate date, CostClass int, Meter varchar(6) NULL, InspDays int NULL);
INSERT INTO #A
SELECT a.AssetId, c.Ord, c.Code, c.Name, c.Parent, c.PMDays, c.FailRate, a.ResponsibleTeamId, a.Criticality, c.Dept, a.PurchaseCost, c.Life, a.InstallDate, ISNULL(a.DisposalDate, DATEADD(day, 45, @Today)),
       CASE WHEN a.PurchaseCost < 20000 THEN 1 WHEN a.PurchaseCost < 300000 THEN 2 ELSE 3 END, c.Meter, c.InspDays
FROM eam.Asset a JOIN ref.AssetCategory cat ON cat.CategoryId = a.CategoryId JOIN #Cat c ON c.Code = cat.CategoryCode;

INSERT INTO mnt.PMPlan (AssetId, PlanName, FrequencyDays, EstimatedHours, TeamId, IsStatutory, IsActive)
SELECT a.AssetId, CONCAT(CASE a.PMDays WHEN 30 THEN N'Monthly' WHEN 90 THEN N'Quarterly' WHEN 180 THEN N'Half-yearly' ELSE N'Annual' END, N' preventive maintenance: ', a.Name),
       a.PMDays, CHOOSE(a.CostClass, 0.5, 2, 4) * CASE WHEN a.PMDays >= 180 THEN 1.5 ELSE 1 END, a.TeamId, CASE WHEN a.InspDays IS NOT NULL THEN 1 ELSE 0 END,
       CASE WHEN a.EndDate <= @Today THEN 0 ELSE 1 END
FROM #A a ORDER BY a.AssetId;

-- --------------------------------------------------------------------------------------------------- work orders
CREATE TABLE #W (K bigint PRIMARY KEY, AssetId int, WorkType varchar(12), PMPlanId int NULL, Sched date, Reported datetime2(0), Rnd int);
-- Preventive: every plan on its frequency, from the first due date in the period until 45 days ahead.
INSERT INTO #W
SELECT CAST(a.AssetId AS bigint) * 1000 + n.value, a.AssetId, 'Preventive', p.PMPlanId, s.Sched, DATEADD(hour, 7, CAST(DATEADD(day, -14, s.Sched) AS datetime2(0))), dbo.R(CAST(a.AssetId AS bigint) * 1000 + n.value, 10)
FROM #A a JOIN mnt.PMPlan p ON p.AssetId = a.AssetId
CROSS APPLY GENERATE_SERIES(0, (@Days + 45) / a.PMDays) n
CROSS APPLY (SELECT DATEADD(day, dbo.R(a.AssetId, 11) % a.PMDays + n.value * a.PMDays, @Start) AS Sched) s
WHERE s.Sched >= DATEADD(day, 16, @Start) AND s.Sched > DATEADD(day, 16, a.InstallDate) AND s.Sched < a.EndDate;
-- Corrective: failures per year from the category, rising with age; one in twelve is an emergency.
INSERT INTO #W
SELECT CAST(a.AssetId AS bigint) * 1000 + 500 + n.value, a.AssetId, CASE WHEN x.Rnd / 3 % 12 = 0 THEN 'Emergency' ELSE 'Corrective' END, NULL, CAST(f.At AS date), f.At, x.Rnd
FROM #A a
CROSS APPLY (SELECT CASE WHEN a.InstallDate > @Start THEN a.InstallDate ELSE @Start END AS Lo, CASE WHEN a.EndDate > @Today THEN @Today ELSE a.EndDate END AS Hi) w
CROSS APPLY (SELECT a.FailRate * DATEDIFF(day, w.Lo, w.Hi) / 365.0 * (0.6 + 1.0 * DATEDIFF(day, a.InstallDate, w.Hi) / (a.Life * 365.0)) AS Lambda) l
CROSS APPLY GENERATE_SERIES(1, CAST(CEILING(l.Lambda * 2) AS int) + 1) n
CROSS APPLY (SELECT dbo.R(CAST(a.AssetId AS bigint) * 1000 + 500 + n.value, 12) AS Rnd, dbo.R(CAST(a.AssetId AS bigint) * 1000 + 500 + n.value, 13) AS Rnd2) x
CROSS APPLY (SELECT DATEADD(minute, x.Rnd2 % 1440, CAST(DATEADD(day, x.Rnd2 / 1440 % (DATEDIFF(day, w.Lo, w.Hi) + 1), w.Lo) AS datetime2(0))) AS At) f
WHERE w.Hi > w.Lo AND x.Rnd % 10000 < 10000 * l.Lambda / (CEILING(l.Lambda * 2) + 1) AND f.At < @Now;

INSERT INTO mnt.WorkOrder WITH (TABLOCK) (WorkOrderNumber, AssetId, WorkType, Priority, WorkOrderStatus, PMPlanId, FailureCodeId, Description, ReportedDateTime, ScheduledDate, StartDateTime,
    CompletedDateTime, TargetResponseHours, ResponseHours, IsResponseOnTime, DowntimeHours, LabourHours, LabourCost, MaterialCost, ContractorCost, TeamId, AssignedTechnicianId, VendorId,
    RequestedByDepartmentCode)
SELECT CONCAT('WO-', RIGHT(CONCAT('000000', ROW_NUMBER() OVER (ORDER BY q.Reported, q.K)), 7)), q.AssetId, q.WorkType, q.Priority, q.St, q.PMPlanId, q.FailureCodeId, q.Descr, q.Reported, q.Sched,
       CASE WHEN q.St IN ('Completed', 'In progress') THEN q.StartAt END, CASE WHEN q.St = 'Completed' THEN q.DoneAt END, q.Target,
       CASE WHEN q.St IN ('Completed', 'In progress') AND q.WorkType <> 'Preventive' THEN CAST(DATEDIFF(minute, q.Reported, q.StartAt) / 60.0 AS decimal(7,1)) END,
       CASE WHEN q.St IN ('Completed', 'In progress') AND q.WorkType <> 'Preventive' THEN CASE WHEN DATEDIFF(minute, q.Reported, q.StartAt) <= q.Target * 60 THEN 1 ELSE 0 END END,
       CASE WHEN q.St = 'Completed' AND q.WorkType <> 'Preventive' THEN CAST(DATEDIFF(minute, q.Reported, q.DoneAt) / 60.0 * CASE q.Crit WHEN 'C' THEN 0.4 ELSE 1 END AS decimal(8,1))
            WHEN q.St = 'Completed' AND q.PMDays = 30 THEN q.Dur ELSE 0 END,
       CASE WHEN q.St = 'Completed' THEN q.Dur * q.Crew ELSE 0 END, 0, 0,
       CASE WHEN q.St = 'Completed' AND q.WorkType <> 'Preventive' AND q.CostClass = 3 AND q.Rnd / 41 % 100 < 35 THEN 800 + q.Rnd / 43 % 53 * 100 ELSE 0 END,
       q.TeamId, CASE WHEN q.St <> 'Open' THEN tm.TechnicianId END, NULL, q.Dept
FROM (
    SELECT w.K, w.AssetId, w.WorkType, w.PMPlanId, w.Sched, w.Reported, w.Rnd, a.TeamId, a.Crit, a.Dept, a.CostClass, a.PMDays, pr.Priority, pr.Target, st.St, t.StartAt,
           d.Dur, DATEADD(minute, CAST(d.Dur * 60 AS int), t.StartAt) AS DoneAt, CASE WHEN w.Rnd / 47 % 100 < CASE WHEN w.WorkType = 'Preventive' THEN 20 ELSE 45 END THEN 2 ELSE 1 END AS Crew,
           CASE WHEN w.WorkType <> 'Preventive' THEN fc.FailureCodeId END AS FailureCodeId,
           CASE WHEN w.WorkType = 'Preventive' THEN CONCAT(N'Scheduled maintenance: ', a.Name) ELSE CONCAT(fc.Description, N': ', a.Name) END AS Descr
    FROM #W w JOIN #A a ON a.AssetId = w.AssetId
    CROSS APPLY (SELECT CASE WHEN w.WorkType = 'Emergency' THEN 1 WHEN w.WorkType = 'Preventive' THEN CASE WHEN a.Crit = 'A' THEN 3 ELSE 4 END WHEN a.Crit = 'A' THEN 2 WHEN a.Crit = 'B' THEN 3 ELSE 4 END AS Priority) p0
    CROSS APPLY (SELECT p0.Priority, CHOOSE(p0.Priority, 2, 8, 24, 72) AS Target) pr
    CROSS APPLY (SELECT CASE WHEN w.WorkType = 'Preventive' THEN DATEADD(minute, 420 + w.Rnd / 53 % 480, CAST(DATEADD(day, w.Rnd / 59 % 100 / 9 - 2 + CASE WHEN w.Rnd / 61 % 100 < 6 THEN 12 + w.Rnd / 67 % 30 ELSE 0 END, w.Sched) AS datetime2(0)))
                             ELSE DATEADD(minute, CAST(pr.Target * 60 * CASE WHEN w.Rnd / 53 % 100 < 82 THEN 0.1 + (w.Rnd / 59 % 85) / 100.0 ELSE 1.05 + (w.Rnd / 59 % 300) / 100.0 END AS int), w.Reported) END AS StartAt) t
    CROSS APPLY (SELECT CAST(CHOOSE(a.CostClass, 0.5, 1.5, 3.0) * (0.5 + (w.Rnd / 71 % 250) / 100.0) * CASE WHEN w.WorkType = 'Preventive' THEN 1 WHEN w.Rnd / 73 % 20 = 0 THEN 6 ELSE 1.6 END AS decimal(6,1)) AS Dur) d
    CROSS APPLY (SELECT CASE WHEN t.StartAt > @Now THEN CASE WHEN w.WorkType = 'Preventive' THEN 'Scheduled' ELSE 'Open' END
                             WHEN w.Rnd / 79 % 1000 < 12 THEN 'Cancelled'
                             WHEN w.WorkType = 'Preventive' AND w.Rnd / 79 % 1000 < 30 THEN 'Deferred'
                             WHEN DATEADD(minute, CAST(d.Dur * 60 AS int), t.StartAt) > @Now THEN 'In progress' ELSE 'Completed' END AS St) st
    CROSS APPLY (SELECT FailureCodeId, Description FROM ref.FailureCode WHERE FailureCodeId =
                     CASE a.Parent WHEN 'ICT' THEN CHOOSE(w.Rnd / 83 % 8 + 1, 1, 4, 11, 12, 12, 13, 13, 21) WHEN 'SEC' THEN CHOOSE(w.Rnd / 83 % 8 + 1, 1, 4, 11, 12, 13, 14, 16, 18)
                                   WHEN 'ELC' THEN CHOOSE(w.Rnd / 83 % 8 + 1, 1, 1, 2, 2, 3, 3, 4, 19) WHEN 'AFL' THEN CHOOSE(w.Rnd / 83 % 8 + 1, 2, 3, 3, 3, 15, 17, 17, 20)
                                   WHEN 'GSE' THEN CHOOSE(w.Rnd / 83 % 8 + 1, 5, 7, 8, 8, 15, 16, 19, 19) WHEN 'CIV' THEN CHOOSE(w.Rnd / 83 % 8 + 1, 8, 9, 15, 16, 18, 19, 19, 20)
                                   ELSE CHOOSE(w.Rnd / 83 % 12 + 1, 5, 6, 6, 7, 8, 9, 9, 10, 11, 12, 19, 21) END) fc
) q
LEFT JOIN #TM tm ON tm.TeamId = q.TeamId AND tm.MonthStart = DATEFROMPARTS(YEAR(q.StartAt), MONTH(q.StartAt), 1) AND tm.Idx = q.Rnd / 89 % tm.Cnt
ORDER BY q.Reported, q.K;
CREATE INDEX IX_WorkOrder_Asset ON mnt.WorkOrder (AssetId) INCLUDE (WorkType, WorkOrderStatus, ReportedDateTime);
CREATE INDEX IX_WorkOrder_Reported ON mnt.WorkOrder (ReportedDateTime);

-- Vendors called out on work that had a contractor cost.
UPDATE w SET VendorId = v.VendorId
FROM mnt.WorkOrder w JOIN eam.Asset a ON a.AssetId = w.AssetId JOIN ref.AssetCategory c ON c.CategoryId = a.CategoryId JOIN ref.AssetCategory p ON p.CategoryId = c.ParentCategoryId
JOIN (SELECT VendorId, Specialty, ROW_NUMBER() OVER (PARTITION BY Specialty ORDER BY VendorId) - 1 AS Idx FROM ref.Vendor) v ON v.Specialty = p.CategoryCode AND v.Idx = w.WorkOrderId % 4
WHERE w.ContractorCost > 0;

-- Labour: the assigned technician, and a second one from the same team on two-person jobs.
INSERT INTO mnt.WorkOrderLabour WITH (TABLOCK) (WorkOrderId, TechnicianId, WorkDate, RegularHours, OvertimeHours, LabourCost)
SELECT w.WorkOrderId, t.TechnicianId, CAST(w.StartDateTime AS date), h.Hrs - h.OT, h.OT, ROUND((h.Hrs - h.OT) * t.HourlyRate + h.OT * t.HourlyRate * 1.25, 2)
FROM mnt.WorkOrder w
CROSS APPLY (SELECT CAST(w.LabourHours / CASE WHEN w.LabourHours > DATEDIFF(minute, w.StartDateTime, w.CompletedDateTime) / 60.0 + 0.2 THEN 2 ELSE 1 END AS decimal(6,1)) AS Hrs,
                    CASE WHEN w.LabourHours > DATEDIFF(minute, w.StartDateTime, w.CompletedDateTime) / 60.0 + 0.2 THEN 2 ELSE 1 END AS Crew) c
CROSS APPLY GENERATE_SERIES(1, c.Crew) n
CROSS APPLY (SELECT c.Hrs, CAST(CASE WHEN DATEPART(hour, w.StartDateTime) >= 18 OR DATEPART(hour, w.StartDateTime) < 6 THEN c.Hrs * 0.5 ELSE 0 END AS decimal(6,1)) AS OT) h
JOIN #TM tm0 ON tm0.TeamId = w.TeamId AND tm0.MonthStart = DATEFROMPARTS(YEAR(w.StartDateTime), MONTH(w.StartDateTime), 1) AND tm0.TechnicianId = w.AssignedTechnicianId
JOIN #TM tm ON tm.TeamId = tm0.TeamId AND tm.MonthStart = tm0.MonthStart AND tm.Idx = (tm0.Idx + (n.value - 1) * (1 + w.WorkOrderId % 5)) % tm0.Cnt
JOIN mnt.Technician t ON t.TechnicianId = tm.TechnicianId
WHERE w.WorkOrderStatus = 'Completed'
ORDER BY w.WorkOrderId, n.value;

-- ---------------------------------------------------------------------------------------------------- spare parts
INSERT INTO inv.SparePart (PartNumber, PartName, CategoryId, ManufacturerId, WarehouseId, UnitOfMeasure, UnitCost, QuantityOnHand, ReorderLevel, LeadTimeDays, IsCritical)
SELECT CONCAT('SP-', c.Code, '-', RIGHT(CONCAT('0', k.Ord), 2)), CONCAT(k.Name, N' for ', LOWER(c.Name)), cat.CategoryId, m.ManufacturerId,
       CASE WHEN c.Parent = 'GSE' THEN 3 WHEN c.Parent = 'AFL' THEN 2 ELSE 1 END, CASE WHEN k.Ord IN (2, 9, 13) THEN 'm' WHEN k.Ord IN (5, 15) THEN 'set' ELSE 'each' END,
       CAST(ROUND(CASE WHEN c.CostMin * k.Factor < 15 THEN 15 ELSE c.CostMin * k.Factor END * (0.7 + x.Rnd % 60 / 100.0), 0) AS decimal(10,2)),
       x.Rnd / 3 % 60, 4 + x.Rnd / 5 % 12, 7 + x.Rnd / 7 % 80, CASE WHEN c.Crit = 'A' AND k.Ord IN (1, 3, 6, 12) THEN 1 ELSE 0 END
FROM #Cat c JOIN ref.AssetCategory cat ON cat.CategoryCode = c.Code
CROSS JOIN (VALUES (1,N'Drive motor',0.060),(2,N'Belt',0.012),(3,N'Control board',0.045),(4,N'Sensor',0.008),(5,N'Bearing set',0.006),(6,N'Power supply unit',0.020),(7,N'Fuse and breaker',0.002),
                   (8,N'Filter',0.003),(9,N'Cable assembly',0.004),(10,N'Contactor and relay',0.005),(11,N'Lamp and LED module',0.004),(12,N'Gearbox',0.070),(13,N'Hose and seal',0.004),
                   (14,N'Battery',0.010),(15,N'Fastener kit',0.001),(16,N'Display and indicator',0.015),(17,N'Valve and actuator',0.018),(18,N'Lubricant and consumables',0.001),
                   (19,N'Switch and push button',0.003),(20,N'Fan and blower',0.012)) k (Ord, Name, Factor)
CROSS APPLY (SELECT dbo.R(c.Ord * 100 + k.Ord, 20) AS Rnd) x
CROSS APPLY (SELECT COUNT(*) AS Cnt FROM ref.Manufacturer m WHERE m.Specialty = c.Parent) mc
JOIN (SELECT ManufacturerId, Specialty, ROW_NUMBER() OVER (PARTITION BY Specialty ORDER BY ManufacturerId) - 1 AS Idx FROM ref.Manufacturer) m ON m.Specialty = c.Parent AND m.Idx = x.Rnd / 11 % mc.Cnt
ORDER BY c.Ord, k.Ord;

INSERT INTO mnt.WorkOrderPart WITH (TABLOCK) (WorkOrderId, PartId, Quantity, UnitCost)
SELECT w.WorkOrderId, sp.PartId, 1 + x.Rnd / 3 % CASE WHEN sp.UnitCost < 200 THEN 6 ELSE 2 END, sp.UnitCost
FROM mnt.WorkOrder w JOIN eam.Asset a ON a.AssetId = w.AssetId
CROSS JOIN GENERATE_SERIES(1, 3) n
CROSS APPLY (SELECT dbo.R(CAST(w.WorkOrderId AS bigint) * 10 + n.value, 21) AS Rnd) x
JOIN (SELECT PartId, CategoryId, UnitCost, ROW_NUMBER() OVER (PARTITION BY CategoryId ORDER BY PartId) - 1 AS Idx FROM inv.SparePart) sp
  ON sp.CategoryId = a.CategoryId AND sp.Idx = CASE WHEN w.WorkType = 'Preventive' THEN CHOOSE(x.Rnd % 5 + 1, 7, 10, 14, 17, 17) ELSE x.Rnd % 20 END
WHERE w.WorkOrderStatus = 'Completed'
  AND n.value <= CASE WHEN w.WorkType = 'Preventive' THEN CASE WHEN w.WorkOrderId % 100 < 30 THEN 1 ELSE 0 END
                      ELSE CASE WHEN w.WorkOrderId % 100 < 25 THEN 0 WHEN w.WorkOrderId % 100 < 70 THEN 1 WHEN w.WorkOrderId % 100 < 92 THEN 2 ELSE 3 END END
ORDER BY w.WorkOrderId, n.value;
CREATE INDEX IX_WorkOrderPart_WorkOrder ON mnt.WorkOrderPart (WorkOrderId) INCLUDE (PartId, Quantity, UnitCost);
CREATE INDEX IX_WorkOrderLabour_WorkOrder ON mnt.WorkOrderLabour (WorkOrderId) INCLUDE (TechnicianId, LabourCost);
UPDATE w SET LabourCost = ISNULL(l.Cost, 0), MaterialCost = ISNULL(p.Cost, 0)
FROM mnt.WorkOrder w
LEFT JOIN (SELECT WorkOrderId, SUM(LabourCost) AS Cost FROM mnt.WorkOrderLabour GROUP BY WorkOrderId) l ON l.WorkOrderId = w.WorkOrderId
LEFT JOIN (SELECT WorkOrderId, SUM(LineCost) AS Cost FROM mnt.WorkOrderPart GROUP BY WorkOrderId) p ON p.WorkOrderId = w.WorkOrderId
WHERE w.WorkOrderStatus = 'Completed';
UPDATE p SET LastCompletedDate = x.LastDone, NextDueDate = DATEADD(day, p.FrequencyDays, ISNULL(x.LastDone, @Today))
FROM mnt.PMPlan p LEFT JOIN (SELECT PMPlanId, MAX(CAST(CompletedDateTime AS date)) AS LastDone FROM mnt.WorkOrder WHERE PMPlanId IS NOT NULL AND WorkOrderStatus = 'Completed' GROUP BY PMPlanId) x ON x.PMPlanId = p.PMPlanId;

-- ------------------------------------------------------------------------------------------------ purchase orders
INSERT INTO inv.PurchaseOrder (PurchaseOrderNumber, VendorId, OrderType, OrderDate, ExpectedDate, ReceivedDate, OrderStatus, TotalAmount, RequestedByDepartmentCode)
SELECT CONCAT('PO-', RIGHT(CONCAT('00000', ROW_NUMBER() OVER (ORDER BY q.OrderDate, q.N)), 6)), q.VendorId, q.OrderType, q.OrderDate, q.Expected,
       CASE WHEN q.St IN ('Received', 'Part received') THEN q.Received END, q.St, 0, CASE WHEN q.OrderType = 'Capital' THEN 'ENG' ELSE CHOOSE(q.N % 4 + 1, 'ENG', 'ENG', 'ITD', 'GHS') END
FROM (
    SELECT n.value AS N, 1 + x.Rnd % 48 AS VendorId, CASE WHEN x.Rnd / 50 % 10 < 7 THEN 'Spare parts' WHEN x.Rnd / 50 % 10 < 9 THEN 'Services' ELSE 'Capital' END AS OrderType, d.OrderDate,
           DATEADD(day, 10 + x.Rnd / 500 % 50, d.OrderDate) AS Expected, DATEADD(day, 10 + x.Rnd / 500 % 50 + CASE WHEN x.Rnd / 7 % 100 < 28 THEN 1 + x.Rnd / 11 % 35 ELSE -(x.Rnd / 11 % 6) END, d.OrderDate) AS Received,
           CASE WHEN x.Rnd / 13 % 100 < 2 THEN 'Cancelled'
                WHEN DATEADD(day, 10 + x.Rnd / 500 % 50 + CASE WHEN x.Rnd / 7 % 100 < 28 THEN 1 + x.Rnd / 11 % 35 ELSE -(x.Rnd / 11 % 6) END, d.OrderDate) > @Today THEN CASE WHEN x.Rnd / 17 % 5 = 0 THEN 'Part received' ELSE 'Open' END
                ELSE 'Received' END AS St
    FROM GENERATE_SERIES(1, @Days * 67 / 10) n
    CROSS APPLY (SELECT dbo.R(n.value, 30) AS Rnd) x
    CROSS APPLY (SELECT DATEADD(day, (n.value - 1) * 10 / 67, @Start) AS OrderDate) d
) q;
DECLARE @Parts int = (SELECT COUNT(*) FROM inv.SparePart);
SELECT po.PurchaseOrderId, n.value AS N, r.Rnd, 1 + r.Rnd / 3 % @Parts AS PartId INTO #PL
FROM inv.PurchaseOrder po CROSS JOIN GENERATE_SERIES(1, 5) n CROSS APPLY (SELECT dbo.R(po.PurchaseOrderId * 10 + n.value, 31) AS Rnd) r;
INSERT INTO inv.PurchaseOrderLine (PurchaseOrderId, PartId, Description, OrderedQuantity, ReceivedQuantity, UnitPrice)
SELECT po.PurchaseOrderId, CASE WHEN po.OrderType = 'Spare parts' THEN sp.PartId END,
       CASE po.OrderType WHEN 'Spare parts' THEN sp.PartName WHEN 'Services' THEN CONCAT(CHOOSE(x.Rnd % 5 + 1, N'Annual maintenance service', N'Call-out repair service', N'Calibration service', N'Overhaul', N'Inspection and certification'), N', ', LOWER(c.CategoryName))
                         ELSE CONCAT(N'Replacement ', LOWER(c.CategoryName)) END,
       q.Qty, CASE po.OrderStatus WHEN 'Received' THEN q.Qty WHEN 'Part received' THEN q.Qty / 2 ELSE 0 END,
       CASE po.OrderType WHEN 'Spare parts' THEN CAST(sp.UnitCost * (0.9 + x.Rnd / 7 % 20 / 100.0) AS decimal(12,2)) WHEN 'Services' THEN 2000 + x.Rnd / 11 % 480 * 100 ELSE 20000 + x.Rnd / 11 % 60 * 10000 END
FROM #PL x JOIN inv.PurchaseOrder po ON po.PurchaseOrderId = x.PurchaseOrderId
CROSS APPLY (SELECT x.N AS value) n
JOIN inv.SparePart sp ON sp.PartId = x.PartId
JOIN ref.AssetCategory c ON c.CategoryId = sp.CategoryId
CROSS APPLY (SELECT CASE po.OrderType WHEN 'Spare parts' THEN 2 + x.Rnd / 13 % CASE WHEN sp.UnitCost < 500 THEN 60 ELSE 8 END ELSE 1 END AS Qty) q
WHERE n.value <= CASE po.OrderType WHEN 'Spare parts' THEN 1 + po.PurchaseOrderId % 5 WHEN 'Services' THEN 1 + po.PurchaseOrderId % 2 ELSE 1 END
ORDER BY po.PurchaseOrderId, n.value;
UPDATE po SET TotalAmount = t.Amt FROM inv.PurchaseOrder po JOIN (SELECT PurchaseOrderId, SUM(LineAmount) AS Amt FROM inv.PurchaseOrderLine GROUP BY PurchaseOrderId) t ON t.PurchaseOrderId = po.PurchaseOrderId;

-- ------------------------------------------------------------------- contracts, inspections, meters, depreciation
INSERT INTO mnt.ServiceContract (ContractNumber, VendorId, CategoryId, StartDate, EndDate, AnnualValue, ResponseTimeHours, ContractStatus)
SELECT CONCAT('SC-', RIGHT(CONCAT('000', ROW_NUMBER() OVER (ORDER BY q.StartDate, q.CategoryId)), 4)), q.VendorId, q.CategoryId, q.StartDate, q.EndDate, q.AnnualValue, q.Resp,
       CASE WHEN q.EndDate < @Today THEN 'Expired' WHEN q.StartDate > @Today THEN 'Planned' ELSE 'Active' END
FROM (
    SELECT cat.CategoryId, v.VendorId, DATEADD(month, g.value * 24 + c.Ord % 12, '2021-01-01') AS StartDate, DATEADD(day, -1, DATEADD(month, g.value * 24 + c.Ord % 12 + 24, '2021-01-01')) AS EndDate,
           CAST(ROUND(CAST(c.Cnt AS float) * (c.CostMin + c.CostMax) / 2.0 * 0.03, -3) AS decimal(12,2)) AS AnnualValue, CASE c.Crit WHEN 'A' THEN 2 WHEN 'B' THEN 4 ELSE 8 END AS Resp
    FROM #Cat c JOIN ref.AssetCategory cat ON cat.CategoryCode = c.Code
    JOIN (SELECT VendorId, Specialty, ROW_NUMBER() OVER (PARTITION BY Specialty ORDER BY VendorId) - 1 AS Idx FROM ref.Vendor) v ON v.Specialty = c.Parent
    CROSS JOIN GENERATE_SERIES(0, 3) g
    WHERE (c.CostMax >= 300000 OR c.InspDays IS NOT NULL) AND v.Idx = (c.Ord + g.value) % 4
) q WHERE q.StartDate <= DATEADD(month, 6, @Today);

INSERT INTO eam.Inspection (AssetId, InspectionDate, InspectionType, VendorId, Result, FindingsCount, CertificateNumber, NextDueDate)
SELECT a.AssetId, s.d, CASE a.Parent WHEN 'VTS' THEN N'Lift and escalator safety inspection' WHEN 'FLS' THEN N'Civil defence fire system inspection' WHEN 'GSE' THEN N'Vehicle roadworthiness inspection'
                                    WHEN 'AFL' THEN N'Flight calibration check' WHEN 'PAX' THEN N'Boarding bridge load test' ELSE N'Pressure and electrical safety inspection' END,
       v.VendorId, r.Result, CASE r.Result WHEN 'Pass' THEN x.Rnd / 100 % 2 WHEN 'Conditional' THEN 1 + x.Rnd / 100 % 3 ELSE 3 + x.Rnd / 100 % 4 END,
       CASE WHEN r.Result <> 'Fail' THEN CONCAT('CERT-', RIGHT(CONCAT('000000', x.Rnd / 7 % 1000000), 6)) END, DATEADD(day, a.InspDays, s.d)
FROM #A a CROSS APPLY GENERATE_SERIES(0, @Days / a.InspDays) n
CROSS APPLY (SELECT DATEADD(day, dbo.R(a.AssetId, 40) % a.InspDays + n.value * a.InspDays, @Start) AS d, dbo.R(CAST(a.AssetId AS bigint) * 100 + n.value, 41) AS Rnd) s
CROSS APPLY (SELECT s.Rnd) x
CROSS APPLY (SELECT CASE WHEN x.Rnd % 100 < 88 THEN 'Pass' WHEN x.Rnd % 100 < 97 THEN 'Conditional' ELSE 'Fail' END AS Result) r
JOIN (SELECT VendorId, Specialty, ROW_NUMBER() OVER (PARTITION BY Specialty ORDER BY VendorId) - 1 AS Idx FROM ref.Vendor) v ON v.Specialty = a.Parent AND v.Idx = a.AssetId % 4
WHERE a.InspDays IS NOT NULL AND s.d > a.InstallDate AND s.d <= @Today AND s.d < a.EndDate
ORDER BY s.d, a.AssetId;

INSERT INTO eam.MeterReading WITH (TABLOCK) (AssetId, ReadingDate, MeterType, ReadingValue, UsageSinceLast)
SELECT a.AssetId, m.d, a.Meter, CAST(u.PerMonth * DATEDIFF(month, a.InstallDate, m.d) * (0.97 + x.Rnd % 7 / 100.0) AS decimal(12,1)), CAST(u.PerMonth * (0.8 + x.Rnd / 10 % 40 / 100.0) AS decimal(10,1))
FROM #A a
JOIN (SELECT EOMONTH(DATEADD(month, g.value, @Start)) AS d FROM GENERATE_SERIES(0, DATEDIFF(month, @Start, @Today) - 1) g) m ON m.d > a.InstallDate AND m.d < a.EndDate
CROSS APPLY (SELECT dbo.R(CAST(a.AssetId AS bigint) * 100 + DATEDIFF(month, @Start, m.d), 50) AS Rnd) x
CROSS APPLY (SELECT CASE a.Meter WHEN 'Hours' THEN 180 + dbo.R(a.AssetId, 51) % 400 WHEN 'Km' THEN 600 + dbo.R(a.AssetId, 51) % 2200 ELSE 2500 + dbo.R(a.AssetId, 51) % 6000 END AS PerMonth) u
WHERE a.Meter IS NOT NULL
ORDER BY m.d, a.AssetId;

INSERT INTO fin.AssetDepreciation WITH (TABLOCK) (AssetId, PeriodStart, DepreciationAmount, AccumulatedDepreciation, NetBookValue)
SELECT a.AssetId, m.d, dep.Monthly, dep.Monthly * k.Months, a.PurchaseCost - dep.Monthly * k.Months
FROM eam.Asset a
JOIN (SELECT DATEADD(month, g.value, @Start) AS d FROM GENERATE_SERIES(0, DATEDIFF(month, @Start, @Today)) g) m
  ON m.d >= DATEFROMPARTS(YEAR(a.InstallDate), MONTH(a.InstallDate), 1) AND (a.DisposalDate IS NULL OR m.d <= a.DisposalDate)
CROSS APPLY (SELECT CAST(ROUND((a.PurchaseCost - a.SalvageValue) / (a.UsefulLifeYears * 12), 2) AS decimal(12,2)) AS Monthly) dep
CROSS APPLY (SELECT DATEDIFF(month, a.InstallDate, m.d) + 1 AS Months) k
WHERE k.Months <= a.UsefulLifeYears * 12
ORDER BY m.d, a.AssetId;
CREATE INDEX IX_AssetDepreciation_Asset ON fin.AssetDepreciation (AssetId, PeriodStart);

-- ------------------------------------------------------------------------------------------------------- summary
SELECT 'Assets' AS Item, COUNT(*) AS Rows_ FROM eam.Asset
UNION ALL SELECT 'Technicians', COUNT(*) FROM mnt.Technician
UNION ALL SELECT 'Work orders', COUNT(*) FROM mnt.WorkOrder
UNION ALL SELECT 'Work orders without a technician (not open)', COUNT(*) FROM mnt.WorkOrder WHERE AssignedTechnicianId IS NULL AND WorkOrderStatus IN ('Completed', 'In progress')
UNION ALL SELECT 'Labour lines', COUNT(*) FROM mnt.WorkOrderLabour
UNION ALL SELECT 'Part lines', COUNT(*) FROM mnt.WorkOrderPart
UNION ALL SELECT 'Purchase orders', COUNT(*) FROM inv.PurchaseOrder
UNION ALL SELECT 'Inspections', COUNT(*) FROM eam.Inspection
UNION ALL SELECT 'Meter readings', COUNT(*) FROM eam.MeterReading
UNION ALL SELECT 'Depreciation rows', COUNT(*) FROM fin.AssetDepreciation;
GO
