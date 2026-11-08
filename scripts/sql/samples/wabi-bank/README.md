# Wabi Bank — five sample source databases and the WabiInsightDB seed

**Wabi Bank** (code `WBK`, Arabic بنك وابي) is a fictional UAE retail and commercial bank: currency AED, time zone Asia/Dubai, a Head Office in Dubai and six branches. This folder builds its five systems as five SQL Server databases with calendar-year 2025 data, and registers the bank in Wabi Insight as a second sample organization next to Gulf Aviation Services (`../README.md`). All names, people, customers, banks and accounts are invented.

The systems are **not integrated**. Each keeps its own tables, its own `Users` table and its own copy of what it needs. Identifiers are shared because every system reuses the Personnel master data unchanged:

| Identifier | Example | Owner | Reused by |
|---|---|---|---|
| `DepartmentCode` | `FIN`, `BR-DXB01` | Personnel: a Head Office department or a branch (a branch's department code is its `BranchCode`) | all; Finance cost centres; Core Banking `BranchCode`/`PostingUnitCode` |
| `SectionCode` | `FIN-PRC`, `BR-DXB01-TLR` | Personnel | all; IT assignment groups are the ITD sections |
| `EmployeeNumber` | `W1004` | Personnel (the subject: who a row is about) | all |
| `UserId` | `PMENON` | Personnel (`SystemUsers`, one login per person across systems; service accounts such as `SYS_EOD` have no employee) | all (the actor: who did it) |
| `CIF`, account number | `C100503`, `101000000135` | Core Banking | Digital Banking (profiles, channel transfers posted in Core Banking by `SYS_DIGITAL`) |

Column naming is the same everywhere: actors are `…ByUserId`, subjects are `…EmployeeNumber`, owning units are `DepartmentCode` / `SectionCode` / `BranchCode`. Head Office is a location, not a branch: no customer account is domiciled there.

## Files

| # | File | Creates | Contents | Run time* |
|---|---|---|---|---|
| 1 | `01-create-wabibankpersonneldb.sql` | `WabiBankPersonnelDB`, logins `wbpersonnel_owner` / `wbpersonnel_reader` | 17 tables: 478 employees (438 active at year end), 69 sections, 106,127 attendance days, 1,829 leave requests, 474 system users, 1,461 access rights | ≈ 25 s |
| 2 | `02-create-wabibankcorebankingdb.sql` | `WabiBankCoreBankingDB`, `wbcore_owner` / `wbcore_reader` | 28 tables, 5 views: 5,978 customers, 7,699 accounts, 446,913 postings, 32,732 cheques, 28,882 transfers, 1,096 loans, 84,888 month-end balances | ≈ 85 s |
| 3 | `03-create-wabibankfinancedb.sql` | `WabiBankFinanceDB`, `wbfinance_owner` / `wbfinance_reader` | 18 tables: 90 cost centres, 11,670 budget lines, 1,197 requisitions, 1,101 purchase orders, 990 invoices, 885 payments, 722 assets, 9,571 journal lines | ≈ 10 s |
| 4 | `04-create-wabibankdigitaldb.sql` | `WabiBankDigitalDB`, `wbdigital_owner` / `wbdigital_reader` | 9 tables, 2 views: 3,179 profiles, 5,703 devices, 95,616 sign-ins, 3,781 channel transfers, 3,641 Contact Centre requests | ≈ 15 s |
| 5 | `05-create-wabibankitdb.sql` | `WabiBankITDB`, `wbit_owner` / `wbit_reader` | 15 tables, 1 view: 6,000 service requests (4,102 approvals, 7,278 tasks), 4,500 incidents (33,601 activities), 598 configuration items | ≈ 16 s |
| 6 | `06-seed-wabiinsightdb.sql` | the WBK organization in `WabiInsightDB` | structure, 8 users, 11 roles, 5 applications, catalogue, permissions, 5 Monitoring Rules | ≈ 2 s |
| 7 | `07-check-consistency.sql` | nothing (read-only) | 230 cross-system identifier checks; every one must report 0 | ≈ 5 s |

\* On a 4-core development SQL Server 2022.

Each of 01–05 creates its database if missing, creates its owner (`db_owner`) and read-only (`db_datareader`) logins and users, drops and recreates its tables and views, loads the data, runs its own consistency checks and prints a summary. Re-running one rebuilds that database from scratch; none creates objects in another database. 06 adds only what is missing and refreshes the documentation, so it is safe to run again.

## Organization

Board → CEO → eight divisions, each led by an executive who also heads its main department. Headcount is active staff on 31 December 2025.

| Division | Department / branch | Code | Staff | Sections (staff) |
|---|---|---|---|---|
| CEO Office (CEO) | CEO Office | `CEO` | 12 | Executive Office 4, Strategy and PMO 4, Board Secretariat 3 |
| | Legal | `LGL` | 9 | Legal Advisory 4, Litigation 4 |
| | Compliance | `CMP` | 21 | AML and Sanctions 9, KYC Review 7, Regulatory Compliance 4 |
| | Internal Audit | `IAD` | 10 | Branch and Operations Audit 5, IT Audit 4 |
| Retail Banking (CRBO) | Retail Banking | `RBD` | 16 | Branch Network Management 4, Retail Products 6, Cards 5 |
| | Digital Banking and Contact Centre | `DCC` | 43 | Digital Channels 8, Contact Centre 34 (three shifts) |
| | Deira (Dubai Main) Branch | `BR-DXB01` | 21 | Customer Service 6, Teller and Cash 5, Personal Banking and Sales 5, Branch Operations 4 |
| | Business Bay Branch | `BR-DXB02` | 24 | 7, 7, 5, 4 (same four sections) |
| | Abu Dhabi Main Branch | `BR-AUH01` | 23 | 8, 7, 3, 4 |
| | Al Ain Branch | `BR-AAN01` | 21 | 5, 6, 5, 4 |
| | Sharjah Branch | `BR-SHJ01` | 20 | 6, 4, 5, 4 |
| | Ras Al Khaimah Branch | `BR-RAK01` | 17 | 4, 4, 4, 4 |
| Wholesale Banking (CWBO) | Corporate and SME Banking | `CBD` | 24 | Corporate Relationship 9, SME Banking 8, Trade Finance 6 |
| | Treasury | `TRY` | 11 | Dealing Room 6, ALM and Liquidity 4 |
| Operations and Technology (COO) | Central Operations | `OPS` | 43 | Clearing and Cheques 10, Payments and Remittances 10, Account Services 8, Loan Operations 8, Cash and Vault 6 |
| | Information Technology | `ITD` | 40 | Service Desk 10, Infrastructure 8, Applications Support 10, Network 6, Information Security 5 |
| | Administration and Facilities | `ADM` | 15 | Facilities 6, Physical Security 8 |
| Risk (CRO) | Risk Management | `RSK` | 18 | Credit Risk and Approvals 9, Market and Liquidity Risk 4, Operational Risk 4 |
| Finance (CFO) | Finance | `FIN` | 22 | Financial Control and Reporting 7, Budgeting and Planning 4, Procurement 5, Accounts Payable 5 |
| Human Resources (CHRO) | Human Resources | `HRD` | 15 | Recruitment 4, Personnel Services 6, Learning and Development 4 |
| Marketing (CMO) | Marketing and Communications | `MKT` | 13 | Marketing 5, Customer Experience 4, Corporate Communications 3 |

15 Head Office departments, 6 branches (each with the four sections `-CSV`, `-TLR`, `-SLS`, `-OPS` under a branch manager), 69 sections; 21 department heads and branch managers sit at department level without a section. Branch opening dates run from Deira (2008) to Ras Al Khaimah (2021).

## Contents

**WabiBankPersonnelDB (HRMS, system of record):** `Countries`, `Locations` (Head Office and the six branches), `Divisions`, `Departments` (type HeadOffice or Branch, division, head), `Sections`, `JobGrades` (G1–G10 salary bands), `Positions`, `PublicHolidays`, `Shifts` (Head Office, branch, three Contact Centre shifts, security), `Employees`, `EmployeeMovements` (transfers and promotions), `LeaveTypes`, `LeaveRequests` (submitted and decided by user ids), `AttendanceRecords` (every scheduled day: Present, Late, Absent, On Leave), and the identity directory `Systems`, `SystemUsers` and `UserSystemAccess` (role and approval limit per system, with grant and revoke dates).

**WabiBankCoreBankingDB (CBS):** `Branches`, `Units`, `Users`, `BusinessCalendar`, `Currencies`, `FxRates`, `Products`, `TransactionCodes`, `Customers` (staff, retail, Priority, SME, corporate; relationship managers), `KycDocuments`, `Accounts` (IBAN, opened by / approved by, dormant and reactivated), `FixedDeposits`, `ClearingSessions`, `ChequeReturnReasons`, `ChequeBooks`, `OutwardCheques` and `InwardCheques` (clearing, returns with reason codes), `OutwardTransfers` (maker, checker, AML screening for SWIFT) and `InwardTransfers`, `Transactions` (every posting with `PostingUnitCode`, `PostedByUserId`, `ApprovedByUserId`, running balance, reversals), `TellerTills`, `MonthEndBalances`, `LoanProducts`, `LoanApplications`, `Loans`, `LoanSchedules`, `LoanRepayments`, `MonthEndArrears`. Views: `vw_TransactionsByStaff`, `vw_OutwardChequesByStaff`, `vw_OutwardTransfersByStaff` (the posting user's employee number, department and section, for `SELF` scopes) and the live views `vw_TransactionsLive`, `vw_InwardChequesLive`.

**WabiBankFinanceDB (FIN):** `Users`, `ChartOfAccounts`, `CostCentres` (every department and section), `FiscalPeriods`, `BudgetVersions` (original and revised FY2025), `BudgetLines`, `BudgetTransfers`, `Vendors`, `PurchaseRequisitions` and lines, `PurchaseOrders` and lines (5 % VAT, approval limits before VAT), `GoodsReceipts`, `VendorInvoices` (three-way match), `Payments` (maker and checker), `FixedAssets`, `JournalEntries` and `JournalLines` (payroll interface, depreciation, accruals, payables, core-banking income).

**WabiBankDigitalDB (DIGITAL):** `Branches`, `Users`, `DigitalProfiles` (CIF, username, enrolled at a branch or self-service), `Devices`, `SignIns` (device, channel, IP country, success or failure reason), `Beneficiaries`, `ChannelTransfers` (each completed one posted in Core Banking), `ServiceRequests` (Contact Centre, SLA) and `ProfileActions`. Live views: `vw_SignInsLive`, `vw_ChannelTransfersLive`.

**WabiBankITDB (ITSM, Requests and Incidents only):** `Locations`, `EmployeeDirectory` (the help desk's copy of Personnel), `Users`, `AssignmentGroups` (the ITD sections), `Services`, `ConfigurationItems`, `Categories`, `SlaPolicies`, `BusinessHours`, `ServiceCatalogItems`, `ServiceRequests`, `RequestApprovals`, `RequestTasks`, `Incidents`, `IncidentActivities`. Live view: `vw_IncidentsLive`.

**Live views.** Each `vw_…Live` view returns December 2025 moved forward so that 31 December 2025 is *today* in Asia/Dubai. Monitoring Rules look at the last hours or days, so the planted December cases are always "now".

## Run order

1. Migrate the configuration database: `dotnet WabiInsight.Api.dll migrate`.
2. On the **same** SQL Server instance as `WabiInsightDB`, as a login allowed to create databases and logins (sysadmin in a sandbox):

```bash
SQL="sqlcmd -S <server> -U <admin login> -P <password> -C -b -I"
cd scripts/sql/samples/wabi-bank
$SQL -i 01-create-wabibankpersonneldb.sql
$SQL -i 02-create-wabibankcorebankingdb.sql
$SQL -i 03-create-wabibankfinancedb.sql
$SQL -i 04-create-wabibankdigitaldb.sql
$SQL -i 05-create-wabibankitdb.sql
$SQL -i 07-check-consistency.sql      # optional: every Violations value is 0
$SQL -i 06-seed-wabiinsightdb.sql
```

01 must run first (its codes are the reference for the others); 06 needs all five databases. If the configuration database has another name, change `USE [WabiInsightDB]` at the top of 06.

3. Sign in as Huda Al Marzooqi (W1001) and, under **Organization Administration → Applications**, for each of *Personnel and Attendance*, *Core Banking*, *Finance*, *Digital Banking* and *IT Help Desk*: **Replace connection** with the read-only connection string 06 prints (use the server address the API reaches), **Test connection** (must report *read-only verified*), then **Approve**. Connection strings are encrypted with the deployment's Data Protection key ring, so SQL cannot write them.

## Users

| Employee | Name | Position | Wabi Insight roles | What they see |
|---|---|---|---|---|
| W1001 | Huda Al Marzooqi | Head of IT Security (`ITD-SEC`) | Organization Administrator, Cyber Security | Administers WBK. Digital sign-ins and devices, Personnel system users and access rights, IT access requests (all rows). |
| W1002 | Rashid Al Hammadi | Chief Executive Officer (`CEO`) | Management | Every approved object of the five systems, all rows. |
| W1003 | Omar Haddad | Branch Manager, Deira (`BR-DXB01`) | Branch Manager | Core Banking, Digital Banking and IT Help Desk rows of **their own branch** (`DEPARTMENT` on `BranchCode` / `PostingUnitCode` / `HomeBranchCode` / `DepartmentCode`), reference tables, no Restricted columns. |
| W1004 | Priya Menon | Teller, Deira (`BR-DXB01-TLR`) | Teller | **Their own** postings and cheques (`SELF` through `vw_TransactionsByStaff` and `vw_OutwardChequesByStaff`), transaction codes and products. |
| W1005 | Mariam Al Ketbi | HR officer (`HRD-PSV`) | HR Officer | Personnel and Attendance, all rows, without Restricted columns (Emirates ID, passport). |
| W1006 | Imran Qureshi | Budget controller (`FIN-BGP`) | Budget Controller | Finance, all rows; Personnel structure and employees; no Restricted columns (vendor IBAN, Emirates ID, passport). |
| W1007 | Layla Darwish | Senior AML officer (`CMP-AML`) | Compliance and AML Officer | Core Banking and Digital Banking, all rows including KYC numbers and IBANs; no Personnel. |
| W1008 | Mark Santos | IT Service Desk Manager (`ITD-SD`) | IT Service Desk Manager | IT Help Desk, all rows; the Personnel employee directory and structure without Restricted columns; no banking or finance data. |

Everyone also holds **Employee self-service**: their own IT requests and incidents (`SELF` on `RequestedForEmployeeNumber` / `ReportedByEmployeeNumber`). Sign-in e-mails are the Personnel work e-mails, `firstname.lastname@wabibank.example` (`huda.almarzooqi@…`, `rashid.alhammadi@…`, `omar.haddad@…`, `priya.menon@…`, `mariam.alketbi@…`, `imran.qureshi@…`, `layla.darwish@…`, `mark.santos@…`); set `Email` in `#Users` at the top of 06 for real mailboxes. All eight start with `Wabi#Sample#2026` and **must change it at first sign-in**. This known password is for sandboxes only; on a shared server change it immediately or have the administrator reset each user so Wabi Insight e-mails a temporary one (decision H).

SQL logins (change them with `ALTER LOGIN … WITH PASSWORD` outside a sandbox):

| Logins | Passwords in the scripts |
|---|---|
| `wbpersonnel_owner` / `wbpersonnel_reader` | `WbPersonnel#Owner#2026` / `WbPersonnel#Reader#2026` |
| `wbcore_owner` / `wbcore_reader` | `WbCore#Owner#2026` / `WbCore#Reader#2026` |
| `wbfinance_owner` / `wbfinance_reader` | `WbFinance#Owner#2026` / `WbFinance#Reader#2026` |
| `wbdigital_owner` / `wbdigital_reader` | `WbDigital#Owner#2026` / `WbDigital#Reader#2026` |
| `wbit_owner` / `wbit_reader` | `WbIt#Owner#2026` / `WbIt#Reader#2026` |

## Questions to try

- *Rashid (CEO):* "Deposits and loans by branch at the end of each quarter", "IT department actual spend against the revised budget per quarter", "Headcount by division and grade".
- *Omar (Branch Manager):* "Cash deposits and withdrawals per teller this month", "Cheques returned at my branch by reason", "Open IT incidents reported by my branch" — only Deira rows come back.
- *Priya (Teller):* "My cash deposits and withdrawals per day in December", "My reversed postings" — only their own.
- *Mariam (HR):* "Absence days by department in Q3", "Leave requests pending approval", "Leavers in 2025 and the date their access was revoked".
- *Imran (Budget):* "Budget versus actual by department for Q3", "Purchase orders between AED 90,000 and 100,000 by buyer and vendor".
- *Layla (AML):* "Individual customers with cash deposits of AED 50,000 or more in a day", "Accounts with more than two returned cheques", "Dormant accounts reactivated this year and their transfers".
- *Mark (Service Desk):* "SLA breach rate by assignment group and quarter", "Configuration items with the most incidents in December", "Access-removal requests fulfilled after the employee's last day".

## Planted findings

Each one is found with plain SQL; the five Monitoring Rules seeded by 06 find their cases at once (**Test now**) because they read the live views.

| System | Finding | Where to look |
|---|---|---|
| Personnel | Leaver W1396 Ayesha Hussain (`AHUSSAIN2`, Sharjah teller) left on 24 July; the Core Banking access was revoked on 27 July | `UserSystemAccess.RevokedDate > Employees.TerminationDate` |
| Personnel | Marketing user `NHADDAD2` holds Core Banking *AccountServices*; Recruitment user `ASAYED2` holds Finance *Buyer* (never used) | `UserSystemAccess` joined to `Employees.SectionCode` |
| Core Banking | Structuring: individual customer `C100503` deposits cash just under AED 50,000 on seven December days, then twice a day on 30 and 31 December (AED 57,000 and 58,000 a day) | rule *Cash deposits of AED 55,000 or more in a day (individuals)* |
| Core Banking | Two accounts (`C100509`, `C105301`) with four returned cheques each in December | rule *More than two returned cheques per account in 30 days* |
| Core Banking | Sharjah branch returns 9.1 % of deposited cheques (others 2.5–3 %) | `OutwardCheques` by `DepositBranchCode` |
| Core Banking | Teller `ASHARMA` reverses 224 postings (next: 26) | `Transactions WHERE Status = 'Reversed'` by `ReversedByUserId` |
| Core Banking | Dormant account `201000000203` reactivated on 11 November by `HCHAUDHRY3`, then AED 512,000 in and 495,000 out by SWIFT | `Accounts.ReactivatedByUserId`, `InwardTransfers`, `OutwardTransfers` |
| Core Banking | Abu Dhabi teller `KIYER` posts three cash transactions to their own account (the only staff self-processing in any table) | `Customers.StaffEmployeeNumber` = poster's employee number |
| Core Banking | Two loans approved above the credit officer's AED 250,000 limit (`LA2500003`, `LA2500004`) | `LoanApplications` joined to `Users.ApprovalLimitAed` |
| Core Banking | Auto-loan arrears rise sharply in Q3 (overdue AED 0.25 M in June, 0.65 M in September) | `MonthEndArrears` by `ProductCode` |
| Finance | IT overspends its revised Q3 budget by 18.7 % (licence true-up, incident response, contractors); next department +3.2 % | budget versus `JournalLines` on expense accounts |
| Finance | Split orders: buyer `FKETBI2` issues four orders of AED 92,640–99,180 (before VAT) to vendor V0053 between 20 and 24 June from one requisition, each just under the Procurement Manager's 100,000 limit | `PurchaseOrders` 90,000–100,000 grouped by buyer, vendor, cost centre |
| Finance | Four invoices paid before the goods arrived (`MatchStatus = 'NoReceipt'`) | `Payments` versus first `GoodsReceipts` |
| Digital | Account takeover: Priority customer `anjali00484` signs in from three new devices abroad on 30–31 December, adds a payee at 02:41 and transfers AED 38,500 and 41,200 at night | rule *Same digital profile, several devices in a day* |
| Digital | Credential stuffing: 45 profiles get failed sign-ins from foreign IP addresses within an hour on 6 October | `SignIns WHERE Succeeded = 0` by hour and country |
| IT Help Desk | P1 core-banking outage on 13 May, 10:12–13:13, with eight linked branch incidents; phishing campaign on 17 September with nine linked reports | `Incidents.IsMajorIncident`, `ParentIncidentNumber` |
| IT Help Desk | `ATM-SHJ01-02` fails four times in the last seven days of December | rule *More than three incidents on one configuration item in 7 days* |
| IT Help Desk | Network group (`ITD-NET`) breaches its SLA on 41 % of Q3 incidents (next: 17 %) | `Incidents.SlaBreached` by group and quarter |
| IT Help Desk | The access-removal request for W1396 is fulfilled three days after the last working day | `ServiceRequests` (`ACC-REMOVE`) versus `EmployeeDirectory.TerminationDate` |

The generator docstrings (`build_*.py`) hold the SQL for each finding.

## Monitoring Rules (Phase 14)

| Rule | Object | Owner (runs with their permissions) | Sent to |
|---|---|---|---|
| Same digital profile, several devices in a day (more than two in 24 h, High) | `vw_SignInsLive` | W1001 | Cyber Security |
| Cash deposits of AED 55,000 or more in a day, individuals (High) | `vw_TransactionsLive` | W1007 | Compliance and AML Officer |
| More than two returned cheques per account in 30 days (Medium) | `vw_InwardChequesLive` | W1007 | Compliance and AML Officer |
| More than three incidents on one configuration item in 7 days (Medium) | `vw_IncidentsLive` | W1008 | IT Service Desk Manager |
| KYC documents looked up outside Compliance (access rule, hourly, High) | Wabi Insight's access ledger on `KycDocuments` | W1001 | Compliance and AML Officer |

Data rules run daily (06:00–07:00 Asia/Dubai) once their application is connected and approved; **Test now** shows the planted case immediately. Matches are shown only to people who may read the rule's data, so Huda (administrator) sees the cash-deposit match count but not the customer. The access rule fires when anyone without the Compliance role (for example the CEO) asks a question that reads `KycDocuments`. Alerts never block or suspend anyone.

## Consistency

Python asserts every rule before writing SQL, and each database script checks itself again at the end:

- every actor held that system's role on the day and was at work (attendance, leave and shifts come from the same model as Personnel's `AttendanceRecords`);
- approvers differ from requesters and checkers from makers, and approvals stay within the approver's limit;
- dates follow the business process (requisition → order → receipt → invoice → payment; request → approval → task → fulfilment);
- running balances never go negative;
- nobody processes their own customer record, account, deposit, loan or Contact Centre request.

The planted findings above are the only exceptions. `07-check-consistency.sql` then checks across the databases that every `*UserId`, `*EmployeeNumber`, `*DepartmentCode`, `*SectionCode`, `*BranchCode`, unit code, cost centre and digital `CIF` exists in Personnel (or Core Banking), that each local `Users` table matches Personnel, and that every completed channel transfer is posted exactly once in Core Banking.

## Regenerating

The scripts are generated by Python 3 (standard library only) from `bank_people.py` (organization, people, rosters and access, shared by all five), `bank_customers.py` (customers, accounts, loans and digital activity, shared by Core Banking and Digital Banking) and one `build_*.py` per database. Generation is deterministic: an unchanged model produces identical files.

```bash
python3 scripts/sql/samples/wabi-bank/build_bank_sql.py
```

`WabiBankSampleScriptTests` (integration tests) runs 01–07 against a freshly migrated database that already holds two other organizations and checks the structure, the consistency checks, the users and roles, least privilege, the rules and their planted cases, idempotency and isolation.
