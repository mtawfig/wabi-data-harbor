# Sample source databases and the WabiInsightDB seed

Three realistic, fictional source databases for **Gulf Aviation Services** (code `GAS`), an airport-services company at Sharjah International Airport, plus a script that registers them in the Wabi Insight configuration database with four sample users. All data covers calendar year 2025 and is generated deterministically: the same script always produces the same rows.

| # | File | Creates | Size |
|---|---|---|---|
| 1 | `01-create-flightoperationsdb.sql` | `FlightOperationsDB`, logins `flightops_owner` (db_owner) and `flightops_reader` (db_datareader) | 12 tables, 132,096 flights, ≈ 45 s |
| 2 | `02-create-humanresourcesdb.sql` | `HumanResourcesDB`, logins `hr_owner` and `hr_reader` | 18 tables, 599 employees, 136,387 attendance days, 6,405 payslips, ≈ 50 s |
| 3 | `03-create-procurementstoresdb.sql` | `ProcurementStoresDB`, logins `procurement_owner` and `procurement_reader` | 18 tables, 1,800 requisitions, 1,657 purchase orders, 4,200 store issues, ≈ 5 s |
| 4 | `04-seed-wabiinsightdb.sql` | organization, departments, roles, users, applications, catalogue and permissions in `WabiInsightDB` | ≈ 1 s |

Each of 01–03 creates the database if missing, creates its owner and read-only logins and users, switches to it with `USE`, drops and recreates its tables, loads the data, and prints a summary. Re-running one rebuilds that database from scratch. Script 04 adds only what is missing, so it is safe to run again.

## Contents

**FlightOperationsDB (flight operations):** `Countries`, `Cities`, `Airports` (69, SHJ is home), `Airlines` (23: Air Arabia based at SHJ plus visiting carriers), `AircraftTypes`, `Aircraft` (82 registrations), `Terminals`, `Gates`, `DelayReasons` (IATA codes), `FlightSchedules` (the flights master: 502 scheduled services with operating days, seasons and validity), `Calendar`, `Flights` (every departure and arrival in 2025: scheduled and actual times, delays and causes, status, aircraft, seats, passengers by type, load factor, baggage, cargo, terminal and gate). Summer, year-end and Eid peaks, about 30 % delayed, 1 % cancelled.

**HumanResourcesDB (HR, personnel, payroll, attendance):** `Countries`, `Departments` (10, with cost centres and managers), `Sections` (28), `JobGrades` (G1–G10), `Positions`, `Shifts`, `Employees` (599, hires and leavers during the year), `EmployeeContracts` (April 2025 increments), `EmployeeBankAccounts` (WPS), `PerformanceReviews`, `LeaveTypes`, `LeaveRequests`, `AttendanceRecords` (daily, office and shift rosters, late, overtime, absence, leave), `PayElements`, `PayrollPeriods`, `Payslips` (prorated pay, overtime at 125 %, absence deduction, pension for UAE nationals, advance repayments) and `PayslipLines`. Monthly payroll ≈ AED 5.4–5.8 million.

**ProcurementStoresDB (purchasing and stores):** `CostCenters`, `ItemCategories`, `UnitsOfMeasure`, `Warehouses` (5 stores), `Suppliers` (52, rated A/B/C; lateness follows the rating), `Items` (159), `PurchaseRequisitions` and lines (Routine/Urgent/AOG), `PurchaseOrders` and lines (5 % VAT), `GoodsReceipts` and lines (partial deliveries, rejections, days late), `SupplierInvoices`, `StoreIssues` and lines, `StockMovements` (ledger) and `StockBalances` (31 December 2025).

Department codes, section codes and employee numbers are shared by all three databases and by the Wabi Insight organization structure, so row scopes (`DEPARTMENT`, `SECTION`, `SELF`) work on real columns.

## Run order

1. Migrate the configuration database: `dotnet WabiInsight.Api.dll migrate` (or `dotnet run --project src/WabiInsight.Api -- migrate`).
2. On the **same** SQL Server instance as `WabiInsightDB`, as a login allowed to create databases and logins (sysadmin in a sandbox):

```bash
SQL="sqlcmd -S <server> -U <admin login> -P <password> -C -b -I"
$SQL -i scripts/sql/samples/01-create-flightoperationsdb.sql
$SQL -i scripts/sql/samples/02-create-humanresourcesdb.sql
$SQL -i scripts/sql/samples/03-create-procurementstoresdb.sql
$SQL -i scripts/sql/samples/04-seed-wabiinsightdb.sql
```

SQL Server Management Studio and Azure Data Studio work too: open each file and execute it. Script 04 reads the departments, the four employees and the table catalogue of the three databases directly, so they must exist first; it stops with a clear message otherwise. If the configuration database has another name, change the `USE [WabiInsightDB]` line at the top of 04.

3. Sign in to Wabi Insight as the organization administrator and, under **Organization Administration → Applications**, for each of *Flight Operations*, *HR, Payroll and Attendance* and *Procurement and Stores*: **Replace connection** with the read-only connection string (04 prints them; use the server address the API reaches), **Test connection** (must report *read-only verified*), then **Approve**. This step cannot be scripted: connection strings are encrypted with the deployment's Data Protection key ring and never stored in plain text.

## Sample users

| Employee | Name | Position | Role in Wabi Insight | What they can see |
|---|---|---|---|---|
| E1001 | Faisal Al Shamsi | IT Manager | Organization Administrator | Manages users, structure, roles, applications and settings; no business data by itself. |
| E1002 | Khalid Al Suwaidi | Chief Executive Officer | Management | Every approved table of the three applications, all rows. |
| E1003 | Arjun Nair | Operations Controller | Operations Analyst | Flight operations and procurement, all rows; only **his own** attendance, leave and payslips (`SELF`). |
| E1004 | Sara Khoury | Payroll Specialist | HR and Payroll Officer | All HR, payroll and attendance rows without the Restricted columns (Emirates ID, passport, IBAN); purchase requisitions and store issues of **her own department** (`DEPARTMENT`). |

Sign-in e-mails are `faisal.alshamsi`, `khalid.alsuwaidi`, `arjun.nair` and `sara.khoury` at `gulfaviation.example` (edit the variables at the top of 04 to use real mailboxes). All four start with the initial password `Wabi#Sample#2026` and **must change it at first sign-in**. This known password is for sandboxes only. On a shared or production server, either change it immediately or, once SMTP is configured, have the administrator reset each user's password so Wabi Insight e-mails them a temporary one (decision H).

Change the SQL login passwords as well (`ALTER LOGIN … WITH PASSWORD`). They are in the scripts and the connection strings above:

| Login | Password in the scripts |
|---|---|
| `flightops_owner` / `flightops_reader` | `FlightOps#Owner#2026` / `FlightOps#Reader#2026` |
| `hr_owner` / `hr_reader` | `HumanRes#Owner#2026` / `HumanRes#Reader#2026` |
| `procurement_owner` / `procurement_reader` | `Procure#Owner#2026` / `Procure#Reader#2026` |

## Questions to try

- *Khalid (Management):* "Monthly passengers and load factor in 2025 by airline", "Payroll cost by department for each month", "Spend by supplier rating and how many deliveries were late".
- *Arjun (Operations Analyst):* "Top 10 routes by delayed departures and their main delay reasons", "AOG requisitions by month and average days to receive", "Show my net pay for each month of 2025" (only his payslips).
- *Sara (HR and Payroll Officer):* "Overtime hours by department and shift", "Absence days per section in Q3", "Purchase requisitions raised by my department" (HR only).

## Regenerating

01–03 are generated by Python 3 (standard library only) from `build_flights.py`, `build_hr.py` and `build_procurement.py`, with helpers in `build_common.py` and the shared organization model in `build_people.py`:

```bash
python3 scripts/sql/samples/build_sample_sql.py
```

04 is written by hand. `SampleDatabaseScriptTests` (integration tests) runs all four scripts against a fresh migrated database next to the test organizations and checks the data, users, catalogue, permissions, idempotency and tenant isolation.
