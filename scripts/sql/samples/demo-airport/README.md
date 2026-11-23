# Demo Airport — four sample source databases

A fictional international airport, **Demo Airport (DMO)**, with **Demo Airlines (XD)** as its base carrier and 58 real airlines operating to 79 real airports. Four separate SQL Server databases cover the systems an airport normally runs. The data starts on 1 January 2023 and ends on the day the scripts are run.

Everything is invented: the airport, its base carrier, people, flight numbers, registrations, vendors and every figure. Other airlines, airports and equipment manufacturers are real names used for illustration only.

## How to create them

Run the scripts **in this order**, as a sysadmin, in SSMS or `sqlcmd`, on SQL Server 2022 (Express is enough; they use `GENERATE_SERIES` and need compatibility level 160):

| # | Script | Database | Depends on | Time (approx.) |
|---|---|---|---|---|
| 1 | `01-create-demoairport-hcm.sql` | `DemoAirport_HCM` | — | 4 min |
| 2 | `02-create-demoairport-aodb.sql` | `DemoAirport_AODB` | — | 2 min |
| 3 | `03-create-demoairport-assets.sql` | `DemoAirport_Assets` | HCM | 1 min |
| 4 | `04-create-demoairport-rms.sql` | `DemoAirport_RMS` | HCM, AODB | 3 min |
| 5 | `05-check-consistency.sql` | — | all four | every line must show `OK` |

```bash
sqlcmd -S localhost -E -C -b -i 01-create-demoairport-hcm.sql
```

- Each script creates its database if it does not exist, then drops and recreates every table. **Re-running resets the data.**
- Each script creates the read-only login `demoairport_reader` (`db_datareader` in all four databases). That is the login to register in Wabi Insight. Its sample password is in the scripts: **change it before using the databases outside a sandbox.**
- The order matters because the systems share people and flights, as they do in a real airport: maintenance technicians and operational staff are HCM employees (work is only given to someone employed, and on shift, that day); RMS plans the flights and stands of the AODB; RMS equipment codes are asset tags of the asset register.
- The data is generated inside the scripts, so the files are small. It is deterministic for a given run date.
- All times are airport local time (UTC+4).

## What is in each database

### `DemoAirport_HCM` — Human Capital Management (27 tables)

**1,864 employees**: 1,268 in service; 596 left (352 resigned, 96 terminated, 84 end of contract, 52 retired, 8 absconded, 4 deceased). 1,180 were in service on 1 January 2023 and 684 joined since.

| Schema | Tables | Rows (approx.) |
|---|---|---|
| `org` | Country, Grade, Department (14, EN/AR names), Section (48), Position (128) | |
| `hr` | Employee, EmploymentHistory, Separation, Dependent, EmployeeDocument (passport, visa, Emirates ID, airport pass, airside permit, with expiry dates), DisciplinaryAction | 1,864 / 2,900 / 596 / 2,400 / 7,600 / 380 |
| `pay` | EmployeeCompensation (yearly increments), PayComponent, PayrollRun (one per month), Payslip, PayslipLine | 5,700 / 10 / 46 / 59,000 / 306,000 |
| `ta` | Shift, PublicHoliday, LeaveType, LeaveRequest, LeaveBalance, Attendance (daily, shift and office patterns) | 13,000 leave requests / 1,216,000 attendance |
| `talent` | PerformanceReview, TrainingCourse, TrainingRecord (with expiry), JobRequisition, JobApplication | 3,600 / 22 / 28,000 / 730 / 7,000 |

Overtime and unpaid absence on a payslip come from that month's attendance; the March bonus follows the previous year's performance rating; end-of-service gratuity is paid in the month of leaving.

### `DemoAirport_AODB` — Airport Operational Database (22 tables)

**Exactly 255,336 flights**: 127,668 arrivals and 127,668 departures, passenger and cargo (about 18,700 cargo flights), 59 airlines, 79 destinations.

| Schema | Tables | Rows (approx.) |
|---|---|---|
| `ref` | Country, Airport, Airline, AircraftType, Aircraft (registrations), Terminal (T1, T2, cargo), Stand (66), Gate (42), CheckInDesk (144), BaggageBelt (12), Runway, GroundHandler, ServiceType, DelayCode (IATA), MilestoneType | |
| `ops` | ScheduledService (seasonal schedule with days of operation), Flight, FlightLoad (passengers by class, transfer, baggage, cargo, mail), FlightDelay, FlightCodeShare, FlightMilestone (landing, in-block, first/last bag, check-in, boarding, off-block, take-off), DailyWeather | 164 / 255,336 / 252,000 / 59,000 / 101,000 / 1,204,000 / 1,400 |

Every flight is linked to its other leg (`LinkedFlightId`): other airlines arrive and turn round, the base carrier departs and returns. Traffic grows from 2023 to 2026 with summer and December peaks. About 81 % of flights are on time (within 15 minutes); bad-weather days (fog, dust, thunderstorm) have more delays and cancellations. A few flights dated the day after the run date are return legs still to operate (`Scheduled`).

### `DemoAirport_Assets` — Enterprise Asset Management (20 tables)

**18,380 assets** in 60 categories under 12 families, worth about AED 2.75 billion.

| Schema | Tables | Rows (approx.) |
|---|---|---|
| `ref` | AssetCategory, Location, Manufacturer, Vendor, FailureCode, MaintenanceTeam | |
| `eam` | Asset (tag, cost, life, criticality, condition, status), MeterReading, Inspection (statutory) | 18,380 / 107,000 / 9,000 |
| `mnt` | Technician (from HCM), PMPlan, WorkOrder (preventive, corrective, emergency), WorkOrderLabour, WorkOrderPart, ServiceContract | 279 / 18,380 / 209,000 / 245,000 / 88,000 / 120 |
| `inv` | Warehouse, SparePart, PurchaseOrder, PurchaseOrderLine | 3 / 1,200 / 9,500 / 24,000 |
| `fin` | AssetDepreciation (monthly, straight line) | 701,000 |

Older assets fail more often; response-time targets depend on priority; work-order cost is labour plus parts plus contractor call-out.

### `DemoAirport_RMS` — Resource Management System (22 tables)

| Schema | Tables | Rows (approx.) |
|---|---|---|
| `ref` | Terminal, Airline, AircraftType, ChangeReason, StaffGroup, EquipmentType, TaskType | |
| `res` | Stand, Gate, CheckInDesk, BaggageBelt, Equipment (asset tags of the GSE fleet) | |
| `plan` | FlightLeg (the AODB flights), StandAllocation, GateAllocation, CheckInAllocation (one row per desk), BeltAllocation, AllocationChange, ResourceClosure | 255,336 / 194,000 / 237,000 / 540,000 / 118,000 / 35,000 / 12,000 |
| `staff` | Staff (operational departments of HCM), ShiftAssignment (their HCM attendance) | 791 / 504,000 |
| `ops` | Task (marshalling, baggage, check-in, boarding, load control, pushback, cargo, special assistance) | 1,494,000 |

Allocations keep the planned and the actual resource, so changes, occupancy and conflicts can be analysed. A closure never overlaps an allocation of the same resource.

## After creating them

Register the four databases as applications of the **Demo Airport** organization in Wabi Insight (Applications > add, connection with `demoairport_reader`), then discover and approve the catalogue. Foreign keys are declared on every table, so relationships are discovered automatically.
