-- ==============================================================================================================
-- DemoAirport_AODB — Airport Operational Database of Demo Airport (DMO), sample
-- Exactly 255,336 flights (127,668 arrivals and 127,668 departures, passenger and cargo) from 1 January 2023
-- until the day the script runs, with real airlines and real destination airports. Demo Airlines (XD) is the
-- base carrier; 58 other airlines operate to and from the airport. Reference data (countries, airports, airlines,
-- aircraft, terminals, stands, gates, check-in desks, baggage belts, runways, handlers, delay codes), the seasonal
-- schedule, flights, loads (passengers, baggage, cargo, mail), delays, code-shares, milestones and daily weather.
--
-- Run it SECOND (as a sysadmin, in SSMS or sqlcmd): 04 (RMS) takes its flights and resources from this database.
-- Re-running resets the data (about 2 minutes on SQL Server Express 2022). All times are airport local time (UTC+4).
-- The airport, its base carrier, flight numbers, registrations and every figure are invented; the other airlines
-- and the airports are real names used for illustration only.
-- ==============================================================================================================
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
USE [master];
GO
IF DB_ID(N'DemoAirport_AODB') IS NULL CREATE DATABASE [DemoAirport_AODB];
GO
ALTER DATABASE [DemoAirport_AODB] SET RECOVERY SIMPLE;
IF SUSER_ID(N'demoairport_reader') IS NULL
    CREATE LOGIN [demoairport_reader] WITH PASSWORD = N'DemoAirport#Reader#2026', CHECK_POLICY = ON;
GO
USE [DemoAirport_AODB];
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
IF SCHEMA_ID(N'ops') IS NULL EXEC (N'CREATE SCHEMA ops');
GO
CREATE FUNCTION dbo.R (@k bigint, @s int) RETURNS int WITH SCHEMABINDING AS
BEGIN
    RETURN CAST(HASHBYTES('MD5', CONCAT(@k, N':', @s)) AS int) & 2147483647;
END
GO
-- ---------------------------------------------------------------------------------------------------- tables
CREATE TABLE dbo.SampleInfo (DatabaseName sysname NOT NULL, DataFrom date NOT NULL, DataTo date NOT NULL, GeneratedAt datetime2(0) NOT NULL);

CREATE TABLE ref.Country (CountryCode char(2) NOT NULL PRIMARY KEY, CountryName nvarchar(60) NOT NULL, Region nvarchar(30) NOT NULL);
CREATE TABLE ref.Airport (AirportId int IDENTITY(1,1) NOT NULL PRIMARY KEY, IataCode char(3) NOT NULL UNIQUE, IcaoCode char(4) NOT NULL, AirportName nvarchar(80) NOT NULL,
    City nvarchar(60) NOT NULL, CountryCode char(2) NOT NULL REFERENCES ref.Country (CountryCode), BlockMinutes smallint NOT NULL, IsHomeAirport bit NOT NULL);
CREATE TABLE ref.GroundHandler (GroundHandlerId int NOT NULL PRIMARY KEY, HandlerCode varchar(5) NOT NULL UNIQUE, HandlerName nvarchar(80) NOT NULL);
CREATE TABLE ref.Terminal (TerminalId int NOT NULL PRIMARY KEY, TerminalCode varchar(3) NOT NULL UNIQUE, TerminalName nvarchar(60) NOT NULL, TerminalType varchar(10) NOT NULL,
    AnnualCapacityPassengers int NULL);
CREATE TABLE ref.Airline (AirlineId int IDENTITY(1,1) NOT NULL PRIMARY KEY, IataCode char(2) NOT NULL UNIQUE, IcaoCode char(3) NOT NULL, AirlineName nvarchar(80) NOT NULL,
    CountryCode char(2) NOT NULL REFERENCES ref.Country (CountryCode), AirlineType varchar(12) NOT NULL, Alliance varchar(15) NULL, IsBaseCarrier bit NOT NULL,
    TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), GroundHandlerId int NOT NULL REFERENCES ref.GroundHandler (GroundHandlerId), RegistrationPrefix varchar(5) NOT NULL);
CREATE TABLE ref.AircraftType (AircraftTypeId int IDENTITY(1,1) NOT NULL PRIMARY KEY, IataCode char(3) NOT NULL UNIQUE, IcaoCode char(4) NOT NULL, TypeName nvarchar(60) NOT NULL,
    Manufacturer nvarchar(20) NOT NULL, BodyType varchar(6) NOT NULL, SizeCategory char(1) NOT NULL, Seats smallint NOT NULL, CargoCapacityKg int NOT NULL, IsFreighter bit NOT NULL);
CREATE TABLE ref.Aircraft (AircraftId int IDENTITY(1,1) NOT NULL PRIMARY KEY, Registration varchar(10) NOT NULL UNIQUE, AirlineId int NOT NULL REFERENCES ref.Airline (AirlineId),
    AircraftTypeId int NOT NULL REFERENCES ref.AircraftType (AircraftTypeId), DeliveryYear smallint NOT NULL);
CREATE TABLE ref.Stand (StandId int IDENTITY(1,1) NOT NULL PRIMARY KEY, StandCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId),
    StandType varchar(8) NOT NULL, Apron nvarchar(30) NOT NULL, MaxSizeCategory char(1) NOT NULL, HasBoardingBridge bit NOT NULL, StandGroup varchar(4) NOT NULL);
CREATE TABLE ref.Gate (GateId int IDENTITY(1,1) NOT NULL PRIMARY KEY, GateCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId),
    GateType varchar(8) NOT NULL, StandId int NULL REFERENCES ref.Stand (StandId));
CREATE TABLE ref.CheckInDesk (CheckInDeskId int NOT NULL PRIMARY KEY, DeskCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), Island char(1) NOT NULL);
CREATE TABLE ref.BaggageBelt (BaggageBeltId int NOT NULL PRIMARY KEY, BeltCode varchar(5) NOT NULL UNIQUE, TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), BeltLengthMetres smallint NOT NULL);
CREATE TABLE ref.Runway (RunwayId int NOT NULL PRIMARY KEY, Designator varchar(3) NOT NULL UNIQUE, LengthMetres smallint NOT NULL, Surface varchar(10) NOT NULL);
CREATE TABLE ref.ServiceType (ServiceTypeCode char(1) NOT NULL PRIMARY KEY, ServiceTypeName nvarchar(40) NOT NULL, FlightNature varchar(10) NOT NULL);
CREATE TABLE ref.DelayCode (DelayCodeId int NOT NULL PRIMARY KEY, IataNumericCode char(2) NOT NULL UNIQUE, Description nvarchar(80) NOT NULL, Category nvarchar(30) NOT NULL, IsAirportAttributable bit NOT NULL);
CREATE TABLE ref.MilestoneType (MilestoneCode varchar(6) NOT NULL PRIMARY KEY, MilestoneName nvarchar(40) NOT NULL, Direction char(1) NOT NULL, SortOrder tinyint NOT NULL);

CREATE TABLE ops.ScheduledService (ScheduledServiceId int NOT NULL PRIMARY KEY, AirlineId int NOT NULL REFERENCES ref.Airline (AirlineId), AirportId int NOT NULL REFERENCES ref.Airport (AirportId),
    ArrivalFlightNumber varchar(8) NOT NULL, DepartureFlightNumber varchar(8) NOT NULL, ServiceTypeCode char(1) NOT NULL REFERENCES ref.ServiceType (ServiceTypeCode),
    AircraftTypeId int NOT NULL REFERENCES ref.AircraftType (AircraftTypeId), ScheduledArrivalTime time(0) NOT NULL, ScheduledDepartureTime time(0) NOT NULL,
    WeeklyFrequency tinyint NOT NULL, DaysOfOperation char(7) NOT NULL);
CREATE TABLE ops.DailyWeather (WeatherDate date NOT NULL PRIMARY KEY, Condition varchar(15) NOT NULL, MaxTemperatureC tinyint NOT NULL, MinTemperatureC tinyint NOT NULL,
    VisibilityMetres smallint NOT NULL, WindSpeedKnots tinyint NOT NULL, RainfallMm decimal(4,1) NOT NULL, LowVisibilityProcedures bit NOT NULL);
CREATE TABLE ops.Flight (
    FlightId int IDENTITY(1,1) NOT NULL PRIMARY KEY, RotationId int NOT NULL, LinkedFlightId int NULL REFERENCES ops.Flight (FlightId),
    ScheduledServiceId int NOT NULL REFERENCES ops.ScheduledService (ScheduledServiceId), FlightDate date NOT NULL,
    AirlineId int NOT NULL REFERENCES ref.Airline (AirlineId), FlightNumber varchar(8) NOT NULL, Direction char(1) NOT NULL,
    RouteAirportId int NOT NULL REFERENCES ref.Airport (AirportId), ServiceTypeCode char(1) NOT NULL REFERENCES ref.ServiceType (ServiceTypeCode), FlightNature varchar(10) NOT NULL,
    AircraftTypeId int NOT NULL REFERENCES ref.AircraftType (AircraftTypeId), AircraftId int NOT NULL REFERENCES ref.Aircraft (AircraftId),
    TerminalId int NOT NULL REFERENCES ref.Terminal (TerminalId), StandId int NOT NULL REFERENCES ref.Stand (StandId), GateId int NULL REFERENCES ref.Gate (GateId),
    BaggageBeltId int NULL REFERENCES ref.BaggageBelt (BaggageBeltId), CheckInDeskFromId int NULL REFERENCES ref.CheckInDesk (CheckInDeskId),
    CheckInDeskToId int NULL REFERENCES ref.CheckInDesk (CheckInDeskId), RunwayId int NOT NULL REFERENCES ref.Runway (RunwayId),
    GroundHandlerId int NOT NULL REFERENCES ref.GroundHandler (GroundHandlerId),
    ScheduledDateTime datetime2(0) NOT NULL, EstimatedDateTime datetime2(0) NULL, ActualRunwayDateTime datetime2(0) NULL, ActualBlockDateTime datetime2(0) NULL,
    DelayMinutes smallint NULL, IsDelayedOver15 AS (CASE WHEN DelayMinutes > 15 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END) PERSISTED,
    FlightStatus varchar(10) NOT NULL, CancellationReason nvarchar(40) NULL, DivertedToAirportId int NULL REFERENCES ref.Airport (AirportId));
CREATE TABLE ops.FlightLoad (FlightId int NOT NULL PRIMARY KEY REFERENCES ops.Flight (FlightId), SeatsAvailable smallint NOT NULL, PassengersTotal smallint NOT NULL,
    PassengersBusiness smallint NOT NULL, PassengersEconomy smallint NOT NULL, PassengersChildren smallint NOT NULL, PassengersInfants smallint NOT NULL,
    PassengersTransfer smallint NOT NULL, PassengersLocal AS (PassengersTotal - PassengersTransfer) PERSISTED, PassengersReducedMobility smallint NOT NULL,
    LoadFactorPercent decimal(5,1) NULL, BagsCount smallint NOT NULL, BagsWeightKg int NOT NULL, CargoKg int NOT NULL, MailKg int NOT NULL, CrewCount tinyint NOT NULL);
CREATE TABLE ops.FlightDelay (FlightDelayId int IDENTITY(1,1) NOT NULL PRIMARY KEY, FlightId int NOT NULL REFERENCES ops.Flight (FlightId),
    DelayCodeId int NOT NULL REFERENCES ref.DelayCode (DelayCodeId), DelayMinutes smallint NOT NULL, IsPrimary bit NOT NULL);
CREATE TABLE ops.FlightCodeShare (FlightCodeShareId int IDENTITY(1,1) NOT NULL PRIMARY KEY, FlightId int NOT NULL REFERENCES ops.Flight (FlightId),
    MarketingAirlineId int NOT NULL REFERENCES ref.Airline (AirlineId), MarketingFlightNumber varchar(8) NOT NULL);
CREATE TABLE ops.FlightMilestone (FlightMilestoneId int IDENTITY(1,1) NOT NULL PRIMARY KEY, FlightId int NOT NULL REFERENCES ops.Flight (FlightId),
    MilestoneCode varchar(6) NOT NULL REFERENCES ref.MilestoneType (MilestoneCode), PlannedDateTime datetime2(0) NOT NULL, ActualDateTime datetime2(0) NULL);
GO
-- ------------------------------------------------------------------------------------------------ reference data
DECLARE @Start date = '2023-01-01', @Today date = CAST(GETDATE() AS date), @Now datetime2(0) = SYSDATETIME();
DECLARE @Days int = DATEDIFF(day, @Start, @Today) + 1, @Rotations int = 127668;
INSERT INTO dbo.SampleInfo VALUES (N'DemoAirport_AODB', @Start, @Today, @Now);

INSERT INTO ref.Country VALUES
('AE',N'United Arab Emirates',N'Middle East'),('QA',N'Qatar',N'Middle East'),('BH',N'Bahrain',N'Middle East'),('KW',N'Kuwait',N'Middle East'),('OM',N'Oman',N'Middle East'),
('SA',N'Saudi Arabia',N'Middle East'),('JO',N'Jordan',N'Middle East'),('LB',N'Lebanon',N'Middle East'),('IQ',N'Iraq',N'Middle East'),('EG',N'Egypt',N'Africa'),
('TR',N'Türkiye',N'Europe'),('IN',N'India',N'South Asia'),('PK',N'Pakistan',N'South Asia'),('BD',N'Bangladesh',N'South Asia'),('LK',N'Sri Lanka',N'South Asia'),
('NP',N'Nepal',N'South Asia'),('MV',N'Maldives',N'South Asia'),('PH',N'Philippines',N'East Asia'),('SG',N'Singapore',N'East Asia'),('MY',N'Malaysia',N'East Asia'),
('TH',N'Thailand',N'East Asia'),('HK',N'Hong Kong SAR',N'East Asia'),('CN',N'China',N'East Asia'),('UZ',N'Uzbekistan',N'Central Asia'),('KZ',N'Kazakhstan',N'Central Asia'),
('AZ',N'Azerbaijan',N'Central Asia'),('GE',N'Georgia',N'Central Asia'),('AM',N'Armenia',N'Central Asia'),('ET',N'Ethiopia',N'Africa'),('KE',N'Kenya',N'Africa'),
('UG',N'Uganda',N'Africa'),('TZ',N'Tanzania',N'Africa'),('SD',N'Sudan',N'Africa'),('MA',N'Morocco',N'Africa'),('TN',N'Tunisia',N'Africa'),('DZ',N'Algeria',N'Africa'),
('GB',N'United Kingdom',N'Europe'),('DE',N'Germany',N'Europe'),('FR',N'France',N'Europe'),('NL',N'Netherlands',N'Europe'),('CH',N'Switzerland',N'Europe'),
('AT',N'Austria',N'Europe'),('HU',N'Hungary',N'Europe'),('GR',N'Greece',N'Europe'),('IT',N'Italy',N'Europe'),('CZ',N'Czechia',N'Europe'),('PL',N'Poland',N'Europe'),
('RO',N'Romania',N'Europe'),('LU',N'Luxembourg',N'Europe'),('BE',N'Belgium',N'Europe'),('US',N'United States',N'Americas');

INSERT INTO ref.Airport (IataCode, IcaoCode, AirportName, City, CountryCode, BlockMinutes, IsHomeAirport) VALUES
('DMO','ODMO',N'Demo Airport',N'Demo City','AE',0,1),
('DOH','OTHH',N'Hamad International Airport',N'Doha','QA',65,0),('BAH','OBBI',N'Bahrain International Airport',N'Manama','BH',75,0),
('KWI','OKKK',N'Kuwait International Airport',N'Kuwait City','KW',105,0),('MCT','OOMS',N'Muscat International Airport',N'Muscat','OM',65,0),
('SLL','OOSA',N'Salalah Airport',N'Salalah','OM',110,0),('RUH','OERK',N'King Khalid International Airport',N'Riyadh','SA',110,0),
('JED','OEJN',N'King Abdulaziz International Airport',N'Jeddah','SA',175,0),('DMM','OEDF',N'King Fahd International Airport',N'Dammam','SA',85,0),
('MED','OEMA',N'Prince Mohammad bin Abdulaziz Airport',N'Madinah','SA',165,0),('AMM','OJAI',N'Queen Alia International Airport',N'Amman','JO',190,0),
('BEY','OLBA',N'Beirut Rafic Hariri International Airport',N'Beirut','LB',220,0),('BGW','ORBI',N'Baghdad International Airport',N'Baghdad','IQ',150,0),
('BSR','ORMM',N'Basra International Airport',N'Basra','IQ',110,0),('EBL','ORER',N'Erbil International Airport',N'Erbil','IQ',165,0),
('NJF','ORNI',N'Al Najaf International Airport',N'Najaf','IQ',140,0),('CAI','HECA',N'Cairo International Airport',N'Cairo','EG',225,0),
('HBE','HEBA',N'Borg El Arab International Airport',N'Alexandria','EG',235,0),('SSH','HESH',N'Sharm El Sheikh International Airport',N'Sharm El Sheikh','EG',205,0),
('IST','LTFM',N'Istanbul Airport',N'Istanbul','TR',280,0),('SAW','LTFJ',N'Sabiha Gökçen International Airport',N'Istanbul','TR',275,0),
('DEL','VIDP',N'Indira Gandhi International Airport',N'Delhi','IN',205,0),('BOM','VABB',N'Chhatrapati Shivaji Maharaj International Airport',N'Mumbai','IN',190,0),
('BLR','VOBL',N'Kempegowda International Airport',N'Bengaluru','IN',235,0),('MAA','VOMM',N'Chennai International Airport',N'Chennai','IN',245,0),
('HYD','VOHS',N'Rajiv Gandhi International Airport',N'Hyderabad','IN',220,0),('COK','VOCI',N'Cochin International Airport',N'Kochi','IN',235,0),
('CCJ','VOCL',N'Calicut International Airport',N'Kozhikode','IN',230,0),('TRV','VOTV',N'Trivandrum International Airport',N'Thiruvananthapuram','IN',250,0),
('AMD','VAAH',N'Sardar Vallabhbhai Patel International Airport',N'Ahmedabad','IN',180,0),('LKO','VILK',N'Chaudhary Charan Singh International Airport',N'Lucknow','IN',215,0),
('JAI','VIJP',N'Jaipur International Airport',N'Jaipur','IN',195,0),('KHI','OPKC',N'Jinnah International Airport',N'Karachi','PK',125,0),
('LHE','OPLA',N'Allama Iqbal International Airport',N'Lahore','PK',190,0),('ISB','OPIS',N'Islamabad International Airport',N'Islamabad','PK',195,0),
('PEW','OPPS',N'Bacha Khan International Airport',N'Peshawar','PK',185,0),('MUX','OPMT',N'Multan International Airport',N'Multan','PK',170,0),
('SKT','OPST',N'Sialkot International Airport',N'Sialkot','PK',190,0),('DAC','VGHS',N'Hazrat Shahjalal International Airport',N'Dhaka','BD',285,0),
('CGP','VGEG',N'Shah Amanat International Airport',N'Chattogram','BD',300,0),('CMB','VCBI',N'Bandaranaike International Airport',N'Colombo','LK',275,0),
('KTM','VNKT',N'Tribhuvan International Airport',N'Kathmandu','NP',255,0),('MLE','VRMM',N'Velana International Airport',N'Malé','MV',255,0),
('MNL','RPLL',N'Ninoy Aquino International Airport',N'Manila','PH',540,0),('SIN','WSSS',N'Singapore Changi Airport',N'Singapore','SG',445,0),
('KUL','WMKK',N'Kuala Lumpur International Airport',N'Kuala Lumpur','MY',430,0),('BKK','VTBS',N'Suvarnabhumi Airport',N'Bangkok','TH',385,0),
('HKG','VHHH',N'Hong Kong International Airport',N'Hong Kong','HK',450,0),('CAN','ZGGG',N'Guangzhou Baiyun International Airport',N'Guangzhou','CN',440,0),
('PVG','ZSPD',N'Shanghai Pudong International Airport',N'Shanghai','CN',510,0),('TAS','UTTT',N'Tashkent International Airport',N'Tashkent','UZ',190,0),
('ALA','UAAA',N'Almaty International Airport',N'Almaty','KZ',245,0),('GYD','UBBB',N'Heydar Aliyev International Airport',N'Baku','AZ',175,0),
('TBS','UGTB',N'Tbilisi International Airport',N'Tbilisi','GE',195,0),('EVN','UDYZ',N'Zvartnots International Airport',N'Yerevan','AM',190,0),
('ADD','HAAB',N'Addis Ababa Bole International Airport',N'Addis Ababa','ET',245,0),('NBO','HKJK',N'Jomo Kenyatta International Airport',N'Nairobi','KE',310,0),
('EBB','HUEN',N'Entebbe International Airport',N'Entebbe','UG',325,0),('DAR','HTDA',N'Julius Nyerere International Airport',N'Dar es Salaam','TZ',340,0),
('ZNZ','HTZA',N'Abeid Amani Karume International Airport',N'Zanzibar','TZ',335,0),('KRT','HSSK',N'Khartoum International Airport',N'Khartoum','SD',225,0),
('CMN','GMMN',N'Mohammed V International Airport',N'Casablanca','MA',500,0),('TUN','DTTA',N'Tunis-Carthage International Airport',N'Tunis','TN',390,0),
('ALG','DAAG',N'Houari Boumediene Airport',N'Algiers','DZ',430,0),('LHR','EGLL',N'London Heathrow Airport',N'London','GB',450,0),
('MAN','EGCC',N'Manchester Airport',N'Manchester','GB',465,0),('FRA','EDDF',N'Frankfurt Airport',N'Frankfurt','DE',410,0),
('CDG','LFPG',N'Paris Charles de Gaulle Airport',N'Paris','FR',430,0),('AMS','EHAM',N'Amsterdam Airport Schiphol',N'Amsterdam','NL',425,0),
('ZRH','LSZH',N'Zurich Airport',N'Zurich','CH',400,0),('VIE','LOWW',N'Vienna International Airport',N'Vienna','AT',360,0),
('BUD','LHBP',N'Budapest Ferenc Liszt International Airport',N'Budapest','HU',345,0),('ATH','LGAV',N'Athens International Airport',N'Athens','GR',300,0),
('MXP','LIMC',N'Milan Malpensa Airport',N'Milan','IT',385,0),('PRG','LKPR',N'Václav Havel Airport Prague',N'Prague','CZ',375,0),
('WAW','EPWA',N'Warsaw Chopin Airport',N'Warsaw','PL',365,0),('OTP','LROP',N'Henri Coandă International Airport',N'Bucharest','RO',315,0),
('LEJ','EDDP',N'Leipzig/Halle Airport',N'Leipzig','DE',400,0),('CGN','EDDK',N'Cologne Bonn Airport',N'Cologne','DE',410,0),
('LUX','ELLX',N'Luxembourg Airport',N'Luxembourg','LU',420,0);

INSERT INTO ref.GroundHandler VALUES (1,'DAGS',N'Demo Airport Ground Services'),(2,'GAH',N'Gulf Aero Handling');
INSERT INTO ref.Terminal VALUES (1,'T1',N'Terminal 1',N'Passenger',9000000),(2,'T2',N'Terminal 2',N'Passenger',4500000),(3,'CT',N'Cargo Terminal',N'Cargo',NULL);
INSERT INTO ref.Runway VALUES (1,'12L',4000,'Asphalt'),(2,'30R',4000,'Asphalt'),(3,'12R',3800,'Asphalt'),(4,'30L',3800,'Asphalt');
INSERT INTO ref.ServiceType VALUES ('J',N'Scheduled passenger','Passenger'),('C',N'Charter passenger','Passenger'),('F',N'Scheduled cargo','Cargo'),('H',N'Charter cargo','Cargo');

INSERT INTO ref.Airline (IataCode, IcaoCode, AirlineName, CountryCode, AirlineType, Alliance, IsBaseCarrier, TerminalId, GroundHandlerId, RegistrationPrefix)
SELECT v.Iata, v.Icao, v.Name, v.Country, v.Type, v.Alliance, CASE WHEN v.Iata = 'XD' THEN 1 ELSE 0 END,
       CASE v.Type WHEN 'Cargo' THEN 3 WHEN 'Low cost' THEN 2 ELSE 1 END,
       CASE WHEN v.Iata = 'XD' OR dbo.R(v.Ord, 1) % 100 < 60 THEN 1 ELSE 2 END, v.Prefix
FROM (VALUES
(1,'XD','DMA',N'Demo Airlines','AE','Full service',NULL,'A6-D'),
(2,'QR','QTR',N'Qatar Airways','QA','Full service','oneworld','A7-A'),(3,'SV','SVA',N'Saudia','SA','Full service','SkyTeam','HZ-A'),
(4,'XY','KNE',N'flynas','SA','Low cost',NULL,'HZ-N'),(5,'F3','FAD',N'flyadeal','SA','Low cost',NULL,'HZ-F'),
(6,'GF','GFA',N'Gulf Air','BH','Full service',NULL,'A9C-A'),(7,'KU','KAC',N'Kuwait Airways','KW','Full service',NULL,'9K-A'),
(8,'J9','JZR',N'Jazeera Airways','KW','Low cost',NULL,'9K-C'),(9,'WY','OMA',N'Oman Air','OM','Full service','oneworld','A4O-B'),
(10,'OV','OMS',N'SalamAir','OM','Low cost',NULL,'A4O-O'),(11,'MS','MSR',N'EgyptAir','EG','Full service','Star Alliance','SU-G'),
(12,'SM','MSC',N'Air Cairo','EG','Low cost',NULL,'SU-B'),(13,'NP','NIA',N'Nile Air','EG','Full service',NULL,'SU-N'),
(14,'RJ','RJA',N'Royal Jordanian','JO','Full service','oneworld','JY-A'),(15,'ME','MEA',N'Middle East Airlines','LB','Full service','SkyTeam','OD-M'),
(16,'TK','THY',N'Turkish Airlines','TR','Full service','Star Alliance','TC-J'),(17,'PC','PGT',N'Pegasus Airlines','TR','Low cost',NULL,'TC-N'),
(18,'AI','AIC',N'Air India','IN','Full service','Star Alliance','VT-E'),(19,'IX','AXB',N'Air India Express','IN','Low cost',NULL,'VT-A'),
(20,'6E','IGO',N'IndiGo','IN','Low cost',NULL,'VT-I'),(21,'SG','SEJ',N'SpiceJet','IN','Low cost',NULL,'VT-S'),
(22,'QP','AKJ',N'Akasa Air','IN','Low cost',NULL,'VT-Y'),(23,'PK','PIA',N'Pakistan International Airlines','PK','Full service',NULL,'AP-B'),
(24,'PA','ABQ',N'airblue','PK','Low cost',NULL,'AP-E'),(25,'PF','SIF',N'AirSial','PK','Low cost',NULL,'AP-S'),
(26,'BG','BBC',N'Biman Bangladesh Airlines','BD','Full service',NULL,'S2-A'),(27,'BS','UBG',N'US-Bangla Airlines','BD','Low cost',NULL,'S2-U'),
(28,'UL','ALK',N'SriLankan Airlines','LK','Full service','oneworld','4R-A'),(29,'RA','RNA',N'Nepal Airlines','NP','Full service',NULL,'9N-A'),
(30,'H9','HIM',N'Himalaya Airlines','NP','Low cost',NULL,'9N-H'),(31,'PR','PAL',N'Philippine Airlines','PH','Full service',NULL,'RP-C'),
(32,'5J','CEB',N'Cebu Pacific','PH','Low cost',NULL,'RP-D'),(33,'ET','ETH',N'Ethiopian Airlines','ET','Full service','Star Alliance','ET-A'),
(34,'KQ','KQA',N'Kenya Airways','KE','Full service','SkyTeam','5Y-K'),(35,'BA','BAW',N'British Airways','GB','Full service','oneworld','G-Z'),
(36,'LH','DLH',N'Lufthansa','DE','Full service','Star Alliance','D-A'),(37,'AF','AFR',N'Air France','FR','Full service','SkyTeam','F-H'),
(38,'KL','KLM',N'KLM Royal Dutch Airlines','NL','Full service','SkyTeam','PH-B'),(39,'LX','SWR',N'Swiss International Air Lines','CH','Full service','Star Alliance','HB-J'),
(40,'W6','WZZ',N'Wizz Air','HU','Low cost',NULL,'HA-L'),(41,'HY','UZB',N'Uzbekistan Airways','UZ','Full service',NULL,'UK-3'),
(42,'KC','KZR',N'Air Astana','KZ','Full service',NULL,'EI-K'),(43,'J2','AHY',N'Azerbaijan Airlines','AZ','Full service',NULL,'4K-A'),
(44,'A3','AEE',N'Aegean Airlines','GR','Full service','Star Alliance','SX-D'),(45,'SQ','SIA',N'Singapore Airlines','SG','Full service','Star Alliance','9V-S'),
(46,'MH','MAS',N'Malaysia Airlines','MY','Full service','oneworld','9M-M'),(47,'TG','THA',N'Thai Airways International','TH','Full service','Star Alliance','HS-T'),
(48,'CX','CPA',N'Cathay Pacific','HK','Full service','oneworld','B-L'),(49,'CZ','CSN',N'China Southern Airlines','CN','Full service',NULL,'B-2'),
(50,'MU','CES',N'China Eastern Airlines','CN','Full service','SkyTeam','B-6'),(51,'AT','RAM',N'Royal Air Maroc','MA','Full service','oneworld','CN-R'),
(52,'TU','TAR',N'Tunisair','TN','Full service',NULL,'TS-I'),(53,'AH','DAH',N'Air Algérie','DZ','Full service',NULL,'7T-V'),
(54,'IA','IAW',N'Iraqi Airways','IQ','Full service',NULL,'YI-A'),(55,'FX','FDX',N'FedEx Express','US','Cargo',NULL,'N8'),
(56,'5X','UPS',N'UPS Airlines','US','Cargo',NULL,'N3'),(57,'QY','BCS',N'European Air Transport (DHL)','DE','Cargo',NULL,'D-AE'),
(58,'ES','DHX',N'DHL International Aviation ME','BH','Cargo',NULL,'A9C-D'),(59,'CV','CLX',N'Cargolux','LU','Cargo',NULL,'LX-V')) v (Ord, Iata, Icao, Name, Country, Type, Alliance, Prefix)
ORDER BY v.Ord;

INSERT INTO ref.AircraftType (IataCode, IcaoCode, TypeName, Manufacturer, BodyType, SizeCategory, Seats, CargoCapacityKg, IsFreighter) VALUES
('320','A320',N'Airbus A320-200',N'Airbus','Narrow','C',168,2000,0),('32N','A20N',N'Airbus A320neo',N'Airbus','Narrow','C',180,2000,0),
('32Q','A21N',N'Airbus A321neo',N'Airbus','Narrow','C',220,2500,0),('738','B738',N'Boeing 737-800',N'Boeing','Narrow','C',176,2200,0),
('7M8','B38M',N'Boeing 737 MAX 8',N'Boeing','Narrow','C',178,2200,0),('333','A333',N'Airbus A330-300',N'Airbus','Wide','E',290,14000,0),
('359','A359',N'Airbus A350-900',N'Airbus','Wide','E',315,16000,0),('788','B788',N'Boeing 787-8',N'Boeing','Wide','E',242,12000,0),
('789','B789',N'Boeing 787-9',N'Boeing','Wide','E',290,14000,0),('77X','B77L',N'Boeing 777F',N'Boeing','Wide','E',0,102000,1),
('74Y','B744',N'Boeing 747-400F',N'Boeing','Wide','E',0,112000,1),('76F','B763',N'Boeing 767-300F',N'Boeing','Wide','D',0,52000,1),
('33X','A332',N'Airbus A330-200F',N'Airbus','Wide','E',0,65000,1),('73P','B738',N'Boeing 737-800BCF',N'Boeing','Narrow','C',0,22000,1);

-- Stands, gates, check-in desks and baggage belts.
INSERT INTO ref.Stand (StandCode, TerminalId, StandType, Apron, MaxSizeCategory, HasBoardingBridge, StandGroup)
SELECT CONCAT(v.Prefix, RIGHT(CONCAT('0', n.value), 2)), v.TerminalId, v.StandType, v.Apron, v.Size, v.Bridge, v.Grp
FROM (VALUES (1,'A',1,8,1,'Contact',N'Terminal 1 apron','E',1,'T1W'),(2,'A',9,20,1,'Contact',N'Terminal 1 apron','C',1,'T1N'),(3,'B',1,12,2,'Contact',N'Terminal 2 apron','C',0,'T2N'),
             (4,'R',1,16,1,'Remote',N'Remote apron','C',0,'RMN'),(5,'R',17,24,1,'Remote',N'Remote apron','E',0,'RMW'),(6,'C',1,10,3,'Cargo',N'Cargo apron','F',0,'CGO')) v (Ord, Prefix, Lo, Hi, TerminalId, StandType, Apron, Size, Bridge, Grp)
CROSS APPLY GENERATE_SERIES(v.Lo, v.Hi) n ORDER BY v.Ord, n.value;
INSERT INTO ref.Gate (GateCode, TerminalId, GateType, StandId)
SELECT CONCAT('G', RIGHT(CONCAT('0', ROW_NUMBER() OVER (ORDER BY s.StandId)), 2)), s.TerminalId, CASE WHEN s.HasBoardingBridge = 1 THEN 'Bridge' ELSE 'Walk-out' END, s.StandId
FROM ref.Stand s WHERE s.StandType = 'Contact' ORDER BY s.StandId;
INSERT INTO ref.Gate (GateCode, TerminalId, GateType, StandId)
SELECT CONCAT('G', 40 + n.value), CASE WHEN n.value <= 6 THEN 1 ELSE 2 END, 'Bus', NULL FROM GENERATE_SERIES(1, 10) n ORDER BY n.value;
INSERT INTO ref.CheckInDesk SELECT n.value, CONCAT('D', RIGHT(CONCAT('00', n.value), 3)), CASE WHEN n.value <= 96 THEN 1 ELSE 2 END, CHAR(65 + (n.value - 1) / 12) FROM GENERATE_SERIES(1, 144) n;
INSERT INTO ref.BaggageBelt SELECT n.value, CONCAT('B', RIGHT(CONCAT('0', n.value), 2)), CASE WHEN n.value <= 8 THEN 1 ELSE 2 END, CASE WHEN n.value <= 4 THEN 90 ELSE 60 END FROM GENERATE_SERIES(1, 12) n;

INSERT INTO ref.DelayCode VALUES
(1,'11',N'Late check-in, acceptance after deadline',N'Passenger and baggage',0),(2,'15',N'Boarding discrepancies and paging',N'Passenger and baggage',0),
(3,'18',N'Baggage processing and sorting',N'Passenger and baggage',1),(4,'32',N'Loading and unloading',N'Aircraft and ramp handling',0),
(5,'34',N'Servicing equipment not available',N'Aircraft and ramp handling',0),(6,'35',N'Aircraft cleaning',N'Aircraft and ramp handling',0),
(7,'36',N'Fuelling and defuelling',N'Aircraft and ramp handling',0),(8,'37',N'Catering late delivery',N'Aircraft and ramp handling',0),
(9,'41',N'Aircraft defects',N'Technical',0),(10,'46',N'Aircraft change for technical reasons',N'Technical',0),
(11,'55',N'Departure control system failure',N'Automated equipment',0),(12,'63',N'Late crew boarding or departure procedures',N'Flight operations and crew',0),
(13,'64',N'Flight deck crew shortage',N'Flight operations and crew',0),(14,'71',N'Weather at departure station',N'Weather',0),
(15,'72',N'Weather at destination station',N'Weather',0),(16,'81',N'Air traffic flow management, en-route',N'Air traffic control',0),
(17,'83',N'Air traffic flow management at destination',N'Air traffic control',0),(18,'85',N'Mandatory security',N'Airport and authorities',1),
(19,'86',N'Immigration, customs and health',N'Airport and authorities',1),(20,'87',N'Airport facilities: stands, gates, congestion',N'Airport and authorities',1),
(21,'89',N'Restrictions at airport of departure, start-up and push-back',N'Airport and authorities',1),(22,'93',N'Aircraft rotation, late arrival from another flight',N'Reactionary',0),
(23,'95',N'Crew rotation, awaiting crew from another flight',N'Reactionary',0),(24,'96',N'Operations control, re-routing',N'Reactionary',0);
INSERT INTO ref.MilestoneType VALUES ('ALDT',N'Landing','A',1),('AIBT',N'In-block','A',2),('FBAG',N'First bag on belt','A',3),('LBAG',N'Last bag on belt','A',4),
('CKIO',N'Check-in open','D',1),('CKIC',N'Check-in closed','D',2),('BRDS',N'Boarding started','D',3),('GCLS',N'Gate closed','D',4),('AOBT',N'Off-block','D',5),('ATOT',N'Take-off','D',6);

-- ------------------------------------------------------------------------------------------------------ schedule
-- Routes with their flights per week (airline, airport, per week, aircraft type).
CREATE TABLE #Route (RouteNo int IDENTITY(1,1) PRIMARY KEY, Airline char(2), Airport char(3), PerWeek int, AcType char(3));
INSERT INTO #Route (Airline, Airport, PerWeek, AcType) VALUES
('XD','DOH',11,'32N'),('XD','BAH',7,'32N'),('XD','KWI',11,'32N'),('XD','MCT',11,'32N'),('XD','SLL',4,'32N'),('XD','RUH',14,'32N'),('XD','JED',14,'32Q'),('XD','DMM',7,'32N'),
('XD','MED',5,'32N'),('XD','AMM',7,'32N'),('XD','BEY',5,'32N'),('XD','BGW',4,'32N'),('XD','BSR',3,'32N'),('XD','EBL',3,'32N'),('XD','NJF',4,'32N'),('XD','CAI',14,'32Q'),
('XD','HBE',5,'32N'),('XD','SSH',3,'32N'),('XD','IST',7,'32Q'),('XD','SAW',4,'32N'),('XD','DEL',11,'32Q'),('XD','BOM',11,'32Q'),('XD','BLR',5,'32N'),('XD','MAA',5,'32N'),
('XD','HYD',5,'32N'),('XD','COK',7,'32Q'),('XD','CCJ',5,'32N'),('XD','TRV',4,'32N'),('XD','AMD',4,'32N'),('XD','LKO',3,'32N'),('XD','JAI',3,'32N'),('XD','KHI',7,'32N'),
('XD','LHE',7,'32N'),('XD','ISB',7,'32N'),('XD','PEW',4,'32N'),('XD','MUX',3,'32N'),('XD','SKT',3,'32N'),('XD','DAC',7,'32Q'),('XD','CGP',3,'32N'),('XD','CMB',5,'32N'),
('XD','KTM',5,'32N'),('XD','MLE',3,'32N'),('XD','TAS',3,'32N'),('XD','ALA',3,'32N'),('XD','GYD',4,'32N'),('XD','TBS',4,'32N'),('XD','EVN',3,'32N'),('XD','ADD',2,'32N'),
('XD','NBO',4,'32N'),('XD','EBB',2,'32N'),('XD','DAR',2,'32N'),('XD','ZNZ',2,'32N'),('XD','KRT',2,'32N'),('XD','ATH',3,'32N'),('XD','VIE',2,'32Q'),('XD','BUD',2,'32N'),
('XD','PRG',2,'32N'),('XD','WAW',2,'32N'),('XD','OTP',2,'32N'),('XD','MXP',2,'32Q'),('XD','LHR',7,'789'),('XD','MAN',2,'789'),('XD','MNL',4,'789'),('XD','BKK',4,'789'),
('XD','KUL',3,'789'),('XD','CDG',3,'789'),('XD','FRA',3,'789'),('XD','BOM',3,'33X'),('XD','NBO',2,'33X'),('XD','DAC',2,'33X'),('XD','KHI',2,'73P'),('XD','RUH',3,'73P'),
('QR','DOH',11,'320'),('SV','RUH',7,'320'),('SV','JED',7,'333'),('XY','RUH',5,'32N'),('XY','JED',4,'32N'),('F3','RUH',4,'32N'),('GF','BAH',7,'32N'),('KU','KWI',5,'32N'),
('J9','KWI',5,'32N'),('WY','MCT',7,'738'),('OV','MCT',4,'32N'),('OV','SLL',2,'32N'),('MS','CAI',7,'738'),('SM','HBE',3,'32N'),('NP','CAI',2,'320'),('RJ','AMM',4,'320'),
('ME','BEY',4,'32N'),('TK','IST',7,'333'),('PC','SAW',5,'32N'),('AI','DEL',4,'32N'),('AI','BOM',4,'32N'),('IX','COK',4,'738'),('IX','CCJ',4,'738'),('IX','TRV',2,'738'),
('6E','DEL',7,'32N'),('6E','BOM',7,'32N'),('6E','HYD',4,'32N'),('6E','COK',4,'32N'),('6E','AMD',3,'32N'),('6E','LKO',2,'32N'),('SG','DEL',3,'738'),('SG','JAI',2,'738'),
('QP','BOM',3,'7M8'),('PK','KHI',4,'320'),('PK','LHE',4,'320'),('PK','ISB',4,'320'),('PK','PEW',2,'320'),('PA','LHE',3,'32N'),('PA','ISB',3,'32N'),('PF','SKT',2,'320'),
('BG','DAC',4,'738'),('BG','CGP',2,'738'),('BS','DAC',4,'738'),('UL','CMB',4,'32N'),('RA','KTM',2,'320'),('H9','KTM',3,'320'),('PR','MNL',4,'333'),('5J','MNL',4,'32Q'),
('ET','ADD',5,'738'),('KQ','NBO',3,'738'),('BA','LHR',7,'789'),('LH','FRA',4,'333'),('AF','CDG',3,'359'),('KL','AMS',4,'789'),('LX','ZRH',3,'333'),('W6','BUD',3,'32Q'),
('W6','OTP',2,'32Q'),('HY','TAS',3,'32N'),('KC','ALA',3,'32N'),('J2','GYD',3,'320'),('A3','ATH',2,'32N'),('SQ','SIN',4,'359'),('MH','KUL',3,'333'),('TG','BKK',3,'359'),
('CX','HKG',3,'359'),('CZ','CAN',3,'789'),('MU','PVG',2,'789'),('AT','CMN',2,'788'),('TU','TUN',2,'32N'),('AH','ALG',2,'738'),('IA','BGW',4,'738'),('IA','BSR',2,'738'),
('FX','CDG',5,'77X'),('5X','CGN',5,'76F'),('QY','LEJ',6,'33X'),('ES','BAH',6,'76F'),('CV','LUX',3,'74Y'),('ET','ADD',2,'77X'),('TK','IST',3,'33X'),('QR','DOH',4,'77X'),
('SV','RUH',2,'77X'),('CX','HKG',2,'74Y'),('SQ','SIN',2,'74Y');

-- One slot per (possible) daily frequency of a route; each slot flies on its own days of the week.
CREATE TABLE #Slot (SlotId int IDENTITY(1,1) PRIMARY KEY, RouteNo int, SlotNo int, DaysPerWeek int, AirlineId int, AirportId int, AircraftTypeId int, IsBase bit, IsCargo bit,
    IsWide bit, BlockMinutes int, SlotMinute int, TurnMinutes int, FlightNo int NULL, StaticRank int NULL);
INSERT INTO #Slot (RouteNo, SlotNo, DaysPerWeek, AirlineId, AirportId, AircraftTypeId, IsBase, IsCargo, IsWide, BlockMinutes, SlotMinute, TurnMinutes)
SELECT r.RouteNo, n.value, CASE WHEN r.PerWeek - 7 * (n.value - 1) > 7 THEN 7 ELSE r.PerWeek - 7 * (n.value - 1) END, al.AirlineId, ap.AirportId, t.AircraftTypeId, al.IsBaseCarrier,
       t.IsFreighter, CASE WHEN t.BodyType = 'Wide' THEN 1 ELSE 0 END, ap.BlockMinutes,
       -- Hour from the airport's daily waves, then spread the slots of the same route over the day.
       ((h.Hr * 60 + dbo.R(r.RouteNo, 12) % 12 * 5) + (n.value - 1) * (1440 / ((r.PerWeek + 6) / 7))) % 1440,
       CASE WHEN t.IsFreighter = 1 THEN 120 + dbo.R(r.RouteNo, 13) % 13 * 5 WHEN t.BodyType = 'Wide' THEN 90 + dbo.R(r.RouteNo, 13) % 13 * 5 ELSE 45 + dbo.R(r.RouteNo, 13) % 7 * 5 END
FROM #Route r JOIN ref.Airline al ON al.IataCode = r.Airline JOIN ref.Airport ap ON ap.IataCode = r.Airport JOIN ref.AircraftType t ON t.IataCode = r.AcType
CROSS APPLY GENERATE_SERIES(1, (r.PerWeek + 6) / 7) n
CROSS APPLY (SELECT CAST(SUBSTRING('000001010102020506070707080808090910111213141415161718181919202021212222232300', (dbo.R(r.RouteNo, 11) % 39) * 2 + 1, 2) AS int) AS Hr) h
ORDER BY r.RouteNo, n.value;
-- Flight numbers: an even/odd pair per slot within the airline (cargo in the 9000 range); a fixed rank decides which slots are dropped first on quiet days.
WITH x AS (SELECT SlotId, ROW_NUMBER() OVER (PARTITION BY AirlineId, IsCargo ORDER BY SlotId) AS rn, ROW_NUMBER() OVER (ORDER BY DaysPerWeek DESC, dbo.R(SlotId, 14)) AS sr FROM #Slot)
UPDATE s SET FlightNo = CASE WHEN s.IsCargo = 1 THEN 9000 ELSE 100 END + x.rn * 2, StaticRank = x.sr FROM #Slot s JOIN x ON x.SlotId = s.SlotId;

CREATE TABLE #SlotDay (SlotId int, Wd int, Operates bit, PRIMARY KEY (SlotId, Wd));
INSERT INTO #SlotDay SELECT s.SlotId, w.value, CASE WHEN ROW_NUMBER() OVER (PARTITION BY s.SlotId ORDER BY dbo.R(s.SlotId * 10 + w.value, 15)) <= s.DaysPerWeek THEN 1 ELSE 0 END
FROM #Slot s CROSS JOIN GENERATE_SERIES(0, 6) w;

INSERT INTO ops.ScheduledService (ScheduledServiceId, AirlineId, AirportId, ArrivalFlightNumber, DepartureFlightNumber, ServiceTypeCode, AircraftTypeId, ScheduledArrivalTime,
    ScheduledDepartureTime, WeeklyFrequency, DaysOfOperation)
SELECT s.SlotId, s.AirlineId, s.AirportId, CONCAT(al.IataCode, s.FlightNo + CASE WHEN s.IsBase = 1 THEN 1 ELSE 0 END), CONCAT(al.IataCode, s.FlightNo + CASE WHEN s.IsBase = 1 THEN 0 ELSE 1 END),
       CASE WHEN s.IsCargo = 1 THEN 'F' ELSE 'J' END, s.AircraftTypeId,
       CAST(DATEADD(minute, CASE WHEN s.IsBase = 1 THEN s.SlotMinute + 2 * s.BlockMinutes + 60 ELSE s.SlotMinute END, CAST('2000-01-01' AS datetime2(0))) AS time(0)),
       CAST(DATEADD(minute, CASE WHEN s.IsBase = 1 THEN s.SlotMinute ELSE s.SlotMinute + s.TurnMinutes END, CAST('2000-01-01' AS datetime2(0))) AS time(0)),
       s.DaysPerWeek, (SELECT STRING_AGG(CASE WHEN d.Operates = 1 THEN CAST(d.Wd + 1 AS char(1)) ELSE '.' END, '') WITHIN GROUP (ORDER BY d.Wd) FROM #SlotDay d WHERE d.SlotId = s.SlotId)
FROM #Slot s JOIN ref.Airline al ON al.AirlineId = s.AirlineId;

-- Aircraft: a small pool of tails per airline and type.
INSERT INTO ref.Aircraft (Registration, AirlineId, AircraftTypeId, DeliveryYear)
SELECT CONCAT(q.RegistrationPrefix, CHAR(65 + (q.k - 1) / 26 % 26), CHAR(65 + (q.k - 1) % 26)), q.AirlineId, q.AircraftTypeId, q.DeliveryYear
FROM (
    SELECT al.RegistrationPrefix, f.AirlineId, f.AircraftTypeId, 2008 + dbo.R(f.Ord * 100 + n.value, 16) % 16 AS DeliveryYear,
           ROW_NUMBER() OVER (PARTITION BY f.AirlineId ORDER BY f.AircraftTypeId, n.value) AS k, f.Ord, n.value AS n
    FROM (SELECT AirlineId, AircraftTypeId, SUM(DaysPerWeek) AS PerWeek, ROW_NUMBER() OVER (ORDER BY AirlineId, AircraftTypeId) AS Ord FROM #Slot GROUP BY AirlineId, AircraftTypeId) f
    JOIN ref.Airline al ON al.AirlineId = f.AirlineId
    CROSS APPLY GENERATE_SERIES(1, CASE WHEN al.IsBaseCarrier = 1 THEN 2 + f.PerWeek / 9 ELSE 2 + f.PerWeek / 3 END) n
) q ORDER BY q.Ord, q.n;

-- ------------------------------------------------------------------------------------------------------- weather
INSERT INTO ops.DailyWeather (WeatherDate, Condition, MaxTemperatureC, MinTemperatureC, VisibilityMetres, WindSpeedKnots, RainfallMm, LowVisibilityProcedures)
SELECT w.d, w.Cond, w.Tmax, w.Tmax - 9 - w.Rnd / 11 % 4,
       CASE w.Cond WHEN 'Fog' THEN 150 + w.Rnd / 13 % 600 WHEN 'Dust storm' THEN 800 + w.Rnd / 13 % 2200 WHEN 'Haze' THEN 4000 + w.Rnd / 13 % 3000 WHEN 'Thunderstorm' THEN 3000 + w.Rnd / 13 % 3000 ELSE 9999 END,
       CASE WHEN w.Cond IN ('Dust storm', 'Thunderstorm') THEN 22 + w.Rnd / 17 % 18 ELSE 4 + w.Rnd / 17 % 14 END,
       CASE w.Cond WHEN 'Rain' THEN 1 + w.Rnd / 19 % 120 / 10.0 WHEN 'Thunderstorm' THEN 8 + w.Rnd / 19 % 400 / 10.0 ELSE 0 END, CASE WHEN w.Cond = 'Fog' THEN 1 ELSE 0 END
FROM (
    SELECT x.d, x.Rnd, CAST(CHOOSE(MONTH(x.d), 24, 26, 30, 35, 40, 42, 43, 43, 40, 36, 31, 26) + x.Rnd / 7 % 5 - 2 AS int) AS Tmax,
           CASE WHEN x.Rnd % 1000 < CASE WHEN MONTH(x.d) IN (12, 1, 2) THEN 70 WHEN MONTH(x.d) IN (3, 11) THEN 25 ELSE 4 END THEN 'Fog'
                WHEN x.Rnd % 1000 < 70 + CASE WHEN MONTH(x.d) BETWEEN 3 AND 8 THEN 50 ELSE 10 END THEN 'Dust storm'
                WHEN x.Rnd % 1000 < 125 + CASE WHEN MONTH(x.d) IN (12, 1, 2, 3) THEN 35 ELSE 2 END THEN 'Rain'
                WHEN x.Rnd % 1000 < 168 THEN 'Thunderstorm' WHEN x.Rnd % 1000 < 330 THEN 'Haze' ELSE 'Clear' END AS Cond
    FROM (SELECT DATEADD(day, g.value, @Start) AS d, dbo.R(g.value, 20) AS Rnd FROM GENERATE_SERIES(0, @Days - 1) g) x
) w;
UPDATE ops.DailyWeather SET Condition = 'Clear', VisibilityMetres = 9999 WHERE Condition = 'Thunderstorm' AND DAY(WeatherDate) % 3 <> 0;

-- ------------------------------------------------------------------------------------------------------- flights
-- Rotations per day: the weekday's scheduled slots, scaled by season and by four years of growth, then adjusted
-- so that the total is exactly 127,668 rotations (255,336 flights).
CREATE TABLE #Day (d date PRIMARY KEY, DayNo int, Wd int, Raw float, N int, BadWx bit);
INSERT INTO #Day (d, DayNo, Wd, Raw, BadWx)
SELECT x.d, x.DayNo, x.Wd, b.Ops * CHOOSE(MONTH(x.d), 1.02, 0.97, 1.00, 1.00, 0.95, 1.03, 1.10, 1.08, 0.95, 0.98, 0.97, 1.08) * (0.86 + 0.26 * x.DayNo / @Days)
           * (0.98 + dbo.R(x.DayNo, 21) % 5 / 100.0),
       CASE WHEN w.Condition IN ('Fog', 'Dust storm', 'Thunderstorm') THEN 1 ELSE 0 END
FROM (SELECT DATEADD(day, g.value, @Start) AS d, g.value AS DayNo, DATEDIFF(day, '2023-01-02', DATEADD(day, g.value, @Start)) % 7 AS Wd FROM GENERATE_SERIES(0, @Days - 1) g) x
JOIN (SELECT Wd, SUM(CAST(Operates AS int)) AS Ops FROM #SlotDay GROUP BY Wd) b ON b.Wd = (x.Wd + 7) % 7
JOIN ops.DailyWeather w ON w.WeatherDate = x.d;
DECLARE @Scale float = @Rotations / (SELECT SUM(Raw) FROM #Day);
UPDATE #Day SET N = FLOOR(Raw * @Scale);
DECLARE @Left int = @Rotations - (SELECT SUM(N) FROM #Day);
WITH x AS (SELECT d, ROW_NUMBER() OVER (ORDER BY Raw * @Scale - FLOOR(Raw * @Scale) DESC, d) AS rn FROM #Day)
UPDATE dd SET N = N + 1 FROM #Day dd JOIN x ON x.d = dd.d WHERE x.rn <= @Left;
IF (SELECT MAX(N) FROM #Day) > (SELECT COUNT(*) FROM #Slot) THROW 50001, 'Not enough schedule slots for the busiest day.', 1;

CREATE TABLE #Rot (RotationId int IDENTITY(1,1) PRIMARY KEY, d date, SlotId int, T0 datetime2(0), Rnd int, Cancelled bit, Diverted bit, AircraftId int NULL);
INSERT INTO #Rot (d, SlotId, T0, Rnd, Cancelled, Diverted)
SELECT q.d, q.SlotId, DATEADD(minute, q.SlotMinute, CAST(q.d AS datetime2(0))), q.Rnd,
       CASE WHEN q.Rnd % 1000 < CASE WHEN q.BadWx = 1 THEN 40 ELSE 9 END THEN 1 ELSE 0 END,
       CASE WHEN q.Rnd % 1000 >= 990 AND q.Rnd / 1000 % 10 < CASE WHEN q.BadWx = 1 THEN 6 ELSE 1 END THEN 1 ELSE 0 END
FROM (
    SELECT dd.d, dd.N, dd.BadWx, s.SlotId, s.SlotMinute, dbo.R(CAST(dd.DayNo AS bigint) * 1000 + s.SlotId, 22) AS Rnd,
           ROW_NUMBER() OVER (PARTITION BY dd.d ORDER BY sd.Operates DESC, CASE WHEN sd.Operates = 1 THEN s.StaticRank ELSE dbo.R(CAST(dd.DayNo AS bigint) * 1000 + s.SlotId, 23) END) AS rn
    FROM #Day dd CROSS JOIN #Slot s JOIN #SlotDay sd ON sd.SlotId = s.SlotId AND sd.Wd = (dd.Wd + 7) % 7
) q WHERE q.rn <= q.N
ORDER BY q.d, q.SlotMinute, q.SlotId;
UPDATE r SET AircraftId = a.AircraftId
FROM #Rot r JOIN #Slot s ON s.SlotId = r.SlotId
JOIN (SELECT AircraftId, AirlineId, AircraftTypeId, ROW_NUMBER() OVER (PARTITION BY AirlineId, AircraftTypeId ORDER BY AircraftId) - 1 AS Idx, COUNT(*) OVER (PARTITION BY AirlineId, AircraftTypeId) AS Cnt FROM ref.Aircraft) a
  ON a.AirlineId = s.AirlineId AND a.AircraftTypeId = s.AircraftTypeId AND a.Idx = r.Rnd / 100 % a.Cnt;

-- Two legs per rotation. Foreign carriers arrive and turn round; the base carrier departs and comes back.
CREATE TABLE #Leg (RotationId int, LegNo tinyint, Direction char(1), Sched datetime2(0), DelayMin int, Rnd int, VisitNo int NULL, VisitStart datetime2(0) NULL, VisitGroup varchar(4) NULL,
    StandId int NULL, GateId int NULL, BeltId int NULL, DeskFrom int NULL, DeskTo int NULL, PRIMARY KEY (RotationId, LegNo));
INSERT INTO #Leg (RotationId, LegNo, Direction, Sched, DelayMin, Rnd)
SELECT r.RotationId, 1, CASE WHEN s.IsBase = 1 THEN 'D' ELSE 'A' END, r.T0, d1.D, x.Rnd
FROM #Rot r JOIN #Slot s ON s.SlotId = r.SlotId JOIN #Day dd ON dd.d = r.d
CROSS APPLY (SELECT dbo.R(r.RotationId, 30) AS Rnd) x
CROSS APPLY (SELECT x.Rnd % 10000 + CASE WHEN dd.BadWx = 1 THEN 1500 ELSE 0 END AS U, x.Rnd / 10000 AS V) u
CROSS APPLY (SELECT CASE WHEN s.IsBase = 1
                         THEN CASE WHEN u.U < 6600 THEN -3 + u.V % 8 WHEN u.U < 8700 THEN 5 + u.V % 11 WHEN u.U < 9700 THEN 16 + u.V % 45 WHEN u.U < 9970 THEN 61 + u.V % 120 ELSE 181 + u.V % 240 END
                         ELSE CASE WHEN u.U < 6200 THEN -15 + u.V % 21 WHEN u.U < 8600 THEN 6 + u.V % 10 WHEN u.U < 9680 THEN 16 + u.V % 45 WHEN u.U < 9965 THEN 61 + u.V % 120 ELSE 181 + u.V % 300 END END AS D) d1;
INSERT INTO #Leg (RotationId, LegNo, Direction, Sched, DelayMin, Rnd)
SELECT r.RotationId, 2, CASE WHEN s.IsBase = 1 THEN 'A' ELSE 'D' END,
       DATEADD(minute, CASE WHEN s.IsBase = 1 THEN 2 * s.BlockMinutes + 60 ELSE s.TurnMinutes END, r.T0),
       CASE WHEN s.IsBase = 1 THEN CASE WHEN l1.DelayMin > 15 THEN l1.DelayMin * 7 / 10 ELSE 0 END + d2.Own
            ELSE CASE WHEN l1.DelayMin - 20 > d2.Own THEN l1.DelayMin - 20 ELSE d2.Own END END, x.Rnd
FROM #Rot r JOIN #Slot s ON s.SlotId = r.SlotId JOIN #Day dd ON dd.d = r.d JOIN #Leg l1 ON l1.RotationId = r.RotationId AND l1.LegNo = 1
CROSS APPLY (SELECT dbo.R(r.RotationId, 31) AS Rnd) x
CROSS APPLY (SELECT x.Rnd % 10000 + CASE WHEN dd.BadWx = 1 THEN 1000 ELSE 0 END AS U, x.Rnd / 10000 AS V) u
CROSS APPLY (SELECT CASE WHEN s.IsBase = 1
                         THEN CASE WHEN u.U < 7000 THEN -15 + u.V % 21 WHEN u.U < 9000 THEN 6 + u.V % 10 WHEN u.U < 9800 THEN 16 + u.V % 45 WHEN u.U < 9975 THEN 61 + u.V % 120 ELSE 181 + u.V % 300 END
                         ELSE CASE WHEN u.U < 6600 THEN -3 + u.V % 8 WHEN u.U < 8700 THEN 5 + u.V % 11 WHEN u.U < 9700 THEN 16 + u.V % 45 WHEN u.U < 9970 THEN 61 + u.V % 120 ELSE 181 + u.V % 240 END END AS Own) d2;

-- Stands: every ground visit takes the next stand of its group in time order, so stands are not double-booked.
UPDATE l SET VisitNo = CASE WHEN s.IsBase = 1 THEN l.LegNo ELSE 1 END,
             VisitStart = CASE WHEN s.IsBase = 1 AND l.Direction = 'D' THEN DATEADD(minute, -70, l.Sched) WHEN s.IsBase = 1 THEN l.Sched ELSE r.T0 END,
             VisitGroup = CASE WHEN s.IsCargo = 1 THEN 'CGO'
                               WHEN s.IsWide = 1 THEN CASE WHEN dbo.R(r.RotationId * 2 + CASE WHEN s.IsBase = 1 THEN l.LegNo ELSE 1 END, 32) % 100 < 80 THEN 'T1W' ELSE 'RMW' END
                               WHEN al.TerminalId = 1 THEN CASE WHEN dbo.R(r.RotationId * 2 + CASE WHEN s.IsBase = 1 THEN l.LegNo ELSE 1 END, 32) % 100 < 70 THEN 'T1N' ELSE 'RMN' END
                               ELSE CASE WHEN dbo.R(r.RotationId * 2 + CASE WHEN s.IsBase = 1 THEN l.LegNo ELSE 1 END, 32) % 100 < 65 THEN 'T2N' ELSE 'RMN' END END
FROM #Leg l JOIN #Rot r ON r.RotationId = l.RotationId JOIN #Slot s ON s.SlotId = r.SlotId JOIN ref.Airline al ON al.AirlineId = s.AirlineId;
WITH v AS (SELECT RotationId, VisitNo, VisitGroup, MIN(VisitStart) AS VisitStart FROM #Leg GROUP BY RotationId, VisitNo, VisitGroup),
     o AS (SELECT v.RotationId, v.VisitNo, v.VisitGroup, ROW_NUMBER() OVER (PARTITION BY v.VisitGroup ORDER BY v.VisitStart, v.RotationId) - 1 AS Idx FROM v),
     st AS (SELECT StandId, StandGroup, ROW_NUMBER() OVER (PARTITION BY StandGroup ORDER BY StandId) - 1 AS Idx, COUNT(*) OVER (PARTITION BY StandGroup) AS Cnt FROM ref.Stand)
UPDATE l SET StandId = st.StandId
FROM #Leg l JOIN o ON o.RotationId = l.RotationId AND o.VisitNo = l.VisitNo JOIN st ON st.StandGroup = o.VisitGroup AND st.Idx = o.Idx % st.Cnt;
-- Gates (the stand's own gate, or a bus gate for remote stands), baggage belts and check-in desks for passenger flights.
WITH o AS (SELECT l.RotationId, l.LegNo, al.TerminalId, ROW_NUMBER() OVER (PARTITION BY al.TerminalId ORDER BY l.Sched, l.RotationId) - 1 AS Idx
           FROM #Leg l JOIN #Rot r ON r.RotationId = l.RotationId JOIN #Slot s ON s.SlotId = r.SlotId JOIN ref.Airline al ON al.AirlineId = s.AirlineId
           WHERE s.IsCargo = 0 AND l.VisitGroup IN ('RMN', 'RMW')),
     bg AS (SELECT GateId, TerminalId, ROW_NUMBER() OVER (PARTITION BY TerminalId ORDER BY GateId) - 1 AS Idx, COUNT(*) OVER (PARTITION BY TerminalId) AS Cnt FROM ref.Gate WHERE GateType = 'Bus')
UPDATE l SET GateId = bg.GateId FROM #Leg l JOIN o ON o.RotationId = l.RotationId AND o.LegNo = l.LegNo JOIN bg ON bg.TerminalId = o.TerminalId AND bg.Idx = o.Idx % bg.Cnt;
UPDATE l SET GateId = g.GateId FROM #Leg l JOIN ref.Gate g ON g.StandId = l.StandId;
WITH o AS (SELECT l.RotationId, l.LegNo, al.TerminalId, ROW_NUMBER() OVER (PARTITION BY al.TerminalId ORDER BY l.Sched, l.RotationId) - 1 AS Idx
           FROM #Leg l JOIN #Rot r ON r.RotationId = l.RotationId JOIN #Slot s ON s.SlotId = r.SlotId JOIN ref.Airline al ON al.AirlineId = s.AirlineId
           WHERE s.IsCargo = 0 AND l.Direction = 'A'),
     b AS (SELECT BaggageBeltId, TerminalId, ROW_NUMBER() OVER (PARTITION BY TerminalId ORDER BY BaggageBeltId) - 1 AS Idx, COUNT(*) OVER (PARTITION BY TerminalId) AS Cnt FROM ref.BaggageBelt)
UPDATE l SET BeltId = b.BaggageBeltId FROM #Leg l JOIN o ON o.RotationId = l.RotationId AND o.LegNo = l.LegNo JOIN b ON b.TerminalId = o.TerminalId AND b.Idx = o.Idx % b.Cnt;
WITH o AS (SELECT l.RotationId, l.LegNo, al.TerminalId, s.IsWide, ROW_NUMBER() OVER (PARTITION BY al.TerminalId ORDER BY l.Sched, l.RotationId) - 1 AS Idx
           FROM #Leg l JOIN #Rot r ON r.RotationId = l.RotationId JOIN #Slot s ON s.SlotId = r.SlotId JOIN ref.Airline al ON al.AirlineId = s.AirlineId
           WHERE s.IsCargo = 0 AND l.Direction = 'D')
UPDATE l SET DeskFrom = f.DeskFrom, DeskTo = f.DeskFrom + CASE WHEN o.IsWide = 1 THEN 7 ELSE 3 END
FROM #Leg l JOIN o ON o.RotationId = l.RotationId AND o.LegNo = l.LegNo
CROSS APPLY (SELECT CASE WHEN o.TerminalId = 1 THEN 1 + o.Idx % 11 * 8 ELSE 97 + o.Idx % 5 * 8 END AS DeskFrom) f;

INSERT INTO ops.Flight WITH (TABLOCK) (RotationId, ScheduledServiceId, FlightDate, AirlineId, FlightNumber, Direction, RouteAirportId, ServiceTypeCode, FlightNature, AircraftTypeId, AircraftId,
    TerminalId, StandId, GateId, BaggageBeltId, CheckInDeskFromId, CheckInDeskToId, RunwayId, GroundHandlerId, ScheduledDateTime, EstimatedDateTime, ActualRunwayDateTime,
    ActualBlockDateTime, DelayMinutes, FlightStatus, CancellationReason, DivertedToAirportId)
SELECT r.RotationId, s.SlotId, CAST(l.Sched AS date), s.AirlineId,
       CONCAT(al.IataCode, s.FlightNo + CASE WHEN (s.IsBase = 1 AND l.Direction = 'A') OR (s.IsBase = 0 AND l.Direction = 'D') THEN 1 ELSE 0 END), l.Direction, s.AirportId,
       CASE WHEN s.IsCargo = 1 THEN 'F' ELSE 'J' END, CASE WHEN s.IsCargo = 1 THEN 'Cargo' ELSE 'Passenger' END, s.AircraftTypeId, r.AircraftId,
       CASE WHEN s.IsCargo = 1 THEN 3 ELSE al.TerminalId END, l.StandId, l.GateId, l.BeltId, l.DeskFrom, l.DeskTo,
       CASE WHEN dbo.R(DATEDIFF(day, @Start, CAST(l.Sched AS date)), 33) % 100 < 72 THEN CASE WHEN l.Direction = 'A' THEN 2 ELSE 4 END ELSE CASE WHEN l.Direction = 'A' THEN 1 ELSE 3 END END,
       al.GroundHandlerId, l.Sched,
       CASE WHEN st.Status IN ('Cancelled', 'Diverted') THEN NULL WHEN st.Status = 'Scheduled' THEN l.Sched ELSE DATEADD(minute, l.DelayMin, l.Sched) END,
       CASE WHEN st.Status IN ('Arrived', 'Departed') THEN DATEADD(minute, l.DelayMin + CASE WHEN l.Direction = 'A' THEN -(5 + l.Rnd / 3 % 6) ELSE 9 + l.Rnd / 3 % 10 END, l.Sched) END,
       CASE WHEN st.Status IN ('Arrived', 'Departed') THEN DATEADD(minute, l.DelayMin, l.Sched) END,
       CASE WHEN st.Status IN ('Arrived', 'Departed') THEN l.DelayMin END, st.Status,
       CASE WHEN st.Status = 'Cancelled' THEN CASE WHEN r.Diverted = 1 THEN N'Inbound aircraft diverted' WHEN dd.BadWx = 1 AND r.Rnd / 50 % 3 > 0 THEN N'Weather'
                                                 ELSE CHOOSE(r.Rnd / 50 % 5 + 1, N'Technical', N'Operational', N'Commercial', N'Crew', N'Airspace restriction') END END,
       CASE WHEN st.Status = 'Diverted' THEN (SELECT AirportId FROM ref.Airport WHERE IataCode = CHOOSE(r.Rnd / 70 % 3 + 1, 'DOH', 'MCT', 'BAH')) END
FROM #Leg l JOIN #Rot r ON r.RotationId = l.RotationId JOIN #Slot s ON s.SlotId = r.SlotId JOIN ref.Airline al ON al.AirlineId = s.AirlineId JOIN #Day dd ON dd.d = r.d
CROSS APPLY (SELECT CASE WHEN r.Cancelled = 1 THEN 'Cancelled'
                         WHEN r.Diverted = 1 THEN CASE WHEN l.Direction = 'A' AND l.LegNo = 1 THEN 'Diverted' WHEN l.LegNo = 2 THEN 'Cancelled' ELSE CASE WHEN l.Direction = 'A' THEN 'Arrived' ELSE 'Departed' END END
                         WHEN DATEADD(minute, l.DelayMin, l.Sched) > @Now THEN 'Scheduled'
                         WHEN l.Direction = 'A' THEN 'Arrived' ELSE 'Departed' END AS Status) st
ORDER BY l.Sched, r.RotationId, l.LegNo;
UPDATE f SET LinkedFlightId = o.FlightId FROM ops.Flight f JOIN ops.Flight o ON o.RotationId = f.RotationId AND o.FlightId <> f.FlightId;
CREATE INDEX IX_Flight_FlightDate ON ops.Flight (FlightDate) INCLUDE (AirlineId, Direction, FlightStatus, DelayMinutes);
CREATE INDEX IX_Flight_Scheduled ON ops.Flight (ScheduledDateTime);
CREATE INDEX IX_Flight_Airline ON ops.Flight (AirlineId, FlightDate);
CREATE INDEX IX_Flight_Rotation ON ops.Flight (RotationId);

-- --------------------------------------------------------------------------------------------------------- loads
INSERT INTO ops.FlightLoad WITH (TABLOCK) (FlightId, SeatsAvailable, PassengersTotal, PassengersBusiness, PassengersEconomy, PassengersChildren, PassengersInfants, PassengersTransfer,
    PassengersReducedMobility, LoadFactorPercent, BagsCount, BagsWeightKg, CargoKg, MailKg, CrewCount)
SELECT p.FlightId, p.Seats, p.Pax, p.Biz, p.Pax - p.Biz, p.Pax * (5 + p.Rnd / 11 % 8) / 100, p.Pax * (p.Rnd / 13 % 4) / 100,
       p.Pax * CASE WHEN p.IsBase = 1 THEN 20 + p.Rnd / 17 % 18 ELSE 2 + p.Rnd / 17 % 6 END / 100, p.Pax * (p.Rnd / 19 % 3) / 100,
       CASE WHEN p.Seats > 0 THEN CAST(100.0 * p.Pax / p.Seats AS decimal(5,1)) END, b.Bags, b.Bags * (15 + p.Rnd / 23 % 6),
       CASE WHEN p.IsFreighter = 1 THEN p.CargoCap / 100 * (55 + p.Rnd / 29 % 41) ELSE p.CargoCap / 100 * (15 + p.Rnd / 29 % 70) END,
       CASE WHEN p.Rnd / 31 % 4 = 0 THEN 20 + p.Rnd / 37 % 600 ELSE 0 END, CASE WHEN p.IsFreighter = 1 THEN 3 + p.Rnd / 41 % 2 WHEN p.BodyType = 'Wide' THEN 12 + p.Rnd / 41 % 4 ELSE 6 + p.Rnd / 41 % 2 END
FROM (
    SELECT f.FlightId, t.Seats, t.CargoCapacityKg AS CargoCap, t.IsFreighter, t.BodyType, al.IsBaseCarrier AS IsBase, x.Rnd, pax.Pax,
           CASE WHEN al.AirlineType = 'Full service' AND t.Seats > 0 THEN pax.Pax * CASE WHEN t.BodyType = 'Wide' THEN 9 + x.Rnd / 7 % 5 ELSE 4 + x.Rnd / 7 % 4 END / 100 ELSE 0 END AS Biz
    FROM ops.Flight f JOIN ref.AircraftType t ON t.AircraftTypeId = f.AircraftTypeId JOIN ref.Airline al ON al.AirlineId = f.AirlineId
    CROSS APPLY (SELECT dbo.R(f.FlightId, 40) AS Rnd) x
    CROSS APPLY (SELECT CAST(t.Seats * CASE WHEN lf.V > 100 THEN 100 ELSE lf.V END / 100 AS int) AS Pax
                 FROM (SELECT 62 + CASE WHEN al.AirlineType = 'Low cost' THEN 8 ELSE 0 END + CHOOSE(MONTH(f.FlightDate), 6, 2, 4, 5, 0, 7, 12, 11, 1, 3, 2, 10)
                              + YEAR(f.FlightDate) - 2023 + x.Rnd % 25 AS V) lf) pax
    WHERE f.FlightStatus IN ('Arrived', 'Departed')
) p CROSS APPLY (SELECT p.Pax * (95 + p.Rnd / 43 % 30) / 100 AS Bags) b;

-- -------------------------------------------------------------------------------------------------------- delays
INSERT INTO ops.FlightDelay (FlightId, DelayCodeId, DelayMinutes, IsPrimary)
SELECT q.FlightId, q.DelayCodeId, q.Minutes, q.IsPrimary
FROM (
    SELECT f.FlightId, f.ScheduledDateTime, n.value AS Part,
           CASE WHEN n.value = 1 THEN c1.Code ELSE c2.Code END AS DelayCodeId,
           CASE WHEN x.Two = 0 THEN f.DelayMinutes WHEN n.value = 1 THEN f.DelayMinutes * 65 / 100 ELSE f.DelayMinutes - f.DelayMinutes * 65 / 100 END AS Minutes,
           CASE WHEN n.value = 1 THEN 1 ELSE 0 END AS IsPrimary, x.Two
    FROM ops.Flight f JOIN ops.DailyWeather w ON w.WeatherDate = f.FlightDate
    CROSS APPLY (SELECT dbo.R(f.FlightId, 50) AS Rnd) r
    CROSS APPLY (SELECT CASE WHEN f.DelayMinutes > 40 AND r.Rnd % 100 < 45 THEN 1 ELSE 0 END AS Two) x
    CROSS APPLY (SELECT CASE WHEN w.Condition IN ('Fog', 'Dust storm', 'Thunderstorm') AND r.Rnd / 100 % 100 < 55 THEN CASE WHEN f.Direction = 'D' THEN 14 ELSE 15 END
                             WHEN f.Direction = 'D' AND r.Rnd / 100 % 100 < 38 THEN 22
                             ELSE CHOOSE(r.Rnd / 10000 % 30 + 1, 1, 2, 2, 3, 4, 4, 5, 6, 7, 8, 9, 9, 9, 10, 11, 12, 12, 13, 16, 16, 16, 17, 17, 18, 19, 20, 20, 21, 23, 24) END AS Code) c1
    CROSS APPLY (SELECT CHOOSE(r.Rnd / 300000 % 12 + 1, 2, 4, 7, 9, 12, 16, 17, 20, 21, 22, 22, 23) AS Code) c2
    CROSS JOIN GENERATE_SERIES(1, 2) n
    WHERE f.DelayMinutes > 15 AND (n.value = 1 OR x.Two = 1)
) q ORDER BY q.ScheduledDateTime, q.FlightId, q.Part;

-- --------------------------------------------------------------------------------------------------- code-shares
INSERT INTO ops.FlightCodeShare (FlightId, MarketingAirlineId, MarketingFlightNumber)
SELECT f.FlightId, m.AirlineId, CONCAT(m.IataCode, 4000 + dbo.R(f.ScheduledServiceId * 10 + n.value, 60) % 4000 + CASE WHEN f.Direction = 'D' THEN 1 ELSE 0 END)
FROM ops.Flight f JOIN ref.Airline al ON al.AirlineId = f.AirlineId AND al.AirlineType = 'Full service'
CROSS JOIN GENERATE_SERIES(1, 2) n
JOIN (SELECT AirlineId, IataCode, ROW_NUMBER() OVER (ORDER BY AirlineId) - 1 AS Idx, COUNT(*) OVER () AS Cnt FROM ref.Airline WHERE AirlineType = 'Full service') m
  ON m.Idx = dbo.R(f.ScheduledServiceId * 10 + n.value, 61) % m.Cnt AND m.AirlineId <> f.AirlineId
WHERE f.FlightNature = 'Passenger' AND dbo.R(f.ScheduledServiceId, 62) % 100 < 30 AND n.value <= 1 + dbo.R(f.ScheduledServiceId, 63) % 2
ORDER BY f.FlightId, n.value;

-- ---------------------------------------------------------------------------------------------------- milestones
INSERT INTO ops.FlightMilestone WITH (TABLOCK) (FlightId, MilestoneCode, PlannedDateTime, ActualDateTime)
SELECT f.FlightId, m.Code, DATEADD(minute, m.PlanOffset, f.ScheduledDateTime),
       CASE WHEN f.FlightStatus IN ('Arrived', 'Departed') THEN DATEADD(minute, m.PlanOffset + m.Shift, f.ScheduledDateTime) END
FROM ops.Flight f
CROSS APPLY (SELECT dbo.R(f.FlightId, 70) AS Rnd, ISNULL(f.DelayMinutes, 0) AS D, DATEDIFF(minute, f.ActualBlockDateTime, f.ActualRunwayDateTime) AS Taxi) x
CROSS APPLY (VALUES
    ('A', 0, 1, 'ALDT', -7, x.D + 7 + ISNULL(x.Taxi, 0)), ('A', 0, 2, 'AIBT', 0, x.D),
    ('A', 1, 3, 'FBAG', 15, x.D + x.Rnd % 14 - 3), ('A', 1, 4, 'LBAG', 35, x.D + x.Rnd / 100 % 25 - 5),
    ('D', 1, 1, 'CKIO', -180, 0), ('D', 1, 2, 'CKIC', -60, CASE WHEN x.D > 30 THEN x.D / 2 ELSE 0 END),
    ('D', 1, 3, 'BRDS', -40, CASE WHEN x.D > 5 THEN x.D - 5 ELSE 0 END + x.Rnd % 6), ('D', 1, 4, 'GCLS', -15, CASE WHEN x.D > 0 THEN x.D ELSE 0 END),
    ('D', 0, 5, 'AOBT', 0, x.D), ('D', 0, 6, 'ATOT', 12, x.D + ISNULL(x.Taxi, 12) - 12)) m (Dir, PaxOnly, Ord, Code, PlanOffset, Shift)
WHERE m.Dir = f.Direction AND (m.PaxOnly = 0 OR f.FlightNature = 'Passenger') AND f.FlightStatus NOT IN ('Cancelled', 'Diverted')
ORDER BY f.FlightId, m.Ord;
CREATE INDEX IX_FlightMilestone_Flight ON ops.FlightMilestone (FlightId, MilestoneCode);
CREATE INDEX IX_FlightDelay_Flight ON ops.FlightDelay (FlightId);

-- ------------------------------------------------------------------------------------------------------- summary
SELECT 'Flights' AS Item, COUNT(*) AS Rows_ FROM ops.Flight
UNION ALL SELECT 'Arrivals', COUNT(*) FROM ops.Flight WHERE Direction = 'A'
UNION ALL SELECT 'Departures', COUNT(*) FROM ops.Flight WHERE Direction = 'D'
UNION ALL SELECT 'Cargo flights', COUNT(*) FROM ops.Flight WHERE FlightNature = 'Cargo'
UNION ALL SELECT 'Airlines operating', COUNT(DISTINCT AirlineId) FROM ops.Flight
UNION ALL SELECT 'Destinations', COUNT(DISTINCT RouteAirportId) FROM ops.Flight
UNION ALL SELECT 'Flight loads', COUNT(*) FROM ops.FlightLoad
UNION ALL SELECT 'Delay records', COUNT(*) FROM ops.FlightDelay
UNION ALL SELECT 'Milestones', COUNT(*) FROM ops.FlightMilestone
UNION ALL SELECT 'Code-shares', COUNT(*) FROM ops.FlightCodeShare;
GO
