"""WabiBankCoreBankingDB: Wabi Bank's core banking system (customers and KYC, accounts and deposits, branch transactions and
teller cash, cheque clearing, loans, funds transfers) for 2025.

Accounts are always domiciled at one of the six banking branches. Every posting carries PostingUnitCode, the department
code of the unit that posted it (a branch, OPS central operations and batch, DCC digital channel). Staff actions are taken
by users with core-banking access who were at work that day (rosters from bank_people), within their approval limits.

Planted findings: one branch (Sharjah) with a high cheque-return rate; one teller with many reversals; cash deposits by
one customer just below AED 50,000 on several days and two deposits a day on the last two days (AED 55,000 or more a day);
a dormant account reactivated and used to pass a large SWIFT transfer through; a teller posting to their own account; two
loan approvals above the credit officer's limit; auto-loan arrears rising in Q3; two customers with four returned cheques
in December.
"""
import datetime as dt
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bank_customers as bc  # noqa: E402
import bank_people as bp  # noqa: E402
from build_common import UAE_HOLIDAYS_2025, calendar_table, epilogue, insert, prologue, rng  # noqa: E402
from build_personnel import DROP_FOREIGN_KEYS  # noqa: E402

DB = "WabiBankCoreBankingDB"
TABLES = ["MonthEndLoanBalances", "MonthEndArrears", "LoanRepayments", "LoanSchedules", "Loans", "LoanApplications", "LoanProducts", "TellerTills", "MonthEndBalances", "Transactions",
          "InwardTransfers", "OutwardTransfers", "InwardCheques", "OutwardCheques", "ChequeBooks", "ChequeReturnReasons", "ClearingSessions", "FixedDeposits",
          "KycDocuments", "Accounts", "Customers", "TransactionCodes", "Products", "FxRates", "Currencies", "Users", "Units", "Branches", "BusinessCalendar", "Calendar"]
VIEWS = ["vw_TransactionsByStaff", "vw_OutwardChequesByStaff", "vw_OutwardTransfersByStaff", "vw_TransactionsLive", "vw_InwardChequesLive"]

OBJECT_DOCS = [
    ("Branches", "Branches", "الفروع", "The six banking branches (Head Office is not a branch). BranchCode is the department code shared with every Wabi Bank system."),
    ("Units", "Posting units", "وحدات الترحيل", "Units that post transactions: the branches and the Head Office departments OPS (central operations and batch), DCC (digital channel), CBD and TRY."),
    ("Users", "Core banking users", "مستخدمو النظام المصرفي", "Users with core-banking access in 2025 (same UserId, employee number and units as Personnel) and the service accounts."),
    ("BusinessCalendar", "Business calendar 2025", "التقويم 2025", "Every day of 2025 with weekday, weekend flag (Saturday, Sunday) and UAE public holidays; branches open Monday to Friday."),
    ("Currencies", "Currencies", "العملات", "Currencies held or transferred."), ("FxRates", "Exchange rates", "أسعار الصرف", "Daily rate to AED per currency (USD, SAR, JOD pegged)."),
    ("Products", "Products", "المنتجات", "Current, savings and business account products with monthly fee and interest rate."),
    ("TransactionCodes", "Transaction codes", "رموز المعاملات", "Posting codes: salary, ATM, card, teller cash, cheques, transfers, digital, fees, interest, loans, fixed deposits."),
    ("Customers", "Customers", "العملاء", "Customer master (CIF): individuals (including staff, whose salary is paid into Wabi Bank), SMEs and corporates, segment, nationality, risk rating, home branch, relationship manager (employee number) and the user who onboarded them."),
    ("KycDocuments", "KYC documents", "وثائق اعرف عميلك", "Emirates ID, passport and trade licence of each customer with issue and expiry dates; expired documents need renewal."),
    ("Accounts", "Accounts", "الحسابات", "Accounts with IBAN, product, currency, home branch, status (Active, Dormant, Closed), who opened and approved them, opening balance (1 January 2025 or the opening date) and ledger balance (31 December 2025 or closure)."),
    ("FixedDeposits", "Fixed deposits", "الودائع لأجل", "Term deposits with principal, rate, tenor, start and maturity, funding account and the user who booked them."),
    ("Transactions", "Transactions", "المعاملات", "Every posting of 2025: account, code, debit or credit, amount (and AED), posting time, channel, posting unit, the user who posted and approved it, reversals and the balance after the posting."),
    ("TellerTills", "Teller tills", "صناديق الصرافين", "Each teller's cash position per business day: opening cash, cash in, cash out, expected and counted closing cash and the difference."),
    ("MonthEndBalances", "Month-end balances", "أرصدة نهاية الشهر", "Balance of every open account at each month end of 2025 (account currency and AED)."),
    ("ClearingSessions", "Clearing sessions", "جلسات المقاصة", "Two UAE cheque clearing sessions per business day."),
    ("ChequeReturnReasons", "Cheque return reasons", "أسباب إرجاع الشيكات", "Why a cheque was returned unpaid."),
    ("ChequeBooks", "Cheque books", "دفاتر الشيكات", "Cheque books issued on current accounts with the leaf range."),
    ("OutwardCheques", "Cheques deposited (outward clearing)", "الشيكات المودعة", "Cheques drawn on other banks that customers deposited at a branch, captured by branch operations, cleared or returned."),
    ("InwardCheques", "Cheques presented (inward clearing)", "الشيكات المقدمة", "Cheques written by Wabi Bank customers and presented by other banks, paid or returned (insufficient funds, signature, stopped ...)."),
    ("OutwardTransfers", "Outward transfers", "الحوالات الصادرة", "Local (UAEFTS) and SWIFT transfers initiated at branches and processed by Central Operations (maker and checker), with sanctions screening and charges."),
    ("InwardTransfers", "Inward transfers", "الحوالات الواردة", "Local and SWIFT transfers received for customers."),
    ("LoanProducts", "Loan products", "منتجات القروض", "Personal, auto, home and SME loans."),
    ("LoanApplications", "Loan applications 2025", "طلبات القروض 2025", "Applications sourced by branch sales or SME relationship managers (employee number), decided by Credit Risk users within their approval limits."),
    ("Loans", "Loans", "القروض", "The loan book: principal, rate, tenor, start and maturity, monthly instalment, repayment account and who disbursed and approved the disbursement."),
    ("LoanSchedules", "Loan schedules", "جداول سداد القروض", "Monthly instalments falling due in 2025 with principal, interest and outstanding balance."),
    ("LoanRepayments", "Loan repayments", "سداد القروض", "Instalments paid on time, late or not paid in 2025."),
    ("MonthEndArrears", "Month-end arrears", "المتأخرات في نهاية الشهر", "Overdue instalments per loan at each month end with days past due and bucket (Current, 1-30, 31-60, 61-90, 90+)."),
    ("MonthEndLoanBalances", "Month-end loan balances", "أرصدة القروض في نهاية الشهر", "Outstanding principal of every active loan at each month end of 2025 (after the instalments due by then), with customer, product and branch. Use it for loan book size by branch, product or month."),
    ("vw_TransactionsByStaff", "Transactions with posting staff", "المعاملات مع الموظف المرحِّل", "Transactions with the posting user's employee number, department and section (from Users)."),
    ("vw_OutwardChequesByStaff", "Deposited cheques with capturing staff", "الشيكات المودعة مع الموظف", "Outward cheques with the capturing user's employee number, department and section."),
    ("vw_OutwardTransfersByStaff", "Outward transfers with approving staff", "الحوالات الصادرة مع الموظف المعتمِد", "Outward transfers with the approving (checker) user's employee number, department and section."),
    ("vw_TransactionsLive", "Transactions (live demo window)", "المعاملات (نافذة العرض المباشر)", "December 2025 transactions shifted so that 31 December 2025 is today (Asia/Dubai), for Monitoring Rules demos."),
    ("vw_InwardChequesLive", "Presented cheques (live demo window)", "الشيكات المقدمة (نافذة العرض المباشر)", "December 2025 presented cheques shifted so that 31 December 2025 is today (Asia/Dubai)."),
]
COLUMN_DOCS = [
    ("Customers", "DateOfBirth", "Date of birth", "تاريخ الميلاد", None, "Confidential"), ("Customers", "MobileNumber", "Mobile number", "رقم الهاتف", None, "Confidential"),
    ("Customers", "MonthlySalary", "Monthly salary", "الراتب الشهري", "AED, as declared or credited.", "Confidential"),
    ("Customers", "StaffEmployeeNumber", "Staff employee number", "الرقم الوظيفي للموظف", "For staff customers: their employee number in Personnel.", None),
    ("Customers", "RelationshipManagerEmployeeNumber", "Relationship manager", "مدير العلاقة", "Employee number of the relationship manager.", None),
    ("KycDocuments", "DocumentNumber", "Document number", "رقم الوثيقة", "Emirates ID, passport or trade licence number.", "Restricted"),
    ("Accounts", "IBAN", "IBAN", "رقم الحساب المصرفي الدولي", None, "Restricted"),
    ("Accounts", "OpeningBalance", "Opening balance", "الرصيد الافتتاحي", "Balance on 1 January 2025 (or at opening).", "Confidential"),
    ("Accounts", "LedgerBalance", "Ledger balance", "الرصيد الدفتري", "Balance on 31 December 2025 (or at closure).", "Confidential"),
    ("Transactions", "BalanceAfter", "Balance after", "الرصيد بعد المعاملة", None, "Confidential"),
    ("Transactions", "PostingUnitCode", "Posting unit", "وحدة الترحيل", "Department code of the unit that posted: a branch, OPS, DCC, CBD or TRY.", None),
    ("Transactions", "Direction", "Debit or credit", "مدين أو دائن", "D = debit (money out), C = credit (money in).", None),
    ("Transactions", "Status", "Status", "الحالة", "Posted or Reversed (reversed postings do not change the balance).", None),
    ("MonthEndBalances", "Balance", "Balance", "الرصيد", None, "Confidential"),
    ("OutwardTransfers", "BeneficiaryAccount", "Beneficiary account", "حساب المستفيد", None, "Restricted"),
    ("InwardCheques", "Status", "Status", "الحالة", "Paid or Returned.", None), ("OutwardCheques", "Status", "Status", "الحالة", "Cleared or Returned.", None),
    ("TellerTills", "Difference", "Cash difference", "فرق النقد", "Counted minus expected closing cash (negative = shortage).", None),
    ("Loans", "Principal", "Principal", "أصل القرض", None, "Confidential"),
    ("MonthEndLoanBalances", "OutstandingPrincipal", "Outstanding principal (AED)", "أصل القرض القائم (درهم)", "Principal still owed at the month end.", "Confidential"), ("LoanApplications", "RequestedAmount", "Requested amount", "المبلغ المطلوب", None, "Confidential"),
]
FIELD_ROLES = [
    ("Transactions", "AccountNumber", 3), ("Transactions", "Amount", 6), ("Transactions", "AmountAed", 6), ("Transactions", "PostedAt", 5), ("Transactions", "Status", 8),
    ("Transactions", "PostingUnitCode", 9), ("vw_TransactionsLive", "AccountNumber", 3), ("vw_TransactionsLive", "CIF", 2), ("vw_TransactionsLive", "Amount", 6),
    ("vw_TransactionsLive", "PostedAt", 5), ("vw_TransactionsLive", "TransactionCode", 8), ("vw_TransactionsByStaff", "PostedByEmployeeNumber", 2),
    ("vw_TransactionsByStaff", "PostedAt", 5), ("vw_TransactionsByStaff", "Amount", 6), ("Accounts", "AccountNumber", 3), ("Accounts", "Status", 8),
    ("Customers", "CIF", 2), ("Customers", "RiskRating", 8), ("InwardCheques", "AccountNumber", 3), ("InwardCheques", "PresentedDate", 5), ("InwardCheques", "Amount", 6),
    ("InwardCheques", "Status", 8), ("vw_InwardChequesLive", "AccountNumber", 3), ("vw_InwardChequesLive", "CIF", 2), ("vw_InwardChequesLive", "PresentedDate", 5),
    ("vw_InwardChequesLive", "Status", 8), ("vw_InwardChequesLive", "Amount", 6), ("OutwardTransfers", "AmountAed", 6), ("OutwardTransfers", "CreatedAt", 5),
    ("Loans", "Principal", 6), ("LoanRepayments", "DueDate", 5), ("LoanRepayments", "Status", 8), ("MonthEndArrears", "DaysPastDue", 7), ("MonthEndLoanBalances", "MonthEnd", 5), ("MonthEndLoanBalances", "OutstandingPrincipal", 6), ("MonthEndLoanBalances", "LoanNumber", 3),
]
ROW_SCOPES = [
    ("Customers", None, "HomeBranchCode", None), ("Accounts", None, "BranchCode", None), ("Transactions", None, "PostingUnitCode", None),
    ("vw_TransactionsByStaff", "PostedByEmployeeNumber", "PostingUnitCode", "PostedBySectionCode"), ("vw_TransactionsLive", None, "PostingUnitCode", None),
    ("OutwardCheques", None, "DepositBranchCode", None), ("vw_OutwardChequesByStaff", "CapturedByEmployeeNumber", "DepositBranchCode", "CapturedBySectionCode"),
    ("InwardCheques", None, "BranchCode", None), ("vw_InwardChequesLive", None, "BranchCode", None), ("TellerTills", None, "BranchCode", None),
    ("LoanApplications", "SourcedByEmployeeNumber", "BranchCode", None), ("Loans", None, "BranchCode", None), ("FixedDeposits", None, "BranchCode", None),
    ("OutwardTransfers", None, "InitiatedAtUnitCode", None), ("MonthEndBalances", None, "BranchCode", None), ("MonthEndLoanBalances", None, "BranchCode", None),
    ("MonthEndArrears", None, "BranchCode", None),
]

PRODUCTS = [  # code, name, Arabic, type, currency, monthly fee, interest %
    ("CA-SAL", "Salary current account", "حساب جاري للراتب", "Current", "AED", 0, 0), ("CA-STD", "Current account", "حساب جاري", "Current", "AED", 25, 0),
    ("SA-STD", "Savings account", "حساب توفير", "Savings", "AED", 0, 1.5), ("SA-USD", "USD savings account", "حساب توفير بالدولار", "Savings", "USD", 0, 2.0),
    ("CA-BUS", "Business current account", "حساب جاري للأعمال", "Current", "AED", 100, 0), ("CA-BUSUSD", "Business current account USD", "حساب جاري للأعمال بالدولار", "Current", "USD", 27, 0),
]
TX_CODES = [  # code, description, Arabic, direction, category
    ("SAL", "Salary credit", "إيداع راتب", "C", "Salary"), ("ATW", "ATM cash withdrawal", "سحب نقدي من الصراف الآلي", "D", "Cash"),
    ("POS", "Card purchase", "شراء بالبطاقة", "D", "Card"), ("CDP", "Cash deposit at branch", "إيداع نقدي في الفرع", "C", "Cash"),
    ("CWD", "Cash withdrawal at branch", "سحب نقدي من الفرع", "D", "Cash"), ("CHD", "Cheque deposit credit", "إيداع شيك", "C", "Cheque"),
    ("CHP", "Cheque paid", "صرف شيك", "D", "Cheque"), ("OTR", "Outward transfer", "حوالة صادرة", "D", "Transfer"), ("ITR", "Inward transfer", "حوالة واردة", "C", "Transfer"),
    ("DTD", "Digital transfer out", "تحويل رقمي صادر", "D", "Digital"), ("DTC", "Digital transfer in", "تحويل رقمي وارد", "C", "Digital"),
    ("FEE", "Charges", "رسوم", "D", "Fee"), ("INT", "Interest credit", "فائدة دائنة", "C", "Interest"), ("LDB", "Loan disbursement", "صرف قرض", "C", "Loan"),
    ("LRP", "Loan instalment", "قسط قرض", "D", "Loan"), ("FDP", "Fixed deposit placement", "إيداع لأجل", "D", "Deposit"), ("FDM", "Fixed deposit maturity", "استحقاق وديعة", "C", "Deposit"),
    ("CLS", "Account closure payout", "صرف رصيد إغلاق الحساب", "D", "Cash"),
]
RETURN_REASONS = [("01", "Insufficient funds", "عدم كفاية الرصيد", 1), ("02", "Signature mismatch", "عدم تطابق التوقيع", 1), ("03", "Payment stopped by drawer", "إيقاف الصرف من الساحب", 1),
                  ("04", "Post-dated cheque", "شيك مؤجل التاريخ", 1), ("05", "Account closed", "الحساب مغلق", 1), ("06", "Amount in words and figures differ", "اختلاف المبلغ كتابة ورقماً", 1),
                  ("07", "Technical reason (image quality)", "سبب فني (جودة الصورة)", 0)]
CURRENCIES = [("AED", "UAE dirham", "درهم إماراتي", 1, 1.0), ("USD", "US dollar", "دولار أمريكي", 1, 3.6725), ("SAR", "Saudi riyal", "ريال سعودي", 1, 0.9793),
              ("JOD", "Jordanian dinar", "دينار أردني", 1, 5.18), ("EUR", "Euro", "يورو", 0, 4.02), ("GBP", "Pound sterling", "جنيه إسترليني", 0, 4.65),
              ("INR", "Indian rupee", "روبية هندية", 0, 0.0435), ("PKR", "Pakistani rupee", "روبية باكستانية", 0, 0.0131), ("PHP", "Philippine peso", "بيزو فلبيني", 0, 0.0635),
              ("EGP", "Egyptian pound", "جنيه مصري", 0, 0.0745)]


def _rows():
    bp.validate()
    r = rng(9191)
    out = {}
    # Reference ------------------------------------------------------------------------------------------------
    out["Branches"] = [(loc[0], loc[1], loc[2], bc.BRANCH_NUM[loc[0]], loc[4], loc[5], loc[6], loc[7], bp.head_of(loc[0], bp.Y1)["EmployeeNumber"])
                       for loc in bp.LOCATIONS[1:]]
    units = [d for d in bp.departments() if d["DepartmentType"] == "Branch" or d["DepartmentCode"] in ("OPS", "DCC", "CBD", "TRY")]
    out["Units"] = [(d["DepartmentCode"], d["DepartmentName"], d["DepartmentNameAr"], d["DepartmentType"], d["DivisionCode"]) for d in units]
    out["Users"] = [(u["UserId"], u["EmployeeNumber"], u["FullName"], u["FullNameAr"], u["DepartmentCode"], u["SectionCode"], u["RoleCode"], u["ApprovalLimitAed"],
                     u["Status"], u["AccessGrantedDate"], u["AccessRevokedDate"]) for u in bp.local_users("CBS")]
    out["Currencies"] = [(c[0], c[1], c[2], c[3]) for c in CURRENCIES]
    out["Products"] = PRODUCTS
    out["TransactionCodes"] = TX_CODES
    out["ChequeReturnReasons"] = RETURN_REASONS
    out["LoanProducts"] = [(p[0], p[1], p[2], p[3][0], p[3][1], p[4][0], p[4][1]) for p in bc.LOAN_PRODUCTS]
    # Customers and accounts --------------------------------------------------------------------------------------
    custs = bc.customers()
    out["Customers"] = [(c["CIF"], c["CustomerType"], c["Segment"], c["FullName"], c["FullNameAr"], c["Gender"], c["DateOfBirth"], c.get("IncorporationDate"),
                         c["NationalityCode"], c["MobileNumber"], c["RiskRating"], c["IsPep"], c["HomeBranchCode"], c["RelationshipManagerEmployeeNumber"],
                         c["OnboardedDate"], c["CreatedByUserId"], c["IsStaff"], c["StaffEmployeeNumber"], c["EmployerName"], c["MonthlySalary"], c.get("Turnover"))
                        for c in custs]
    accts = bc.accounts()
    out["Accounts"] = [(a["AccountNumber"], a["IBAN"], a["CIF"], a["ProductCode"], a["CurrencyCode"], a["BranchCode"], a["OpenedDate"], a["OpenedByUserId"],
                        a["ApprovedByUserId"], a["Status"], a["DormantSince"], a["ReactivatedDate"], a["ReactivatedByUserId"], a["ClosedDate"],
                        a["Key"], a["Kind"], a["SalaryAmount"], a["SalaryDay"], a["EmployerName"], a["SalaryFrom"], a["SalaryTo"], a["AtmPerMonth"], a["PosPerMonth"],
                        a["TellerPerMonth"], a["ChequeInPerMonth"], a["ChequeOutPerMonth"], a["TransferOutPerMonth"], a["TransferInPerMonth"], a["AmountScale"])
                       for a in accts]
    out["FixedDeposits"] = [(f["DepositNumber"], f["CIF"], f["BranchCode"], f["FundingAccountNumber"], f["Principal"], f["InterestRate"], f["TenorMonths"], f["StartDate"],
                             f["MaturityDate"], f["InterestAtMaturity"], f["RenewalInstruction"], f["Status"], f["BookedByUserId"]) for f in bc.fixed_deposits()]
    apps, book = bc.loans()
    out["LoanApplications"] = [(x["ApplicationId"], x["ApplicationNumber"], x["CIF"], x["ProductCode"], x["BranchCode"], x["SourcingUnitCode"], x["SourcedByEmployeeNumber"],
                                x["CreatedByUserId"], x["AppliedDate"], x["RequestedAmount"], x["TenorMonths"], x["Status"], x["DecidedByUserId"], x["DecisionDate"],
                                x["RejectionReason"]) for x in apps]
    out["Loans"] = [(ln["LoanNumber"], ln["ApplicationId"], ln["CIF"], ln["ProductCode"], ln["BranchCode"], ln["Principal"], ln["InterestRate"], ln["TenorMonths"],
                     ln["StartDate"], ln["MaturityDate"], ln["InstallmentAmount"], ln["RepaymentAccountNumber"], ln["DisbursedByUserId"], ln["DisbursementApprovedByUserId"])
                    for ln in book]
    # Rosters: who is at work with which core-banking role, per business day ------------------------------------------
    roster = []
    for day in bc.business_days_2025():
        groups = []
        for b in bp.BRANCH_CODES:
            groups.append((b, "TELLER", bp.staff_on(day, f"{b}-TLR", "CBS", {"Teller"})))
            sup = (bp.staff_on(day, f"{b}-TLR", "CBS", {"HeadTeller"}) + bp.staff_on(day, f"{b}-OPS", "CBS", {"BranchOperationsManager"})
                   + bp.staff_on(day, b, "CBS", {"BranchManager"}))
            groups.append((b, "SUPER", sup))
            groups.append((b, "BROPS", bp.staff_on(day, f"{b}-OPS", "CBS", {"BranchOperations", "BranchOperationsManager"})))
            groups.append((b, "CSV", bp.staff_on(day, f"{b}-CSV", "CBS", {"CustomerService", "CustomerServiceManager"})))
        groups.append(("OPS", "CLR", bp.staff_on(day, "OPS-CLR", "CBS", {"ClearingOfficer", "ClearingSupervisor"})))
        groups.append(("OPS", "PAYM", bp.staff_on(day, "OPS-PAY", "CBS", {"PaymentsOfficer"})))
        groups.append(("OPS", "PAYC", bp.staff_on(day, "OPS-PAY", "CBS", {"PaymentsSupervisor"}) or bp.staff_on(day, "OPS", "CBS", {"OperationsHead"})))
        groups.append(("OPS", "AML", bp.staff_on(day, "CMP-AML", "CBS", {"ComplianceInvestigator"})))
        for unit, group, people in groups:
            for slot, p in enumerate(sorted(people, key=lambda x: x["UserId"]), 1):
                a = bp.access_row(p["UserId"], "CBS", day)
                roster.append((unit, day, group, slot, p["UserId"], a["ApprovalLimitAed"] or (50000 if group == "TELLER" else 0)))
    out["#Roster"] = roster
    # Digital channel transfers (also in WabiBankDigitalDB) -------------------------------------------------------
    profiles, _, transfers, _ = bc.digital()
    internal = {a["AccountNumber"] for a in accts}
    out["#Digital"] = [(t["TransferId"], t["FromAccountNumber"], t["ToAccount"] if t["ToAccount"] in internal else None, t["Amount"], t["InitiatedAt"], t["CoreBankingReference"],
                        t["TransferType"]) for t in transfers if t["Status"] == "Completed"]
    # Planted findings ------------------------------------------------------------------------------------------
    facts = {}
    tellers = [p for p in bp.employees() if p["SectionCode"] and p["SectionCode"].endswith("-TLR") and p["Level"] == "Staff" and p["TerminationDate"] is None
               and p["HireDate"] < bp.Y0 and not any(m["EmployeeNumber"] == p["EmployeeNumber"] for m in bp.movements())]
    facts["reversal_teller"] = next(p for p in tellers if p["DepartmentCode"] == "BR-SHJ01")["UserId"]
    self_teller = next(p for p in tellers if p["DepartmentCode"] == "BR-AUH01")
    own = next(a for a in accts if a["Customer"].get("StaffEmployeeNumber") == self_teller["EmployeeNumber"])
    planted_tx = []
    for month, kind, amount in ((3, "CWD", 4500), (6, "CDP", 12000), (9, "CWD", 7800)):
        day = next(x for x in bc.business_days_2025() if x.month == month and x.day >= 10 and bp.present(self_teller, x))
        planted_tx.append((own["AccountNumber"], kind, amount, dt.datetime.combine(day, dt.time(13, 47)), "BR-AUH01", self_teller["UserId"], None, "Planted: teller posting to own account"))
    structurer = next(c for c in custs if c["Segment"] == "Mass" and c["HomeBranchCode"] == "BR-DXB01" and c["MonthlySalary"] and c["OnboardedDate"] < dt.date(2023, 1, 1))
    s_acct = next(a for a in accts if a["CIF"] == structurer["CIF"])
    deposits = [(dt.datetime(2025, 12, d, 10, 5 + d), 45000 + (d * 397) % 4900) for d in (4, 5, 9, 11, 16, 18, 23)]
    deposits += [(dt.datetime(2025, 12, 30, 10, 0), 28000), (dt.datetime(2025, 12, 30, 14, 30), 29000),
                 (dt.datetime(2025, 12, 31, 9, 10), 27500), (dt.datetime(2025, 12, 31, 13, 40), 30500)]
    for at, amount in deposits:
        teller = r.choice(bp.staff_on(at.date(), "BR-DXB01-TLR", "CBS", {"Teller"}))
        planted_tx.append((s_acct["AccountNumber"], "CDP", amount, at, "BR-DXB01", teller["UserId"], None, "Planted: cash deposits just below the reporting threshold"))
    out["#Planted"] = planted_tx
    facts["structurer"] = structurer["CIF"]
    # Returned cheques in December for two customers (four each).
    returners = [next(a for a in accts if a["Kind"] == "sme" and a["BranchCode"] == "BR-SHJ01" and a["Status"] == "Active" and a["ProductCode"] == "CA-BUS"),
                 next(a for a in accts if a["Kind"] == "salary" and a["BranchCode"] == "BR-DXB02" and a["Status"] == "Active" and a["ProductCode"] == "CA-SAL" and not a["Customer"]["IsStaff"])]
    returned = []
    for a in returners:
        for k, d in enumerate((8, 15, 22, 29)):
            day = dt.date(2025, 12, d)
            clerk = r.choice(bp.staff_on(day, "OPS-CLR", "CBS", {"ClearingOfficer", "ClearingSupervisor"}))
            returned.append((a["AccountNumber"], 900 + k, r.choice(bc.OTHER_BANKS)[0], 7500 + k * 1250 + (int(a["AccountNumber"]) % 9) * 100, day, clerk["UserId"]))
    out["#Returned"] = returned
    facts["returners"] = [a["CIF"] for a in returners]
    # Dormant account reactivated, then a large inward and outward SWIFT pass-through.
    dormant = next(a for a in accts if a.get("Planted") == "dormant_reactivated")
    in_day, out_day = bc.next_business_day(dormant["ReactivatedDate"] + dt.timedelta(days=1)), bc.next_business_day(dormant["ReactivatedDate"] + dt.timedelta(days=2))
    pay_in = r.choice(bp.staff_on(in_day, "OPS-PAY", "CBS", {"PaymentsOfficer"}))
    maker = r.choice(bp.staff_on(out_day, "OPS-PAY", "CBS", {"PaymentsOfficer"}))
    checker = (bp.staff_on(out_day, "OPS-PAY", "CBS", {"PaymentsSupervisor"}) or bp.staff_on(out_day, "OPS", "CBS", {"OperationsHead"}))[0]
    aml = r.choice(bp.staff_on(out_day, "CMP-AML", "CBS", {"ComplianceInvestigator"}))
    initiator = r.choice(bp.staff_on(out_day, f"{dormant['BranchCode']}-OPS", "CBS", {"BranchOperations", "BranchOperationsManager"}))
    out["#PassThrough"] = [(dormant["AccountNumber"], in_day, pay_in["UserId"], out_day, maker["UserId"], checker["UserId"], aml["UserId"], initiator["UserId"], dormant["BranchCode"])]
    facts["dormant"] = dormant["AccountNumber"]
    facts["self_teller"] = self_teller["UserId"]
    return out, facts


SCHEMA = """CREATE TABLE dbo.Branches (BranchCode nvarchar(10) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY, BranchName nvarchar(80) NOT NULL, BranchNameAr nvarchar(80) NOT NULL,
    BranchNumber char(3) NOT NULL, Emirate nvarchar(30) NOT NULL, Address nvarchar(200) NOT NULL, OpenedDate date NOT NULL, Counters tinyint NOT NULL,
    BranchManagerEmployeeNumber nvarchar(10) NOT NULL);
CREATE TABLE dbo.Units (UnitCode nvarchar(10) NOT NULL CONSTRAINT PK_Units PRIMARY KEY, UnitName nvarchar(120) NOT NULL, UnitNameAr nvarchar(120) NOT NULL,
    UnitType nvarchar(20) NOT NULL, DivisionCode nvarchar(10) NOT NULL);
CREATE TABLE dbo.Users (UserId nvarchar(20) NOT NULL CONSTRAINT PK_Users PRIMARY KEY, EmployeeNumber nvarchar(10) NULL, FullName nvarchar(120) NOT NULL, FullNameAr nvarchar(120) NULL,
    DepartmentCode nvarchar(10) NULL, SectionCode nvarchar(20) NULL, RoleCode nvarchar(40) NOT NULL, ApprovalLimitAed decimal(18,2) NULL, Status nvarchar(10) NOT NULL,
    AccessGrantedDate date NOT NULL, AccessRevokedDate date NULL);
CREATE TABLE dbo.Currencies (CurrencyCode char(3) NOT NULL CONSTRAINT PK_Currencies PRIMARY KEY, CurrencyName nvarchar(40) NOT NULL, CurrencyNameAr nvarchar(40) NOT NULL, IsPegged bit NOT NULL);
CREATE TABLE dbo.FxRates (RateDate date NOT NULL, CurrencyCode char(3) NOT NULL CONSTRAINT FK_FxRates_Currencies REFERENCES dbo.Currencies, RateToAed decimal(18,6) NOT NULL,
    CONSTRAINT PK_FxRates PRIMARY KEY (RateDate, CurrencyCode));
CREATE TABLE dbo.Products (ProductCode nvarchar(10) NOT NULL CONSTRAINT PK_Products PRIMARY KEY, ProductName nvarchar(60) NOT NULL, ProductNameAr nvarchar(60) NOT NULL,
    ProductType nvarchar(20) NOT NULL, CurrencyCode char(3) NOT NULL, MonthlyFee decimal(18,2) NOT NULL, InterestRatePct decimal(6,3) NOT NULL);
CREATE TABLE dbo.TransactionCodes (TransactionCode nvarchar(4) NOT NULL CONSTRAINT PK_TransactionCodes PRIMARY KEY, Description nvarchar(60) NOT NULL, DescriptionAr nvarchar(60) NOT NULL,
    Direction char(1) NOT NULL, Category nvarchar(20) NOT NULL);
CREATE TABLE dbo.ChequeReturnReasons (ReasonCode char(2) NOT NULL CONSTRAINT PK_ChequeReturnReasons PRIMARY KEY, Description nvarchar(80) NOT NULL, DescriptionAr nvarchar(80) NOT NULL,
    IsCustomerFault bit NOT NULL);
CREATE TABLE dbo.LoanProducts (ProductCode nvarchar(4) NOT NULL CONSTRAINT PK_LoanProducts PRIMARY KEY, ProductName nvarchar(60) NOT NULL, ProductNameAr nvarchar(60) NOT NULL,
    MinRatePct decimal(6,3) NOT NULL, MaxRatePct decimal(6,3) NOT NULL, MinTenorMonths smallint NOT NULL, MaxTenorMonths smallint NOT NULL);
CREATE TABLE dbo.Customers (CIF nvarchar(10) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY, CustomerType nvarchar(12) NOT NULL, Segment nvarchar(12) NOT NULL,
    FullName nvarchar(150) NOT NULL, FullNameAr nvarchar(150) NULL, Gender char(1) NULL, DateOfBirth date NULL, IncorporationDate date NULL, NationalityCode char(2) NOT NULL,
    MobileNumber nvarchar(20) NOT NULL, RiskRating nvarchar(8) NOT NULL, IsPep bit NOT NULL, HomeBranchCode nvarchar(10) NOT NULL CONSTRAINT FK_Customers_Branches REFERENCES dbo.Branches,
    RelationshipManagerEmployeeNumber nvarchar(10) NULL, OnboardedDate date NOT NULL, CreatedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_Customers_Users REFERENCES dbo.Users,
    IsStaff bit NOT NULL, StaffEmployeeNumber nvarchar(10) NULL, EmployerName nvarchar(150) NULL, MonthlySalary decimal(18,2) NULL, AnnualTurnover decimal(18,2) NULL,
    Status nvarchar(10) NOT NULL CONSTRAINT DF_Customers_Status DEFAULT N'Active');
CREATE TABLE dbo.Accounts (AccountNumber char(12) NOT NULL CONSTRAINT PK_Accounts PRIMARY KEY, IBAN char(23) NOT NULL, CIF nvarchar(10) NOT NULL CONSTRAINT FK_Accounts_Customers REFERENCES dbo.Customers,
    ProductCode nvarchar(10) NOT NULL CONSTRAINT FK_Accounts_Products REFERENCES dbo.Products, CurrencyCode char(3) NOT NULL, BranchCode nvarchar(10) NOT NULL CONSTRAINT FK_Accounts_Branches REFERENCES dbo.Branches,
    OpenedDate date NOT NULL, OpenedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_Accounts_OpenedBy REFERENCES dbo.Users, ApprovedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_Accounts_ApprovedBy REFERENCES dbo.Users,
    Status nvarchar(10) NOT NULL, DormantSince date NULL, ReactivatedDate date NULL, ReactivatedByUserId nvarchar(20) NULL, ClosedDate date NULL, ClosedByUserId nvarchar(20) NULL,
    OpeningBalance decimal(18,2) NULL, LedgerBalance decimal(18,2) NULL, LastTransactionDate date NULL,
    GenKey bigint NULL, GenKind nvarchar(12) NULL, GenSalary decimal(18,2) NULL, GenSalaryDay tinyint NULL, GenEmployer nvarchar(150) NULL, GenSalaryFrom date NULL, GenSalaryTo date NULL,
    GenAtm tinyint NULL, GenPos tinyint NULL, GenTeller decimal(6,2) NULL, GenChqIn decimal(6,2) NULL, GenChqOut decimal(6,2) NULL, GenTrfOut decimal(6,2) NULL,
    GenTrfIn decimal(6,2) NULL, GenScale decimal(18,2) NULL);
CREATE TABLE dbo.KycDocuments (DocumentId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_KycDocuments PRIMARY KEY, CIF nvarchar(10) NOT NULL CONSTRAINT FK_KycDocuments_Customers REFERENCES dbo.Customers,
    DocumentType nvarchar(20) NOT NULL, DocumentNumber nvarchar(30) NOT NULL, IssueDate date NOT NULL, ExpiryDate date NOT NULL, VerifiedByUserId nvarchar(20) NOT NULL, VerifiedDate date NOT NULL,
    Status nvarchar(10) NOT NULL);
CREATE TABLE dbo.FixedDeposits (DepositNumber nvarchar(12) NOT NULL CONSTRAINT PK_FixedDeposits PRIMARY KEY, CIF nvarchar(10) NOT NULL CONSTRAINT FK_FixedDeposits_Customers REFERENCES dbo.Customers,
    BranchCode nvarchar(10) NOT NULL, FundingAccountNumber char(12) NOT NULL CONSTRAINT FK_FixedDeposits_Accounts REFERENCES dbo.Accounts, Principal decimal(18,2) NOT NULL,
    InterestRatePct decimal(6,3) NOT NULL, TenorMonths smallint NOT NULL, StartDate date NOT NULL, MaturityDate date NOT NULL, InterestAtMaturity decimal(18,2) NOT NULL,
    RenewalInstruction nvarchar(40) NOT NULL, Status nvarchar(10) NOT NULL, BookedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_FixedDeposits_Users REFERENCES dbo.Users);
CREATE TABLE dbo.ClearingSessions (SessionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClearingSessions PRIMARY KEY, SessionDate date NOT NULL, SessionNumber tinyint NOT NULL,
    CutOffTime time(0) NOT NULL, SettlementTime time(0) NOT NULL, CONSTRAINT UQ_ClearingSessions UNIQUE (SessionDate, SessionNumber));
CREATE TABLE dbo.ChequeBooks (ChequeBookId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChequeBooks PRIMARY KEY, AccountNumber char(12) NOT NULL CONSTRAINT FK_ChequeBooks_Accounts REFERENCES dbo.Accounts,
    FirstLeaf int NOT NULL, LastLeaf int NOT NULL, Leaves tinyint NOT NULL, IssuedDate date NOT NULL, IssuedByUserId nvarchar(20) NOT NULL, Status nvarchar(12) NOT NULL);
CREATE TABLE dbo.OutwardCheques (ChequeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OutwardCheques PRIMARY KEY, ChequeNumber int NOT NULL, DrawnOnBankCode char(3) NOT NULL,
    DrawnOnBankName nvarchar(60) NOT NULL, DepositAccountNumber char(12) NOT NULL CONSTRAINT FK_OutwardCheques_Accounts REFERENCES dbo.Accounts, DepositBranchCode nvarchar(10) NOT NULL,
    Amount decimal(18,2) NOT NULL, DepositDate date NOT NULL, CapturedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_OutwardCheques_Users REFERENCES dbo.Users, CapturedAt datetime2(0) NOT NULL,
    SessionId int NOT NULL CONSTRAINT FK_OutwardCheques_Sessions REFERENCES dbo.ClearingSessions, Status nvarchar(10) NOT NULL,
    ReturnReasonCode char(2) NULL CONSTRAINT FK_OutwardCheques_Reasons REFERENCES dbo.ChequeReturnReasons, CreditDate date NULL);
CREATE TABLE dbo.InwardCheques (ChequeId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InwardCheques PRIMARY KEY, ChequeNumber int NULL, AccountNumber char(12) NOT NULL CONSTRAINT FK_InwardCheques_Accounts REFERENCES dbo.Accounts,
    BranchCode nvarchar(10) NOT NULL, PresentingBankCode char(3) NOT NULL, PresentingBankName nvarchar(60) NOT NULL, Amount decimal(18,2) NOT NULL, PresentedDate date NOT NULL,
    SessionId int NOT NULL CONSTRAINT FK_InwardCheques_Sessions REFERENCES dbo.ClearingSessions, ProcessedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_InwardCheques_Users REFERENCES dbo.Users,
    ProcessedAt datetime2(0) NOT NULL, Status nvarchar(10) NOT NULL, ReturnReasonCode char(2) NULL CONSTRAINT FK_InwardCheques_Reasons REFERENCES dbo.ChequeReturnReasons);
CREATE TABLE dbo.OutwardTransfers (TransferId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OutwardTransfers PRIMARY KEY, TransferReference nvarchar(16) NULL,
    AccountNumber char(12) NOT NULL CONSTRAINT FK_OutwardTransfers_Accounts REFERENCES dbo.Accounts, TransferType nvarchar(10) NOT NULL, Amount decimal(18,2) NOT NULL,
    CurrencyCode char(3) NOT NULL, AmountAed decimal(18,2) NOT NULL, BeneficiaryName nvarchar(120) NOT NULL, BeneficiaryBank nvarchar(80) NOT NULL, BeneficiaryCountry char(2) NOT NULL,
    BeneficiaryAccount nvarchar(40) NOT NULL, Purpose nvarchar(40) NOT NULL, InitiatedAtUnitCode nvarchar(10) NOT NULL CONSTRAINT FK_OutwardTransfers_Units REFERENCES dbo.Units,
    CreatedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_OutwardTransfers_CreatedBy REFERENCES dbo.Users, CreatedAt datetime2(0) NOT NULL, ScreeningStatus nvarchar(16) NOT NULL,
    ScreenedByUserId nvarchar(20) NULL, ProcessedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_OutwardTransfers_ProcessedBy REFERENCES dbo.Users,
    ApprovedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_OutwardTransfers_ApprovedBy REFERENCES dbo.Users, ProcessedAt datetime2(0) NOT NULL, ValueDate date NOT NULL,
    ChargesAed decimal(18,2) NOT NULL, Status nvarchar(10) NOT NULL);
CREATE TABLE dbo.InwardTransfers (TransferId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_InwardTransfers PRIMARY KEY, TransferReference nvarchar(16) NULL,
    AccountNumber char(12) NOT NULL CONSTRAINT FK_InwardTransfers_Accounts REFERENCES dbo.Accounts, TransferType nvarchar(10) NOT NULL, Amount decimal(18,2) NOT NULL, CurrencyCode char(3) NOT NULL,
    AmountAed decimal(18,2) NOT NULL, OrderingCustomer nvarchar(120) NOT NULL, OrderingBank nvarchar(80) NOT NULL, OrderingCountry char(2) NOT NULL, ReceivedAt datetime2(0) NOT NULL,
    PostedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_InwardTransfers_Users REFERENCES dbo.Users, ValueDate date NOT NULL, Status nvarchar(10) NOT NULL);
CREATE TABLE dbo.Transactions (TransactionId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Transactions PRIMARY KEY, TransactionReference nvarchar(20) NOT NULL,
    AccountNumber char(12) NOT NULL CONSTRAINT FK_Transactions_Accounts REFERENCES dbo.Accounts, TransactionCode nvarchar(4) NOT NULL CONSTRAINT FK_Transactions_Codes REFERENCES dbo.TransactionCodes,
    Direction char(1) NOT NULL, Amount decimal(18,2) NOT NULL, CurrencyCode char(3) NOT NULL, AmountAed decimal(18,2) NOT NULL, ValueDate date NOT NULL, PostedAt datetime2(0) NOT NULL,
    Channel nvarchar(10) NOT NULL, PostingUnitCode nvarchar(10) NOT NULL CONSTRAINT FK_Transactions_Units REFERENCES dbo.Units,
    PostedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_Transactions_PostedBy REFERENCES dbo.Users, ApprovedByUserId nvarchar(20) NULL CONSTRAINT FK_Transactions_ApprovedBy REFERENCES dbo.Users,
    Status nvarchar(10) NOT NULL, ReversedByUserId nvarchar(20) NULL, ReversedAt datetime2(0) NULL, BalanceAfter decimal(18,2) NULL, Narrative nvarchar(140) NULL, SourceReference nvarchar(20) NULL);
CREATE TABLE dbo.TellerTills (TillId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TellerTills PRIMARY KEY, BranchCode nvarchar(10) NOT NULL, TellerUserId nvarchar(20) NOT NULL CONSTRAINT FK_TellerTills_Users REFERENCES dbo.Users,
    BusinessDate date NOT NULL, OpeningCash decimal(18,2) NOT NULL, CashIn decimal(18,2) NOT NULL, CashOut decimal(18,2) NOT NULL, ExpectedClosingCash decimal(18,2) NOT NULL,
    CountedCash decimal(18,2) NOT NULL, Difference decimal(18,2) NOT NULL, TransactionCount int NOT NULL, VerifiedByUserId nvarchar(20) NULL);
CREATE TABLE dbo.MonthEndBalances (AccountNumber char(12) NOT NULL CONSTRAINT FK_MonthEndBalances_Accounts REFERENCES dbo.Accounts, MonthEnd date NOT NULL, BranchCode nvarchar(10) NOT NULL,
    CurrencyCode char(3) NOT NULL, Balance decimal(18,2) NOT NULL, BalanceAed decimal(18,2) NOT NULL, CONSTRAINT PK_MonthEndBalances PRIMARY KEY (AccountNumber, MonthEnd));
CREATE TABLE dbo.LoanApplications (ApplicationId int NOT NULL CONSTRAINT PK_LoanApplications PRIMARY KEY, ApplicationNumber nvarchar(12) NOT NULL, CIF nvarchar(10) NOT NULL CONSTRAINT FK_LoanApplications_Customers REFERENCES dbo.Customers,
    ProductCode nvarchar(4) NOT NULL CONSTRAINT FK_LoanApplications_Products REFERENCES dbo.LoanProducts, BranchCode nvarchar(10) NOT NULL, SourcingUnitCode nvarchar(10) NOT NULL,
    SourcedByEmployeeNumber nvarchar(10) NOT NULL, CreatedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_LoanApplications_Users REFERENCES dbo.Users, AppliedDate date NOT NULL,
    RequestedAmount decimal(18,2) NOT NULL, TenorMonths smallint NOT NULL, Status nvarchar(12) NOT NULL, DecidedByUserId nvarchar(20) NULL, DecisionDate date NULL, RejectionReason nvarchar(80) NULL);
CREATE TABLE dbo.Loans (LoanNumber nvarchar(12) NOT NULL CONSTRAINT PK_Loans PRIMARY KEY, ApplicationId int NULL CONSTRAINT FK_Loans_Applications REFERENCES dbo.LoanApplications,
    CIF nvarchar(10) NOT NULL CONSTRAINT FK_Loans_Customers REFERENCES dbo.Customers, ProductCode nvarchar(4) NOT NULL CONSTRAINT FK_Loans_Products REFERENCES dbo.LoanProducts,
    BranchCode nvarchar(10) NOT NULL, Principal decimal(18,2) NOT NULL, InterestRatePct decimal(6,3) NOT NULL, TenorMonths smallint NOT NULL, StartDate date NOT NULL, MaturityDate date NOT NULL,
    InstallmentAmount decimal(18,2) NOT NULL, RepaymentAccountNumber char(12) NOT NULL CONSTRAINT FK_Loans_Accounts REFERENCES dbo.Accounts, DisbursedByUserId nvarchar(20) NULL,
    DisbursementApprovedByUserId nvarchar(20) NULL, Status nvarchar(10) NULL);
CREATE TABLE dbo.LoanSchedules (LoanNumber nvarchar(12) NOT NULL CONSTRAINT FK_LoanSchedules_Loans REFERENCES dbo.Loans, InstallmentNumber smallint NOT NULL, DueDate date NOT NULL,
    PrincipalDue decimal(18,2) NOT NULL, InterestDue decimal(18,2) NOT NULL, InstallmentDue decimal(18,2) NOT NULL, OutstandingAfter decimal(18,2) NOT NULL,
    CONSTRAINT PK_LoanSchedules PRIMARY KEY (LoanNumber, InstallmentNumber));
CREATE TABLE dbo.LoanRepayments (LoanNumber nvarchar(12) NOT NULL, InstallmentNumber smallint NOT NULL, DueDate date NOT NULL, AmountDue decimal(18,2) NOT NULL, PaidDate date NULL,
    AmountPaid decimal(18,2) NOT NULL, DaysLate smallint NULL, Status nvarchar(10) NOT NULL, CONSTRAINT PK_LoanRepayments PRIMARY KEY (LoanNumber, InstallmentNumber),
    CONSTRAINT FK_LoanRepayments_Schedules FOREIGN KEY (LoanNumber, InstallmentNumber) REFERENCES dbo.LoanSchedules);
CREATE TABLE dbo.MonthEndArrears (LoanNumber nvarchar(12) NOT NULL CONSTRAINT FK_MonthEndArrears_Loans REFERENCES dbo.Loans, MonthEnd date NOT NULL, ProductCode nvarchar(4) NOT NULL, BranchCode nvarchar(10) NOT NULL,
    OverdueInstallments smallint NOT NULL, OverdueAmount decimal(18,2) NOT NULL, DaysPastDue smallint NOT NULL, Bucket nvarchar(8) NOT NULL, CONSTRAINT PK_MonthEndArrears PRIMARY KEY (LoanNumber, MonthEnd));
CREATE TABLE dbo.MonthEndLoanBalances (LoanNumber nvarchar(12) NOT NULL CONSTRAINT FK_MonthEndLoanBalances_Loans REFERENCES dbo.Loans, MonthEnd date NOT NULL,
    CIF nvarchar(10) NOT NULL, ProductCode nvarchar(4) NOT NULL, BranchCode nvarchar(10) NOT NULL, OutstandingPrincipal decimal(18,2) NOT NULL,
    CONSTRAINT PK_MonthEndLoanBalances PRIMARY KEY (LoanNumber, MonthEnd));
GO
"""

COLUMNS = {
    "Branches": ["BranchCode", "BranchName", "BranchNameAr", "BranchNumber", "Emirate", "Address", "OpenedDate", "Counters", "BranchManagerEmployeeNumber"],
    "Units": ["UnitCode", "UnitName", "UnitNameAr", "UnitType", "DivisionCode"],
    "Users": ["UserId", "EmployeeNumber", "FullName", "FullNameAr", "DepartmentCode", "SectionCode", "RoleCode", "ApprovalLimitAed", "Status", "AccessGrantedDate", "AccessRevokedDate"],
    "Currencies": ["CurrencyCode", "CurrencyName", "CurrencyNameAr", "IsPegged"],
    "Products": ["ProductCode", "ProductName", "ProductNameAr", "ProductType", "CurrencyCode", "MonthlyFee", "InterestRatePct"],
    "TransactionCodes": ["TransactionCode", "Description", "DescriptionAr", "Direction", "Category"],
    "ChequeReturnReasons": ["ReasonCode", "Description", "DescriptionAr", "IsCustomerFault"],
    "LoanProducts": ["ProductCode", "ProductName", "ProductNameAr", "MinRatePct", "MaxRatePct", "MinTenorMonths", "MaxTenorMonths"],
    "Customers": ["CIF", "CustomerType", "Segment", "FullName", "FullNameAr", "Gender", "DateOfBirth", "IncorporationDate", "NationalityCode", "MobileNumber", "RiskRating",
                  "IsPep", "HomeBranchCode", "RelationshipManagerEmployeeNumber", "OnboardedDate", "CreatedByUserId", "IsStaff", "StaffEmployeeNumber", "EmployerName",
                  "MonthlySalary", "AnnualTurnover"],
    "Accounts": ["AccountNumber", "IBAN", "CIF", "ProductCode", "CurrencyCode", "BranchCode", "OpenedDate", "OpenedByUserId", "ApprovedByUserId", "Status", "DormantSince",
                 "ReactivatedDate", "ReactivatedByUserId", "ClosedDate", "GenKey", "GenKind", "GenSalary", "GenSalaryDay", "GenEmployer", "GenSalaryFrom", "GenSalaryTo",
                 "GenAtm", "GenPos", "GenTeller", "GenChqIn", "GenChqOut", "GenTrfOut", "GenTrfIn", "GenScale"],
    "FixedDeposits": ["DepositNumber", "CIF", "BranchCode", "FundingAccountNumber", "Principal", "InterestRatePct", "TenorMonths", "StartDate", "MaturityDate",
                      "InterestAtMaturity", "RenewalInstruction", "Status", "BookedByUserId"],
    "LoanApplications": ["ApplicationId", "ApplicationNumber", "CIF", "ProductCode", "BranchCode", "SourcingUnitCode", "SourcedByEmployeeNumber", "CreatedByUserId",
                         "AppliedDate", "RequestedAmount", "TenorMonths", "Status", "DecidedByUserId", "DecisionDate", "RejectionReason"],
    "Loans": ["LoanNumber", "ApplicationId", "CIF", "ProductCode", "BranchCode", "Principal", "InterestRatePct", "TenorMonths", "StartDate", "MaturityDate",
              "InstallmentAmount", "RepaymentAccountNumber", "DisbursedByUserId", "DisbursementApprovedByUserId"],
}
LOAD_ORDER = ["Branches", "Units", "Users", "Currencies", "Products", "TransactionCodes", "ChequeReturnReasons", "LoanProducts", "Customers", "Accounts",
              "FixedDeposits", "LoanApplications", "Loans"]

BANKS_VALUES = ", ".join(f"(N'{c}', N'{n}', {i})" for i, (c, n) in enumerate(bc.OTHER_BANKS, 1))
FOREIGN_VALUES = ", ".join(f"(N'{c}', N'{n}', {i})" for i, (c, n) in enumerate(bc.FOREIGN_BANKS, 1))

GENERATE = """-- ============================================================================================ generation (set-based)
-- Business days, rosters and helper lists ---------------------------------------------------------------------------------
SELECT CalendarDate AS D, MonthNumber AS M, ROW_NUMBER() OVER (PARTITION BY MonthNumber ORDER BY CalendarDate) AS Rn
INTO #WorkDays FROM dbo.BusinessCalendar WHERE IsWeekend = 0 AND IsPublicHoliday = 0;
SELECT M, COUNT(*) AS Cnt INTO #MonthWorkDays FROM #WorkDays GROUP BY M;
SELECT CalendarDate AS D, MonthNumber AS M, DayOfMonth AS Dm INTO #AllDays FROM dbo.BusinessCalendar;
CREATE TABLE #Roster (UnitCode nvarchar(10) NOT NULL, WorkDate date NOT NULL, RoleGroup nvarchar(8) NOT NULL, Slot int NOT NULL, UserId nvarchar(20) NOT NULL, LimitAed decimal(18,2) NOT NULL,
    PRIMARY KEY (UnitCode, WorkDate, RoleGroup, Slot));
{roster}
SELECT UnitCode, WorkDate, RoleGroup, COUNT(*) AS Slots, MAX(LimitAed) AS MaxLimit INTO #RosterCount FROM #Roster GROUP BY UnitCode, WorkDate, RoleGroup;
CREATE UNIQUE CLUSTERED INDEX IX_RosterCount ON #RosterCount (UnitCode, WorkDate, RoleGroup);
SELECT UnitCode, WorkDate, UserId, LimitAed INTO #TopSuper FROM (SELECT UnitCode, WorkDate, UserId, LimitAed, ROW_NUMBER() OVER (PARTITION BY UnitCode, WorkDate ORDER BY LimitAed DESC, Slot) AS Rn
    FROM #Roster WHERE RoleGroup = N'SUPER') x WHERE Rn = 1;
CREATE UNIQUE CLUSTERED INDEX IX_TopSuper ON #TopSuper (UnitCode, WorkDate);
SELECT w.D, (SELECT MIN(w2.D) FROM #WorkDays w2 WHERE w2.D > w.D) AS NextD INTO #NextWorkDay FROM #WorkDays w;
SELECT BranchCode, ROW_NUMBER() OVER (ORDER BY BranchCode) AS N INTO #BranchList FROM dbo.Branches;
CREATE TABLE #Banks (Code char(3), Name nvarchar(60), N int); INSERT INTO #Banks VALUES {banks};
CREATE TABLE #Foreign (Country char(2), Name nvarchar(60), N int); INSERT INTO #Foreign VALUES {foreign};
DECLARE @Banks int = (SELECT COUNT(*) FROM #Banks), @Foreign int = (SELECT COUNT(*) FROM #Foreign);
CREATE TABLE #N (N int PRIMARY KEY); INSERT INTO #N SELECT TOP (30) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;
CREATE TABLE #Months (M int PRIMARY KEY, MonthStart date, MonthEnd date);
INSERT INTO #Months SELECT N, DATEFROMPARTS(2025, N, 1), EOMONTH(DATEFROMPARTS(2025, N, 1)) FROM #N WHERE N <= 12;
-- An account is in use on a day when it is open, not closed yet and not dormant (a reactivated account only from its reactivation).
SELECT AccountNumber, GenKey, BranchCode, CurrencyCode, ProductCode, GenKind, GenScale, OpenedDate, ClosedDate, ReactivatedDate, GenTeller, GenChqIn, GenChqOut, GenTrfOut, GenTrfIn, GenAtm, GenPos,
       CASE CurrencyCode WHEN N'USD' THEN 3.6725 ELSE 1 END AS Fx
INTO #Live FROM dbo.Accounts WHERE Status IN (N'Active', N'Closed') AND ReactivatedDate IS NULL;
CREATE UNIQUE CLUSTERED INDEX IX_Live ON #Live (AccountNumber);
-- Staff never process their own accounts: when a roster pick lands on the account holder, a colleague of the same unit,
-- day and role group takes the work instead (the updates after each table below). The one planted exception is posted later.
SELECT a.AccountNumber, u.UserId INTO #StaffOwner FROM dbo.Accounts a JOIN dbo.Customers c ON c.CIF = a.CIF JOIN dbo.Users u ON u.EmployeeNumber = c.StaffEmployeeNumber;
CREATE UNIQUE CLUSTERED INDEX IX_StaffOwner ON #StaffOwner (AccountNumber);

-- Clearing sessions: two per business day.
INSERT INTO dbo.ClearingSessions (SessionDate, SessionNumber, CutOffTime, SettlementTime)
SELECT w.D, s.S, CASE s.S WHEN 1 THEN '11:00' ELSE '14:30' END, CASE s.S WHEN 1 THEN '13:00' ELSE '16:30' END FROM #WorkDays w CROSS JOIN (VALUES (1), (2)) s(S);

-- Exchange rates: pegged currencies are fixed, the others follow a small daily random walk.
INSERT INTO dbo.FxRates (RateDate, CurrencyCode, RateToAed)
SELECT d.D, c.Code, CASE WHEN c.Pegged = 1 THEN c.Base ELSE c.Base * (1 + 0.04 * SIN(DATEPART(DAYOFYEAR, d.D) / 58.0 + c.Phase) + (r.R1 - 0.5) * 0.006) END
FROM #AllDays d CROSS JOIN (VALUES {fx}) c(Code, Pegged, Base, Phase)
CROSS APPLY gen.Rand(DATEPART(DAYOFYEAR, d.D) * 100 + c.Phase * 10) r;

-- KYC documents: Emirates ID for every individual, passport for expatriates, trade licence for companies.
INSERT INTO dbo.KycDocuments (CIF, DocumentType, DocumentNumber, IssueDate, ExpiryDate, VerifiedByUserId, VerifiedDate, Status)
SELECT c.CIF, k.DocType,
       CASE k.DocType WHEN N'EmiratesId' THEN CONCAT(N'000-', YEAR(c.DateOfBirth), N'-', 1000000 + CAST(r.R1 * 8999999 AS int), N'-', CAST(r.R2 * 9 AS int))
                      WHEN N'Passport' THEN CONCAT(CHAR(65 + CAST(r.R3 * 26 AS int)), CHAR(65 + CAST(r.R4 * 26 AS int)), 1000000 + CAST(r.R5 * 8999999 AS int))
                      ELSE CONCAT(N'CN-', 1000000 + CAST(r.R6 * 8999999 AS int)) END,
       x.Issue, DATEADD(YEAR, k.Years, x.Issue), c.CreatedByUserId, c.OnboardedDate,
       CASE WHEN DATEADD(YEAR, k.Years, x.Issue) < '2025-12-31' THEN N'Expired' ELSE N'Valid' END
FROM dbo.Customers c
CROSS APPLY (SELECT N'EmiratesId', 2 WHERE c.CustomerType = N'Individual' UNION ALL SELECT N'Passport', 10 WHERE c.CustomerType = N'Individual' AND c.NationalityCode <> 'AE'
             UNION ALL SELECT N'TradeLicence', 1 WHERE c.CustomerType <> N'Individual') k(DocType, Years)
CROSS APPLY gen.Rand(CAST(SUBSTRING(c.CIF, 2, 9) AS bigint) * 10 + LEN(k.DocType)) r
CROSS APPLY (SELECT DATEADD(DAY, -CAST(r.R7 * k.Years * 365 * (CASE WHEN r.R8 < 0.07 THEN 1.15 ELSE 0.92 END) AS int), CAST('2025-12-31' AS date)) AS Issue) x;

RAISERROR(N'  clearing sessions, fx, kyc done', 0, 1) WITH NOWAIT;
-- Outward clearing: cheques drawn on other banks deposited by customers, captured by branch operations.
{cand_oc}
INSERT INTO dbo.OutwardCheques (ChequeNumber, DrawnOnBankCode, DrawnOnBankName, DepositAccountNumber, DepositBranchCode, Amount, DepositDate, CapturedByUserId, CapturedAt,
                                SessionId, Status, ReturnReasonCode, CreditDate)
SELECT 100000 + CAST(c.R1 * 899999 AS int), b.Code, b.Name, c.AccountNumber, c.BranchCode, ROUND(c.GenScale * (0.15 + 2.2 * c.R2 * c.R2) * c.Fx, 0), c.D, ro.UserId,
       DATEADD(MINUTE, 480 + CAST(c.R3 * 420 AS int), CAST(c.D AS datetime2(0))), cs.SessionId,
       CASE WHEN c.R4 < x.ReturnRate THEN N'Returned' ELSE N'Cleared' END,
       CASE WHEN c.R4 < x.ReturnRate THEN CASE WHEN c.R5 < 0.6 THEN '01' WHEN c.R5 < 0.75 THEN '02' WHEN c.R5 < 0.85 THEN '04' ELSE '06' END END,
       CASE WHEN c.R4 >= x.ReturnRate THEN nx.NextD END
FROM #OC c
CROSS APPLY (SELECT CASE c.BranchCode WHEN N'BR-SHJ01' THEN 0.09 ELSE 0.03 END AS ReturnRate) x
{ro_oc}
JOIN #Banks b ON b.N = 1 + CAST(c.R8 * @Banks AS int)
JOIN dbo.ClearingSessions cs ON cs.SessionDate = c.D AND cs.SessionNumber = CASE WHEN c.R3 < 0.45 THEN 1 ELSE 2 END
JOIN #NextWorkDay nx ON nx.D = c.D
WHERE c.CurrencyCode = 'AED';
UPDATE x SET CapturedByUserId = alt.UserId
FROM dbo.OutwardCheques x JOIN #StaffOwner so ON so.AccountNumber = x.DepositAccountNumber AND so.UserId = x.CapturedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = x.DepositBranchCode AND r.WorkDate = x.DepositDate AND r.RoleGroup = N'BROPS' AND r.UserId <> so.UserId
             ORDER BY r.Slot) alt;
RAISERROR(N'  outward cheques done', 0, 1) WITH NOWAIT;
-- Inward clearing: cheques written by our customers and presented by other banks; processed by Central Operations clearing.
{cand_ic}
INSERT INTO dbo.InwardCheques (AccountNumber, BranchCode, PresentingBankCode, PresentingBankName, Amount, PresentedDate, SessionId, ProcessedByUserId, ProcessedAt, Status, ReturnReasonCode)
SELECT c.AccountNumber, c.BranchCode, b.Code, b.Name, ROUND(c.GenScale * (0.1 + 1.6 * c.R2 * c.R2), 0), c.D, cs.SessionId, ro.UserId,
       DATEADD(MINUTE, CASE WHEN cs.SessionNumber = 1 THEN 660 ELSE 870 END + CAST(c.R3 * 90 AS int), CAST(c.D AS datetime2(0))),
       CASE WHEN c.R4 < x.ReturnRate THEN N'Returned' ELSE N'Paid' END,
       CASE WHEN c.R4 < x.ReturnRate THEN CASE WHEN c.R5 < 0.7 THEN '01' WHEN c.R5 < 0.82 THEN '02' WHEN c.R5 < 0.92 THEN '03' ELSE '04' END END
FROM #IC c
CROSS APPLY (SELECT CASE c.BranchCode WHEN N'BR-SHJ01' THEN 0.085 ELSE 0.028 END AS ReturnRate) x
{ro_ic}
JOIN #Banks b ON b.N = 1 + CAST(c.R8 * @Banks AS int)
JOIN dbo.ClearingSessions cs ON cs.SessionDate = c.D AND cs.SessionNumber = CASE WHEN c.R3 < 0.5 THEN 1 ELSE 2 END
WHERE c.D >= DATEADD(DAY, 14, c.OpenedDate);
-- Planted: two customers with four returned cheques (insufficient funds) in December.
CREATE TABLE #Returned (AccountNumber char(12), Seq int, BankCode char(3), Amount decimal(18,2), D date, UserId nvarchar(20));
{returned}
INSERT INTO dbo.InwardCheques (AccountNumber, BranchCode, PresentingBankCode, PresentingBankName, Amount, PresentedDate, SessionId, ProcessedByUserId, ProcessedAt, Status, ReturnReasonCode)
SELECT x.AccountNumber, a.BranchCode, x.BankCode, b.Name, x.Amount, x.D, cs.SessionId, x.UserId, DATEADD(MINUTE, 690, CAST(x.D AS datetime2(0))), N'Returned', '01'
FROM #Returned x JOIN dbo.Accounts a ON a.AccountNumber = x.AccountNumber JOIN #Banks b ON b.Code = x.BankCode
JOIN dbo.ClearingSessions cs ON cs.SessionDate = x.D AND cs.SessionNumber = 1;
-- Cheque numbers come from the cheque books issued on the account (25 leaves per book).
WITH n AS (SELECT ChequeId, ROW_NUMBER() OVER (PARTITION BY AccountNumber ORDER BY PresentedDate, ChequeId) AS Rn FROM dbo.InwardCheques)
UPDATE c SET ChequeNumber = 100000 + n.Rn FROM dbo.InwardCheques c JOIN n ON n.ChequeId = c.ChequeId;
INSERT INTO dbo.ChequeBooks (AccountNumber, FirstLeaf, LastLeaf, Leaves, IssuedDate, IssuedByUserId, Status)
SELECT bk.AccountNumber, 100001 + bk.Book * 25, 100025 + bk.Book * 25, 25, iss.D, ISNULL(ro.UserId, a.OpenedByUserId),
       CASE WHEN bk.Used >= 25 THEN N'Exhausted' ELSE N'InUse' END
FROM (SELECT AccountNumber, (ChequeNumber - 100001) / 25 AS Book, MIN(PresentedDate) AS FirstUse, COUNT(*) AS Used FROM dbo.InwardCheques GROUP BY AccountNumber, (ChequeNumber - 100001) / 25) bk
JOIN dbo.Accounts a ON a.AccountNumber = bk.AccountNumber
CROSS APPLY (SELECT MAX(w.D) AS D FROM #WorkDays w WHERE w.D <= DATEADD(DAY, -10, bk.FirstUse) AND w.D >= a.OpenedDate) i0
CROSS APPLY (SELECT ISNULL(i0.D, a.OpenedDate) AS D) iss
LEFT JOIN #Roster ro ON ro.UnitCode = a.BranchCode AND ro.WorkDate = iss.D AND ro.RoleGroup = N'CSV' AND ro.Slot = 1;

UPDATE x SET IssuedByUserId = alt.UserId
FROM dbo.ChequeBooks x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.IssuedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = (SELECT a.BranchCode FROM dbo.Accounts a WHERE a.AccountNumber = x.AccountNumber) AND r.WorkDate = x.IssuedDate AND r.RoleGroup = N'CSV' AND r.UserId <> so.UserId
             ORDER BY r.Slot) alt;
RAISERROR(N'  inward cheques and books done', 0, 1) WITH NOWAIT;
-- Outward transfers initiated at the branch, processed by Central Operations (maker), approved by a supervisor (checker),
-- SWIFT ones screened by Compliance.
{cand_ot}
INSERT INTO dbo.OutwardTransfers (AccountNumber, TransferType, Amount, CurrencyCode, AmountAed, BeneficiaryName, BeneficiaryBank, BeneficiaryCountry, BeneficiaryAccount, Purpose,
                                  InitiatedAtUnitCode, CreatedByUserId, CreatedAt, ScreeningStatus, ScreenedByUserId, ProcessedByUserId, ApprovedByUserId, ProcessedAt, ValueDate,
                                  ChargesAed, Status)
SELECT c.AccountNumber, t.Kind, t.Amount, c.CurrencyCode, ROUND(t.Amount * c.Fx, 2),
       CASE WHEN t.Kind = N'Local' THEN CONCAT(N'Supplier ', 1000 + CAST(c.R9 * 8999 AS int)) ELSE CONCAT(N'Overseas supplier ', 100 + CAST(c.R9 * 899 AS int)) END,
       CASE WHEN t.Kind = N'Local' THEN b.Name ELSE f.Name END, CASE WHEN t.Kind = N'Local' THEN 'AE' ELSE f.Country END,
       CASE WHEN t.Kind = N'Local' THEN CONCAT('AE', 10 + CAST(c.R10 * 89 AS int), b.Code, RIGHT(CONCAT('0000000000000000', CAST(c.R11 * 1e15 AS bigint)), 16))
            ELSE CONCAT(f.Country, CAST(c.R11 * 1e12 AS bigint)) END,
       CASE WHEN c.R12 < 0.6 THEN N'Supplier payment' WHEN c.R12 < 0.8 THEN N'Rent' WHEN c.R12 < 0.9 THEN N'Family support' ELSE N'Services' END,
       c.BranchCode, cr.UserId, DATEADD(MINUTE, 480 + CAST(c.R3 * 330 AS int), CAST(c.D AS datetime2(0))),
       CASE WHEN t.Kind = N'SWIFT' THEN CASE WHEN c.R13 < 0.03 THEN N'HitReleased' ELSE N'Clear' END ELSE N'NotRequired' END,
       CASE WHEN t.Kind = N'SWIFT' THEN aml.UserId END, mk.UserId, ck.UserId,
       DATEADD(MINUTE, 900 + CAST(c.R14 * 60 AS int), CAST(c.D AS datetime2(0))), c.D, CASE WHEN t.Kind = N'Local' THEN 10.50 ELSE 75.00 END, N'Processed'
FROM #OT c
CROSS APPLY (SELECT CASE WHEN c.R1 < 0.72 THEN N'Local' ELSE N'SWIFT' END AS Kind, ROUND(c.GenScale * (0.2 + 2.4 * c.R2 * c.R2), 0) AS Amount) t
{ro_ot_cr}
{ro_ot_mk}
JOIN #Roster ck ON ck.UnitCode = N'OPS' AND ck.WorkDate = c.D AND ck.RoleGroup = N'PAYC' AND ck.Slot = 1
{ro_ot_aml}
JOIN #Banks b ON b.N = 1 + CAST(c.R5 * @Banks AS int)
JOIN #Foreign f ON f.N = 1 + CAST(c.R16 * @Foreign AS int)
WHERE ck.LimitAed >= t.Amount * c.Fx;
UPDATE x SET CreatedByUserId = alt.UserId
FROM dbo.OutwardTransfers x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.CreatedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = x.InitiatedAtUnitCode AND r.WorkDate = x.ValueDate AND r.RoleGroup = N'BROPS' AND r.UserId <> so.UserId
             ORDER BY r.Slot) alt;
UPDATE x SET ProcessedByUserId = alt.UserId
FROM dbo.OutwardTransfers x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.ProcessedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = N'OPS' AND r.WorkDate = x.ValueDate AND r.RoleGroup = N'PAYM' AND r.UserId <> so.UserId AND r.UserId <> ISNULL(x.ApprovedByUserId, N'')
             ORDER BY r.Slot) alt;
UPDATE x SET ApprovedByUserId = alt.UserId
FROM dbo.OutwardTransfers x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.ApprovedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = N'OPS' AND r.WorkDate = x.ValueDate AND r.RoleGroup = N'PAYC' AND r.UserId <> so.UserId AND r.UserId <> ISNULL(x.ProcessedByUserId, N'') AND r.LimitAed >= x.AmountAed
             ORDER BY r.Slot) alt;
UPDATE x SET ScreenedByUserId = alt.UserId
FROM dbo.OutwardTransfers x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.ScreenedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = N'OPS' AND r.WorkDate = x.ValueDate AND r.RoleGroup = N'AML' AND r.UserId <> so.UserId
             ORDER BY r.Slot) alt;
RAISERROR(N'  outward transfers done', 0, 1) WITH NOWAIT;
-- Inward transfers: local ones posted by the clearing interface, SWIFT ones by a payments officer at work that day.
{cand_it}
INSERT INTO dbo.InwardTransfers (AccountNumber, TransferType, Amount, CurrencyCode, AmountAed, OrderingCustomer, OrderingBank, OrderingCountry, ReceivedAt, PostedByUserId, ValueDate, Status)
SELECT c.AccountNumber, t.Kind, t.Amount, c.CurrencyCode, ROUND(t.Amount * c.Fx, 2), CONCAT(N'Customer ', 1000 + CAST(c.R9 * 8999 AS int)),
       CASE WHEN t.Kind = N'Local' THEN b.Name ELSE f.Name END, CASE WHEN t.Kind = N'Local' THEN 'AE' ELSE f.Country END,
       DATEADD(MINUTE, 450 + CAST(c.R3 * 420 AS int), CAST(c.D AS datetime2(0))), CASE WHEN t.Kind = N'Local' THEN N'SYS_CLEARING' ELSE pm.UserId END, c.D, N'Posted'
FROM #ITR c
CROSS APPLY (SELECT CASE WHEN c.R1 < 0.75 THEN N'Local' ELSE N'SWIFT' END AS Kind, ROUND(c.GenScale * (0.3 + 2.6 * c.R2 * c.R2), 0) AS Amount) t
{ro_it}
JOIN #Banks b ON b.N = 1 + CAST(c.R5 * @Banks AS int)
JOIN #Foreign f ON f.N = 1 + CAST(c.R16 * @Foreign AS int);
UPDATE x SET PostedByUserId = alt.UserId
FROM dbo.InwardTransfers x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.PostedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = N'OPS' AND r.WorkDate = x.ValueDate AND r.RoleGroup = N'PAYM' AND r.UserId <> so.UserId
             ORDER BY r.Slot) alt;
RAISERROR(N'  inward transfers done', 0, 1) WITH NOWAIT;
-- Planted: a dormant account reactivated in November receives a large SWIFT transfer and sends it on the next day.
CREATE TABLE #Pass (AccountNumber char(12), InDay date, InUser nvarchar(20), OutDay date, Maker nvarchar(20), Checker nvarchar(20), Aml nvarchar(20), Initiator nvarchar(20), BranchCode nvarchar(10));
{passthrough}
INSERT INTO dbo.InwardTransfers (AccountNumber, TransferType, Amount, CurrencyCode, AmountAed, OrderingCustomer, OrderingBank, OrderingCountry, ReceivedAt, PostedByUserId, ValueDate, Status)
SELECT AccountNumber, N'SWIFT', 512000, 'AED', 512000, N'Eastline Holdings Ltd', N'Hudson River Bank', 'US', DATEADD(MINUTE, 610, CAST(InDay AS datetime2(0))), InUser, InDay, N'Posted' FROM #Pass;
INSERT INTO dbo.OutwardTransfers (AccountNumber, TransferType, Amount, CurrencyCode, AmountAed, BeneficiaryName, BeneficiaryBank, BeneficiaryCountry, BeneficiaryAccount, Purpose,
                                  InitiatedAtUnitCode, CreatedByUserId, CreatedAt, ScreeningStatus, ScreenedByUserId, ProcessedByUserId, ApprovedByUserId, ProcessedAt, ValueDate, ChargesAed, Status)
SELECT AccountNumber, N'SWIFT', 495000, 'AED', 495000, N'Northgate Trading FZE', N'Manila Harbour Bank', 'PH', N'PH778811223344', N'Services', BranchCode, Initiator,
       DATEADD(MINUTE, 560, CAST(OutDay AS datetime2(0))), N'Clear', Aml, Maker, Checker, DATEADD(MINUTE, 905, CAST(OutDay AS datetime2(0))), OutDay, 75.00, N'Processed' FROM #Pass;
UPDATE dbo.OutwardTransfers SET TransferReference = CONCAT(N'OT25', RIGHT(CONCAT(N'000000', TransferId), 6));
UPDATE dbo.InwardTransfers SET TransferReference = CONCAT(N'IT25', RIGHT(CONCAT(N'000000', TransferId), 6));
GO

-- Postings ---------------------------------------------------------------------------------------------------------------
CREATE TABLE #Tx (AccountNumber char(12) NOT NULL, TransactionCode nvarchar(4) NOT NULL, Amount decimal(18,2) NOT NULL, PostedAt datetime2(0) NOT NULL, Channel nvarchar(10) NOT NULL,
    PostingUnitCode nvarchar(10) NOT NULL, PostedByUserId nvarchar(20) NOT NULL, ApprovedByUserId nvarchar(20) NULL, Narrative nvarchar(140) NULL, SourceReference nvarchar(20) NULL,
    IsTeller bit NOT NULL DEFAULT 0, RevKey bigint NULL);
DECLARE @ReversalTeller nvarchar(20) = N'{reversal_teller}';
-- Salary (WPS through the clearing interface; staff salaries by the end-of-day batch), on the last business day on or before the salary day.
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative)
SELECT a.AccountNumber, N'SAL', ROUND(a.GenSalary * (0.97 + 0.06 * r.R1), 2), DATEADD(MINUTE, 120 + CAST(r.R2 * 300 AS int), CAST(pd.D AS datetime2(0))), N'Clearing', N'OPS',
       CASE WHEN a.GenEmployer = N'Wabi Bank' THEN N'SYS_EOD' ELSE N'SYS_CLEARING' END, CONCAT(N'Salary ', FORMAT(m.MonthStart, 'MMM yyyy'), N' - ', a.GenEmployer)
FROM dbo.Accounts a JOIN #Months m ON 1 = 1
CROSS APPLY gen.Rand(a.GenKey * 100 + m.M + 11) r
CROSS APPLY (SELECT MAX(w.D) AS D FROM #WorkDays w WHERE w.D <= DATEFROMPARTS(2025, m.M, a.GenSalaryDay)) pd
WHERE a.GenSalary IS NOT NULL AND a.Status IN (N'Active', N'Closed') AND pd.D >= a.OpenedDate AND (a.ClosedDate IS NULL OR pd.D < a.ClosedDate)
  AND (a.GenSalaryFrom IS NULL OR pd.D >= a.GenSalaryFrom) AND (a.GenSalaryTo IS NULL OR pd.D <= a.GenSalaryTo);
RAISERROR(N'  salaries done', 0, 1) WITH NOWAIT;
-- ATM withdrawals (any day) and card purchases.
{cand_atm}
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative)
SELECT c.AccountNumber, N'ATW', CASE WHEN x.Amt < 100 THEN 100 WHEN x.Amt > 5000 THEN 5000 ELSE x.Amt END,
       DATEADD(MINUTE, 360 + CAST(c.R3 * 1060 AS int), CAST(c.D AS datetime2(0))), N'ATM', CASE WHEN c.R4 < 0.8 THEN c.BranchCode ELSE bo.BranchCode END, N'SYS_ATM', N'ATM withdrawal'
FROM #ATM c CROSS APPLY (SELECT ROUND(c.GenScale * (0.15 + 0.9 * c.R2) / 100, 0) * 100 AS Amt) x
JOIN #BranchList bo ON bo.N = 1 + CAST(c.R5 * 6 AS int);
{cand_pos}
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative)
SELECT c.AccountNumber, N'POS', ROUND(20 + c.GenScale * 0.6 * c.R2 * c.R2, 2), DATEADD(MINUTE, 420 + CAST(c.R3 * 960 AS int), CAST(c.D AS datetime2(0))), N'Card', N'OPS', N'SYS_ATM',
       CHOOSE(1 + CAST(c.R4 * 8 AS int), N'Supermarket', N'Fuel station', N'Restaurant', N'Online retailer', N'Pharmacy', N'Electronics store', N'Clothing store', N'Utilities')
FROM #POS c;
RAISERROR(N'  atm and cards done', 0, 1) WITH NOWAIT;
-- Teller cash deposits and withdrawals: at the home branch (mostly), by a teller at work that day; above the teller's limit
-- the supervisor of the branch with the highest limit at work that day approves.
{cand_tl}
-- Counter cash is smaller than the account's transfer scale: individuals up to about AED 30,000 a visit, companies about a
-- quarter of their scale (large cash moves through cash-in-transit, not the counter).
SELECT c.*, CASE WHEN c.R1 < 0.85 THEN c.BranchCode ELSE bo.BranchCode END AS B,
       CASE c.GenKind WHEN N'corporate' THEN c.GenScale * 0.23 WHEN N'sme' THEN c.GenScale * 0.8 ELSE CASE WHEN c.GenScale > 12000 THEN 12000 ELSE c.GenScale END END AS TScale
INTO #TL2 FROM #TL c JOIN #BranchList bo ON bo.N = 1 + CAST(c.R9 * 6 AS int);
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, ApprovedByUserId, Narrative, IsTeller, RevKey)
SELECT c.AccountNumber, t.Code, t.Amount, DATEADD(MINUTE, 480 + CAST(c.R3 * 420 AS int), CAST(c.D AS datetime2(0))), N'Branch', c.B, tl.UserId,
       CASE WHEN t.Amount * c.Fx > tl.LimitAed THEN sup.UserId END, CASE t.Code WHEN N'CDP' THEN N'Cash deposit' ELSE N'Cash withdrawal' END, 1, c.GenKey * 10000 + c.M * 100 + c.N
FROM #TL2 c
JOIN #RosterCount tlc ON tlc.UnitCode = c.B AND tlc.WorkDate = c.D AND tlc.RoleGroup = N'TELLER'
JOIN #Roster tl ON tl.UnitCode = c.B AND tl.WorkDate = c.D AND tl.RoleGroup = N'TELLER' AND tl.Slot = 1 + CAST(c.R7 * tlc.Slots AS int)
JOIN #TopSuper sup ON sup.UnitCode = c.B AND sup.WorkDate = c.D
CROSS APPLY (SELECT CASE WHEN c.R8 < 0.58 THEN N'CDP' ELSE N'CWD' END AS Code,
                    CASE WHEN ROUND(c.TScale * (0.1 + 2.5 * c.R2 * c.R2 * c.R2), -1) * c.Fx > sup.LimitAed THEN ROUND(sup.LimitAed / c.Fx * 0.9, -2)
                         ELSE ROUND(c.TScale * (0.1 + 2.5 * c.R2 * c.R2 * c.R2), -1) END AS Amount) t
WHERE t.Amount > 0;
RAISERROR(N'  teller postings done', 0, 1) WITH NOWAIT;
-- Planted postings (teller posting to own account, structured cash deposits).
CREATE TABLE #Planted (AccountNumber char(12), Code nvarchar(4), Amount decimal(18,2), At datetime2(0), Unit nvarchar(10), UserId nvarchar(20), Approver nvarchar(20), Narrative nvarchar(140));
{planted}
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, ApprovedByUserId, Narrative, IsTeller)
SELECT AccountNumber, Code, Amount, At, N'Branch', Unit, UserId, Approver, CASE Code WHEN N'CDP' THEN N'Cash deposit' ELSE N'Cash withdrawal' END, 1 FROM #Planted;
-- Cheques: deposits credited on the next business day, presented cheques debited when paid (clearing interface).
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative, SourceReference)
SELECT DepositAccountNumber, N'CHD', Amount, DATEADD(MINUTE, 600, CAST(CreditDate AS datetime2(0))), N'Clearing', N'OPS', N'SYS_CLEARING', CONCAT(N'Cheque ', ChequeNumber, N' ', DrawnOnBankName),
       CONCAT(N'OC', ChequeId) FROM dbo.OutwardCheques WHERE Status = N'Cleared' AND CreditDate <= '2025-12-31';
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative, SourceReference)
SELECT AccountNumber, N'CHP', Amount, ProcessedAt, N'Clearing', N'OPS', N'SYS_CLEARING', CONCAT(N'Cheque ', ChequeNumber, N' presented by ', PresentingBankName), CONCAT(N'IC', ChequeId)
FROM dbo.InwardCheques WHERE Status = N'Paid';
-- Transfers (maker posts, checker approves) and their charges; inward transfers.
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, ApprovedByUserId, Narrative, SourceReference)
SELECT AccountNumber, N'OTR', Amount, ProcessedAt, N'Transfer', N'OPS', ProcessedByUserId, ApprovedByUserId, CONCAT(TransferType, N' transfer to ', BeneficiaryName), TransferReference
FROM dbo.OutwardTransfers WHERE Status = N'Processed'
UNION ALL
SELECT o.AccountNumber, N'FEE', ROUND(o.ChargesAed / CASE o.CurrencyCode WHEN N'USD' THEN 3.6725 ELSE 1 END, 2), DATEADD(SECOND, 1, o.ProcessedAt), N'Transfer', N'OPS', o.ProcessedByUserId,
       o.ApprovedByUserId, N'Transfer charges', o.TransferReference FROM dbo.OutwardTransfers o WHERE o.Status = N'Processed';
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative, SourceReference)
SELECT AccountNumber, N'ITR', Amount, ReceivedAt, N'Transfer', N'OPS', PostedByUserId, CONCAT(TransferType, N' transfer from ', OrderingCustomer), TransferReference FROM dbo.InwardTransfers;
-- Digital channel transfers (WabiBankDigitalDB.ChannelTransfers), posted by the channel's service account.
CREATE TABLE #Digital (TransferId int, FromAccount char(12), ToAccount char(12) NULL, Amount decimal(18,2), At datetime2(0), Ref nvarchar(20), Kind nvarchar(20));
{digital}
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative, SourceReference)
SELECT FromAccount, N'DTD', Amount, At, N'Digital', N'DCC', N'SYS_DIGITAL', CONCAT(N'Digital transfer (', Kind, N')'), Ref FROM #Digital
UNION ALL SELECT ToAccount, N'DTC', Amount, At, N'Digital', N'DCC', N'SYS_DIGITAL', CONCAT(N'Digital transfer (', Kind, N')'), Ref FROM #Digital WHERE ToAccount IS NOT NULL;
RAISERROR(N'  cheque, transfer and digital postings done', 0, 1) WITH NOWAIT;
-- Monthly fees (end-of-day batch on the last day of the month) and quarterly savings interest.
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative)
SELECT a.AccountNumber, N'FEE', p.MonthlyFee, DATEADD(MINUTE, 1410, CAST(m.MonthEnd AS datetime2(0))), N'Batch', N'OPS', N'SYS_EOD', CONCAT(N'Monthly account fee ', FORMAT(m.MonthStart, 'MMM yyyy'))
FROM dbo.Accounts a JOIN dbo.Products p ON p.ProductCode = a.ProductCode JOIN #Months m ON 1 = 1
WHERE p.MonthlyFee > 0 AND a.Status IN (N'Active', N'Closed') AND a.OpenedDate <= m.MonthEnd AND (a.ClosedDate IS NULL OR a.ClosedDate > m.MonthEnd)
  AND (a.ReactivatedDate IS NULL OR a.ReactivatedDate <= m.MonthEnd);
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative)
SELECT a.AccountNumber, N'INT', ROUND(a.GenScale * 4 * p.InterestRatePct / 400 * (0.6 + 0.8 * r.R1), 2), DATEADD(MINUTE, 1415, CAST(m.MonthEnd AS datetime2(0))), N'Batch', N'OPS', N'SYS_EOD',
       CONCAT(N'Interest Q', m.M / 3)
FROM dbo.Accounts a JOIN dbo.Products p ON p.ProductCode = a.ProductCode JOIN #Months m ON m.M % 3 = 0
CROSS APPLY gen.Rand(a.GenKey * 100 + m.M + 29) r
WHERE p.InterestRatePct > 0 AND a.Status IN (N'Active', N'Closed') AND a.ReactivatedDate IS NULL AND a.OpenedDate <= m.MonthEnd AND (a.ClosedDate IS NULL OR a.ClosedDate > m.MonthEnd);
-- Fixed deposits placed or maturing (credited back) in 2025.
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative, SourceReference)
SELECT FundingAccountNumber, N'FDP', Principal, DATEADD(MINUTE, 600, CAST(StartDate AS datetime2(0))), N'Branch', BranchCode, BookedByUserId, CONCAT(N'Fixed deposit ', DepositNumber), DepositNumber
FROM dbo.FixedDeposits WHERE StartDate >= '2025-01-01'
UNION ALL
SELECT FundingAccountNumber, N'FDM', Principal + InterestAtMaturity, DATEADD(MINUTE, 300, CAST(MaturityDate AS datetime2(0))), N'Batch', N'OPS', N'SYS_EOD', CONCAT(N'Maturity ', DepositNumber), DepositNumber
FROM dbo.FixedDeposits WHERE MaturityDate <= '2025-12-31' AND RenewalInstruction = N'Credit to account';
GO

-- Loans: schedules, repayments (auto loans slip in Q3), disbursements and instalment postings ------------------------------
INSERT INTO dbo.LoanSchedules (LoanNumber, InstallmentNumber, DueDate, PrincipalDue, InterestDue, InstallmentDue, OutstandingAfter)
SELECT l.LoanNumber, k.K, DATEADD(MONTH, k.K, l.StartDate),
       ROUND(l.InstallmentAmount - x.OutBefore * l.InterestRatePct / 1200, 2), ROUND(x.OutBefore * l.InterestRatePct / 1200, 2), l.InstallmentAmount,
       CASE WHEN x.OutAfter < 0 THEN 0 ELSE ROUND(x.OutAfter, 2) END
FROM dbo.Loans l
CROSS APPLY (SELECT TOP (l.TenorMonths) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS K FROM sys.all_objects a CROSS JOIN sys.all_objects b) k
CROSS APPLY (SELECT CAST(l.InterestRatePct AS float) / 1200 AS R) rr
CROSS APPLY (SELECT CAST(l.Principal AS float) * POWER(1 + rr.R, k.K - 1) - CAST(l.InstallmentAmount AS float) * (POWER(1 + rr.R, k.K - 1) - 1) / rr.R AS OutBefore,
                    CAST(l.Principal AS float) * POWER(1 + rr.R, k.K) - CAST(l.InstallmentAmount AS float) * (POWER(1 + rr.R, k.K) - 1) / rr.R AS OutAfter) x
WHERE DATEADD(MONTH, k.K, l.StartDate) BETWEEN '2025-01-01' AND '2025-12-31';
INSERT INTO dbo.LoanRepayments (LoanNumber, InstallmentNumber, DueDate, AmountDue, PaidDate, AmountPaid, DaysLate, Status)
SELECT s.LoanNumber, s.InstallmentNumber, s.DueDate, s.InstallmentDue, p.PaidDate, CASE WHEN p.PaidDate IS NULL THEN 0 ELSE s.InstallmentDue END,
       CASE WHEN p.PaidDate IS NOT NULL THEN DATEDIFF(DAY, s.DueDate, p.PaidDate) END,
       CASE WHEN p.PaidDate IS NULL THEN N'Unpaid' WHEN p.PaidDate > s.DueDate THEN N'Late' ELSE N'OnTime' END
FROM dbo.LoanSchedules s JOIN dbo.Loans l ON l.LoanNumber = s.LoanNumber
CROSS APPLY gen.Rand(CAST(SUBSTRING(s.LoanNumber, 3, 10) AS bigint) * 1000 + s.InstallmentNumber) r
CROSS APPLY (SELECT CASE WHEN l.ProductCode = N'AL' AND MONTH(s.DueDate) BETWEEN 7 AND 9 THEN 0.16 ELSE 0.025 END AS Miss, 0.08 AS Late) q
CROSS APPLY (SELECT CASE WHEN r.R1 < q.Miss THEN NULL
                         WHEN r.R1 < q.Miss + q.Late THEN CASE WHEN DATEADD(DAY, 5 + CAST(r.R2 * 40 AS int), s.DueDate) <= '2025-12-31' THEN DATEADD(DAY, 5 + CAST(r.R2 * 40 AS int), s.DueDate) END
                         ELSE s.DueDate END AS PaidDate) p;
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, ApprovedByUserId, Narrative, SourceReference)
SELECT RepaymentAccountNumber, N'LDB', Principal, DATEADD(MINUTE, 720, CAST(StartDate AS datetime2(0))), N'Branch', N'OPS', DisbursedByUserId, DisbursementApprovedByUserId,
       CONCAT(N'Loan disbursement ', LoanNumber), LoanNumber FROM dbo.Loans WHERE StartDate >= '2025-01-01';
INSERT INTO #Tx (AccountNumber, TransactionCode, Amount, PostedAt, Channel, PostingUnitCode, PostedByUserId, Narrative, SourceReference)
SELECT l.RepaymentAccountNumber, N'LRP', p.AmountPaid, DATEADD(MINUTE, 250, CAST(p.PaidDate AS datetime2(0))), N'Batch', N'OPS', N'SYS_EOD',
       CONCAT(N'Instalment ', p.InstallmentNumber, N' ', p.LoanNumber), p.LoanNumber
FROM dbo.LoanRepayments p JOIN dbo.Loans l ON l.LoanNumber = p.LoanNumber WHERE p.PaidDate IS NOT NULL;
UPDATE l SET Status = CASE WHEN l.MaturityDate <= '2025-12-31' THEN N'Closed' ELSE N'Active' END FROM dbo.Loans l;
INSERT INTO dbo.MonthEndArrears (LoanNumber, MonthEnd, ProductCode, BranchCode, OverdueInstallments, OverdueAmount, DaysPastDue, Bucket)
SELECT l.LoanNumber, m.MonthEnd, l.ProductCode, l.BranchCode, ISNULL(o.Cnt, 0), ISNULL(o.Amt, 0), ISNULL(DATEDIFF(DAY, o.Oldest, m.MonthEnd), 0),
       CASE WHEN o.Cnt IS NULL THEN N'Current' WHEN DATEDIFF(DAY, o.Oldest, m.MonthEnd) <= 30 THEN N'1-30' WHEN DATEDIFF(DAY, o.Oldest, m.MonthEnd) <= 60 THEN N'31-60'
            WHEN DATEDIFF(DAY, o.Oldest, m.MonthEnd) <= 90 THEN N'61-90' ELSE N'90+' END
FROM dbo.Loans l JOIN #Months m ON l.StartDate <= m.MonthEnd AND l.MaturityDate > m.MonthStart
OUTER APPLY (SELECT COUNT(*) AS Cnt, SUM(p.AmountDue) AS Amt, MIN(p.DueDate) AS Oldest FROM dbo.LoanRepayments p
             WHERE p.LoanNumber = l.LoanNumber AND p.DueDate <= m.MonthEnd AND (p.PaidDate IS NULL OR p.PaidDate > m.MonthEnd) HAVING COUNT(*) > 0) o;
-- Outstanding principal at each month end: after the last instalment due by then (the full principal before the first one).
INSERT INTO dbo.MonthEndLoanBalances (LoanNumber, MonthEnd, CIF, ProductCode, BranchCode, OutstandingPrincipal)
SELECT l.LoanNumber, m.MonthEnd, l.CIF, l.ProductCode, l.BranchCode, ISNULL(s.OutstandingAfter, l.Principal)
FROM dbo.Loans l JOIN #Months m ON l.StartDate <= m.MonthEnd AND l.MaturityDate > m.MonthEnd
OUTER APPLY (SELECT TOP (1) x.OutstandingAfter FROM dbo.LoanSchedules x WHERE x.LoanNumber = l.LoanNumber AND x.DueDate <= m.MonthEnd ORDER BY x.InstallmentNumber DESC) s;
GO

RAISERROR(N'  loans done', 0, 1) WITH NOWAIT;
-- Tellers never serve their own account (except the planted case): another teller, else a supervisor at work that day takes
-- the posting; with nobody else at the counter the customer comes back another day (the posting is dropped).
UPDATE x SET PostedByUserId = alt.UserId
FROM #Tx x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.PostedByUserId
CROSS APPLY (SELECT TOP 1 r.UserId FROM #Roster r WHERE r.UnitCode = x.PostingUnitCode AND r.WorkDate = CAST(x.PostedAt AS date) AND r.RoleGroup IN (N'TELLER', N'SUPER')
                AND r.UserId <> so.UserId AND r.UserId <> ISNULL(x.ApprovedByUserId, N'')
                AND r.LimitAed >= (SELECT r0.LimitAed FROM #Roster r0 WHERE r0.UnitCode = r.UnitCode AND r0.WorkDate = r.WorkDate AND r0.RoleGroup = N'TELLER' AND r0.UserId = x.PostedByUserId)
             ORDER BY CASE r.RoleGroup WHEN N'TELLER' THEN 0 ELSE 1 END, r.Slot) alt
WHERE x.IsTeller = 1 AND NOT EXISTS (SELECT 1 FROM #Planted p WHERE p.AccountNumber = x.AccountNumber AND p.UserId = x.PostedByUserId AND p.At = x.PostedAt);
DELETE x FROM #Tx x JOIN #StaffOwner so ON so.AccountNumber = x.AccountNumber AND so.UserId = x.PostedByUserId
WHERE x.IsTeller = 1 AND NOT EXISTS (SELECT 1 FROM #Planted p WHERE p.AccountNumber = x.AccountNumber AND p.UserId = x.PostedByUserId AND p.At = x.PostedAt);
-- Load the postings in time order, reverse a few teller postings, then derive balances --------------------------------
INSERT INTO dbo.Transactions (TransactionReference, AccountNumber, TransactionCode, Direction, Amount, CurrencyCode, AmountAed, ValueDate, PostedAt, Channel, PostingUnitCode,
                              PostedByUserId, ApprovedByUserId, Status, Narrative, SourceReference)
SELECT N'', t.AccountNumber, t.TransactionCode, tc.Direction, t.Amount, a.CurrencyCode, ROUND(t.Amount * CASE a.CurrencyCode WHEN N'USD' THEN 3.6725 ELSE 1 END, 2),
       CAST(t.PostedAt AS date), t.PostedAt, t.Channel, t.PostingUnitCode, t.PostedByUserId, t.ApprovedByUserId,
       CASE WHEN t.IsTeller = 1 AND r.R1 < CASE WHEN t.PostedByUserId = N'{reversal_teller}' THEN 0.085 ELSE 0.004 END THEN N'Reversed' ELSE N'Posted' END,
       t.Narrative, t.SourceReference
FROM #Tx t JOIN dbo.Accounts a ON a.AccountNumber = t.AccountNumber JOIN dbo.TransactionCodes tc ON tc.TransactionCode = t.TransactionCode
CROSS APPLY gen.Rand(ISNULL(t.RevKey, CHECKSUM(t.AccountNumber, t.PostedAt)) + 7) r
WHERE t.PostedAt < '2026-01-01'
ORDER BY t.PostedAt, t.AccountNumber, t.TransactionCode;
CREATE INDEX IX_Transactions_Account ON dbo.Transactions (AccountNumber, PostedAt, TransactionId) INCLUDE (BalanceAfter, Status, Direction, Amount);
RAISERROR(N'  transactions loaded', 0, 1) WITH NOWAIT;
UPDATE t SET ReversedByUserId = t.PostedByUserId, ReversedAt = DATEADD(MINUTE, 4 + t.TransactionId % 35, t.PostedAt)
FROM dbo.Transactions t WHERE t.Status = N'Reversed';
UPDATE dbo.Transactions SET TransactionReference = CONCAT(N'TX25', RIGHT(CONCAT(N'00000000', TransactionId), 8));
RAISERROR(N'  reversals done', 0, 1) WITH NOWAIT;
-- Opening balances are set so that no account ever goes below zero; BalanceAfter is the running balance.
WITH run AS (SELECT TransactionId, AccountNumber,
                    SUM(CASE WHEN Status = N'Posted' THEN CASE Direction WHEN 'C' THEN Amount ELSE -Amount END ELSE 0 END)
                        OVER (PARTITION BY AccountNumber ORDER BY PostedAt, TransactionId ROWS UNBOUNDED PRECEDING) AS Running FROM dbo.Transactions)
SELECT AccountNumber, MIN(Running) AS MinRunning INTO #MinRun FROM run GROUP BY AccountNumber;
UPDATE a SET OpeningBalance = ROUND(CASE WHEN ISNULL(m.MinRunning, 0) < 0 THEN -m.MinRunning ELSE 0 END + a.GenScale * (0.3 + 2 * r.R1), 2)
FROM dbo.Accounts a LEFT JOIN #MinRun m ON m.AccountNumber = a.AccountNumber CROSS APPLY gen.Rand(a.GenKey + 3) r;
WITH run AS (SELECT t.TransactionId, a.OpeningBalance + SUM(CASE WHEN t.Status = N'Posted' THEN CASE t.Direction WHEN 'C' THEN t.Amount ELSE -t.Amount END ELSE 0 END)
                        OVER (PARTITION BY t.AccountNumber ORDER BY t.PostedAt, t.TransactionId ROWS UNBOUNDED PRECEDING) AS Bal
             FROM dbo.Transactions t JOIN dbo.Accounts a ON a.AccountNumber = t.AccountNumber)
UPDATE t SET BalanceAfter = run.Bal FROM dbo.Transactions t JOIN run ON run.TransactionId = t.TransactionId;
RAISERROR(N'  balances done', 0, 1) WITH NOWAIT;
-- Closed accounts: the remaining balance is paid out by a teller of the home branch on the closing day.
INSERT INTO dbo.Transactions (TransactionReference, AccountNumber, TransactionCode, Direction, Amount, CurrencyCode, AmountAed, ValueDate, PostedAt, Channel, PostingUnitCode,
                              PostedByUserId, ApprovedByUserId, Status, BalanceAfter, Narrative)
SELECT N'', a.AccountNumber, N'CLS', 'D', b.Bal, a.CurrencyCode, ROUND(b.Bal * CASE a.CurrencyCode WHEN N'USD' THEN 3.6725 ELSE 1 END, 2), a.ClosedDate,
       DATEADD(MINUTE, 690, CAST(a.ClosedDate AS datetime2(0))), N'Branch', a.BranchCode, tl.UserId, CASE WHEN b.Bal * CASE a.CurrencyCode WHEN N'USD' THEN 3.6725 ELSE 1 END > tl.LimitAed THEN sup.UserId END,
       N'Posted', 0, N'Account closed - balance paid out'
FROM dbo.Accounts a
CROSS APPLY (SELECT ISNULL((SELECT TOP (1) BalanceAfter FROM dbo.Transactions t WHERE t.AccountNumber = a.AccountNumber ORDER BY t.PostedAt DESC, t.TransactionId DESC), a.OpeningBalance) AS Bal) b
CROSS APPLY (SELECT TOP (1) UserId, LimitAed FROM #Roster ro WHERE ro.UnitCode = a.BranchCode AND ro.WorkDate = a.ClosedDate AND ro.RoleGroup = N'TELLER' ORDER BY ro.Slot) tl
CROSS APPLY (SELECT TOP (1) UserId FROM #Roster s WHERE s.UnitCode = a.BranchCode AND s.WorkDate = a.ClosedDate AND s.RoleGroup = N'SUPER' ORDER BY s.LimitAed DESC) sup
WHERE a.Status = N'Closed';
UPDATE a SET ClosedByUserId = t.PostedByUserId FROM dbo.Accounts a JOIN dbo.Transactions t ON t.AccountNumber = a.AccountNumber AND t.TransactionCode = N'CLS';
UPDATE dbo.Transactions SET TransactionReference = CONCAT(N'TX25', RIGHT(CONCAT(N'00000000', TransactionId), 8)) WHERE TransactionReference = N'';
UPDATE a SET LedgerBalance = ISNULL(x.Bal, a.OpeningBalance), LastTransactionDate = x.D
FROM dbo.Accounts a
OUTER APPLY (SELECT TOP (1) t.BalanceAfter AS Bal, CAST(t.PostedAt AS date) AS D FROM dbo.Transactions t WHERE t.AccountNumber = a.AccountNumber ORDER BY t.PostedAt DESC, t.TransactionId DESC) x;
RAISERROR(N'  closures and ledger balances done', 0, 1) WITH NOWAIT;
-- Month-end balances of every open account.
INSERT INTO dbo.MonthEndBalances (AccountNumber, MonthEnd, BranchCode, CurrencyCode, Balance, BalanceAed)
SELECT a.AccountNumber, m.MonthEnd, a.BranchCode, a.CurrencyCode, b.Bal, ROUND(b.Bal * CASE a.CurrencyCode WHEN N'USD' THEN 3.6725 ELSE 1 END, 2)
FROM dbo.Accounts a JOIN #Months m ON a.OpenedDate <= m.MonthEnd AND (a.ClosedDate IS NULL OR a.ClosedDate > m.MonthEnd)
CROSS APPLY (SELECT ISNULL((SELECT TOP (1) t.BalanceAfter FROM dbo.Transactions t WHERE t.AccountNumber = a.AccountNumber AND t.PostedAt < DATEADD(DAY, 1, CAST(m.MonthEnd AS datetime2(0)))
                            ORDER BY t.PostedAt DESC, t.TransactionId DESC), a.OpeningBalance) AS Bal) b;
RAISERROR(N'  month-end balances done', 0, 1) WITH NOWAIT;
-- Teller tills: cash in and out per teller per business day from the posted cash transactions.
INSERT INTO dbo.TellerTills (BranchCode, TellerUserId, BusinessDate, OpeningCash, CashIn, CashOut, ExpectedClosingCash, CountedCash, Difference, TransactionCount, VerifiedByUserId)
SELECT x.PostingUnitCode, x.PostedByUserId, x.D, o.Opening, x.CashIn, x.CashOut, o.Opening + x.CashIn - x.CashOut,
       o.Opening + x.CashIn - x.CashOut + d.Diff, d.Diff, x.Cnt, sup.UserId
FROM (SELECT PostingUnitCode, PostedByUserId, ValueDate AS D, SUM(CASE WHEN TransactionCode = N'CDP' THEN AmountAed ELSE 0 END) AS CashIn,
             SUM(CASE WHEN TransactionCode IN (N'CWD', N'CLS') THEN AmountAed ELSE 0 END) AS CashOut, COUNT(*) AS Cnt
      FROM dbo.Transactions WHERE Channel = N'Branch' AND TransactionCode IN (N'CDP', N'CWD', N'CLS') AND Status = N'Posted' GROUP BY PostingUnitCode, PostedByUserId, ValueDate) x
CROSS APPLY gen.Rand(CHECKSUM(x.PostedByUserId, x.D)) r
CROSS APPLY (SELECT ROUND(60000 + r.R1 * 90000, -3) AS Opening) o
CROSS APPLY (SELECT CASE WHEN x.PostedByUserId = N'{reversal_teller}' AND r.R2 < 0.12 THEN -ROUND(50 + r.R3 * 450, -1) WHEN r.R2 < 0.015 THEN CASE WHEN r.R3 < 0.5 THEN -50 ELSE 50 END ELSE 0 END AS Diff) d
OUTER APPLY (SELECT TOP (1) s.UserId FROM #Roster s WHERE s.UnitCode = x.PostingUnitCode AND s.WorkDate = x.D AND s.RoleGroup = N'SUPER' AND s.UserId <> x.PostedByUserId ORDER BY s.Slot) sup;
RAISERROR(N'  tills done', 0, 1) WITH NOWAIT;
-- Customers: status follows their accounts.
UPDATE c SET Status = CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.Accounts a WHERE a.CIF = c.CIF AND a.Status <> N'Closed') THEN N'Closed'
                           WHEN NOT EXISTS (SELECT 1 FROM dbo.Accounts a WHERE a.CIF = c.CIF AND a.Status IN (N'Active')) THEN N'Dormant' ELSE N'Active' END
FROM dbo.Customers c;
-- Generator columns are not part of the sample schema.
ALTER TABLE dbo.Accounts DROP COLUMN GenKey, GenKind, GenSalary, GenSalaryDay, GenEmployer, GenSalaryFrom, GenSalaryTo, GenAtm, GenPos, GenTeller, GenChqIn, GenChqOut, GenTrfOut, GenTrfIn, GenScale;
CREATE INDEX IX_Transactions_PostedAt ON dbo.Transactions (PostedAt) INCLUDE (AccountNumber, TransactionCode, Amount, PostingUnitCode, PostedByUserId);
GO
"""

VIEWS_SQL = """CREATE VIEW dbo.vw_TransactionsByStaff AS
SELECT t.TransactionId, t.TransactionReference, t.AccountNumber, t.TransactionCode, t.Direction, t.Amount, t.CurrencyCode, t.AmountAed, t.ValueDate, t.PostedAt, t.Channel,
       t.PostingUnitCode, t.PostedByUserId, u.EmployeeNumber AS PostedByEmployeeNumber, u.FullName AS PostedByName, u.DepartmentCode AS PostedByDepartmentCode,
       u.SectionCode AS PostedBySectionCode, t.ApprovedByUserId, t.Status, t.ReversedByUserId, t.Narrative
FROM dbo.Transactions t JOIN dbo.Users u ON u.UserId = t.PostedByUserId
WHERE u.EmployeeNumber IS NOT NULL;
GO
CREATE VIEW dbo.vw_OutwardChequesByStaff AS
SELECT c.ChequeId, c.ChequeNumber, c.DrawnOnBankName, c.DepositAccountNumber, c.DepositBranchCode, c.Amount, c.DepositDate, c.Status, c.ReturnReasonCode, c.CapturedByUserId,
       u.EmployeeNumber AS CapturedByEmployeeNumber, u.FullName AS CapturedByName, u.DepartmentCode AS CapturedByDepartmentCode, u.SectionCode AS CapturedBySectionCode
FROM dbo.OutwardCheques c JOIN dbo.Users u ON u.UserId = c.CapturedByUserId;
GO
CREATE VIEW dbo.vw_OutwardTransfersByStaff AS
SELECT o.TransferId, o.TransferReference, o.AccountNumber, o.TransferType, o.Amount, o.CurrencyCode, o.AmountAed, o.BeneficiaryCountry, o.InitiatedAtUnitCode, o.ProcessedByUserId,
       o.ApprovedByUserId, u.EmployeeNumber AS ApprovedByEmployeeNumber, u.FullName AS ApprovedByName, u.DepartmentCode AS ApprovedByDepartmentCode,
       u.SectionCode AS ApprovedBySectionCode, o.ProcessedAt, o.ValueDate, o.ScreeningStatus, o.Status
FROM dbo.OutwardTransfers o JOIN dbo.Users u ON u.UserId = o.ApprovedByUserId;
GO
-- Live demo windows: December 2025 shifted so that 31 December 2025 is today in the UAE (Monitoring Rules evaluate "the last N hours").
CREATE VIEW dbo.vw_TransactionsLive AS
SELECT t.TransactionId, t.AccountNumber, a.CIF, cu.CustomerType, t.TransactionCode, t.Direction, t.Amount, t.CurrencyCode, t.AmountAed, t.Channel, t.PostingUnitCode, t.PostedByUserId,
       t.Status, DATEADD(DAY, s.Shift, t.PostedAt) AS PostedAt, DATEADD(DAY, s.Shift, t.ValueDate) AS ValueDate
FROM dbo.Transactions t JOIN dbo.Accounts a ON a.AccountNumber = t.AccountNumber JOIN dbo.Customers cu ON cu.CIF = a.CIF
CROSS JOIN (SELECT DATEDIFF(DAY, CAST('2025-12-31' AS date), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time' AS date)) AS Shift) s
WHERE t.PostedAt >= '2025-12-01' AND t.Status = N'Posted';
GO
CREATE VIEW dbo.vw_InwardChequesLive AS
SELECT c.ChequeId, c.ChequeNumber, c.AccountNumber, a.CIF, c.BranchCode, c.PresentingBankName, c.Amount, c.Status, c.ReturnReasonCode,
       DATEADD(DAY, s.Shift, c.PresentedDate) AS PresentedDate, DATEADD(DAY, s.Shift, c.ProcessedAt) AS ProcessedAt
FROM dbo.InwardCheques c JOIN dbo.Accounts a ON a.AccountNumber = c.AccountNumber
CROSS JOIN (SELECT DATEDIFF(DAY, CAST('2025-12-31' AS date), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time' AS date)) AS Shift) s
WHERE c.PresentedDate >= '2025-12-01';
GO
"""


def cand(name, rate, salt, days="work", where=""):
    """Stage events set-based: per account and month a count from the rate, then one row per event with its random
    numbers and its day (a business day, or any calendar day). Materialising each step keeps the plans simple."""
    day_join = ("JOIN #MonthWorkDays mw ON mw.M = e.M JOIN #WorkDays w ON w.M = e.M AND w.Rn = 1 + CAST(e.R6 * mw.Cnt AS int)" if days == "work"
                else "JOIN #Months mm ON mm.M = e.M JOIN #AllDays w ON w.M = e.M AND w.Dm = 1 + CAST(e.R6 * DAY(mm.MonthEnd) AS int)")
    rs = ", ".join(f"r.R{i}" for i in range(1, 17))
    return f"""SELECT a.AccountNumber, m.M, CAST(FLOOR(a.{rate} * 2 * rc.R1 + rc.R2) AS int) AS Cnt INTO #AM_{name}
FROM #Live a JOIN #Months m ON 1 = 1 CROSS APPLY gen.Rand(a.GenKey * 100 + m.M + {salt}) rc WHERE a.{rate} > 0 {where};
SELECT a.AccountNumber, a.BranchCode, a.CurrencyCode, a.GenKey, a.GenKind, a.GenScale, a.Fx, a.OpenedDate, a.ClosedDate, am.M, n.N, {rs} INTO #E_{name}
FROM #AM_{name} am JOIN #Live a ON a.AccountNumber = am.AccountNumber JOIN #N n ON n.N <= am.Cnt
CROSS APPLY gen.Rand(a.GenKey * 10000 + am.M * 100 + n.N + {salt + 1}) r;
SELECT e.*, w.D INTO #{name} FROM #E_{name} e {day_join}
WHERE w.D >= e.OpenedDate AND (e.ClosedDate IS NULL OR w.D < e.ClosedDate);
"""


def roster(alias, unit, group, rnd):
    """Join one rostered user: a slot chosen by a random number among those at work that day."""
    return (f"JOIN #RosterCount {alias}c ON {alias}c.UnitCode = {unit} AND {alias}c.WorkDate = c.D AND {alias}c.RoleGroup = N'{group}'\n"
            f"JOIN #Roster {alias} ON {alias}.UnitCode = {unit} AND {alias}.WorkDate = c.D AND {alias}.RoleGroup = N'{group}' AND {alias}.Slot = 1 + CAST({rnd} * {alias}c.Slots AS int)")


def build():
    rows, facts = _rows()
    parts = [prologue(DB, "wbcore_owner", "WbCore#Owner#2026", "wbcore_reader", "WbCore#Reader#2026",
                      "WabiBankCoreBankingDB — Wabi Bank core banking (sample)",
                      "Customers and KYC, accounts and deposits, transactions and teller cash, cheque clearing, loans and transfers, 2025.")]
    parts.append(DROP_FOREIGN_KEYS)
    parts.append("\n".join(f"IF OBJECT_ID(N'dbo.{v}', N'V') IS NOT NULL DROP VIEW dbo.{v};" for v in VIEWS) + "\nGO\n")
    parts.append("\n".join(f"IF OBJECT_ID(N'dbo.{t}', N'U') IS NOT NULL DROP TABLE dbo.{t};" for t in TABLES) + "\nGO\n")
    parts.append(calendar_table(UAE_HOLIDAYS_2025))
    parts.append("EXEC sp_rename N'dbo.Calendar', N'BusinessCalendar';\nEXEC sp_rename N'PK_Calendar', N'PK_BusinessCalendar', N'OBJECT';\nGO\n")
    parts.append(SCHEMA)
    for t in LOAD_ORDER:
        parts.append(insert(f"dbo.{t}", COLUMNS[t], rows[t]) + "\nGO\n")
    fx = ", ".join(f"('{c[0]}', {c[3]}, {c[4]}, {i})" for i, c in enumerate(CURRENCIES))
    gen = (GENERATE.replace("{roster}", insert("#Roster", ["UnitCode", "WorkDate", "RoleGroup", "Slot", "UserId", "LimitAed"], rows["#Roster"]))
           .replace("{banks}", BANKS_VALUES).replace("{foreign}", FOREIGN_VALUES).replace("{fx}", fx)
           .replace("{returned}", insert("#Returned", ["AccountNumber", "Seq", "BankCode", "Amount", "D", "UserId"], rows["#Returned"]))
           .replace("{passthrough}", insert("#Pass", ["AccountNumber", "InDay", "InUser", "OutDay", "Maker", "Checker", "Aml", "Initiator", "BranchCode"], rows["#PassThrough"]))
           .replace("{planted}", insert("#Planted", ["AccountNumber", "Code", "Amount", "At", "Unit", "UserId", "Approver", "Narrative"], rows["#Planted"]))
           .replace("{digital}", insert("#Digital", ["TransferId", "FromAccount", "ToAccount", "Amount", "At", "Ref", "Kind"], rows["#Digital"]))
           .replace("{reversal_teller}", facts["reversal_teller"])
           .replace("{cand_oc}", cand("OC", "GenChqIn", 31)).replace("{ro_oc}", roster("ro", "c.BranchCode", "BROPS", "c.R7"))
           .replace("{cand_ic}", cand("IC", "GenChqOut", 57)).replace("{ro_ic}", roster("ro", "N'OPS'", "CLR", "c.R7"))
           .replace("{cand_ot}", cand("OT", "GenTrfOut", 77)).replace("{ro_ot_cr}", roster("cr", "c.BranchCode", "BROPS", "c.R7"))
           .replace("{ro_ot_mk}", roster("mk", "N'OPS'", "PAYM", "c.R8")).replace("{ro_ot_aml}", roster("aml", "N'OPS'", "AML", "c.R15"))
           .replace("{cand_it}", cand("ITR", "GenTrfIn", 97)).replace("{ro_it}", roster("pm", "N'OPS'", "PAYM", "c.R8"))
           .replace("{cand_atm}", cand("ATM", "GenAtm", 13, days="all").replace("FLOOR(a.GenAtm * 2 * rc.R1 + rc.R2)", "a.GenAtm"))
           .replace("{cand_pos}", cand("POS", "GenPos", 17, days="all").replace("FLOOR(a.GenPos * 2 * rc.R1 + rc.R2)", "a.GenPos"))
           .replace("{cand_tl}", cand("TL", "GenTeller", 19)))
    # The roster and the generation share one batch (temporary tables live for the session; GO only splits batches).
    parts.append(gen)
    parts.append(VIEWS_SQL)
    summary = """SELECT N'Customers' AS TableName, COUNT(*) AS Rows FROM dbo.Customers UNION ALL SELECT N'Accounts', COUNT(*) FROM dbo.Accounts
UNION ALL SELECT N'Transactions', COUNT(*) FROM dbo.Transactions UNION ALL SELECT N'OutwardCheques', COUNT(*) FROM dbo.OutwardCheques
UNION ALL SELECT N'InwardCheques', COUNT(*) FROM dbo.InwardCheques UNION ALL SELECT N'OutwardTransfers', COUNT(*) FROM dbo.OutwardTransfers
UNION ALL SELECT N'InwardTransfers', COUNT(*) FROM dbo.InwardTransfers UNION ALL SELECT N'Loans', COUNT(*) FROM dbo.Loans
UNION ALL SELECT N'LoanRepayments', COUNT(*) FROM dbo.LoanRepayments UNION ALL SELECT N'TellerTills', COUNT(*) FROM dbo.TellerTills
UNION ALL SELECT N'MonthEndBalances', COUNT(*) FROM dbo.MonthEndBalances UNION ALL SELECT N'MonthEndLoanBalances', COUNT(*) FROM dbo.MonthEndLoanBalances UNION ALL SELECT N'Negative balances (must be 0)', COUNT(*) FROM dbo.Transactions WHERE BalanceAfter < 0
UNION ALL SELECT N'Staff acting on their own account (planted: 3 postings)', COUNT(*) FROM (
    SELECT a.AccountNumber, u.UserId FROM dbo.Accounts a JOIN dbo.Customers c ON c.CIF = a.CIF JOIN dbo.Users u ON u.EmployeeNumber = c.StaffEmployeeNumber) so
    CROSS APPLY (SELECT so.UserId AS U FROM dbo.Transactions t WHERE t.AccountNumber = so.AccountNumber AND so.UserId IN (t.PostedByUserId, t.ApprovedByUserId)
                 UNION ALL SELECT so.UserId FROM dbo.Accounts x WHERE x.AccountNumber = so.AccountNumber AND so.UserId IN (x.OpenedByUserId, x.ApprovedByUserId, x.ReactivatedByUserId)
                 UNION ALL SELECT so.UserId FROM dbo.OutwardCheques x WHERE x.DepositAccountNumber = so.AccountNumber AND x.CapturedByUserId = so.UserId
                 UNION ALL SELECT so.UserId FROM dbo.ChequeBooks x WHERE x.AccountNumber = so.AccountNumber AND x.IssuedByUserId = so.UserId
                 UNION ALL SELECT so.UserId FROM dbo.OutwardTransfers x WHERE x.AccountNumber = so.AccountNumber
                           AND so.UserId IN (x.CreatedByUserId, x.ProcessedByUserId, x.ApprovedByUserId, ISNULL(x.ScreenedByUserId, N''))
                 UNION ALL SELECT so.UserId FROM dbo.InwardTransfers x WHERE x.AccountNumber = so.AccountNumber AND x.PostedByUserId = so.UserId) own
UNION ALL SELECT N'Staff acting on their own customer record, KYC, deposit or loan (must be 0)', COUNT(*) FROM dbo.Customers c JOIN dbo.Users u ON u.EmployeeNumber = c.StaffEmployeeNumber
    CROSS APPLY (SELECT 1 AS X WHERE c.CreatedByUserId = u.UserId
                 UNION ALL SELECT 1 FROM dbo.KycDocuments k WHERE k.CIF = c.CIF AND k.VerifiedByUserId = u.UserId
                 UNION ALL SELECT 1 FROM dbo.FixedDeposits f WHERE f.CIF = c.CIF AND f.BookedByUserId = u.UserId
                 UNION ALL SELECT 1 FROM dbo.LoanApplications l WHERE l.CIF = c.CIF AND u.UserId IN (l.CreatedByUserId, ISNULL(l.DecidedByUserId, N''))
                 UNION ALL SELECT 1 FROM dbo.Loans l WHERE l.CIF = c.CIF AND u.UserId IN (ISNULL(l.DisbursedByUserId, N''), ISNULL(l.DisbursementApprovedByUserId, N''))) own;
GO"""
    parts.append(epilogue(DB, summary))
    stats = {k: len(v) for k, v in rows.items()}
    stats["facts"] = {k: v for k, v in facts.items()}
    return "\n".join(parts), stats


if __name__ == "__main__":
    text, stats = build()
    path = os.path.join(HERE, "02-create-wabibankcorebankingdb.sql")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(f"{os.path.basename(path)}: {len(text) // 1024} KB {stats}")
