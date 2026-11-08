#!/usr/bin/env python3
"""WabiBankFinanceDB: the finance system of Wabi Bank (WBK), calendar year 2025 (synthetic sample data).

Writes 03-create-wabibankfinancedb.sql (run:  python3 build_finance.py). Every department, section, employee,
user, access right and working day comes from bank_people.py, so the rows join with the other Wabi Bank databases.

What is in it
  General ledger   ChartOfAccounts, CostCentres (every department and every section), FiscalPeriods (2025-01..2025-12;
                   January-November closed by the Financial Controller, December open), JournalEntries + JournalLines.
  Budget           BudgetVersions (FY2025-ORIGINAL approved December 2024, FY2025-REVISED approved June 2025 and current
                   from July), BudgetLines (version x cost centre x operating-expense account x month), BudgetTransfers.
  Purchasing / AP  Vendors, PurchaseRequisitions (+ lines), PurchaseOrders (+ lines), GoodsReceipts, VendorInvoices
                   (three-way match), Payments.
  Fixed assets     FixedAssets: IT equipment, furniture, vehicles, ATMs and branch equipment, leasehold improvements.
  Users            the local copy of bank_people.local_users("FIN").

How the books fit together
  * Actual cost = JournalLines on Expense accounts (ChartOfAccounts.AccountType = 'Expense'), per CostCentreCode or
    DepartmentCode, per JournalEntries.PeriodCode. Journal sources: payroll interface (staff costs, posted per department),
    depreciation (from FixedAssets), premises and ATM costs, vendor-invoice batches (from VendorInvoices and
    PurchaseOrderLines, entry date), vendor-payment batches, core-banking income summary and manual journals (month-end
    accruals and reclassifications prepared by FIN-FCR accountants and approved by the Financial Controller).
  * Budget = BudgetLines. FY2025-REVISED keeps the original January-June lines and re-forecasts July-December.
    BudgetTransfers are reallocations inside one department; they are recorded separately, not folded into BudgetLines.
  * Approval limits are compared with amounts before VAT: requisitions (EstimatedAmount) up to AED 50,000 by the unit's
    department head (BudgetHolder; the CEO for the CEO Office), above that by the CFO up to 2,000,000, above by the CEO;
    purchase orders (SubTotal) up to 100,000 by the Procurement Manager, up to 2,000,000 by the CFO, above by the CEO.
  * Everyone acts only on a day they hold the FIN role and are at work (bank_people.present); SYS_FIN_IF posts interfaces.

Planted findings (the SQL to find each is in FINDINGS below)
  1. Budget overrun: Information Technology (ITD) spent about 19 % more than its FY2025-REVISED budget in Q3 2025
     (July-September): an unplanned database licence true-up after a vendor audit, emergency incident-response services
     and IT contractor accruals. ITD requested no budget transfer. Every other department stays within about +-10 %.
  2. Split purchase orders: one buyer turned a single Head Office furniture requisition (Administration and Facilities,
     ADM-FAC) into four purchase orders of AED 92,000-99,500 before VAT to the same vendor within one week, each just
     under the Procurement Manager's 100,000 limit, so the CFO never approved the orders.
  3. Paid before receipt: four vendor invoices were paid (MatchStatus NoReceipt) before the goods were received.
  Also inherited from bank_people: a Recruitment user (HRD-REC) holds FIN Buyer access; that user never raised an order.
"""
import datetime as dt
import os
import sys
from collections import defaultdict
from decimal import Decimal, ROUND_HALF_UP
from functools import lru_cache

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bank_people as bp  # noqa: E402
from build_common import insert, prologue, epilogue, drop_tables, rng  # noqa: E402

DB, OWNER, OWNER_PASSWORD, READER, READER_PASSWORD = "WabiBankFinanceDB", "wbfinance_owner", "WbFinance#Owner#2026", "wbfinance_reader", "WbFinance#Reader#2026"
OUTPUT = "03-create-wabibankfinancedb.sql"
TITLE = "WabiBankFinanceDB — general ledger, budgets, purchasing, payables and fixed assets of Wabi Bank, 2025 (sample)"
DESCRIPTION = ("Chart of accounts, cost centres, fiscal periods, budget versions, lines and transfers, vendors, requisitions, purchase orders, "
               "goods receipts, vendor invoices, payments, journals, fixed assets and finance users.")

FIN, SYS = "FIN", "SYS_FIN_IF"
Y0, Y1 = bp.Y0, bp.Y1
ONE = dt.timedelta(days=1)
CENT = Decimal("0.01")
ZERO = Decimal("0.00")
VAT = Decimal("0.05")
ORIGINAL, REVISED = "FY2025-ORIGINAL", "FY2025-REVISED"
MONTHS = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"]
MONTHS_AR = ["يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"]

REQ, BH, BUYER, PMR, CFOR, CEOR = ("Requisitioner",), ("BudgetHolder",), ("Buyer",), ("ProcurementManager",), ("ChiefFinancialOfficer",), ("ExecutiveApprover",)
APO, APS, ACC, FCR, BMR, BAR = ("PayablesOfficer",), ("PayablesSupervisor",), ("Accountant",), ("FinancialController",), ("BudgetManager",), ("BudgetAnalyst",)

# ------------------------------------------------------------------------------------------------ chart of accounts
CATEGORY_AR = {
    "Cash and settlement": "النقد والتسويات", "Tax": "الضرائب", "Fixed assets": "الأصول الثابتة", "Accumulated depreciation": "مجمع الإهلاك",
    "Payables": "الذمم الدائنة", "Accruals and provisions": "المستحقات والمخصصات", "Equity": "حقوق الملكية", "Net interest income": "صافي إيرادات الفوائد",
    "Non-interest income": "الإيرادات من غير الفوائد", "Staff costs": "تكاليف الموظفين", "Premises": "المباني", "IT": "تقنية المعلومات",
    "Marketing": "التسويق", "Professional fees": "الأتعاب المهنية", "Cash and ATM operations": "عمليات النقد والصراف الآلي",
    "Office and general": "المصروفات المكتبية والعامة", "Depreciation": "الإهلاك",
}
COA = [  # code, name, Arabic, type, category
    ("110300", "Payments clearing account", "حساب مقاصة المدفوعات", "Asset", "Cash and settlement"),
    ("120100", "Core banking settlement account", "حساب تسوية النظام المصرفي الأساسي", "Asset", "Cash and settlement"),
    ("130100", "VAT recoverable", "ضريبة القيمة المضافة القابلة للاسترداد", "Asset", "Tax"),
    ("150100", "IT equipment - cost", "معدات تقنية المعلومات - التكلفة", "Asset", "Fixed assets"),
    ("150200", "Furniture and fittings - cost", "الأثاث والتجهيزات - التكلفة", "Asset", "Fixed assets"),
    ("150300", "Motor vehicles - cost", "المركبات - التكلفة", "Asset", "Fixed assets"),
    ("150400", "ATMs and branch equipment - cost", "أجهزة الصراف الآلي ومعدات الفروع - التكلفة", "Asset", "Fixed assets"),
    ("150500", "Leasehold improvements - cost", "تحسينات المباني المستأجرة - التكلفة", "Asset", "Fixed assets"),
    ("159100", "IT equipment - accumulated depreciation", "معدات تقنية المعلومات - مجمع الإهلاك", "Asset", "Accumulated depreciation"),
    ("159200", "Furniture and fittings - accumulated depreciation", "الأثاث والتجهيزات - مجمع الإهلاك", "Asset", "Accumulated depreciation"),
    ("159300", "Motor vehicles - accumulated depreciation", "المركبات - مجمع الإهلاك", "Asset", "Accumulated depreciation"),
    ("159400", "ATMs and branch equipment - accumulated depreciation", "أجهزة الصراف الآلي ومعدات الفروع - مجمع الإهلاك", "Asset", "Accumulated depreciation"),
    ("159500", "Leasehold improvements - accumulated depreciation", "تحسينات المباني المستأجرة - مجمع الإهلاك", "Asset", "Accumulated depreciation"),
    ("210100", "Accounts payable - trade", "الذمم الدائنة التجارية", "Liability", "Payables"),
    ("220100", "Accrued expenses", "المصروفات المستحقة", "Liability", "Accruals and provisions"),
    ("220200", "Salaries payable", "الرواتب المستحقة الدفع", "Liability", "Accruals and provisions"),
    ("220300", "End-of-service benefits provision", "مخصص مكافأة نهاية الخدمة", "Liability", "Accruals and provisions"),
    ("220400", "Pension contributions payable", "اشتراكات التقاعد المستحقة", "Liability", "Accruals and provisions"),
    ("220500", "Variable pay accrual", "مستحقات المكافآت المتغيرة", "Liability", "Accruals and provisions"),
    ("310100", "Share capital", "رأس المال", "Equity", "Equity"),
    ("320100", "Retained earnings", "الأرباح المحتجزة", "Equity", "Equity"),
    ("410100", "Net interest income (after funds transfer pricing)", "صافي إيرادات الفوائد (بعد تسعير تحويل الأموال)", "Income", "Net interest income"),
    ("420100", "Fee and commission income", "إيرادات الرسوم والعمولات", "Income", "Non-interest income"),
    ("430100", "Foreign exchange income", "إيرادات صرف العملات الأجنبية", "Income", "Non-interest income"),
    ("610100", "Salaries and allowances", "الرواتب والبدلات", "Expense", "Staff costs"),
    ("610200", "Pension and social security", "التقاعد والضمان الاجتماعي", "Expense", "Staff costs"),
    ("610300", "End-of-service benefits", "مكافأة نهاية الخدمة", "Expense", "Staff costs"),
    ("610400", "Medical insurance", "التأمين الطبي", "Expense", "Staff costs"),
    ("610500", "Variable pay", "المكافآت المتغيرة", "Expense", "Staff costs"),
    ("610600", "Training and development", "التدريب والتطوير", "Expense", "Staff costs"),
    ("610700", "Recruitment", "التوظيف", "Expense", "Staff costs"),
    ("620100", "Rent", "الإيجارات", "Expense", "Premises"),
    ("620200", "Utilities", "المرافق (الكهرباء والمياه والتبريد)", "Expense", "Premises"),
    ("620300", "Premises repairs and maintenance", "صيانة وإصلاح المباني", "Expense", "Premises"),
    ("620400", "Cleaning and security services", "خدمات النظافة والأمن", "Expense", "Premises"),
    ("630100", "Software licences and maintenance", "تراخيص البرمجيات وصيانتها", "Expense", "IT"),
    ("630200", "IT services and contractors", "خدمات تقنية المعلومات والمتعاقدون", "Expense", "IT"),
    ("630300", "Telecommunications", "الاتصالات", "Expense", "IT"),
    ("630400", "IT accessories and consumables", "ملحقات ومستهلكات تقنية المعلومات", "Expense", "IT"),
    ("640100", "Advertising and campaigns", "الإعلان والحملات", "Expense", "Marketing"),
    ("640200", "Sponsorships and events", "الرعايات والفعاليات", "Expense", "Marketing"),
    ("640300", "Printing and branding", "الطباعة والهوية التجارية", "Expense", "Marketing"),
    ("650100", "Audit fees", "أتعاب التدقيق", "Expense", "Professional fees"),
    ("650200", "Legal fees", "الأتعاب القانونية", "Expense", "Professional fees"),
    ("650300", "Consultancy fees", "أتعاب الاستشارات", "Expense", "Professional fees"),
    ("650400", "Regulatory fees and levies", "الرسوم الرقابية", "Expense", "Professional fees"),
    ("650500", "Compliance services", "خدمات الالتزام", "Expense", "Professional fees"),
    ("660100", "Cash-in-transit services", "خدمات نقل النقد", "Expense", "Cash and ATM operations"),
    ("660200", "ATM maintenance and services", "صيانة وخدمات أجهزة الصراف الآلي", "Expense", "Cash and ATM operations"),
    ("660300", "Card production", "إنتاج البطاقات", "Expense", "Cash and ATM operations"),
    ("670100", "Office supplies", "اللوازم المكتبية", "Expense", "Office and general"),
    ("670200", "Bank stationery and postage", "القرطاسية المصرفية والبريد", "Expense", "Office and general"),
    ("670300", "Business travel", "السفر في مهام العمل", "Expense", "Office and general"),
    ("670500", "Vehicle running costs", "مصروفات تشغيل المركبات", "Expense", "Office and general"),
    ("670600", "Subscriptions and market data", "الاشتراكات وبيانات الأسواق", "Expense", "Office and general"),
    ("670700", "Small equipment", "المعدات الصغيرة", "Expense", "Office and general"),
    ("680100", "Depreciation - IT equipment", "إهلاك معدات تقنية المعلومات", "Expense", "Depreciation"),
    ("680200", "Depreciation - furniture and fittings", "إهلاك الأثاث والتجهيزات", "Expense", "Depreciation"),
    ("680300", "Depreciation - motor vehicles", "إهلاك المركبات", "Expense", "Depreciation"),
    ("680400", "Depreciation - ATMs and branch equipment", "إهلاك أجهزة الصراف الآلي ومعدات الفروع", "Expense", "Depreciation"),
    ("680500", "Depreciation - leasehold improvements", "إهلاك تحسينات المباني المستأجرة", "Expense", "Depreciation"),
]
ACCOUNT = {a[0]: a for a in COA}
EXPENSE = {a[0] for a in COA if a[3] == "Expense"}

ASSET_CLASSES = {  # code: name, Arabic, cost account, accumulated depreciation, depreciation expense
    "IT": ("IT equipment", "معدات تقنية المعلومات", "150100", "159100", "680100"),
    "FF": ("Furniture and fittings", "الأثاث والتجهيزات", "150200", "159200", "680200"),
    "MV": ("Motor vehicles", "المركبات", "150300", "159300", "680300"),
    "ATM": ("ATMs and branch equipment", "أجهزة الصراف الآلي ومعدات الفروع", "150400", "159400", "680400"),
    "LHI": ("Leasehold improvements", "تحسينات المباني المستأجرة", "150500", "159500", "680500"),
}

# ------------------------------------------------------------------------------------------------ vendors and spend categories
VENDOR_GROUPS = [  # code, vendor category, count, name suffixes, payment terms
    ("ITHW", "IT hardware", 7, ["Computer Systems LLC", "Technology Trading LLC", "IT Distribution FZE"], [30, 45, 60]),
    ("SW", "Software", 6, ["Software FZ-LLC", "Systems FZ-LLC"], [30, 45]),
    ("ITSVC", "IT services", 5, ["Digital Services LLC", "IT Consulting FZ-LLC"], [30, 45]),
    ("TEL", "Telecommunications", 2, ["Telecom Services LLC", "Data Networks LLC"], [30]),
    ("MKTG", "Advertising and media", 5, ["Advertising LLC", "Media Agency FZ-LLC"], [30, 45]),
    ("EVENTS", "Events and hospitality", 3, ["Events Management LLC"], [30]),
    ("PRINT", "Printing", 4, ["Printing Press LLC", "Printing and Packaging LLC"], [30, 45]),
    ("CARDS", "Card manufacturing", 2, ["Card Solutions FZE"], [45]),
    ("CONSULT", "Consulting", 7, ["Management Consultants", "Advisory LLP"], [30, 45, 60]),
    ("LEGAL", "Legal services", 4, ["Advocates and Legal Consultants"], [30]),
    ("COMPLIANCE", "Compliance and data services", 3, ["RegTech FZ-LLC", "Data Services FZ-LLC"], [30]),
    ("OFFICE", "Office supplies", 4, ["Stationery Trading LLC"], [15, 30]),
    ("FURNITURE", "Furniture", 4, ["Office Furniture LLC", "Furniture Industries LLC"], [30, 45]),
    ("FACILITIES", "Facilities maintenance", 5, ["Technical Services LLC", "Maintenance Contracting LLC"], [30]),
    ("FACSERV", "Cleaning and security services", 3, ["Facility Services LLC", "Security Services LLC"], [30]),
    ("TRAINING", "Training", 4, ["Training Institute", "Learning Centre LLC"], [30]),
    ("RECRUIT", "Recruitment", 3, ["Recruitment Consultants"], [30]),
    ("TRAVEL", "Travel", 2, ["Travel and Tourism LLC"], [15, 30]),
    ("CASHSVC", "Cash and ATM services", 3, ["Cash Services LLC", "ATM Technologies FZE"], [30, 45]),
    ("VEHICLES", "Motor vehicles", 2, ["Motors LLC"], [30]),
    ("MARKET", "Market data", 2, ["Financial Information FZ-LLC"], [30, 60]),
]
VENDOR_PREFIXES = ["Falcon", "Desert Line", "Gulf Star", "Al Noor", "Horizon", "Pearl Coast", "Oasis", "Crescent", "Summit", "Atlas", "Al Reef", "Blue Dune",
                   "Jebel", "Marina", "Sahara", "Palm Grove", "Saqr", "Bayt", "Al Waha", "Al Fajr", "Silver Sands", "Corniche", "Meridian", "Al Bustan",
                   "Qamar", "Najm", "Sidra", "Ghaf", "Areej", "Rimal", "Dana", "Masar", "Tamr", "Nujoom", "Wadi", "Liwa", "Hatta", "Arabian Gulf",
                   "Seven Emirates", "Creekside", "Dhow", "Mirage", "Zenith", "Coral", "Sandstone", "Lighthouse", "Al Majd", "Fanar"]
UAE_BANK_CODES = ["026", "030", "033", "035", "044", "046", "047", "050", "053", "057"]

# Spend categories: name, Arabic, GL account (expense or fixed-asset cost), vendor group, lead days, Goods/Services, asset class, useful life, items.
# Item: (description, unit of measure, unit price low, high, quantity low, high, allowed units or None, once a year per unit)
CATS = {
    "EUC": ("End-user computing", "أجهزة المستخدمين", "150100", "ITHW", 12, "Goods", "IT", 48, [
        ("Business laptop 14-inch", "EA", 4600, 6200, 4, 24, None, False), ("Desktop workstation", "EA", 3600, 4800, 3, 15, None, False),
        ("Teller desktop PC bundle", "EA", 4200, 5400, 2, 8, None, False)]),
    "DCHW": ("Data centre hardware", "أجهزة مركز البيانات", "150100", "ITHW", 30, "Goods", "IT", 60, [
        ("Rack server 2U", "EA", 68000, 118000, 1, 3, None, False), ("Storage array expansion shelf", "EA", 95000, 175000, 1, 2, None, False),
        ("Backup appliance", "EA", 55000, 88000, 1, 1, None, False)]),
    "NETHW": ("Network and security equipment", "معدات الشبكات والأمن", "150100", "ITHW", 21, "Goods", "IT", 60, [
        ("Core switch 48-port", "EA", 16000, 28000, 1, 4, None, False), ("Branch router", "EA", 9000, 15000, 1, 6, None, False),
        ("Wi-Fi access point", "EA", 1500, 2500, 4, 16, None, False), ("Next-generation firewall", "EA", 70000, 135000, 1, 2, None, False)]),
    "ITACC": ("IT accessories and consumables", "ملحقات ومستهلكات تقنية المعلومات", "630400", "ITHW", 7, "Goods", None, None, [
        ("Monitor 24-inch", "EA", 750, 1100, 4, 20, None, False), ("Keyboard and mouse set", "EA", 90, 160, 10, 40, None, False),
        ("Contact centre headset", "EA", 260, 420, 10, 30, None, False), ("Laser printer toner", "EA", 320, 520, 6, 30, None, False),
        ("Receipt printer", "EA", 900, 1400, 2, 6, None, False), ("USB-C docking station", "EA", 650, 950, 5, 20, None, False),
        ("Network cables and patch cords", "LOT", 1200, 3500, 1, 3, None, False)]),
    "SWLIC": ("Software licences and maintenance", "تراخيص البرمجيات وصيانتها", "630100", "SW", 5, "Services", None, None, [
        ("Core banking annual maintenance and support", "YR", 2150000, 2150000, 1, 1, {"ITD-APP"}, True),
        ("Database licence support renewal", "YR", 120000, 240000, 1, 1, {"ITD-APP", "ITD-INF"}, True),
        ("Microsoft 365 E3 subscriptions", "USER", 1250, 1450, 40, 160, {"ITD-INF"}, False),
        ("Endpoint protection subscriptions", "DEVICE", 150, 220, 100, 480, {"ITD-SEC"}, False),
        ("SIEM platform subscription", "YR", 180000, 300000, 1, 1, {"ITD-SEC"}, True),
        ("Mobile banking platform licence", "YR", 250000, 420000, 1, 1, {"DCC-DIG"}, True),
        ("Digital onboarding (eKYC) transactions", "THOUSAND", 3500, 6000, 2, 10, {"DCC-DIG"}, False),
        ("Document management licences", "USER", 600, 900, 20, 80, {"ITD-APP"}, False),
        ("Reporting tool licences", "USER", 1800, 2800, 5, 20, {"ITD-APP", "DCC-DIG", "ITD-INF"}, False),
        ("Virtualisation licence renewal", "YR", 90000, 160000, 1, 1, {"ITD-INF"}, True),
        ("Privileged access management licences", "USER", 2200, 3200, 10, 40, {"ITD-SEC"}, False),
        ("Service desk tool subscriptions", "USER", 900, 1300, 10, 30, {"ITD-SD"}, False)]),
    "ITSVC": ("IT services", "خدمات تقنية المعلومات", "630200", "ITSVC", 30, "Services", None, None, [
        ("Consultant days - core banking", "DAY", 3200, 4500, 10, 60, {"ITD-APP", "ITD-INF"}, False),
        ("Application development days", "DAY", 2600, 3800, 20, 80, {"ITD-APP", "DCC-DIG"}, False),
        ("Penetration test", "JOB", 65000, 140000, 1, 1, {"ITD-SEC", "DCC-DIG"}, False),
        ("Managed network monitoring (quarter)", "QTR", 45000, 80000, 1, 1, {"ITD-NET", "ITD-INF"}, False),
        ("Disaster recovery test support", "JOB", 35000, 70000, 1, 1, {"ITD-INF"}, False),
        ("Hardware support renewal", "YR", 60000, 150000, 1, 1, {"ITD-INF", "ITD-NET"}, False),
        ("Phishing simulation campaign", "JOB", 25000, 45000, 1, 1, {"ITD-SEC"}, False),
        ("UX research and design sprint", "JOB", 45000, 95000, 1, 1, {"DCC-DIG"}, False)]),
    "TEL": ("Telecommunications", "الاتصالات", "630300", "TEL", 10, "Services", None, None, [
        ("Branch data line upgrade (annual)", "LINE", 36000, 60000, 1, 6, {"ITD-NET"}, False),
        ("SIP trunk channels (annual)", "YR", 18000, 30000, 1, 3, {"ITD-NET", "DCC-CC"}, False),
        ("Corporate mobile plans (annual)", "USER", 1800, 2600, 10, 40, None, False),
        ("SMS gateway bundle (100,000 messages)", "BUNDLE", 9000, 14000, 1, 5, {"DCC-CC", "DCC-DIG", "ITD-NET"}, False)]),
    "ADV": ("Advertising and campaigns", "الإعلان والحملات", "640100", "MKTG", 14, "Services", None, None, [
        ("Digital campaign", "JOB", 60000, 240000, 1, 1, None, False), ("Radio campaign", "JOB", 45000, 120000, 1, 1, None, False),
        ("Outdoor billboard (month)", "MONTH", 35000, 90000, 1, 3, None, False), ("Social media content production", "JOB", 15000, 45000, 1, 2, None, False),
        ("Search engine advertising (month)", "MONTH", 20000, 60000, 1, 3, None, False)]),
    "EVT": ("Sponsorships and events", "الرعايات والفعاليات", "640200", "EVENTS", 21, "Services", None, None, [
        ("National Day customer event", "JOB", 80000, 220000, 1, 1, {"MKT-COM", "MKT-MKT"}, True),
        ("Corporate client dinner", "JOB", 25000, 70000, 1, 1, None, False),
        ("Sponsorship package", "JOB", 100000, 300000, 1, 1, {"MKT-COM", "MKT-MKT"}, False),
        ("Staff town hall event", "JOB", 30000, 80000, 1, 1, {"MKT-COM", "CEO-EXO"}, False),
        ("Board meeting venue and catering", "JOB", 8000, 20000, 1, 1, {"CEO-BSC"}, False),
        ("Customer seminar", "JOB", 15000, 45000, 1, 1, None, False)]),
    "BRAND": ("Printing and branding", "الطباعة والهوية التجارية", "640300", "PRINT", 14, "Goods", None, None, [
        ("Product brochures (1,000)", "THOUSAND", 1800, 3200, 2, 20, None, False), ("Branch signage refresh", "JOB", 25000, 60000, 1, 1, None, False),
        ("Roll-up banners", "EA", 450, 800, 4, 20, None, False), ("Branded giveaways", "LOT", 12000, 30000, 1, 1, None, False),
        ("Posters and point-of-sale material", "LOT", 3000, 9000, 1, 2, None, False)]),
    "STAT": ("Bank stationery and postage", "القرطاسية المصرفية والبريد", "670200", "PRINT", 10, "Goods", None, None, [
        ("Cheque books (100)", "HUNDRED", 900, 1400, 5, 40, None, False), ("Statement paper and envelopes", "LOT", 6000, 14000, 1, 3, None, False),
        ("Deposit slips (1,000)", "THOUSAND", 250, 400, 5, 30, None, False), ("Courier services (month)", "MONTH", 2000, 6000, 1, 1, None, False),
        ("Security seals and cash bags", "LOT", 1500, 4500, 1, 3, None, False)]),
    "CARD": ("Card production", "إنتاج البطاقات", "660300", "CARDS", 21, "Goods", None, None, [
        ("Debit card stock (1,000)", "THOUSAND", 9000, 14000, 2, 10, None, False), ("Credit card stock (1,000)", "THOUSAND", 14000, 22000, 1, 5, None, False),
        ("PIN mailers (1,000)", "THOUSAND", 1200, 2000, 2, 10, None, False), ("Card personalisation service (1,000)", "THOUSAND", 3000, 5000, 2, 10, None, False)]),
    "CONS": ("Consultancy", "الاستشارات", "650300", "CONSULT", 45, "Services", None, None, [
        ("Strategy advisory engagement", "JOB", 180000, 650000, 1, 1, {"CEO-STR"}, False),
        ("IFRS 9 model validation", "JOB", 150000, 320000, 1, 1, {"RSK-CRA", "RSK-MLR"}, False),
        ("Operational risk framework review", "JOB", 90000, 180000, 1, 1, {"RSK-OPR"}, False),
        ("Customer experience research", "JOB", 60000, 140000, 1, 1, {"MKT-CX", "RBD-PRD"}, False),
        ("HR policy review", "JOB", 50000, 120000, 1, 1, {"HRD-PSV"}, False),
        ("Co-sourced internal audit days", "DAY", 2800, 3800, 10, 40, {"IAD-BOA", "IAD-ITA"}, False),
        ("Mystery shopping programme", "JOB", 35000, 70000, 1, 1, {"MKT-CX", "RBD-BNM"}, False),
        ("Regulatory advisory", "JOB", 40000, 120000, 1, 1, {"CMP-REG", "TRY-ALM", "RSK-MLR"}, False),
        ("Product pricing study", "JOB", 45000, 110000, 1, 1, {"RBD-PRD", "RBD-BNM"}, False),
        ("Market study", "JOB", 30000, 80000, 1, 1, None, False)]),
    "LEGAL": ("Legal services", "الخدمات القانونية", "650200", "LEGAL", 30, "Services", None, None, [
        ("External counsel retainer (quarter)", "QTR", 45000, 120000, 1, 1, None, False),
        ("Litigation representation", "JOB", 40000, 180000, 1, 1, {"LGL-LIT"}, False),
        ("Legal opinion", "JOB", 15000, 45000, 1, 1, None, False), ("Court and notary fees", "LOT", 3000, 12000, 1, 1, None, False)]),
    "COMPL": ("Compliance services", "خدمات الالتزام", "650500", "COMPLIANCE", 7, "Services", None, None, [
        ("Sanctions screening database subscription", "YR", 180000, 260000, 1, 1, {"CMP-AML"}, True),
        ("Identity verification checks (1,000)", "THOUSAND", 2500, 4000, 5, 30, {"CMP-KYC"}, False),
        ("Regulatory reporting tool support", "YR", 60000, 110000, 1, 1, {"CMP-REG"}, True),
        ("AML e-learning licences", "USER", 180, 320, 50, 250, {"CMP-AML", "CMP-KYC"}, False),
        ("Transaction monitoring scenario tuning", "JOB", 45000, 90000, 1, 1, {"CMP-AML"}, False),
        ("Adverse media screening (1,000)", "THOUSAND", 1500, 2800, 5, 20, {"CMP-KYC", "CMP-AML"}, False),
        ("Regulatory reporting validation review", "JOB", 25000, 60000, 1, 1, {"CMP-REG"}, False)]),
    "OFFICE": ("Office supplies", "اللوازم المكتبية", "670100", "OFFICE", 4, "Goods", None, None, [
        ("A4 copy paper (box of 5 reams)", "BOX", 80, 95, 20, 120, None, False), ("Stationery pack", "LOT", 600, 1800, 1, 4, None, False),
        ("Pantry supplies", "LOT", 900, 2500, 1, 3, None, False), ("Archive boxes (pack of 20)", "PACK", 120, 180, 5, 30, None, False),
        ("Toner for multifunction printers", "EA", 380, 650, 4, 16, None, False)]),
    "FURN": ("Furniture and fit-out", "الأثاث والتجهيز", "150200", "FURNITURE", 30, "Goods", "FF", 84, [
        ("Workstation desk with pedestal", "EA", 2200, 3400, 4, 30, None, False), ("Ergonomic office chair", "EA", 900, 1600, 6, 40, None, False),
        ("Meeting room table and chairs", "SET", 12000, 28000, 1, 2, None, False), ("Branch customer counter", "EA", 38000, 75000, 1, 2, {"BR", "ADM-FAC"}, False),
        ("Branch waiting area seating", "SET", 14000, 30000, 1, 2, {"BR", "ADM-FAC"}, False), ("Filing cabinets", "EA", 1100, 1800, 4, 20, None, False)]),
    "SMFURN": ("Small equipment", "المعدات الصغيرة", "670700", "FURNITURE", 10, "Goods", None, None, [
        ("Water dispenser", "EA", 650, 950, 1, 4, None, False), ("Paper shredder", "EA", 900, 1800, 1, 4, None, False),
        ("Coffee machine", "EA", 2500, 5500, 1, 2, None, False), ("Whiteboard", "EA", 300, 650, 1, 6, None, False), ("Visitor chairs", "EA", 350, 700, 2, 12, None, False)]),
    "FACM": ("Premises maintenance", "صيانة المباني", "620300", "FACILITIES", 5, "Services", None, None, [
        ("Air-conditioning maintenance call-out", "JOB", 2500, 9000, 1, 1, None, False), ("Electrical repairs", "JOB", 1800, 12000, 1, 1, None, False),
        ("Plumbing repairs", "JOB", 900, 6500, 1, 1, None, False), ("Painting and minor civil works", "JOB", 8000, 45000, 1, 1, None, False),
        ("Fire alarm system inspection", "JOB", 12000, 28000, 1, 1, None, False), ("Lift maintenance (quarter)", "QTR", 9000, 16000, 1, 1, {"ADM-FAC"}, False),
        ("Generator servicing", "JOB", 6000, 14000, 1, 1, None, False), ("Signage lighting repair", "JOB", 1500, 6000, 1, 1, None, False)]),
    "FACS": ("Cleaning and security services", "خدمات النظافة والأمن", "620400", "FACSERV", 3, "Services", None, None, [
        ("Additional security guards (month)", "MONTH", 18000, 32000, 1, 3, None, False), ("Deep cleaning", "JOB", 6000, 15000, 1, 1, None, False),
        ("Pest control (quarter)", "QTR", 2500, 5000, 1, 1, None, False), ("CCTV maintenance", "JOB", 8000, 20000, 1, 1, None, False),
        ("Access control card printing", "LOT", 2000, 6000, 1, 1, None, False)]),
    "TRAIN": ("Training and development", "التدريب والتطوير", "610600", "TRAINING", 14, "Services", None, None, [
        ("Leadership programme (participant)", "PERSON", 6000, 14000, 2, 15, {"HRD-LND"}, False),
        ("Professional certification (candidate)", "PERSON", 3500, 7500, 1, 6, None, False),
        ("E-learning platform licences", "USER", 180, 320, 100, 450, {"HRD-LND"}, False),
        ("Banking diploma course (participant)", "PERSON", 4500, 9000, 2, 12, {"HRD-LND"}, False),
        ("Technical training course (participant)", "PERSON", 3000, 8000, 1, 8, None, False),
        ("Customer service training workshop", "JOB", 12000, 30000, 1, 1, {"HRD-LND", "DCC-CC"}, False)]),
    "RECR": ("Recruitment", "التوظيف", "610700", "RECRUIT", 7, "Services", None, None, [
        ("Recruitment agency fee", "PLACEMENT", 15000, 55000, 1, 1, None, False), ("Job portal subscription", "YR", 25000, 60000, 1, 1, None, True),
        ("Assessment centre services", "JOB", 12000, 30000, 1, 1, None, False), ("Career fair stand", "JOB", 15000, 40000, 1, 1, None, False)]),
    "TRAVEL": ("Business travel", "السفر في مهام العمل", "670300", "TRAVEL", 3, "Services", None, None, [
        ("Air tickets (economy)", "TICKET", 1500, 4500, 1, 4, None, False), ("Air tickets (business)", "TICKET", 6500, 18000, 1, 2, None, False),
        ("Hotel accommodation (night)", "NIGHT", 650, 1400, 2, 12, None, False), ("Conference registration", "PERSON", 2500, 9000, 1, 3, None, False)]),
    "CASH": ("Cash equipment servicing", "صيانة معدات النقد", "660200", "CASHSVC", 7, "Goods", None, None, [
        ("ATM spare parts kit", "KIT", 4000, 12000, 1, 4, None, False), ("Cash counting machine servicing", "JOB", 1200, 2800, 1, 4, None, False),
        ("Note sorter calibration", "JOB", 3000, 6000, 1, 2, None, False), ("ATM fascia and signage", "LOT", 2000, 5000, 1, 3, None, False)]),
    "CIT": ("Additional cash-in-transit", "خدمات نقل النقد الإضافية", "660100", "CASHSVC", 3, "Services", None, None, [
        ("Additional cash-in-transit pickups", "TRIP", 650, 1200, 4, 30, None, False)]),
    "ATM": ("ATMs and cash recyclers", "أجهزة الصراف الآلي وإعادة تدوير النقد", "150400", "CASHSVC", 45, "Goods", "ATM", 84, [
        ("Cash recycler ATM", "EA", 95000, 140000, 1, 1, {"BR"}, False), ("Teller cash recycler", "EA", 65000, 95000, 1, 1, {"OPS-CSH", "BR"}, False),
        ("Note counting machine", "EA", 8000, 14000, 1, 2, None, False)]),
    "VEH": ("Motor vehicles", "المركبات", "150300", "VEHICLES", 30, "Goods", "MV", 60, [
        ("Staff transport van", "EA", 95000, 140000, 1, 1, None, False), ("Executive sedan", "EA", 120000, 190000, 1, 1, None, False)]),
    "VEHR": ("Vehicle running costs", "مصروفات تشغيل المركبات", "670500", "VEHICLES", 3, "Services", None, None, [
        ("Vehicle servicing", "JOB", 900, 3500, 1, 3, None, False), ("Tyres (set of 4)", "SET", 1800, 3600, 1, 2, None, False),
        ("Fuel card top-up (month)", "MONTH", 4000, 9000, 1, 1, None, False)]),
    "SUBS": ("Subscriptions and market data", "الاشتراكات وبيانات الأسواق", "670600", "MARKET", 5, "Services", None, None, [
        ("Market data terminal (annual)", "YR", 90000, 140000, 1, 2, {"TRY-DLR", "TRY-ALM", "RSK-MLR"}, False),
        ("Credit rating agency subscription", "YR", 60000, 150000, 1, 1, {"RSK-CRA", "CBD-CRM", "TRY-ALM"}, False),
        ("Professional membership fees", "PERSON", 1500, 6000, 1, 6, None, False), ("Research subscriptions", "YR", 12000, 40000, 1, 1, None, False)]),
}

# Requisitions per unit and year, and the spend-category mix. Branch sections and branch managers repeat for every branch.
HQ_PLAN = {
    "ITD-SD": (60, [("EUC", 5), ("ITACC", 5), ("SWLIC", 1)]), "ITD-INF": (48, [("DCHW", 2), ("SWLIC", 3), ("ITSVC", 3), ("ITACC", 1)]),
    "ITD-APP": (46, [("SWLIC", 4), ("ITSVC", 4)]), "ITD-NET": (38, [("NETHW", 4), ("TEL", 4), ("ITSVC", 1)]),
    "ITD-SEC": (28, [("SWLIC", 3), ("ITSVC", 2), ("NETHW", 1)]), "DCC-DIG": (34, [("SWLIC", 2), ("ITSVC", 2), ("ADV", 2)]),
    "DCC-CC": (24, [("ITACC", 3), ("TEL", 1), ("TRAIN", 1), ("OFFICE", 2)]), "MKT-MKT": (60, [("ADV", 5), ("BRAND", 2), ("EVT", 1)]),
    "MKT-CX": (16, [("CONS", 2), ("BRAND", 1), ("EVT", 1)]), "MKT-COM": (28, [("EVT", 3), ("BRAND", 2), ("ADV", 1)]),
    "RBD-PRD": (22, [("ADV", 2), ("BRAND", 2), ("CONS", 1)]), "RBD-CRD": (28, [("CARD", 5), ("ADV", 1)]),
    "RBD-BNM": (14, [("BRAND", 1), ("CONS", 1), ("SMFURN", 1)]), "CBD-CRM": (16, [("TRAVEL", 3), ("EVT", 1), ("SUBS", 1)]),
    "CBD-SME": (12, [("TRAVEL", 1), ("EVT", 1), ("BRAND", 1)]), "CBD-TRF": (10, [("SUBS", 1), ("TRAVEL", 1), ("OFFICE", 1)]),
    "TRY-DLR": (12, [("SUBS", 3), ("TRAVEL", 1)]), "TRY-ALM": (7, [("SUBS", 2), ("CONS", 1)]),
    "OPS-CLR": (14, [("STAT", 3), ("OFFICE", 2)]), "OPS-PAY": (10, [("OFFICE", 2), ("STAT", 1)]),
    "OPS-ACS": (16, [("STAT", 4), ("OFFICE", 1)]), "OPS-LNO": (10, [("OFFICE", 2), ("STAT", 1)]),
    "OPS-CSH": (28, [("CASH", 4), ("CIT", 3), ("ATM", 1)]), "RSK-CRA": (12, [("CONS", 2), ("SUBS", 1), ("TRAIN", 1)]),
    "RSK-MLR": (9, [("SUBS", 2), ("CONS", 1)]), "RSK-OPR": (9, [("CONS", 2), ("TRAIN", 1)]),
    "CMP-AML": (16, [("COMPL", 4), ("TRAIN", 1)]), "CMP-KYC": (14, [("COMPL", 4)]), "CMP-REG": (9, [("COMPL", 2), ("CONS", 1)]),
    "IAD-BOA": (9, [("CONS", 1), ("TRAIN", 1), ("TRAVEL", 1)]), "IAD-ITA": (7, [("CONS", 1), ("TRAIN", 1)]),
    "LGL-ADV": (12, [("LEGAL", 3), ("SUBS", 1)]), "LGL-LIT": (16, [("LEGAL", 4)]),
    "HRD-REC": (20, [("RECR", 4)]), "HRD-LND": (34, [("TRAIN", 6)]), "HRD-PSV": (12, [("OFFICE", 2), ("CONS", 1), ("STAT", 1)]),
    "ADM-FAC": (125, [("FACM", 5), ("FURN", 2), ("SMFURN", 2), ("OFFICE", 3), ("VEHR", 2), ("VEH", 0.25)]), "ADM-PSC": (16, [("FACS", 4)]),
    "CEO-EXO": (28, [("OFFICE", 2), ("TRAVEL", 3), ("EVT", 1)]), "CEO-STR": (7, [("CONS", 4)]), "CEO-BSC": (9, [("EVT", 2), ("TRAVEL", 1), ("OFFICE", 1)]),
}
BRANCH_PLAN = {"CSV": (7, [("OFFICE", 2), ("BRAND", 1)]), "TLR": (7, [("CASH", 2), ("OFFICE", 1), ("CIT", 1)]), "SLS": (6, [("BRAND", 2), ("EVT", 1)]),
               "OPS": (11, [("FACM", 3), ("OFFICE", 1), ("SMFURN", 1), ("STAT", 1)]), None: (2, [("ATM", 2), ("FURN", 1)])}
ITD_Q3_TARGET = Decimal("1.187")   # planted finding 1: ITD Q3 actual / FY2025-REVISED budget
PLAN_SCALE = 1.05                  # requisition volume multiplier (about 1,200 a year)
MONTH_WEIGHTS = [0.8, 1.0, 0.9, 1.0, 1.0, 1.0, 0.85, 0.8, 1.1, 1.15, 1.2, 0.9]
PR_REJECTIONS = ["Not in the approved budget", "Please obtain three quotations", "Duplicate of an earlier requisition", "Defer to next quarter",
                 "Use the existing framework agreement"]
PO_CANCELLATIONS = ["Vendor unable to deliver", "Requirement cancelled by the requesting unit", "Replaced by a framework order"]

# Premises and ATM costs per month (interface from the lease and facilities systems), core-banking income per month.
RENT = {"ADM": 760000, "BR-DXB01": 135000, "BR-AUH01": 120000, "BR-SHJ01": 85000, "BR-AAN01": 70000, "BR-DXB02": 125000, "BR-RAK01": 60000}
UTILITIES = {"ADM": 140000, "BR-DXB01": 26000, "BR-AUH01": 24000, "BR-SHJ01": 19000, "BR-AAN01": 16000, "BR-DXB02": 22000, "BR-RAK01": 14000}
CLEANING_SECURITY = {"ADM": 185000, "BR-DXB01": 40000, "BR-AUH01": 38000, "BR-SHJ01": 32000, "BR-AAN01": 29000, "BR-DXB02": 36000, "BR-RAK01": 28000}
CASH_IN_TRANSIT = {"BR-DXB01": 24000, "BR-AUH01": 22000, "BR-SHJ01": 19000, "BR-AAN01": 17000, "BR-DXB02": 21000, "BR-RAK01": 16000}
SEASON = [0.75, 0.72, 0.8, 0.95, 1.15, 1.35, 1.5, 1.55, 1.35, 1.1, 0.85, 0.75]   # utilities (cooling) by month
ATM_SERVICE_PER_MACHINE = 3200
INCOME_SCALE = 0.7
INCOME = {  # cost centre: (net interest income, fee and commission, foreign exchange) per month, AED
    "BR-DXB01": (5600000, 1050000, 0), "BR-AUH01": (5100000, 950000, 0), "BR-SHJ01": (3600000, 700000, 0), "BR-AAN01": (2700000, 520000, 0),
    "BR-DXB02": (4400000, 880000, 0), "BR-RAK01": (2100000, 410000, 0), "CBD-CRM": (14000000, 1600000, 0), "CBD-SME": (4500000, 900000, 0),
    "CBD-TRF": (0, 1800000, 0), "TRY-ALM": (5000000, 0, 0), "TRY-DLR": (0, 0, 2200000), "RBD-CRD": (2800000, 1400000, 0), "DCC-DIG": (0, 350000, 0),
}


# ------------------------------------------------------------------------------------------------ helpers
def m2(x):
    return (x if isinstance(x, Decimal) else Decimal(str(x))).quantize(CENT, ROUND_HALF_UP)


def round_to(x, step):
    return (Decimal(str(x)) / step).quantize(Decimal("1"), ROUND_HALF_UP) * step


def workday(d):
    return d.weekday() < 5 and d not in bp.HOLIDAYS


def add_workdays(d, n):
    while n > 0:
        d += ONE
        if workday(d):
            n -= 1
    return d


def weekday_on_or_after(d):
    while d.weekday() >= 5:
        d += ONE
    return d


def month_end(m):
    return (dt.date(2025, m + 1, 1) if m < 12 else dt.date(2026, 1, 1)) - ONE


def period(d):
    return f"{d.year}-{d.month:02d}"


def quarter(d):
    return (d.month - 1) // 3 + 1


def first_day(start, pred, end=Y1):
    d = start
    while d <= end:
        if pred(d):
            return d
        d += ONE
    return None


def stamp(r, day, after=None, lo=8 * 60 + 5, hi=16 * 60 + 25):
    """A local time on a working day; strictly after `after` when that is the same day."""
    t = dt.datetime.combine(day, dt.time()) + dt.timedelta(minutes=r.randint(lo, hi), seconds=r.randint(0, 59))
    if after is not None and t <= after:
        t = after + dt.timedelta(minutes=r.randint(6, 80), seconds=r.randint(0, 59))
    assert t.date() == day, (day, after, t)
    return t


U = bp.by_user()
NUM = bp.by_number()
DEPT = {d["DepartmentCode"]: d for d in bp.departments()}
SECT = {s["SectionCode"]: s for s in bp.sections()}


def role_holder(role):
    ids = sorted({a["UserId"] for a in bp.access() if a["SystemCode"] == FIN and a["RoleCode"] == role})
    assert len(ids) == 1, (role, ids)
    return ids[0]


CFO, CEO, PM, APSUP, FC, BM = (role_holder(x) for x in ("ChiefFinancialOfficer", "ExecutiveApprover", "ProcurementManager", "PayablesSupervisor",
                                                         "FinancialController", "BudgetManager"))
PLANTED_BUYER_FINDING = next(a["UserId"] for a in bp.access() if a.get("Finding") == "Recruitment user with finance buyer access")


def can_act(uid, day, roles):
    """The user holds one of the FIN roles that day and (people only) is at work."""
    if uid == SYS:
        return bp.has_access(uid, FIN, day)
    p = U.get(uid)
    return p is not None and bp.has_access(uid, FIN, day, roles) and bp.present(p, day)


@lru_cache(maxsize=None)
def fin_users(day, unit, roles):
    """User ids of a section or department present that day with one of the FIN roles."""
    return tuple(sorted(p["UserId"] for p in bp.staff_on(day, unit, FIN, roles)))


@lru_cache(maxsize=None)
def present_staff(day, unit):
    return tuple(sorted(p["EmployeeNumber"] for p in bp.staff_on(day, unit)))


@lru_cache(maxsize=None)
def head(unit, day):
    return bp.head_of(unit, day)


def in_unit(p, unit, day):
    return bp.employed(p, day) and unit in bp.unit_on(p, day)[:2]


def unit_members_on(unit, day):
    return [p for p in bp.employees() if in_unit(p, unit, day)]


def cost_centre(dept, sect):
    return sect or dept


def location_of(cc):
    dept = SECT[cc]["DepartmentCode"] if cc in SECT else cc
    return DEPT[dept]["LocationCode"]


def dept_of(cc):
    return SECT[cc]["DepartmentCode"] if cc in SECT else cc


def unit_matches(tokens, unit):
    return tokens is None or unit in tokens or ("BR" in tokens and unit in bp.BRANCH_CODES)


def iban(bban, country="AE"):
    digits = "".join(str(int(c, 36)) for c in bban + country + "00")
    return f"{country}{98 - int(digits) % 97:02d}{bban}"


# ------------------------------------------------------------------------------------------------ the generator
class Finance:
    def __init__(self):
        self.vendors = []
        self.prs, self.pos, self.receipts, self.invoices, self.payments = [], [], [], [], []
        self.assets, self.journals = [], []       # journals built in Python (manual, premises, income)
        self.payroll = []                         # (month, run date, cost centre, salaries, pension, end of service, medical)
        self.actuals = []                         # (date, cost centre, account, amount, planted): every expense posting
        self.preferred = {}
        self.once = set()
        self.budget_base = {}
        self.transfers = []
        self.periods = []
        self.versions = []
        self.split_pos = []
        self.prepaid = []
        self.itd = {}

    # ---------------------------------------------------------------------------------------- vendors
    def make_vendors(self):
        r = rng(31)
        names, trns, n = set(), set(), 1
        prefixes = list(VENDOR_PREFIXES)
        for code, label, count, suffixes, terms in VENDOR_GROUPS:
            for i in range(count):
                while True:
                    name = f"{r.choice(prefixes)} {suffixes[i % len(suffixes)]}"
                    if name not in names:
                        break
                names.add(name)
                while True:
                    trn = f"100{r.randint(10 ** 11, 10 ** 12 - 1)}"
                    if trn not in trns:
                        break
                trns.add(trn)
                rating = "A" if r.random() < 0.35 else ("B" if r.random() < 0.7 else "C")
                city = r.choices(["Dubai", "Abu Dhabi", "Sharjah", "Ajman"], [55, 25, 15, 5])[0]
                slug = "".join(ch for ch in name.lower().split(" llc")[0].split(" fz")[0] if ch.isalnum())
                self.vendors.append({
                    "VendorCode": f"V{n:04d}", "VendorName": name, "VendorCategory": label, "Group": code, "Rating": rating, "TaxRegistrationNumber": trn,
                    "IBAN": iban(f"{r.choice(UAE_BANK_CODES)}{r.randint(10 ** 15, 10 ** 16 - 1)}"), "CountryCode": "AE", "City": city,
                    "PaymentTermsDays": r.choice(terms), "ContactEmail": f"accounts@{slug}.example",
                    "RegisteredDate": dt.date(2012, 1, 15) + dt.timedelta(days=r.randint(0, 4700)), "IsActive": True,
                    "InvoicePrefix": "".join(w[0] for w in name.replace("-", " ").split() if w[0].isalpha()).upper()[:4], "NextInvoice": r.randint(120, 4800)})
                n += 1
        # Two C-rated vendors were deactivated after a performance review and are no longer used.
        for v in [v for v in self.vendors if v["Rating"] == "C"][:2]:
            v["IsActive"] = False
        self.vendor = {v["VendorCode"]: v for v in self.vendors}

    def pick_vendor(self, r, unit, cat):
        group = CATS[cat][3]
        pool = [v for v in self.vendors if v["Group"] == group and v["IsActive"]]
        key = (unit, cat)
        if key not in self.preferred:
            self.preferred[key] = r.choices(pool, [{"A": 3, "B": 2, "C": 1}[v["Rating"]] for v in pool])[0]
        if r.random() < 0.55:
            return self.preferred[key]
        return r.choices(pool, [{"A": 3, "B": 2, "C": 1}[v["Rating"]] for v in pool])[0]

    # ---------------------------------------------------------------------------------------- requisitions
    def creators(self, day, unit):
        """Users who may raise a requisition for the unit that day: its Requisitioners (sections) or its BudgetHolder (branch managers)."""
        if unit in DEPT:
            return tuple(u for u in fin_users(day, unit, BH) if U[u]["Level"] == "DepartmentHead")
        return fin_users(day, unit, REQ)

    def pick_lines(self, r, cat, unit, count, fixed=None):
        name, ar, gl, group, lead, kind, aclass, life, items = CATS[cat]
        pool = [it for it in items if unit_matches(it[6], unit) and not (it[7] and (unit, it[0]) in self.once)]
        if not pool:
            pool = [it for it in items if it[6] is None and not it[7]]
        chosen = fixed or r.sample(pool, min(count, len(pool)))
        lines = []
        for it in chosen:
            desc, uom, lo, hi, qlo, qhi, _, once = it
            if once:
                self.once.add((unit, desc))
            if lo == hi:
                price = m2(lo)
            else:
                v = r.uniform(lo, hi)
                price = m2(round(v / 10) * 10) if hi >= 10000 else (m2(round(v)) if hi >= 1000 else m2(v))
            qty = r.randint(qlo, qhi)
            lines.append({"ItemDescription": desc, "UnitOfMeasure": uom, "GLAccountCode": gl, "Quantity": qty, "EstimatedUnitPrice": price,
                          "EstimatedAmount": m2(price * qty)})
        return lines

    def new_pr(self, r, unit, cat, day, creator, lines, requester=None, description=None):
        dept, sect, _, _ = bp.unit_on(U[creator], day)
        if unit in DEPT:
            assert dept == unit and sect is None, (unit, creator)
            sect = None
        else:
            assert sect == unit, (unit, creator, sect)
        if requester is None:
            if unit in DEPT or r.random() < 0.45:
                requester = U[creator]["EmployeeNumber"]
            else:
                requester = r.choice(present_staff(day, unit))
        created = stamp(r, day)
        name, ar, gl, group, lead, kind = CATS[cat][:6]
        total = sum(ln["EstimatedAmount"] for ln in lines)
        if description is None:
            description = f"{name}: {lines[0]['ItemDescription']}" + (" and other items" if len(lines) > 1 else "")
        pr = {"unit": unit, "cat": cat, "DepartmentCode": dept, "SectionCode": sect, "CostCentreCode": cost_centre(dept, sect),
              "RequestedByEmployeeNumber": requester, "CreatedByUserId": creator, "CreatedAt": created, "RequisitionDate": day,
              "SpendCategory": name, "Description": description[:200], "Priority": "Urgent" if r.random() < 0.12 else "Normal",
              "NeedByDate": day + dt.timedelta(days=lead + r.randint(7, 30)), "EstimatedAmount": total, "Status": "Draft",
              "SubmittedAt": None, "ApprovedByUserId": None, "ApprovedAt": None, "ApproverRoleCode": None, "RejectedByUserId": None,
              "RejectedAt": None, "RejectionReason": None, "lines": lines, "pos": [], "planted": None, "prepay": False}
        self.prs.append(pr)
        return pr

    def pr_approver(self, pr, day):
        """The unit's department head within their limit (never their own request), else the CFO up to 2,000,000, else the CEO."""
        amount = pr["EstimatedAmount"]
        involved = {pr["CreatedByUserId"], NUM[pr["RequestedByEmployeeNumber"]]["UserId"]}
        h = head(pr["DepartmentCode"], day)
        if h and h["UserId"] and h["UserId"] not in involved:
            a = bp.access_row(h["UserId"], FIN, day)
            if a and a["RoleCode"] in ("BudgetHolder", "ExecutiveApprover", "ChiefFinancialOfficer") and (a["ApprovalLimitAed"] is None or amount <= a["ApprovalLimitAed"]):
                return h["UserId"], a["RoleCode"]
        if amount <= 2000000:
            return CFO, "ChiefFinancialOfficer"
        return CEO, "ExecutiveApprover"

    def submit_and_decide(self, pr, r, lag=None, reject_rate=0.05):
        pr["Status"] = "Submitted"
        pr["SubmittedAt"] = pr["CreatedAt"] + dt.timedelta(minutes=r.randint(2, 40))
        sday = pr["SubmittedAt"].date()
        approver, role = self.pr_approver(pr, sday)
        lag = r.choice([0, 0, 1, 1, 2, 3]) if lag is None else lag
        day = first_day(add_workdays(sday, lag) if lag else sday, lambda d: can_act(approver, d, (role,)))
        if day is None:
            return
        at = stamp(r, day, after=pr["SubmittedAt"])
        if r.random() < reject_rate:
            pr.update(Status="Rejected", RejectedByUserId=approver, RejectedAt=at, RejectionReason=r.choice(PR_REJECTIONS))
        else:
            pr.update(Status="Approved", ApprovedByUserId=approver, ApprovedAt=at, ApproverRoleCode=role)

    def random_day(self, r):
        m = r.choices(range(1, 13), MONTH_WEIGHTS)[0]
        return dt.date(2025, m, r.randint(1, 28 if m == 2 else 30))

    def make_requisitions(self):
        r = rng(250901)
        plan = []
        for unit in sorted(HQ_PLAN):
            plan.append((unit, *HQ_PLAN[unit]))
        for b in bp.BRANCH_CODES:
            for suffix, (n, mix) in BRANCH_PLAN.items():
                plan.append((b if suffix is None else f"{b}-{suffix}", n, mix))
        # The core banking maintenance renewal is raised every January (CEO approval: above 2,000,000).
        amc = next(it for it in CATS["SWLIC"][8] if it[0].startswith("Core banking"))
        day = first_day(dt.date(2025, 1, 6), lambda d: bool(self.creators(d, "ITD-APP")))
        pr = self.new_pr(r, "ITD-APP", "SWLIC", day, self.creators(day, "ITD-APP")[0], self.pick_lines(r, "SWLIC", "ITD-APP", 1, fixed=[amc]),
                         description="Core banking annual maintenance and support 2025")
        self.submit_and_decide(pr, r, lag=1, reject_rate=0)
        for unit, n, mix in plan:
            for _ in range(int(round(n * PLAN_SCALE))):
                for _attempt in range(6):
                    day = first_day(self.random_day(r), lambda d: workday(d) and bool(self.creators(d, unit)), end=min(Y1, dt.date(2025, 12, 30)))
                    if day:
                        break
                if not day:
                    continue
                cat = r.choices([c for c, _ in mix], [w for _, w in mix])[0]
                kind = CATS[cat][5]
                count = (1 if r.random() < 0.8 else 2) if kind == "Services" else r.choices([1, 2, 3, 4], [45, 30, 17, 8])[0]
                pr = self.new_pr(r, unit, cat, day, r.choice(self.creators(day, unit)), self.pick_lines(r, cat, unit, count))
                if r.random() < 0.015:
                    continue                      # saved as a draft and never submitted
                self.submit_and_decide(pr, r)

    # ---------------------------------------------------------------------------------------- orders, receipts, invoices, payments
    def po_approver(self, subtotal):
        if subtotal <= 100000:
            return PM, "ProcurementManager"
        if subtotal <= 2000000:
            return CFO, "ChiefFinancialOfficer"
        return CEO, "ExecutiveApprover"

    def create_po(self, pr, r, line_numbers=None, start=None, buyer=None, vendor=None, factor=None, lag=None, cancel_rate=0.012):
        start = start or add_workdays(pr["ApprovedAt"].date(), r.choice([1, 1, 2, 2, 3, 4, 5]))
        if buyer is None:
            day = first_day(start, lambda d: bool(fin_users(d, "FIN-PRC", BUYER)))
            if day is None:
                return None
            buyer = r.choice(fin_users(day, "FIN-PRC", BUYER))
        else:
            day = first_day(start, lambda d: buyer in fin_users(d, "FIN-PRC", BUYER))
            if day is None:
                return None
        vendor = vendor or self.pick_vendor(r, pr["unit"], pr["cat"])
        lines = []
        for i, ln in enumerate(pr["lines"], 1):
            if line_numbers and i not in line_numbers:
                continue
            f = factor if factor is not None else r.uniform(0.93, 1.05)
            price = ln["EstimatedUnitPrice"] if f == 1 else (m2(round(float(ln["EstimatedUnitPrice"]) * f)) if ln["EstimatedUnitPrice"] >= 1000 else m2(float(ln["EstimatedUnitPrice"]) * f))
            lines.append({"RequisitionLineNumber": i, "ItemDescription": ln["ItemDescription"], "UnitOfMeasure": ln["UnitOfMeasure"],
                          "GLAccountCode": ln["GLAccountCode"], "Quantity": ln["Quantity"], "UnitPrice": price, "LineAmount": m2(price * ln["Quantity"]),
                          "QuantityReceived": 0})
        for n, ln in enumerate(lines, 1):
            ln["LineNumber"] = n
        sub = sum(ln["LineAmount"] for ln in lines)
        created = stamp(r, day, after=pr["ApprovedAt"])
        po = {"pr": pr, "cat": pr["cat"], "VendorCode": vendor["VendorCode"], "DepartmentCode": pr["DepartmentCode"], "SectionCode": pr["SectionCode"],
              "CostCentreCode": pr["CostCentreCode"], "RequestedByEmployeeNumber": pr["RequestedByEmployeeNumber"], "SpendCategory": pr["SpendCategory"],
              "Description": pr["Description"], "CreatedByUserId": buyer, "CreatedAt": created, "ApprovedByUserId": None, "ApprovedAt": None,
              "ApproverRoleCode": None, "OrderDate": None, "ExpectedDeliveryDate": None, "PaymentTermsDays": vendor["PaymentTermsDays"],
              "SubTotal": sub, "VatAmount": m2(sub * VAT), "Status": "PendingApproval", "CancellationReason": None, "lines": lines,
              "receipts": [], "invoice": None, "planted": pr["planted"]}
        po["TotalAmount"] = po["SubTotal"] + po["VatAmount"]
        pr["pos"].append(po)
        self.pos.append(po)
        approver, role = self.po_approver(sub)
        lag = r.choice([0, 0, 0, 1, 1, 2]) if lag is None else lag
        aday = first_day(add_workdays(day, lag) if lag else day, lambda d: can_act(approver, d, (role,)))
        if aday is None:
            return po
        po.update(ApprovedByUserId=approver, ApprovedAt=stamp(r, aday, after=created), ApproverRoleCode=role, OrderDate=aday, Status="Open")
        if r.random() < cancel_rate:
            po.update(Status="Cancelled", CancellationReason=r.choice(PO_CANCELLATIONS))
        return po

    def receivers(self, day, po):
        """A requisitioner of the ordering unit (any section of a branch for branch-level orders) or, at Head Office, a facilities coordinator."""
        if po["SectionCode"]:
            own = fin_users(day, po["SectionCode"], REQ)
        else:
            own = tuple(u for s in bp.sections() if s["DepartmentCode"] == po["DepartmentCode"] for u in fin_users(day, s["SectionCode"], REQ))
        fac = () if bp.is_branch(po["DepartmentCode"]) else tuple(u for u in fin_users(day, "ADM-FAC", REQ) if U[u]["Title"] == "Facilities Coordinator")
        return own, fac

    def receive(self, po, r, start, quantities, seq, final):
        def ok(d):
            own, fac = self.receivers(d, po)
            return bool(own or fac)
        day = first_day(max(start, po["OrderDate"] + ONE), ok)
        if day is None:
            return None
        own, fac = self.receivers(day, po)
        uid = r.choice(own) if own and (r.random() < 0.8 or not fac) else r.choice(fac)
        amount = sum(m2(ln["UnitPrice"] * q) for ln, q in zip(po["lines"], quantities))
        after = max([x["ReceivedAt"] for x in po["receipts"]] + [po["ApprovedAt"]])
        rec = {"po": po, "ReceiptDate": day, "ReceivedAt": stamp(r, day, after=after if after.date() == day else None), "ReceivedByUserId": uid,
               "ReceiptType": "Goods" if CATS[po["cat"]][5] == "Goods" else "Service", "DeliverySequence": seq,
               "IsFinalDelivery": final, "ReceivedAmount": amount, "DeliveryNoteNumber": f"DN-{r.randint(100000, 999999)}", "quantities": quantities}
        po["receipts"].append(rec)
        self.receipts.append(rec)
        for ln, q in zip(po["lines"], quantities):
            ln["QuantityReceived"] += q
        return rec

    def enter_invoice(self, po, r, inv_date, match, net, fast=False):
        if inv_date > Y1:
            return None
        day = first_day(inv_date + dt.timedelta(days=1 if fast else r.randint(1, 3)), lambda d: bool(fin_users(d, "FIN-AP", APO)))
        if day is None:
            return None
        uid = r.choice(fin_users(day, "FIN-AP", APO))
        after = max([x["ReceivedAt"] for x in po["receipts"]] + [po["ApprovedAt"]])
        inv = {"po": po, "VendorCode": po["VendorCode"], "InvoiceDate": inv_date, "EnteredAt": stamp(r, day, after=after if after.date() == day else None),
               "EnteredByUserId": uid, "DueDate": inv_date + dt.timedelta(days=po["PaymentTermsDays"]), "NetAmount": net, "VatAmount": m2(net * VAT),
               "MatchStatus": match, "VarianceAmount": net - po["SubTotal"], "Status": "Open", "payment": None, "hold_until": None}
        inv["TotalAmount"] = inv["NetAmount"] + inv["VatAmount"]
        po["invoice"] = inv
        self.invoices.append(inv)
        return inv

    def pay(self, inv, r, target):
        maker_day = first_day(target, lambda d: bool(fin_users(d, "FIN-AP", APO)))
        if maker_day is None:
            return None
        checker_day = first_day(maker_day, lambda d: can_act(APSUP, d, APS))
        if checker_day is None:
            return None
        maker = r.choice(fin_users(maker_day, "FIN-AP", APO))
        created = stamp(r, maker_day, after=inv["EnteredAt"] if inv["EnteredAt"].date() == maker_day else None)
        approved = stamp(r, checker_day, after=created if checker_day == maker_day else None, lo=10 * 60, hi=16 * 60 + 40)
        pay = {"invoice": inv, "VendorCode": inv["VendorCode"], "PaymentDate": checker_day, "Amount": inv["TotalAmount"], "CreatedByUserId": maker,
               "CreatedAt": created, "ApprovedByUserId": APSUP, "ApprovedAt": approved}
        inv["payment"] = pay
        self.payments.append(pay)
        return pay

    def fulfil(self, po, r, prepay=False, fast=False):
        """Delivery, invoice and payment of an approved order."""
        cat = CATS[po["cat"]]
        rating = self.vendor[po["VendorCode"]]["Rating"]
        od = po["OrderDate"]
        po["ExpectedDeliveryDate"] = od + dt.timedelta(days=r.randint(3, 6) if fast else max(1, cat[4] + r.randint(-2, 4)))
        if po["Status"] == "Cancelled":
            return
        full = [ln["Quantity"] for ln in po["lines"]]
        if prepay:
            # Planted: the vendor's invoice is entered and paid before anything is delivered.
            inv = self.enter_invoice(po, r, weekday_on_or_after(od + dt.timedelta(days=r.randint(1, 3))), "NoReceipt", po["SubTotal"], fast=True)
            pay = self.pay(inv, r, inv["EnteredAt"].date() + dt.timedelta(days=r.randint(1, 3)))
            rec = self.receive(po, r, pay["PaymentDate"] + dt.timedelta(days=r.randint(5, 12)), full, 1, True)
            assert rec and rec["ReceiptDate"] > pay["PaymentDate"]
            self.prepaid.append(po)
            return
        delay = {"A": r.randint(-3, 2), "B": r.randint(-1, 7), "C": r.randint(0, 15)}[rating]
        target = max(od + ONE, po["ExpectedDeliveryDate"] + dt.timedelta(days=0 if fast else delay))
        partial = cat[5] == "Goods" and cat[6] is None and not fast and r.random() < 0.07 and any(q >= 2 for q in full)
        if partial:
            p = r.uniform(0.4, 0.7)
            first = [int(q * p) for q in full]
            partial = sum(first) > 0
        if partial:
            r1 = self.receive(po, r, target, first, 1, False)
            r2 = self.receive(po, r, r1["ReceiptDate"] + dt.timedelta(days=r.randint(6, 20)), [a - b for a, b in zip(full, first)], 2, True) if r1 else None
        else:
            r1 = self.receive(po, r, target, full, 1, True)
            r2 = None
        if r1 is None:
            # Not delivered by 31 December: some vendors have already invoiced (held as NoReceipt, unpaid).
            if od <= dt.date(2025, 12, 19) and r.random() < 0.3:
                inv = self.enter_invoice(po, r, weekday_on_or_after(od + dt.timedelta(days=r.randint(3, 10))), "NoReceipt", po["SubTotal"])
                if inv:
                    inv["Status"] = "OnHold"
            return
        final = r2 if partial else r1
        if partial and (final is None or r.random() < 0.45):
            match, base, net = "QuantityVariance", r1["ReceiptDate"], po["SubTotal"]
        elif partial:
            match, base, net = "Matched", final["ReceiptDate"], po["SubTotal"]
        elif cat[6] is None and not fast and r.random() < 0.07:
            match, base, net = "PriceVariance", r1["ReceiptDate"], m2(po["SubTotal"] * Decimal(str(1 + r.uniform(0.012, 0.045))))
        else:
            match, base, net = "Matched", r1["ReceiptDate"], po["SubTotal"]
        inv = self.enter_invoice(po, r, weekday_on_or_after(base + dt.timedelta(days=r.randint(0, 2) if fast else r.randint(0, 6))), match, net, fast=fast)
        if inv is None:
            return
        entered = inv["EnteredAt"].date()
        hold = entered
        if match == "QuantityVariance":
            hold = final["ReceiptDate"] if final else None
        elif match == "PriceVariance":
            hold = entered + dt.timedelta(days=r.randint(3, 12))
        inv["hold_until"] = hold
        if hold is None or hold > Y1:
            inv["Status"] = "OnHold"
            return
        if r.random() < 0.08:
            target = max(hold, entered + dt.timedelta(days=r.randint(2, 8)))
        else:
            target = max(hold, inv["DueDate"] - dt.timedelta(days=r.randint(0, 4)))
        self.pay(inv, r, target)

    def make_orders(self):
        r = rng(250902)
        approved = sorted((p for p in self.prs if p["Status"] == "Approved" and p["planted"] is None), key=lambda p: p["ApprovedAt"])
        # Planted finding 3: four ordinary goods orders whose invoices are paid before delivery.
        candidates = [p for p in approved if dt.date(2025, 3, 1) <= p["ApprovedAt"].date() <= dt.date(2025, 10, 15) and CATS[p["cat"]][5] == "Goods"
                      and CATS[p["cat"]][6] is None and p["EstimatedAmount"] >= 8000]
        picked, sections = [], set()
        for p in rng(77).sample(candidates, len(candidates)):
            if p["CostCentreCode"] not in sections and len(picked) < 4:
                picked.append(p)
                sections.add(p["CostCentreCode"])
        for p in picked:
            p["prepay"] = True
        for pr in approved:
            big = pr["EstimatedAmount"] > 2000000          # the core banking renewal keeps its contract price (CEO approval)
            po = self.create_po(pr, r, factor=1 if big else None, cancel_rate=0 if pr["prepay"] or big else 0.012)
            if po and po["Status"] == "Open":
                self.fulfil(po, r, prepay=pr["prepay"])

    # ---------------------------------------------------------------------------------------- planted finding 2: split orders
    def plant_split_orders(self):
        r = rng(4401)
        unit = "ADM-FAC"
        targets = [("Workstation desk with pedestal", "EA", 34, 97850), ("Ergonomic office chair", "EA", 66, 94320),
                   ("Meeting room table and chairs", "SET", 4, 99180), ("Filing cabinets", "EA", 58, 92640)]

        def head_creator(d):
            return [u for u in self.creators(d, unit) if U[u]["Level"] == "SectionHead"]
        day = first_day(dt.date(2025, 6, 9), lambda d: bool(head_creator(d)))
        lines = []
        for desc, uom, qty, amount in targets:
            price = m2(Decimal(amount) / qty)
            lines.append({"ItemDescription": desc, "UnitOfMeasure": uom, "GLAccountCode": "150200", "Quantity": qty, "EstimatedUnitPrice": price,
                          "EstimatedAmount": m2(price * qty)})
        creator = head_creator(day)[0]
        pr = self.new_pr(r, unit, "FURN", day, creator, lines, requester=U[creator]["EmployeeNumber"],
                         description="Head Office level 14 refurbishment: workstations, chairs, meeting rooms and storage")
        pr["planted"] = "split"
        self.submit_and_decide(pr, r, lag=1, reject_rate=0)
        assert pr["Status"] == "Approved" and pr["ApproverRoleCode"] == "ChiefFinancialOfficer"
        vendor = next(v for v in self.vendors if v["Group"] == "FURNITURE" and v["IsActive"] and v["Rating"] == "B")
        start = add_workdays(pr["ApprovedAt"].date(), 1)
        found = None
        for d0 in (start + dt.timedelta(days=i) for i in range(60)):
            if not can_act(PM, d0, PMR):
                continue
            for buyer in fin_users(d0, "FIN-PRC", BUYER):
                days = [d for d in (d0 + dt.timedelta(days=i) for i in range(7)) if buyer in fin_users(d, "FIN-PRC", BUYER) and can_act(PM, d, PMR)]
                if len(days) >= 3:
                    found = (buyer, days)
                    break
            if found:
                break
        buyer, days = found
        plan = [days[0], days[0], days[1], days[2]]
        for i, d in enumerate(plan, 1):
            po = self.create_po(pr, r, line_numbers={i}, start=d, buyer=buyer, vendor=vendor, factor=1, lag=0, cancel_rate=0)
            assert po["OrderDate"] == d and po["ApproverRoleCode"] == "ProcurementManager" and 92000 <= po["SubTotal"] <= 99500, po["SubTotal"]
            self.split_pos.append(po)
        pr["Status"] = "Converted"
        for po in self.split_pos:
            self.fulfil(po, r)

    # ---------------------------------------------------------------------------------------- fixed assets
    def add_asset(self, desc, aclass, cc, acq, cost, life, custodian, serial=None, vendor=None, po=None, location=None):
        dept = dept_of(cc)
        sect = cc if cc in SECT else None
        assert in_unit(custodian, cc, acq), (desc, cc, acq, custodian["EmployeeNumber"])
        start = (acq.replace(day=1) + dt.timedelta(days=32)).replace(day=1)
        monthly = m2(cost / life)
        self.assets.append({"AssetDescription": desc, "AssetClassCode": aclass, "CostCentreCode": cc, "DepartmentCode": dept, "SectionCode": sect,
                            "LocationCode": location or location_of(cc), "CustodianEmployeeNumber": custodian["EmployeeNumber"], "SerialNumber": serial,
                            "VendorCode": vendor, "po": po, "AcquisitionDate": acq, "AcquisitionCost": m2(cost), "UsefulLifeMonths": life,
                            "DepreciationStartDate": start, "MonthlyDepreciation": monthly, "custodian": custodian})

    @staticmethod
    def depreciation(a, year, month):
        k = (year - a["DepreciationStartDate"].year) * 12 + month - a["DepreciationStartDate"].month + 1
        if 1 <= k < a["UsefulLifeMonths"]:
            return a["MonthlyDepreciation"]
        if k == a["UsefulLifeMonths"]:
            return a["AcquisitionCost"] - a["MonthlyDepreciation"] * (a["UsefulLifeMonths"] - 1)
        return ZERO

    def custodian_for(self, r, unit, day, prefer_heads=(), titles=()):
        """An employee of the unit employed on the day: preferred heads first, then given titles, then anyone; moves the day forward if nobody."""
        for _ in range(120):
            for h_unit in prefer_heads:
                h = head(h_unit, day)
                if h and in_unit(h, unit, day):
                    return h, day
            people = unit_members_on(unit, day)
            titled = [p for p in people if p["Title"] in titles]
            if titled or people:
                return r.choice(titled or people), day
            day += dt.timedelta(days=30)
        raise AssertionError(unit)

    def make_assets(self):
        r = rng(5150)
        ithw = [v["VendorCode"] for v in self.vendors if v["Group"] == "ITHW"]
        sn = lambda: "".join(r.choice("ABCDEFGHJKLMNPQRSTUVWXYZ0123456789") for _ in range(10))  # noqa: E731
        # End-user devices of everyone with a user account hired before 2025 (refreshed from 2021).
        for p in bp.employees():
            if not p["UserId"] or p["HireDate"] >= Y0:
                continue
            if p["HireDate"] < dt.date(2021, 1, 1):
                acq = weekday_on_or_after(dt.date(2021, 1, 4) + dt.timedelta(days=r.randint(0, 1420)))
            else:
                acq = min(weekday_on_or_after(p["HireDate"] + dt.timedelta(days=r.randint(0, 6))), dt.date(2024, 12, 31))
            acq = max(acq, p["HireDate"])
            dept, sect, loc, _ = bp.unit_on(p, acq)
            if p["Level"] == "DepartmentHead":
                desc, cost = "Executive laptop", r.uniform(7500, 9500)
            elif loc != "HO" or sect == "DCC-CC":
                desc, cost = "Desktop PC", r.uniform(3400, 4600)
            else:
                desc, cost = "Business laptop", r.uniform(4300, 6200)
            self.add_asset(f"{desc} - {p['FullName']}", "IT", cost_centre(dept, sect), acq, round(cost), 48, p, serial=sn(), vendor=r.choice(ithw))
        # Data centre, network and security equipment.
        dc = [("ITD-INF", "Rack server 2U", 10, 60000, 110000), ("ITD-INF", "Storage array", 2, 380000, 620000), ("ITD-INF", "Backup appliance", 2, 70000, 95000),
              ("ITD-INF", "Blade chassis", 1, 240000, 320000), ("ITD-NET", "Core switch 48-port", 6, 18000, 30000), ("ITD-NET", "Wi-Fi controller", 1, 45000, 60000),
              ("ITD-SEC", "Next-generation firewall", 2, 90000, 140000), ("ITD-SEC", "Hardware security module", 2, 150000, 220000)]
        for unit, desc, count, lo, hi in dc:
            for i in range(count):
                cust, acq = self.custodian_for(r, unit, weekday_on_or_after(dt.date(2019, 1, 6) + dt.timedelta(days=r.randint(0, 2100))), prefer_heads=(unit,))
                self.add_asset(f"{desc} #{i + 1}", "IT", unit, acq, round(r.uniform(lo, hi), -1), 60, cust, serial=sn(), vendor=r.choice(ithw))
        for loc in ["HO"] + bp.BRANCH_CODES:
            cust, acq = self.custodian_for(r, "ITD-NET", weekday_on_or_after(dt.date(2019, 3, 3) + dt.timedelta(days=r.randint(0, 1800))), prefer_heads=("ITD-NET",))
            self.add_asset(f"Router - {loc}", "IT", "ITD-NET", acq, round(r.uniform(9000, 15000), -1), 60, cust, serial=sn(), vendor=r.choice(ithw), location=loc)
        # Furniture of every cost centre.
        furn = [v["VendorCode"] for v in self.vendors if v["Group"] == "FURNITURE"]
        for cc in sorted(list(DEPT) + list(SECT)):
            dept = dept_of(cc)
            base = dt.date(2018, 3, 4) if not bp.is_branch(dept) else max(bp.LOC_OPENED[dept], dt.date(2016, 1, 10))
            cust, acq = self.custodian_for(r, cc, weekday_on_or_after(base + dt.timedelta(days=r.randint(0, 900))), prefer_heads=(cc,))
            size = SECT[cc]["Size"] if cc in SECT else 1
            label = "Executive office furniture" if cc in DEPT else "Office furniture and workstations"
            self.add_asset(f"{label} - {cc}", "FF", cc, acq, round(size * r.uniform(2600, 3600) + (15000 if cc in DEPT else 0), -1), 84, cust,
                           vendor=r.choice(furn))
        # Leasehold improvements: Head Office fit-out (Administration) and each branch fit-out.
        cust, acq = self.custodian_for(r, "ADM", dt.date(2018, 3, 1), prefer_heads=("ADM", "ADM-FAC"))
        self.add_asset("Head Office fit-out - Wabi Bank Tower", "LHI", "ADM", acq, 18500000, 120, cust)
        for b in bp.BRANCH_CODES:
            cust, acq = self.custodian_for(r, b, weekday_on_or_after(max(bp.LOC_OPENED[b], dt.date(2016, 3, 6)) + dt.timedelta(days=r.randint(0, 700))),
                                           prefer_heads=(b,))
            self.add_asset(f"Branch fit-out - {DEPT[b]['DepartmentName']}", "LHI", b, acq, round(r.uniform(2200000, 4500000), -3), 120, cust)
        # ATMs at every branch, teller cash recyclers and note counters.
        cash = [v["VendorCode"] for v in self.vendors if v["Group"] == "CASHSVC"]
        for b in bp.BRANCH_CODES:
            for where in ("lobby", "drive-through", "off-site"):
                cust, acq = self.custodian_for(r, b, weekday_on_or_after(max(bp.LOC_OPENED[b], dt.date(2018, 1, 7)) + dt.timedelta(days=r.randint(0, 1500))),
                                               prefer_heads=(f"{b}-OPS", b))
                self.add_asset(f"Cash recycler ATM ({where}) - {b}", "ATM", b, acq, round(r.uniform(85000, 135000), -1), 84, cust,
                               serial=f"ATM-{b[3:]}-{r.randint(1000, 9999)}", vendor=r.choice(cash))
            tlr = f"{b}-TLR"
            for desc, count, lo, hi, life in (("Teller cash recycler", 1, 60000, 90000, 84), ("Note counting machine", 2, 7500, 12500, 60)):
                for i in range(count):
                    cust, acq = self.custodian_for(r, tlr, weekday_on_or_after(max(bp.LOC_OPENED[b], dt.date(2019, 1, 6)) + dt.timedelta(days=r.randint(0, 1400))),
                                                   prefer_heads=(tlr,))
                    self.add_asset(f"{desc} #{i + 1} - {b}", "ATM", tlr, acq, round(r.uniform(lo, hi), -1), life, cust, serial=sn(), vendor=r.choice(cash))
        # Vehicles of Administration and Facilities.
        veh = [v["VendorCode"] for v in self.vendors if v["Group"] == "VEHICLES"]
        for desc, year, lo, hi in (("Staff transport van", 2020, 95000, 130000), ("Staff transport van", 2022, 100000, 135000), ("Executive sedan", 2021, 120000, 170000),
                                   ("Executive sedan", 2023, 140000, 185000), ("Utility pickup", 2019, 85000, 110000)):
            cust, acq = self.custodian_for(r, "ADM-FAC", weekday_on_or_after(dt.date(year, r.randint(1, 12), r.randint(1, 28))), titles=("Driver",))
            self.add_asset(desc, "MV", "ADM-FAC", acq, round(r.uniform(lo, hi), -2), 60, cust, serial=f"DXB-{r.choice('ABCDEFGHIJKLMNOPQRSTUVWXYZ')}-{r.randint(10000, 99999)}",
                           vendor=r.choice(veh))
        # 2025 additions: one asset per capitalised order line once delivered.
        for po in self.pos:
            aclass = CATS[po["cat"]][6]
            if not aclass or not po["receipts"] or not po["receipts"][-1]["IsFinalDelivery"]:
                continue
            acq = po["receipts"][-1]["ReceiptDate"]
            unit = po["CostCentreCode"]
            prefer = (f"{unit}-OPS", unit) if aclass == "ATM" and unit in DEPT else (unit,)
            cust, acq2 = self.custodian_for(r, unit, acq, prefer_heads=prefer)
            assert acq2 == acq
            for ln in po["lines"]:
                desc = ln["ItemDescription"] + (f" x {ln['Quantity']}" if ln["Quantity"] > 1 else "") + f" - {unit}"
                self.add_asset(desc, aclass, unit, acq, ln["LineAmount"], CATS[po["cat"]][7], cust, serial=sn() if ln["Quantity"] == 1 else None,
                               vendor=po["VendorCode"], po=po)
        for a in self.assets:
            acc = sum((self.depreciation(a, y, m) for y in range(a["DepreciationStartDate"].year, 2026) for m in range(1, 13)), ZERO)
            a["AccumulatedDepreciation"] = acc
            a["NetBookValue"] = a["AcquisitionCost"] - acc
            c = a["custodian"]
            a["Status"] = ("Returned" if c["TerminationDate"] and a["AssetClassCode"] == "IT" and c["UserId"] and a["AssetDescription"].endswith(c["FullName"])
                           else ("FullyDepreciated" if a["NetBookValue"] == 0 else "InUse"))
        for a in self.assets:
            for m in range(1, 13):
                amt = self.depreciation(a, 2025, m)
                if amt:
                    self.actuals.append((month_end(m), a["CostCentreCode"], ASSET_CLASSES[a["AssetClassCode"]][4], amt, False))

    # ---------------------------------------------------------------------------------------- payroll, premises, income
    def make_payroll(self):
        grades = {g[0]: (g[3], g[4]) for g in bp.GRADES}
        medical = {"G1": 650, "G2": 650, "G3": 650, "G4": 1050, "G5": 1050, "G6": 1050, "G7": 1550, "G8": 1550, "G9": 2300, "G10": 2300}
        sums = defaultdict(lambda: [ZERO, ZERO, ZERO, ZERO])
        for p in bp.employees():
            u = rng(int(p["EmployeeNumber"][1:]) * 7919 + 17).random()
            for m in range(1, 13):
                first, last = dt.date(2025, m, 1), month_end(m)
                lo, hi = max(first, p["HireDate"]), min(last, p["TerminationDate"] or last)
                if lo > hi:
                    continue
                frac = Decimal((hi - lo).days + 1) / Decimal(last.day)
                dept, _, _, grade = bp.unit_on(p, lo)
                band = grades[grade]
                gross = Decimal(str(band[0] + (band[1] - band[0]) * u)) * frac
                s = sums[(m, dept)]
                s[0] += gross
                if p["NationalityCode"] == "AE":
                    s[1] += gross * Decimal("0.75") * Decimal("0.125")
                else:
                    s[2] += gross * Decimal("0.035")
                s[3] += Decimal(medical[grade]) * frac
        for (m, dept), vals in sorted(sums.items()):
            run = month_end(m).replace(day=25)
            while not workday(run):
                run -= ONE
            vals = [m2(v) for v in vals]
            self.payroll.append((m, run, dept, *vals))
            for acct, v in zip(("610100", "610200", "610300", "610400"), vals):
                if v:
                    self.actuals.append((run, dept, acct, v, False))

    def journal(self, prefix, day, source, jtype, description, created_by, created_at, lines, approved_by=None, approved_at=None, reference=None, planted=False):
        dr = sum(x[2] for x in lines)
        cr = sum(x[3] for x in lines)
        assert dr == cr and dr > 0, (description, dr, cr)
        j = {"prefix": prefix, "JournalDate": day, "Source": source, "JournalType": jtype, "Description": description[:200], "ReferenceNumber": reference,
             "CreatedByUserId": created_by, "CreatedAt": created_at, "ApprovedByUserId": approved_by, "ApprovedAt": approved_at,
             "PostedAt": approved_at or created_at + dt.timedelta(minutes=2), "lines": lines, "planted": planted}
        self.journals.append(j)
        for acct, cc, d, c in lines:
            if acct in EXPENSE:
                self.actuals.append((day, cc, acct, d - c, planted))
        return j

    def make_premises_and_income(self):
        r = rng(6161)
        atms = defaultdict(list)
        for a in self.assets:
            if a["AssetClassCode"] == "ATM" and a["AssetDescription"].startswith("Cash recycler ATM"):
                atms[a["CostCentreCode"]].append(a["AcquisitionDate"])
        for m in range(1, 13):
            end = month_end(m)
            posted = dt.datetime.combine(end, dt.time(22, 0))
            lines = []
            for cc in RENT:
                rent = RENT[cc] * (1.05 if cc == "BR-DXB02" and m >= 7 else 1)
                lines.append(("620100", cc, m2(rent), ZERO))
                lines.append(("620200", cc, m2(UTILITIES[cc] * SEASON[m - 1] * r.uniform(0.97, 1.03)), ZERO))
                lines.append(("620400", cc, m2(CLEANING_SECURITY[cc] * r.uniform(0.98, 1.02)), ZERO))
            for b in bp.BRANCH_CODES:
                lines.append(("660100", b, m2(CASH_IN_TRANSIT[b] * r.uniform(0.94, 1.06)), ZERO))
                machines = sum(1 for d in atms[b] if d < dt.date(2025, m, 1))
                lines.append(("660200", b, m2(machines * ATM_SERVICE_PER_MACHINE * r.uniform(0.95, 1.05)), ZERO))
            lines.append(("220100", None, ZERO, sum(x[2] for x in lines)))
            self.journal("PREM", end, "Premises", "PremisesCosts", f"Premises and ATM costs {MONTHS[m - 1]} 2025: rent, utilities, cleaning and security, cash in transit, ATM service",
                         SYS, posted - dt.timedelta(minutes=2), lines, reference=f"FM-2025-{m:02d}")
            lines = []
            growth = 1 + 0.006 * (m - 1)
            for cc, (nii, fee, fx) in INCOME.items():
                for acct, v in (("410100", nii), ("420100", fee), ("430100", fx)):
                    if v:
                        lines.append((acct, cc, ZERO, m2(v * INCOME_SCALE * growth * r.uniform(0.95, 1.05))))
            lines.insert(0, ("120100", None, sum(x[3] for x in lines), ZERO))
            self.journal("CBS", end, "CoreBanking", "IncomeSummary", f"Core banking income summary {MONTHS[m - 1]} 2025 (net interest after funds transfer pricing, fees, FX)",
                         SYS, posted + dt.timedelta(minutes=8), lines, reference=f"CBS-GL-2025-{m:02d}")

    # ---------------------------------------------------------------------------------------- manual journals
    def manual_days(self, m, r, earliest=None):
        """(created by, created day, approved day) of a month-end journal: an accountant prepares it at month end or in the first days of
        the next month, the Financial Controller approves it (December journals must be approved by 31 December)."""
        days = [d for d in bp.DAYS_2025 if d.month == m and workday(d)][-2:]
        if m < 12:
            days += [d for d in bp.DAYS_2025 if d.month == m + 1 and workday(d)][:3]
        else:
            days = [d for d in bp.DAYS_2025 if d.month == 12 and workday(d)][-5:]
        for d in days:
            if earliest and d < earliest:
                continue
            accts = fin_users(d, "FIN-FCR", ACC)
            if accts:
                a_day = first_day(d, lambda x: can_act(FC, x, FCR), end=Y1 if m == 12 else month_end(m + 1))
                if a_day:
                    return r.choice(accts), d, a_day
        raise AssertionError(("no accountant / controller", m))

    def manual(self, m, r, jtype, description, lines, reference=None, planted=False, day=None, earliest=None):
        uid, cday, aday = self.manual_days(m, r, earliest)
        created = stamp(r, cday)
        approved = stamp(r, aday, after=created if aday == cday else None, lo=11 * 60, hi=17 * 60)
        return self.journal("MJ", day or month_end(m), "Manual", jtype, description, uid, created, lines, approved_by=FC, approved_at=approved,
                            reference=reference, planted=planted)

    def make_accruals(self):
        r = rng(7272)
        rates = {d: (Decimal("0.15") if d == "CEO" else Decimal("0.12") if d in ("RBD", "CBD", "TRY", "DCC") or bp.is_branch(d) else Decimal("0.08")) for d in DEPT}
        for m in range(1, 13):
            lines = []
            for (pm, run, dept, sal, *_rest) in self.payroll:
                if pm == m:
                    lines.append(("610500", dept, m2(sal * rates[dept]), ZERO))
            lines.append(("220500", None, ZERO, sum(x[2] for x in lines)))
            self.manual(m, r, "Accrual", f"Variable pay accrual {MONTHS[m - 1]} 2025", lines, reference=f"VPA-2025-{m:02d}")
            audit, regulatory = m2(72500), m2(58000 * r.uniform(0.96, 1.04))
            self.manual(m, r, "Accrual", f"Month-end accruals {MONTHS[m - 1]} 2025: external audit fee and regulatory fees",
                        [("650100", "FIN", audit, ZERO), ("650400", "CMP", regulatory, ZERO), ("220100", None, ZERO, audit + regulatory)], reference=f"ACR-2025-{m:02d}")

    def make_reclassifications(self):
        """Two or three reclassifications a month: part of a vendor invoice moved to another cost centre of the same department."""
        r = rng(7373)
        by_month = defaultdict(list)
        for inv in self.invoices:
            po = inv["po"]
            if po["planted"] or not po["SectionCode"]:
                continue
            for ln in po["lines"]:
                if ln["GLAccountCode"] in EXPENSE and ln["LineAmount"] >= 6000:
                    by_month[inv["EnteredAt"].month].append((inv, ln))
        for m in range(1, 13):
            pool = by_month[m]
            for inv, ln in r.sample(pool, min(len(pool), r.choice([2, 2, 3]))):
                src = inv["po"]["CostCentreCode"]
                others = [s["SectionCode"] for s in bp.sections() if s["DepartmentCode"] == inv["po"]["DepartmentCode"] and s["SectionCode"] != src]
                if not others or m == 12 and inv["EnteredAt"].date() > dt.date(2025, 12, 24):
                    continue
                dst = r.choice(others)
                pct = r.choice([20, 25, 30, 40, 50])
                amt = m2(ln["LineAmount"] * pct / 100)
                j = self.manual(m, r, "Reclassification", "", [(ln["GLAccountCode"], dst, amt, ZERO), (ln["GLAccountCode"], src, ZERO, amt)],
                                earliest=inv["EnteredAt"].date())
                j["reclass"] = (inv, pct, src, dst, ln["GLAccountCode"])

    # ---------------------------------------------------------------------------------------- budgets
    def make_budgets(self):
        r = rng(8484)
        base = defaultdict(lambda: [ZERO] * 5)
        months = defaultdict(set)
        for d, cc, acct, amt, planted in self.actuals:
            if planted:
                continue
            base[(cc, acct)][quarter(d)] += amt
            months[(cc, acct)].add(d.month)
        # Each department's budget is a little tight or generous overall (original and re-forecast), then each line varies.
        dept_f1 = {d: Decimal(str(r.uniform(0.94, 1.07))) for d in sorted(DEPT)}
        dept_f2 = {d: Decimal(str(r.uniform(0.955, 1.06))) for d in sorted(DEPT)}
        for key in sorted(base):
            q = base[key]
            steady = len(months[key]) >= 10
            annual, h2 = sum(q[1:]), q[3] + q[4]
            dept = dept_of(key[0])
            f1 = dept_f1[dept] * Decimal(str(r.uniform(0.98, 1.025) if steady else r.uniform(0.9, 1.15)))
            f2 = dept_f2[dept] * Decimal(str(r.uniform(0.985, 1.015) if steady else r.uniform(0.95, 1.06)))
            orig = [max(ZERO, (q[k] if steady else Decimal("0.6") * q[k] + Decimal("0.4") * annual / 4) * f1) for k in range(1, 5)]
            rev = [max(ZERO, (q[k] if steady else Decimal("0.85") * q[k] + Decimal("0.15") * h2 / 2) * f2) for k in (3, 4)]
            row = [round_to(x, 100) for x in orig + rev]
            if any(row):
                self.budget_base[key] = row

    def budget_month(self, key, version, m):
        row = self.budget_base[key]
        k = (m - 1) // 3
        qv = row[4 + k - 2] if version == REVISED and k >= 2 else row[k]
        return (qv / 3).quantize(Decimal("1"), ROUND_HALF_UP)

    def q3(self, dept):
        budget = sum(self.budget_month(k, REVISED, m) for k in self.budget_base if dept_of(k[0]) == dept for m in (7, 8, 9))
        actual = sum(a for d, cc, acct, a, _ in self.actuals if dept_of(cc) == dept and d.month in (7, 8, 9))
        return budget, actual

    # ---------------------------------------------------------------------------------------- planted finding 1: ITD overrun in Q3
    def plant_itd_overrun(self):
        r = rng(9595)
        budget, actual = self.q3("ITD")
        extra = ITD_Q3_TARGET * budget - actual
        assert extra > budget * Decimal("0.1"), (budget, actual)
        licence, incident = round_to(extra * Decimal("0.42"), 10), round_to(extra * Decimal("0.25"), 10)
        for unit, start, desc, amount, cat, gl in (("ITD-APP", dt.date(2025, 7, 2), "Database licence true-up after vendor audit", licence, "SWLIC", "630100"),
                                                   ("ITD-SEC", dt.date(2025, 7, 21), "Emergency incident response and forensic services", incident, "ITSVC", "630200")):
            day = first_day(start, lambda d: bool(self.creators(d, unit)))
            creator = self.creators(day, unit)[0]
            line = [{"ItemDescription": desc, "UnitOfMeasure": "JOB", "GLAccountCode": gl, "Quantity": 1, "EstimatedUnitPrice": amount, "EstimatedAmount": amount}]
            pr = self.new_pr(r, unit, cat, day, creator, line, requester=U[creator]["EmployeeNumber"], description=desc)
            pr["planted"] = "itd"
            pr["Priority"] = "Urgent"
            self.submit_and_decide(pr, r, lag=0, reject_rate=0)
            po = self.create_po(pr, r, start=add_workdays(pr["ApprovedAt"].date(), 1), factor=1, lag=0, cancel_rate=0)
            pr["Status"] = "Converted"
            self.fulfil(po, r, fast=True)
            assert po["invoice"] and dt.date(2025, 7, 1) <= po["invoice"]["EnteredAt"].date() <= dt.date(2025, 9, 30), desc
            self.itd[unit] = po
        contractors = extra - licence - incident
        monthly = [round_to(contractors / 3, 10), round_to(contractors / 3, 10)]
        monthly.append(contractors - sum(monthly))
        for m, amt in zip((7, 8, 9), monthly):
            self.manual(m, r, "Accrual", f"Accrual: IT contractors for core banking performance remediation, {MONTHS[m - 1]} 2025",
                        [("630200", "ITD-APP", m2(amt), ZERO), ("220100", None, ZERO, m2(amt))], reference=f"ITC-2025-{m:02d}", planted=True)

    # ---------------------------------------------------------------------------------------- budget versions, transfers, periods
    def make_versions(self):
        r = rng(9696)
        ba = U["IQURESHI"]["UserId"]
        rows = []
        for code, name, ar, vtype, eff, prep, appr, current, notes in (
                (ORIGINAL, "FY2025 original budget", "الموازنة الأصلية للسنة المالية 2025", "Original", "2025-01", dt.date(2024, 11, 27), dt.date(2024, 12, 18), False,
                 "Approved by the Board in December 2024; operating expenses by cost centre and month."),
                (REVISED, "FY2025 revised budget (Q2 re-forecast)", "الموازنة المعدلة للسنة المالية 2025 (إعادة التوقع للربع الثاني)", "Revised", "2025-07",
                 dt.date(2025, 6, 10), dt.date(2025, 6, 24), True, "January-June as originally approved; July-December re-forecast in June 2025.")):
            pday = first_day(prep, lambda d: can_act(ba, d, BAR), end=appr)
            aday = first_day(appr, lambda d: can_act(CFO, d, CFOR))
            rows.append({"BudgetVersionCode": code, "VersionName": name, "VersionNameAr": ar, "VersionType": vtype, "EffectiveFromPeriod": eff,
                         "PreparedByUserId": ba, "PreparedAt": stamp(r, pday), "ApprovedByUserId": CFO, "ApprovedAt": stamp(r, aday), "IsCurrent": current, "Notes": notes})
        self.versions = rows

    def make_transfers(self):
        r = rng(9797)
        depts = [d for d in sorted(DEPT) if d not in ("FIN", "CEO", "ITD")]
        for _ in range(38):
            m = r.randint(2, 12)
            dept = r.choice(depts)
            version = ORIGINAL if m <= 6 else REVISED
            pairs = [k for k in sorted(self.budget_base) if dept_of(k[0]) == dept and k[1].startswith("6")]
            source = [k for k in pairs if self.budget_month(k, version, m) >= 20000]
            if not source or len(pairs) < 2:
                continue
            src = r.choice(source)
            dst = r.choice([k for k in pairs if k != src])
            start = dt.date(2025, m, 1) - dt.timedelta(days=r.randint(0, 12))
            holder = lambda d: head(dept, d)["UserId"] if head(dept, d) else None  # noqa: E731
            day = first_day(start, lambda d: holder(d) is not None and can_act(holder(d), d, BH), end=min(Y1, month_end(m)))
            if day is None:
                continue
            requested = stamp(r, day)
            amount = max(Decimal(5000), round_to(self.budget_month(src, version, m) * Decimal(str(r.uniform(0.05, 0.25))), 1000))
            t = {"BudgetVersionCode": version, "PeriodCode": f"2025-{m:02d}", "DepartmentCode": dept, "FromCostCentreCode": src[0], "FromGLAccountCode": src[1],
                 "ToCostCentreCode": dst[0], "ToGLAccountCode": dst[1], "Amount": m2(amount),
                 "Reason": f"Reallocate {ACCOUNT[src[1]][1].lower()} budget to {ACCOUNT[dst[1]][1].lower()} ({dst[0]})",
                 "Status": "Pending", "RequestedByUserId": holder(day), "RequestedAt": requested, "ApprovedByUserId": None, "ApprovedAt": None,
                 "RejectedByUserId": None, "RejectedAt": None, "RejectionReason": None}
            aday = first_day(add_workdays(day, r.choice([0, 1, 1, 2, 3])), lambda d: can_act(BM, d, BMR))
            if aday:
                at = stamp(r, aday, after=requested if aday == day else None)
                if r.random() < 0.1:
                    t.update(Status="Rejected", RejectedByUserId=BM, RejectedAt=at, RejectionReason=r.choice(["Source budget already committed", "Raise a revised forecast instead"]))
                else:
                    t.update(Status="Approved", ApprovedByUserId=BM, ApprovedAt=at)
            self.transfers.append(t)

    def make_periods(self):
        r = rng(9898)
        last_posting = defaultdict(lambda: dt.datetime(2025, 1, 1))
        for j in self.journals:
            key = j["JournalDate"].month
            last_posting[key] = max(last_posting[key], j["PostedAt"])
        for m in range(1, 13):
            first, end = dt.date(2025, m, 1), month_end(m)
            row = {"PeriodCode": f"2025-{m:02d}", "FiscalYear": 2025, "MonthNumber": m, "QuarterNumber": (m - 1) // 3 + 1, "PeriodName": f"{MONTHS[m - 1]} 2025",
                   "PeriodNameAr": f"{MONTHS_AR[m - 1]} 2025", "StartDate": first, "EndDate": end, "Status": "Open", "ClosedAt": None, "ClosedByUserId": None}
            if m < 12:
                fourth = [d for d in bp.DAYS_2025 if d.month == m + 1 and workday(d)][3]
                day = first_day(max(fourth, last_posting[m].date()), lambda d: can_act(FC, d, FCR))
                closed = stamp(r, day, lo=15 * 60, hi=17 * 60 + 30)
                if closed <= last_posting[m]:
                    closed = last_posting[m] + dt.timedelta(minutes=20)
                row.update(Status="Closed", ClosedAt=closed, ClosedByUserId=FC)
            self.periods.append(row)

    # ---------------------------------------------------------------------------------------- numbering and statuses
    def finalize(self):
        for n, pr in enumerate(sorted(self.prs, key=lambda p: p["CreatedAt"]), 1):
            pr["RequisitionNumber"] = f"PR-25-{n:05d}"
            if pr["pos"]:
                pr["Status"] = "Converted"
            for i, ln in enumerate(pr["lines"], 1):
                ln["LineNumber"] = i
        for n, po in enumerate(sorted(self.pos, key=lambda p: p["CreatedAt"]), 1):
            po["PONumber"] = f"PO-25-{n:05d}"
            if po["Status"] in ("PendingApproval", "Cancelled"):
                continue
            recs = po["receipts"]
            inv = po["invoice"]
            if not recs:
                po["Status"] = "Open"
            elif not recs[-1]["IsFinalDelivery"]:
                po["Status"] = "PartiallyReceived"
            else:
                po["Status"] = "Closed" if inv and inv["payment"] else "Received"
        for n, rec in enumerate(sorted(self.receipts, key=lambda x: x["ReceivedAt"]), 1):
            rec["ReceiptNumber"] = f"GRN-25-{n:05d}"
        for n, inv in enumerate(sorted(self.invoices, key=lambda x: x["EnteredAt"]), 1):
            inv["InvoiceDocumentNumber"] = f"VI-25-{n:05d}"
        for inv in sorted(self.invoices, key=lambda x: (x["InvoiceDate"], x["EnteredAt"])):
            v = self.vendor[inv["VendorCode"]]
            inv["VendorInvoiceNumber"] = f"{v['InvoicePrefix']}-{v['NextInvoice']:05d}"
            v["NextInvoice"] += 1 + (v["NextInvoice"] % 7)
            if inv["payment"]:
                inv["Status"] = "Paid"
            elif inv["Status"] != "OnHold":
                inv["Status"] = "Open"
        for n, p in enumerate(sorted(self.payments, key=lambda x: x["CreatedAt"]), 1):
            p["PaymentNumber"] = f"PAY-25-{n:05d}"
            p["BankReference"] = f"FT{p['PaymentDate']:%y%m%d}{n:05d}"
        counters = defaultdict(int)
        for j in sorted(self.journals, key=lambda x: (x["prefix"], x["PostedAt"])):
            counters[j["prefix"]] += 1
            n = counters[j["prefix"]]
            j["JournalNumber"] = f"{j['prefix']}-25-{n:04d}" if j["prefix"] == "MJ" else f"{j['prefix']}-25-{n:02d}"
            if "reclass" in j:
                inv, pct, src, dst, acct = j["reclass"]
                j["Description"] = f"Reclassification: {pct} % of {ACCOUNT[acct][1].lower()} on {inv['InvoiceDocumentNumber']} from {src} to {dst} (shared cost)"
                j["ReferenceNumber"] = inv["InvoiceDocumentNumber"]
        for n, t in enumerate(sorted(self.transfers, key=lambda x: x["RequestedAt"]), 1):
            t["TransferNumber"] = f"BT-25-{n:03d}"
        for n, a in enumerate(sorted(self.assets, key=lambda x: (x["AcquisitionDate"], x["AssetClassCode"], x["CostCentreCode"], x["AssetDescription"])), 1):
            a["AssetNumber"] = f"AST-{n:05d}"

    def add_invoice_actuals(self):
        """Vendor-invoice postings (PO lines plus any price variance on line 1) mirrored for budget versus actual; run before budgets
        are derived and again after the planted ITD orders (flagged planted)."""
        for inv in self.invoices:
            if inv.get("in_actuals"):
                continue
            po = inv["po"]
            d = inv["EnteredAt"].date()
            for ln in po["lines"]:
                if ln["GLAccountCode"] in EXPENSE:
                    self.actuals.append((d, po["CostCentreCode"], ln["GLAccountCode"], ln["LineAmount"], po["planted"] == "itd"))
            if inv["VarianceAmount"] and po["lines"][0]["GLAccountCode"] in EXPENSE:
                self.actuals.append((d, po["CostCentreCode"], po["lines"][0]["GLAccountCode"], inv["VarianceAmount"], False))
            inv["in_actuals"] = True

    # ---------------------------------------------------------------------------------------- run
    def run(self):
        bp.validate()
        self.make_vendors()
        self.make_requisitions()
        self.make_orders()
        self.plant_split_orders()
        self.make_assets()
        self.make_payroll()
        self.make_premises_and_income()
        self.make_accruals()
        self.make_reclassifications()
        self.add_invoice_actuals()
        self.make_budgets()
        self.plant_itd_overrun()
        self.add_invoice_actuals()
        b, a = self.q3("ITD")
        self.itd["ratio"] = a / b
        self.make_versions()
        self.make_transfers()
        self.make_periods()
        self.finalize()
        validate(self)
        return self


# ------------------------------------------------------------------------------------------------ validation
def validate(f):
    """Asserts the consistency rules on the generated rows before any SQL is written."""
    users = {u["UserId"]: u for u in bp.local_users(FIN)}
    assert len(users) == len(bp.local_users(FIN))
    checked = [0]

    def actor(uid, day, roles, what):
        assert uid in users, (what, uid)
        assert can_act(uid, day, roles), (what, uid, day, roles)
        checked[0] += 1

    # Every unit, employee and user code exists in bank_people.
    for rec in f.prs + f.pos + [g["po"] for g in f.receipts] + [i["po"] for i in f.invoices] + f.assets:
        assert rec["DepartmentCode"] in DEPT and (rec["SectionCode"] is None or SECT[rec["SectionCode"]]["DepartmentCode"] == rec["DepartmentCode"])
        assert rec["CostCentreCode"] == (rec["SectionCode"] or rec["DepartmentCode"])
        for col in ("RequestedByEmployeeNumber", "CustodianEmployeeNumber"):
            assert col not in rec or rec[col] in NUM, (col, rec.get(col))
    for key in f.budget_base:
        assert (key[0] in DEPT or key[0] in SECT) and key[1] in EXPENSE
    for j in f.journals:
        assert all(cc is None or cc in DEPT or cc in SECT for _, cc, _, _ in j["lines"])
    for row in f.payroll:
        assert row[2] in DEPT

    for pr in f.prs:
        what = pr["RequisitionNumber"]
        d = pr["CreatedAt"].date()
        assert pr["DepartmentCode"] in DEPT and (pr["SectionCode"] is None or SECT[pr["SectionCode"]]["DepartmentCode"] == pr["DepartmentCode"])
        roles = BH if pr["SectionCode"] is None else REQ
        actor(pr["CreatedByUserId"], d, roles, what)
        assert bp.unit_on(U[pr["CreatedByUserId"]], d)[:2] == (pr["DepartmentCode"], pr["SectionCode"]), what
        req = NUM[pr["RequestedByEmployeeNumber"]]
        assert bp.present(req, d) and pr["unit"] in bp.unit_on(req, d)[:2], what
        assert pr["EstimatedAmount"] == sum(ln["EstimatedAmount"] for ln in pr["lines"])
        assert Y0 <= d <= Y1
        for who, at, role in ((pr["ApprovedByUserId"], pr["ApprovedAt"], pr["ApproverRoleCode"]), (pr["RejectedByUserId"], pr["RejectedAt"], None)):
            if who:
                a = bp.access_row(who, FIN, at.date())
                actor(who, at.date(), (role or a["RoleCode"],), what)
                assert who not in (pr["CreatedByUserId"], req["UserId"]), what
                assert a["ApprovalLimitAed"] is None or pr["EstimatedAmount"] <= a["ApprovalLimitAed"], what
                assert pr["SubmittedAt"] < at <= dt.datetime(2025, 12, 31, 23, 59), what
                if who != CFO and who != CEO:
                    assert head(pr["DepartmentCode"], at.date())["UserId"] == who, what
    for po in f.pos:
        what = po["PONumber"]
        pr = po["pr"]
        assert pr["Status"] == "Converted" and pr["ApprovedAt"] < po["CreatedAt"], what
        actor(po["CreatedByUserId"], po["CreatedAt"].date(), BUYER, what)
        assert po["CreatedByUserId"] != PLANTED_BUYER_FINDING, what
        assert bp.unit_on(U[po["CreatedByUserId"]], po["CreatedAt"].date())[1] == "FIN-PRC", what
        assert po["SubTotal"] == sum(ln["LineAmount"] for ln in po["lines"]) and po["TotalAmount"] == po["SubTotal"] + po["VatAmount"]
        if po["ApprovedByUserId"]:
            a = bp.access_row(po["ApprovedByUserId"], FIN, po["OrderDate"])
            actor(po["ApprovedByUserId"], po["OrderDate"], (po["ApproverRoleCode"],), what)
            assert po["ApprovedByUserId"] != po["CreatedByUserId"] and po["CreatedAt"] < po["ApprovedAt"] and po["ApprovedAt"].date() == po["OrderDate"]
            assert a["ApprovalLimitAed"] is None or po["SubTotal"] <= a["ApprovalLimitAed"], what
            assert po["OrderDate"] <= Y1
        for rec in po["receipts"]:
            actor(rec["ReceivedByUserId"], rec["ReceiptDate"], REQ, rec["ReceiptNumber"])
            ru = U[rec["ReceivedByUserId"]]
            own = bp.unit_on(ru, rec["ReceiptDate"])[0 if po["SectionCode"] is None else 1] == (po["SectionCode"] or po["DepartmentCode"])
            assert own or (ru["Title"] == "Facilities Coordinator" and not bp.is_branch(po["DepartmentCode"])), rec["ReceiptNumber"]
            assert po["OrderDate"] < rec["ReceiptDate"] <= Y1 and rec["ReceivedAt"] > po["ApprovedAt"]
        assert all(ln["QuantityReceived"] <= ln["Quantity"] for ln in po["lines"])
        inv = po["invoice"]
        if inv:
            what = inv["InvoiceDocumentNumber"]
            d = inv["EnteredAt"].date()
            actor(inv["EnteredByUserId"], d, APO, what)
            assert po["OrderDate"] <= inv["InvoiceDate"] <= d <= Y1, what
            planted = po in f.prepaid
            if po["receipts"] and not planted:
                assert po["receipts"][0]["ReceiptDate"] <= inv["InvoiceDate"], what
            if inv["MatchStatus"] == "NoReceipt" and not planted:
                assert not po["receipts"] and inv["payment"] is None, what
            p = inv["payment"]
            if p:
                what = p["PaymentNumber"]
                actor(p["CreatedByUserId"], p["CreatedAt"].date(), APO, what)
                actor(p["ApprovedByUserId"], p["PaymentDate"], APS, what)
                assert p["CreatedByUserId"] != p["ApprovedByUserId"] and inv["EnteredAt"] < p["CreatedAt"] < p["ApprovedAt"] and p["ApprovedAt"].date() == p["PaymentDate"]
                assert inv["InvoiceDate"] <= p["PaymentDate"] <= Y1 and p["Amount"] == inv["TotalAmount"]
                if planted:
                    assert p["PaymentDate"] < po["receipts"][0]["ReceiptDate"], what
                else:
                    assert po["receipts"] and po["receipts"][-1]["IsFinalDelivery"] and po["receipts"][-1]["ReceiptDate"] <= p["PaymentDate"], what
    for j in f.journals:
        what = j["JournalNumber"]
        assert sum(x[2] for x in j["lines"]) == sum(x[3] for x in j["lines"])
        if j["CreatedByUserId"] == SYS:
            actor(SYS, j["JournalDate"], None, what)
        else:
            actor(j["CreatedByUserId"], j["CreatedAt"].date(), ACC, what)
            actor(j["ApprovedByUserId"], j["ApprovedAt"].date(), FCR, what)
            assert j["ApprovedByUserId"] != j["CreatedByUserId"] and j["CreatedAt"] < j["ApprovedAt"] <= dt.datetime(2025, 12, 31, 23, 59)
        per = f.periods[j["JournalDate"].month - 1]
        assert per["ClosedAt"] is None or j["PostedAt"] < per["ClosedAt"], what
        for acct, cc, _, _ in j["lines"]:
            assert acct in ACCOUNT and (cc is None or cc in DEPT or cc in SECT)
    for per in f.periods:
        if per["ClosedAt"]:
            actor(per["ClosedByUserId"], per["ClosedAt"].date(), FCR, per["PeriodCode"])
    for t in f.transfers:
        actor(t["RequestedByUserId"], t["RequestedAt"].date(), BH, t["TransferNumber"])
        assert head(t["DepartmentCode"], t["RequestedAt"].date())["UserId"] == t["RequestedByUserId"]
        for who, at in ((t["ApprovedByUserId"], t["ApprovedAt"]), (t["RejectedByUserId"], t["RejectedAt"])):
            if who:
                actor(who, at.date(), BMR, t["TransferNumber"])
                assert who != t["RequestedByUserId"] and at > t["RequestedAt"]
        assert dept_of(t["FromCostCentreCode"]) == dept_of(t["ToCostCentreCode"]) == t["DepartmentCode"]
    for v in f.versions:
        actor(v["PreparedByUserId"], v["PreparedAt"].date(), BAR, v["BudgetVersionCode"])
        actor(v["ApprovedByUserId"], v["ApprovedAt"].date(), CFOR, v["BudgetVersionCode"])
    for a in f.assets:
        assert in_unit(a["custodian"], a["CostCentreCode"], a["AcquisitionDate"]), a["AssetNumber"]
        assert ZERO <= a["AccumulatedDepreciation"] <= a["AcquisitionCost"] and a["AcquisitionDate"] <= Y1
    # Planted findings: present, and only where planted.
    ratio = f.itd["ratio"]
    assert Decimal("1.15") <= ratio <= Decimal("1.25"), ratio
    for dept in DEPT:
        if dept != "ITD":
            b, a = f.q3(dept)
            assert b and abs(a / b - 1) <= Decimal("0.10"), (dept, a / b)
    clusters = defaultdict(list)
    for po in f.pos:
        if po["OrderDate"] and 90000 <= po["SubTotal"] < 100000:
            clusters[(po["CreatedByUserId"], po["VendorCode"], po["CostCentreCode"])].append(po["OrderDate"])
    hits = [k for k, ds in clusters.items() if len(ds) >= 3 and (max(ds) - min(ds)).days <= 7]
    assert len(hits) == 1 and len(clusters[hits[0]]) == 4, hits
    assert 3 <= len(f.prepaid) <= 5
    early = [p for p in f.payments if p["invoice"]["po"]["receipts"] and p["PaymentDate"] < p["invoice"]["po"]["receipts"][0]["ReceiptDate"]]
    assert len(early) == len(f.prepaid)
    f.actor_checks = checked[0]
    return True


# ------------------------------------------------------------------------------------------------ SQL
def rows(objs, cols):
    out = []
    for o in objs:
        out.append(tuple(float(o[c]) if isinstance(o[c], Decimal) else o[c] for c in cols))
    return out


def ins(table, cols, data):
    return insert(table, cols, [tuple(float(v) if isinstance(v, Decimal) else v for v in r) for r in data])


SCHEMA = """CREATE TABLE dbo.Users (
    UserId            nvarchar(20)  NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    EmployeeNumber    nvarchar(10)  NULL,       -- Personnel employee number; NULL for the interface service account
    FullName          nvarchar(100) NOT NULL,
    FullNameAr        nvarchar(100) NULL,
    DepartmentCode    nvarchar(10)  NULL,       -- unit on 31 Dec 2025 (or when access ended)
    SectionCode       nvarchar(14)  NULL,
    RoleCode          nvarchar(30)  NOT NULL,   -- FIN role: Accountant, FinancialController, BudgetAnalyst, BudgetManager, Buyer, ProcurementManager, ...
    ApprovalLimitAed  decimal(18,2) NULL,       -- before VAT; NULL = no limit (CEO) or the role approves nothing with a limit
    Status            nvarchar(10)  NOT NULL,   -- Active | Disabled
    AccessGrantedDate date          NOT NULL,
    AccessRevokedDate date          NULL
);
CREATE TABLE dbo.ChartOfAccounts (
    GLAccountCode     nvarchar(10)  NOT NULL CONSTRAINT PK_ChartOfAccounts PRIMARY KEY,
    AccountName       nvarchar(100) NOT NULL,
    AccountNameAr     nvarchar(100) NOT NULL,
    AccountType       nvarchar(10)  NOT NULL,   -- Asset | Liability | Equity | Income | Expense
    AccountCategory   nvarchar(40)  NOT NULL,
    AccountCategoryAr nvarchar(60)  NOT NULL,
    NormalBalance     nvarchar(6)   NOT NULL,   -- Debit | Credit
    IsBudgeted        bit           NOT NULL,   -- operating-expense accounts carried in BudgetLines
    IsActive          bit           NOT NULL
);
CREATE TABLE dbo.CostCentres (
    CostCentreCode      nvarchar(14)  NOT NULL CONSTRAINT PK_CostCentres PRIMARY KEY,   -- = DepartmentCode or SectionCode
    CostCentreName      nvarchar(120) NOT NULL,
    CostCentreNameAr    nvarchar(160) NOT NULL,
    CostCentreType      nvarchar(10)  NOT NULL,   -- Department | Section
    DepartmentCode      nvarchar(10)  NOT NULL,
    SectionCode         nvarchar(14)  NULL,
    DivisionCode        nvarchar(10)  NOT NULL,
    LocationCode        nvarchar(10)  NOT NULL,   -- HO or the branch code
    IsBranch            bit           NOT NULL,
    OwnerEmployeeNumber nvarchar(10)  NULL,       -- head of the unit on 31 Dec 2025
    IsActive            bit           NOT NULL
);
CREATE TABLE dbo.FiscalPeriods (
    PeriodCode     nvarchar(7)  NOT NULL CONSTRAINT PK_FiscalPeriods PRIMARY KEY,   -- 2025-01 ... 2025-12
    FiscalYear     smallint     NOT NULL,
    QuarterNumber  tinyint      NOT NULL,
    MonthNumber    tinyint      NOT NULL,
    PeriodName     nvarchar(20) NOT NULL,
    PeriodNameAr   nvarchar(30) NOT NULL,
    StartDate      date         NOT NULL,
    EndDate        date         NOT NULL,
    Status         nvarchar(10) NOT NULL,   -- Closed | Open
    ClosedAt       datetime2(0) NULL,
    ClosedByUserId nvarchar(20) NULL CONSTRAINT FK_FiscalPeriods_Users REFERENCES dbo.Users (UserId)
);
CREATE TABLE dbo.Vendors (
    VendorCode            nvarchar(10)  NOT NULL CONSTRAINT PK_Vendors PRIMARY KEY,
    VendorName            nvarchar(120) NOT NULL,
    VendorCategory        nvarchar(60)  NOT NULL,
    Rating                nvarchar(1)   NOT NULL,   -- A preferred | B acceptable | C under review
    TaxRegistrationNumber nvarchar(15)  NOT NULL,   -- UAE VAT TRN, 15 digits
    IBAN                  nvarchar(34)  NOT NULL,
    CountryCode           nvarchar(2)   NOT NULL,
    City                  nvarchar(40)  NOT NULL,
    PaymentTermsDays      int           NOT NULL,
    ContactEmail          nvarchar(120) NOT NULL,
    RegisteredDate        date          NOT NULL,
    IsActive              bit           NOT NULL
);
CREATE TABLE dbo.BudgetVersions (
    BudgetVersionCode   nvarchar(20)  NOT NULL CONSTRAINT PK_BudgetVersions PRIMARY KEY,
    FiscalYear          smallint      NOT NULL,
    VersionName         nvarchar(60)  NOT NULL,
    VersionNameAr       nvarchar(80)  NOT NULL,
    VersionType         nvarchar(10)  NOT NULL,   -- Original | Revised
    EffectiveFromPeriod nvarchar(7)   NOT NULL CONSTRAINT FK_BudgetVersions_FiscalPeriods REFERENCES dbo.FiscalPeriods (PeriodCode),
    Status              nvarchar(10)  NOT NULL,   -- Approved
    IsCurrent           bit           NOT NULL,
    PreparedByUserId    nvarchar(20)  NOT NULL CONSTRAINT FK_BudgetVersions_PreparedBy REFERENCES dbo.Users (UserId),
    PreparedAt          datetime2(0)  NOT NULL,
    ApprovedByUserId    nvarchar(20)  NOT NULL CONSTRAINT FK_BudgetVersions_ApprovedBy REFERENCES dbo.Users (UserId),
    ApprovedAt          datetime2(0)  NOT NULL,
    Notes               nvarchar(400) NULL
);
CREATE TABLE dbo.BudgetLines (
    BudgetLineId      int           IDENTITY(1, 1) NOT NULL CONSTRAINT PK_BudgetLines PRIMARY KEY,
    BudgetVersionCode nvarchar(20)  NOT NULL CONSTRAINT FK_BudgetLines_BudgetVersions REFERENCES dbo.BudgetVersions (BudgetVersionCode),
    PeriodCode        nvarchar(7)   NOT NULL CONSTRAINT FK_BudgetLines_FiscalPeriods REFERENCES dbo.FiscalPeriods (PeriodCode),
    CostCentreCode    nvarchar(14)  NOT NULL CONSTRAINT FK_BudgetLines_CostCentres REFERENCES dbo.CostCentres (CostCentreCode),
    DepartmentCode    nvarchar(10)  NOT NULL,
    SectionCode       nvarchar(14)  NULL,
    GLAccountCode     nvarchar(10)  NOT NULL CONSTRAINT FK_BudgetLines_ChartOfAccounts REFERENCES dbo.ChartOfAccounts (GLAccountCode),
    BudgetAmount      decimal(18,2) NOT NULL,   -- AED for the month
    CONSTRAINT UQ_BudgetLines UNIQUE (BudgetVersionCode, PeriodCode, CostCentreCode, GLAccountCode)
);
CREATE TABLE dbo.BudgetTransfers (
    TransferNumber     nvarchar(14)  NOT NULL CONSTRAINT PK_BudgetTransfers PRIMARY KEY,
    BudgetVersionCode  nvarchar(20)  NOT NULL CONSTRAINT FK_BudgetTransfers_BudgetVersions REFERENCES dbo.BudgetVersions (BudgetVersionCode),
    PeriodCode         nvarchar(7)   NOT NULL CONSTRAINT FK_BudgetTransfers_FiscalPeriods REFERENCES dbo.FiscalPeriods (PeriodCode),
    DepartmentCode     nvarchar(10)  NOT NULL,
    FromCostCentreCode nvarchar(14)  NOT NULL CONSTRAINT FK_BudgetTransfers_FromCostCentre REFERENCES dbo.CostCentres (CostCentreCode),
    FromGLAccountCode  nvarchar(10)  NOT NULL CONSTRAINT FK_BudgetTransfers_FromAccount REFERENCES dbo.ChartOfAccounts (GLAccountCode),
    ToCostCentreCode   nvarchar(14)  NOT NULL CONSTRAINT FK_BudgetTransfers_ToCostCentre REFERENCES dbo.CostCentres (CostCentreCode),
    ToGLAccountCode    nvarchar(10)  NOT NULL CONSTRAINT FK_BudgetTransfers_ToAccount REFERENCES dbo.ChartOfAccounts (GLAccountCode),
    Amount             decimal(18,2) NOT NULL,
    Reason             nvarchar(200) NOT NULL,
    Status             nvarchar(10)  NOT NULL,   -- Pending | Approved | Rejected
    RequestedByUserId  nvarchar(20)  NOT NULL CONSTRAINT FK_BudgetTransfers_RequestedBy REFERENCES dbo.Users (UserId),
    RequestedAt        datetime2(0)  NOT NULL,
    ApprovedByUserId   nvarchar(20)  NULL CONSTRAINT FK_BudgetTransfers_ApprovedBy REFERENCES dbo.Users (UserId),
    ApprovedAt         datetime2(0)  NULL,
    RejectedByUserId   nvarchar(20)  NULL CONSTRAINT FK_BudgetTransfers_RejectedBy REFERENCES dbo.Users (UserId),
    RejectedAt         datetime2(0)  NULL,
    RejectionReason    nvarchar(200) NULL
);
CREATE TABLE dbo.PurchaseRequisitions (
    RequisitionNumber         nvarchar(14)  NOT NULL CONSTRAINT PK_PurchaseRequisitions PRIMARY KEY,
    RequisitionDate           date          NOT NULL,
    DepartmentCode            nvarchar(10)  NOT NULL,
    SectionCode               nvarchar(14)  NULL,       -- NULL when a branch manager raised it for the whole branch
    CostCentreCode            nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseRequisitions_CostCentres REFERENCES dbo.CostCentres (CostCentreCode),
    RequestedByEmployeeNumber nvarchar(10)  NOT NULL,
    CreatedByUserId           nvarchar(20)  NOT NULL CONSTRAINT FK_PurchaseRequisitions_CreatedBy REFERENCES dbo.Users (UserId),
    CreatedAt                 datetime2(0)  NOT NULL,
    SpendCategory             nvarchar(60)  NOT NULL,
    Description               nvarchar(200) NOT NULL,
    Priority                  nvarchar(10)  NOT NULL,   -- Normal | Urgent
    NeedByDate                date          NOT NULL,
    EstimatedAmount           decimal(18,2) NOT NULL,   -- AED before VAT
    Status                    nvarchar(10)  NOT NULL,   -- Draft | Submitted | Approved | Rejected | Converted
    SubmittedAt               datetime2(0)  NULL,
    ApprovedByUserId          nvarchar(20)  NULL CONSTRAINT FK_PurchaseRequisitions_ApprovedBy REFERENCES dbo.Users (UserId),
    ApprovedAt                datetime2(0)  NULL,
    ApproverRoleCode          nvarchar(30)  NULL,
    RejectedByUserId          nvarchar(20)  NULL CONSTRAINT FK_PurchaseRequisitions_RejectedBy REFERENCES dbo.Users (UserId),
    RejectedAt                datetime2(0)  NULL,
    RejectionReason           nvarchar(200) NULL
);
CREATE TABLE dbo.PurchaseRequisitionLines (
    RequisitionNumber  nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseRequisitionLines_PurchaseRequisitions REFERENCES dbo.PurchaseRequisitions (RequisitionNumber),
    LineNumber         int           NOT NULL,
    ItemDescription    nvarchar(120) NOT NULL,
    UnitOfMeasure      nvarchar(10)  NOT NULL,
    GLAccountCode      nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseRequisitionLines_ChartOfAccounts REFERENCES dbo.ChartOfAccounts (GLAccountCode),
    Quantity           int           NOT NULL,
    EstimatedUnitPrice decimal(18,2) NOT NULL,
    EstimatedAmount    decimal(18,2) NOT NULL,
    CONSTRAINT PK_PurchaseRequisitionLines PRIMARY KEY (RequisitionNumber, LineNumber)
);
CREATE TABLE dbo.PurchaseOrders (
    PONumber                  nvarchar(14)  NOT NULL CONSTRAINT PK_PurchaseOrders PRIMARY KEY,
    RequisitionNumber         nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseOrders_PurchaseRequisitions REFERENCES dbo.PurchaseRequisitions (RequisitionNumber),
    VendorCode                nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseOrders_Vendors REFERENCES dbo.Vendors (VendorCode),
    DepartmentCode            nvarchar(10)  NOT NULL,
    SectionCode               nvarchar(14)  NULL,
    CostCentreCode            nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseOrders_CostCentres REFERENCES dbo.CostCentres (CostCentreCode),
    RequestedByEmployeeNumber nvarchar(10)  NOT NULL,
    SpendCategory             nvarchar(60)  NOT NULL,
    Description               nvarchar(200) NOT NULL,
    CreatedByUserId           nvarchar(20)  NOT NULL CONSTRAINT FK_PurchaseOrders_CreatedBy REFERENCES dbo.Users (UserId),
    CreatedAt                 datetime2(0)  NOT NULL,
    ApprovedByUserId          nvarchar(20)  NULL CONSTRAINT FK_PurchaseOrders_ApprovedBy REFERENCES dbo.Users (UserId),
    ApprovedAt                datetime2(0)  NULL,
    ApproverRoleCode          nvarchar(30)  NULL,
    OrderDate                 date          NULL,       -- the approval date, when the order was issued to the vendor
    ExpectedDeliveryDate      date          NULL,
    PaymentTermsDays          int           NOT NULL,
    CurrencyCode              nvarchar(3)   NOT NULL,
    SubTotal                  decimal(18,2) NOT NULL,   -- before VAT; approval limits apply to it
    VatAmount                 decimal(18,2) NOT NULL,   -- 5 % UAE VAT
    TotalAmount               decimal(18,2) NOT NULL,
    Status                    nvarchar(20)  NOT NULL,   -- PendingApproval | Open | PartiallyReceived | Received | Closed | Cancelled
    CancellationReason        nvarchar(200) NULL
);
CREATE TABLE dbo.PurchaseOrderLines (
    PONumber              nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseOrderLines_PurchaseOrders REFERENCES dbo.PurchaseOrders (PONumber),
    LineNumber            int           NOT NULL,
    RequisitionLineNumber int           NOT NULL,
    ItemDescription       nvarchar(120) NOT NULL,
    UnitOfMeasure         nvarchar(10)  NOT NULL,
    GLAccountCode         nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseOrderLines_ChartOfAccounts REFERENCES dbo.ChartOfAccounts (GLAccountCode),
    Quantity              int           NOT NULL,
    UnitPrice             decimal(18,2) NOT NULL,
    LineAmount            decimal(18,2) NOT NULL,   -- before VAT
    QuantityReceived      int           NOT NULL,
    CONSTRAINT PK_PurchaseOrderLines PRIMARY KEY (PONumber, LineNumber)
);
CREATE TABLE dbo.GoodsReceipts (
    ReceiptNumber      nvarchar(14)  NOT NULL CONSTRAINT PK_GoodsReceipts PRIMARY KEY,
    PONumber           nvarchar(14)  NOT NULL CONSTRAINT FK_GoodsReceipts_PurchaseOrders REFERENCES dbo.PurchaseOrders (PONumber),
    VendorCode         nvarchar(10)  NOT NULL CONSTRAINT FK_GoodsReceipts_Vendors REFERENCES dbo.Vendors (VendorCode),
    DepartmentCode     nvarchar(10)  NOT NULL,
    SectionCode        nvarchar(14)  NULL,
    CostCentreCode     nvarchar(14)  NOT NULL CONSTRAINT FK_GoodsReceipts_CostCentres REFERENCES dbo.CostCentres (CostCentreCode),
    ReceiptDate        date          NOT NULL,
    ReceivedAt         datetime2(0)  NOT NULL,
    ReceivedByUserId   nvarchar(20)  NOT NULL CONSTRAINT FK_GoodsReceipts_Users REFERENCES dbo.Users (UserId),
    ReceiptType        nvarchar(10)  NOT NULL,   -- Goods | Service (service acceptance)
    DeliverySequence   int           NOT NULL,   -- 1 = first (or only) delivery, 2 = balance of a partial delivery
    IsFinalDelivery    bit           NOT NULL,
    ReceivedAmount     decimal(18,2) NOT NULL,   -- value received at order prices, before VAT
    DeliveryNoteNumber nvarchar(20)  NOT NULL
);
CREATE TABLE dbo.VendorInvoices (
    InvoiceDocumentNumber nvarchar(14)  NOT NULL CONSTRAINT PK_VendorInvoices PRIMARY KEY,
    VendorCode            nvarchar(10)  NOT NULL CONSTRAINT FK_VendorInvoices_Vendors REFERENCES dbo.Vendors (VendorCode),
    VendorInvoiceNumber   nvarchar(20)  NOT NULL,
    PONumber              nvarchar(14)  NOT NULL CONSTRAINT FK_VendorInvoices_PurchaseOrders REFERENCES dbo.PurchaseOrders (PONumber),
    DepartmentCode        nvarchar(10)  NOT NULL,
    SectionCode           nvarchar(14)  NULL,
    InvoiceDate           date          NOT NULL,
    EnteredAt             datetime2(0)  NOT NULL,
    EnteredByUserId       nvarchar(20)  NOT NULL CONSTRAINT FK_VendorInvoices_Users REFERENCES dbo.Users (UserId),
    DueDate               date          NOT NULL,
    NetAmount             decimal(18,2) NOT NULL,
    VatAmount             decimal(18,2) NOT NULL,
    TotalAmount           decimal(18,2) NOT NULL,
    MatchStatus           nvarchar(20)  NOT NULL,   -- Matched | PriceVariance | QuantityVariance | NoReceipt (three-way match when entered)
    VarianceAmount        decimal(18,2) NOT NULL,   -- NetAmount minus the order SubTotal
    Status                nvarchar(10)  NOT NULL,   -- Open | OnHold | Paid
    CONSTRAINT UQ_VendorInvoices_VendorNumber UNIQUE (VendorCode, VendorInvoiceNumber)
);
CREATE TABLE dbo.Payments (
    PaymentNumber         nvarchar(14)  NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    InvoiceDocumentNumber nvarchar(14)  NOT NULL CONSTRAINT FK_Payments_VendorInvoices REFERENCES dbo.VendorInvoices (InvoiceDocumentNumber),
    VendorCode            nvarchar(10)  NOT NULL CONSTRAINT FK_Payments_Vendors REFERENCES dbo.Vendors (VendorCode),
    DepartmentCode        nvarchar(10)  NOT NULL,
    SectionCode           nvarchar(14)  NULL,
    PaymentDate           date          NOT NULL,   -- released by the checker
    Amount                decimal(18,2) NOT NULL,
    CurrencyCode          nvarchar(3)   NOT NULL,
    PaymentMethod         nvarchar(20)  NOT NULL,   -- BankTransfer
    BankReference         nvarchar(20)  NOT NULL,
    CreatedByUserId       nvarchar(20)  NOT NULL CONSTRAINT FK_Payments_CreatedBy REFERENCES dbo.Users (UserId),
    CreatedAt             datetime2(0)  NOT NULL,
    ApprovedByUserId      nvarchar(20)  NOT NULL CONSTRAINT FK_Payments_ApprovedBy REFERENCES dbo.Users (UserId),
    ApprovedAt            datetime2(0)  NOT NULL,
    Status                nvarchar(10)  NOT NULL    -- Released
);
CREATE TABLE dbo.JournalEntries (
    JournalNumber    nvarchar(16)  NOT NULL CONSTRAINT PK_JournalEntries PRIMARY KEY,
    JournalDate      date          NOT NULL,
    PeriodCode       nvarchar(7)   NOT NULL CONSTRAINT FK_JournalEntries_FiscalPeriods REFERENCES dbo.FiscalPeriods (PeriodCode),
    Source           nvarchar(20)  NOT NULL,   -- Manual | Payroll | FixedAssets | Premises | CoreBanking | Payables
    JournalType      nvarchar(20)  NOT NULL,   -- Accrual | Reclassification | Payroll | Depreciation | PremisesCosts | IncomeSummary | VendorInvoices | VendorPayments
    Description      nvarchar(200) NOT NULL,
    ReferenceNumber  nvarchar(30)  NULL,
    Status           nvarchar(10)  NOT NULL,   -- Posted
    CreatedByUserId  nvarchar(20)  NOT NULL CONSTRAINT FK_JournalEntries_CreatedBy REFERENCES dbo.Users (UserId),
    CreatedAt        datetime2(0)  NOT NULL,
    ApprovedByUserId nvarchar(20)  NULL CONSTRAINT FK_JournalEntries_ApprovedBy REFERENCES dbo.Users (UserId),
    ApprovedAt       datetime2(0)  NULL,
    PostedAt         datetime2(0)  NOT NULL,
    TotalDebit       decimal(18,2) NOT NULL,
    TotalCredit      decimal(18,2) NOT NULL,
    CurrencyCode     nvarchar(3)   NOT NULL
);
CREATE TABLE dbo.JournalLines (
    JournalNumber   nvarchar(16)  NOT NULL CONSTRAINT FK_JournalLines_JournalEntries REFERENCES dbo.JournalEntries (JournalNumber),
    LineNumber      int           NOT NULL,
    GLAccountCode   nvarchar(10)  NOT NULL CONSTRAINT FK_JournalLines_ChartOfAccounts REFERENCES dbo.ChartOfAccounts (GLAccountCode),
    CostCentreCode  nvarchar(14)  NULL CONSTRAINT FK_JournalLines_CostCentres REFERENCES dbo.CostCentres (CostCentreCode),   -- NULL on balance-sheet lines
    DepartmentCode  nvarchar(10)  NULL,
    SectionCode     nvarchar(14)  NULL,
    DebitAmount     decimal(18,2) NOT NULL,
    CreditAmount    decimal(18,2) NOT NULL,
    LineDescription nvarchar(200) NULL,
    ReferenceNumber nvarchar(30)  NULL,   -- vendor invoice or payment number on payables lines
    VendorCode      nvarchar(10)  NULL CONSTRAINT FK_JournalLines_Vendors REFERENCES dbo.Vendors (VendorCode),
    CONSTRAINT PK_JournalLines PRIMARY KEY (JournalNumber, LineNumber)
);
CREATE TABLE dbo.FixedAssets (
    AssetNumber             nvarchar(12)  NOT NULL CONSTRAINT PK_FixedAssets PRIMARY KEY,
    AssetDescription        nvarchar(150) NOT NULL,
    AssetClassCode          nvarchar(4)   NOT NULL,   -- IT | FF | MV | ATM | LHI
    AssetClass              nvarchar(40)  NOT NULL,
    GLAccountCode           nvarchar(10)  NOT NULL CONSTRAINT FK_FixedAssets_ChartOfAccounts REFERENCES dbo.ChartOfAccounts (GLAccountCode),
    CostCentreCode          nvarchar(14)  NOT NULL CONSTRAINT FK_FixedAssets_CostCentres REFERENCES dbo.CostCentres (CostCentreCode),
    DepartmentCode          nvarchar(10)  NOT NULL,
    SectionCode             nvarchar(14)  NULL,
    LocationCode            nvarchar(10)  NOT NULL,
    CustodianEmployeeNumber nvarchar(10)  NOT NULL,
    SerialNumber            nvarchar(30)  NULL,
    VendorCode              nvarchar(10)  NULL CONSTRAINT FK_FixedAssets_Vendors REFERENCES dbo.Vendors (VendorCode),
    PONumber                nvarchar(14)  NULL CONSTRAINT FK_FixedAssets_PurchaseOrders REFERENCES dbo.PurchaseOrders (PONumber),
    AcquisitionDate         date          NOT NULL,
    AcquisitionCost         decimal(18,2) NOT NULL,
    UsefulLifeMonths        int           NOT NULL,
    DepreciationStartDate   date          NOT NULL,   -- first day of the month after acquisition; straight line, no residual value
    MonthlyDepreciation     decimal(18,2) NOT NULL,   -- the last month takes the rounding difference
    AccumulatedDepreciation decimal(18,2) NOT NULL,   -- as of 31 Dec 2025
    NetBookValue            decimal(18,2) NOT NULL,   -- as of 31 Dec 2025
    Status                  nvarchar(20)  NOT NULL    -- InUse | FullyDepreciated | Returned (device of a leaver, back in the IT store)
);
GO
"""

DERIVED_JOURNALS = """-- Payroll interface (HRMS -> GL), one journal per month: staff costs per department cost centre.
INSERT INTO dbo.JournalEntries (JournalNumber, JournalDate, PeriodCode, Source, JournalType, Description, ReferenceNumber, Status, CreatedByUserId, CreatedAt,
                                ApprovedByUserId, ApprovedAt, PostedAt, TotalDebit, TotalCredit, CurrencyCode)
SELECT DISTINCT CONCAT(N'PAYROLL-25-', RIGHT(CONCAT(N'0', p.MonthNumber), 2)), p.RunDate, fp.PeriodCode, N'Payroll', N'Payroll',
       CONCAT(N'Payroll ', fp.PeriodName, N': staff costs by department'), CONCAT(N'HRMS-RUN-2025-', RIGHT(CONCAT(N'0', p.MonthNumber), 2)), N'Posted', N'SYS_FIN_IF',
       DATEADD(MINUTE, 18 * 60, CAST(p.RunDate AS datetime2(0))), NULL, NULL, DATEADD(MINUTE, 18 * 60 + 2, CAST(p.RunDate AS datetime2(0))), 0, 0, N'AED'
FROM #Payroll p JOIN dbo.FiscalPeriods fp ON fp.MonthNumber = p.MonthNumber;
WITH L AS (
    SELECT p.MonthNumber, 1 AS Ord, p.CostCentreCode, v.Account, v.Amount, 0 AS IsCredit
    FROM #Payroll p CROSS APPLY (VALUES (N'610100', p.Salaries), (N'610200', p.Pension), (N'610300', p.EndOfService), (N'610400', p.Medical)) v(Account, Amount)
    WHERE v.Amount <> 0
    UNION ALL
    SELECT p.MonthNumber, 2, NULL, v.Account, SUM(v.Amount), 1
    FROM #Payroll p CROSS APPLY (VALUES (N'220200', p.Salaries), (N'220400', p.Pension), (N'220300', p.EndOfService), (N'220100', p.Medical)) v(Account, Amount)
    GROUP BY p.MonthNumber, v.Account HAVING SUM(v.Amount) <> 0
)
INSERT INTO dbo.JournalLines (JournalNumber, LineNumber, GLAccountCode, CostCentreCode, DebitAmount, CreditAmount)
SELECT CONCAT(N'PAYROLL-25-', RIGHT(CONCAT(N'0', MonthNumber), 2)), ROW_NUMBER() OVER (PARTITION BY MonthNumber ORDER BY Ord, CostCentreCode, Account), Account, CostCentreCode,
       CASE WHEN IsCredit = 0 THEN Amount ELSE 0 END, CASE WHEN IsCredit = 1 THEN Amount ELSE 0 END
FROM L;
GO
-- Depreciation (fixed-asset register -> GL), one journal per month: straight line from the month after acquisition.
SELECT m.MonthNumber, fa.CostCentreCode, fa.AssetClassCode,
       SUM(CASE WHEN k.K < fa.UsefulLifeMonths THEN fa.MonthlyDepreciation ELSE fa.AcquisitionCost - fa.MonthlyDepreciation * (fa.UsefulLifeMonths - 1) END) AS Amount
INTO #Depreciation
FROM dbo.FixedAssets fa
CROSS JOIN (VALUES (1), (2), (3), (4), (5), (6), (7), (8), (9), (10), (11), (12)) m(MonthNumber)
CROSS APPLY (SELECT DATEDIFF(MONTH, fa.DepreciationStartDate, DATEFROMPARTS(2025, m.MonthNumber, 1)) + 1 AS K) k
WHERE k.K BETWEEN 1 AND fa.UsefulLifeMonths
GROUP BY m.MonthNumber, fa.CostCentreCode, fa.AssetClassCode;
INSERT INTO dbo.JournalEntries (JournalNumber, JournalDate, PeriodCode, Source, JournalType, Description, ReferenceNumber, Status, CreatedByUserId, CreatedAt,
                                ApprovedByUserId, ApprovedAt, PostedAt, TotalDebit, TotalCredit, CurrencyCode)
SELECT CONCAT(N'DEPR-25-', RIGHT(CONCAT(N'0', fp.MonthNumber), 2)), fp.EndDate, fp.PeriodCode, N'FixedAssets', N'Depreciation', CONCAT(N'Depreciation ', fp.PeriodName),
       CONCAT(N'FA-DEPR-2025-', RIGHT(CONCAT(N'0', fp.MonthNumber), 2)), N'Posted', N'SYS_FIN_IF', DATEADD(MINUTE, 21 * 60 + 30, CAST(fp.EndDate AS datetime2(0))), NULL, NULL,
       DATEADD(MINUTE, 21 * 60 + 32, CAST(fp.EndDate AS datetime2(0))), 0, 0, N'AED'
FROM dbo.FiscalPeriods fp;
WITH L AS (
    SELECT d.MonthNumber, 1 AS Ord, d.CostCentreCode, ac.ExpenseAccount AS Account, d.Amount AS Debit, CAST(0 AS decimal(18,2)) AS Credit
    FROM #Depreciation d JOIN #AssetClasses ac ON ac.AssetClassCode = d.AssetClassCode
    UNION ALL
    SELECT d.MonthNumber, 2, NULL, ac.AccumulatedAccount, 0, SUM(d.Amount)
    FROM #Depreciation d JOIN #AssetClasses ac ON ac.AssetClassCode = d.AssetClassCode GROUP BY d.MonthNumber, ac.AccumulatedAccount
)
INSERT INTO dbo.JournalLines (JournalNumber, LineNumber, GLAccountCode, CostCentreCode, DebitAmount, CreditAmount)
SELECT CONCAT(N'DEPR-25-', RIGHT(CONCAT(N'0', MonthNumber), 2)), ROW_NUMBER() OVER (PARTITION BY MonthNumber ORDER BY Ord, Account, CostCentreCode), Account, CostCentreCode, Debit, Credit
FROM L;
DROP TABLE #Depreciation;
GO
-- Vendor invoices (payables -> GL), one interface batch per entry day: order lines (expense or asset), price variance, input VAT, payable.
SELECT CAST(EnteredAt AS date) AS EntryDate, CONCAT(N'APINV-25-', RIGHT(CONCAT(N'000', ROW_NUMBER() OVER (ORDER BY CAST(EnteredAt AS date))), 4)) AS JournalNumber
INTO #InvoiceBatches FROM dbo.VendorInvoices GROUP BY CAST(EnteredAt AS date);
INSERT INTO dbo.JournalEntries (JournalNumber, JournalDate, PeriodCode, Source, JournalType, Description, ReferenceNumber, Status, CreatedByUserId, CreatedAt,
                                ApprovedByUserId, ApprovedAt, PostedAt, TotalDebit, TotalCredit, CurrencyCode)
SELECT b.JournalNumber, b.EntryDate, CONVERT(nchar(7), b.EntryDate, 126), N'Payables', N'VendorInvoices', CONCAT(N'Vendor invoices entered on ', CONVERT(nchar(10), b.EntryDate, 23)),
       NULL, N'Posted', N'SYS_FIN_IF', DATEADD(MINUTE, 19 * 60, CAST(b.EntryDate AS datetime2(0))), NULL, NULL, DATEADD(MINUTE, 19 * 60 + 3, CAST(b.EntryDate AS datetime2(0))), 0, 0, N'AED'
FROM #InvoiceBatches b;
WITH L AS (
    SELECT vi.InvoiceDocumentNumber, vi.VendorCode, CAST(vi.EnteredAt AS date) AS EntryDate, 1 AS Kind, pol.LineNumber AS Seq, pol.GLAccountCode, po.CostCentreCode,
           pol.LineAmount AS Debit, CAST(0 AS decimal(18,2)) AS Credit
    FROM dbo.VendorInvoices vi JOIN dbo.PurchaseOrders po ON po.PONumber = vi.PONumber JOIN dbo.PurchaseOrderLines pol ON pol.PONumber = vi.PONumber
    UNION ALL
    SELECT vi.InvoiceDocumentNumber, vi.VendorCode, CAST(vi.EnteredAt AS date), 2, 0, pol.GLAccountCode, po.CostCentreCode, vi.VarianceAmount, 0
    FROM dbo.VendorInvoices vi JOIN dbo.PurchaseOrders po ON po.PONumber = vi.PONumber JOIN dbo.PurchaseOrderLines pol ON pol.PONumber = vi.PONumber AND pol.LineNumber = 1
    WHERE vi.VarianceAmount <> 0
    UNION ALL
    SELECT vi.InvoiceDocumentNumber, vi.VendorCode, CAST(vi.EnteredAt AS date), 3, 0, N'130100', NULL, vi.VatAmount, 0 FROM dbo.VendorInvoices vi
    UNION ALL
    SELECT vi.InvoiceDocumentNumber, vi.VendorCode, CAST(vi.EnteredAt AS date), 4, 0, N'210100', NULL, 0, vi.TotalAmount FROM dbo.VendorInvoices vi
)
INSERT INTO dbo.JournalLines (JournalNumber, LineNumber, GLAccountCode, CostCentreCode, DebitAmount, CreditAmount, LineDescription, ReferenceNumber, VendorCode)
SELECT b.JournalNumber, ROW_NUMBER() OVER (PARTITION BY b.JournalNumber ORDER BY L.InvoiceDocumentNumber, L.Kind, L.Seq), L.GLAccountCode, L.CostCentreCode, L.Debit, L.Credit,
       LEFT(CONCAT(L.InvoiceDocumentNumber, N' ', v.VendorName), 200), L.InvoiceDocumentNumber, L.VendorCode
FROM L JOIN #InvoiceBatches b ON b.EntryDate = L.EntryDate JOIN dbo.Vendors v ON v.VendorCode = L.VendorCode;
DROP TABLE #InvoiceBatches;
GO
-- Vendor payments (payables -> GL), one interface batch per payment date: payable cleared against the payments clearing account.
SELECT PaymentDate, CONCAT(N'APPAY-25-', RIGHT(CONCAT(N'000', ROW_NUMBER() OVER (ORDER BY PaymentDate)), 4)) AS JournalNumber
INTO #PaymentBatches FROM dbo.Payments GROUP BY PaymentDate;
INSERT INTO dbo.JournalEntries (JournalNumber, JournalDate, PeriodCode, Source, JournalType, Description, ReferenceNumber, Status, CreatedByUserId, CreatedAt,
                                ApprovedByUserId, ApprovedAt, PostedAt, TotalDebit, TotalCredit, CurrencyCode)
SELECT b.JournalNumber, b.PaymentDate, CONVERT(nchar(7), b.PaymentDate, 126), N'Payables', N'VendorPayments', CONCAT(N'Vendor payments released on ', CONVERT(nchar(10), b.PaymentDate, 23)),
       NULL, N'Posted', N'SYS_FIN_IF', DATEADD(MINUTE, 19 * 60 + 30, CAST(b.PaymentDate AS datetime2(0))), NULL, NULL, DATEADD(MINUTE, 19 * 60 + 33, CAST(b.PaymentDate AS datetime2(0))),
       0, 0, N'AED'
FROM #PaymentBatches b;
WITH L AS (
    SELECT p.PaymentNumber, p.VendorCode, p.PaymentDate, 1 AS Kind, N'210100' AS GLAccountCode, p.Amount AS Debit, CAST(0 AS decimal(18,2)) AS Credit FROM dbo.Payments p
    UNION ALL
    SELECT p.PaymentNumber, p.VendorCode, p.PaymentDate, 2, N'110300', 0, p.Amount FROM dbo.Payments p
)
INSERT INTO dbo.JournalLines (JournalNumber, LineNumber, GLAccountCode, CostCentreCode, DebitAmount, CreditAmount, LineDescription, ReferenceNumber, VendorCode)
SELECT b.JournalNumber, ROW_NUMBER() OVER (PARTITION BY b.JournalNumber ORDER BY L.PaymentNumber, L.Kind), L.GLAccountCode, NULL, L.Debit, L.Credit,
       LEFT(CONCAT(L.PaymentNumber, N' ', v.VendorName), 200), L.PaymentNumber, L.VendorCode
FROM L JOIN #PaymentBatches b ON b.PaymentDate = L.PaymentDate JOIN dbo.Vendors v ON v.VendorCode = L.VendorCode;
DROP TABLE #PaymentBatches;
GO
-- Line units from the cost centre, totals from the lines; every journal must balance.
UPDATE jl SET DepartmentCode = cc.DepartmentCode, SectionCode = cc.SectionCode FROM dbo.JournalLines jl JOIN dbo.CostCentres cc ON cc.CostCentreCode = jl.CostCentreCode;
UPDATE jl SET LineDescription = je.Description FROM dbo.JournalLines jl JOIN dbo.JournalEntries je ON je.JournalNumber = jl.JournalNumber WHERE jl.LineDescription IS NULL;
UPDATE je SET TotalDebit = t.Debit, TotalCredit = t.Credit
FROM dbo.JournalEntries je JOIN (SELECT JournalNumber, SUM(DebitAmount) AS Debit, SUM(CreditAmount) AS Credit FROM dbo.JournalLines GROUP BY JournalNumber) t ON t.JournalNumber = je.JournalNumber;
IF EXISTS (SELECT 1 FROM dbo.JournalEntries WHERE TotalDebit <> TotalCredit OR TotalDebit = 0)
    THROW 50001, N'WabiBankFinanceDB: a journal entry does not balance.', 1;
CREATE INDEX IX_JournalLines_Account ON dbo.JournalLines (GLAccountCode, DepartmentCode) INCLUDE (CostCentreCode, DebitAmount, CreditAmount);
CREATE INDEX IX_JournalEntries_Period ON dbo.JournalEntries (PeriodCode, JournalDate);
CREATE INDEX IX_BudgetLines_Department ON dbo.BudgetLines (DepartmentCode, PeriodCode) INCLUDE (GLAccountCode, BudgetAmount);
CREATE INDEX IX_PurchaseRequisitions_Department ON dbo.PurchaseRequisitions (DepartmentCode, RequisitionDate);
CREATE INDEX IX_PurchaseOrders_Vendor ON dbo.PurchaseOrders (VendorCode, OrderDate);
CREATE INDEX IX_GoodsReceipts_PO ON dbo.GoodsReceipts (PONumber, ReceiptDate);
CREATE INDEX IX_VendorInvoices_PO ON dbo.VendorInvoices (PONumber);
CREATE INDEX IX_Payments_Invoice ON dbo.Payments (InvoiceDocumentNumber);
DROP TABLE #Payroll; DROP TABLE #AssetClasses;
GO
"""

BUDGET_LINES = """-- Budget lines: each version's quarterly amount per cost centre and account, spread evenly over the quarter's months (whole AED).
INSERT INTO dbo.BudgetLines (BudgetVersionCode, PeriodCode, CostCentreCode, DepartmentCode, SectionCode, GLAccountCode, BudgetAmount)
SELECT v.BudgetVersionCode, fp.PeriodCode, b.CostCentreCode, cc.DepartmentCode, cc.SectionCode, b.GLAccountCode, x.Amount
FROM #BudgetBase b
JOIN dbo.CostCentres cc ON cc.CostCentreCode = b.CostCentreCode
CROSS JOIN dbo.FiscalPeriods fp
CROSS JOIN (VALUES (N'FY2025-ORIGINAL'), (N'FY2025-REVISED')) v(BudgetVersionCode)
CROSS APPLY (SELECT CAST(ROUND(CASE fp.QuarterNumber WHEN 1 THEN b.OQ1 WHEN 2 THEN b.OQ2
                                   WHEN 3 THEN CASE WHEN v.BudgetVersionCode = N'FY2025-REVISED' THEN b.RQ3 ELSE b.OQ3 END
                                   ELSE CASE WHEN v.BudgetVersionCode = N'FY2025-REVISED' THEN b.RQ4 ELSE b.OQ4 END END / 3.0, 0) AS decimal(18,2)) AS Amount) x
WHERE x.Amount <> 0
ORDER BY v.BudgetVersionCode, fp.PeriodCode, b.CostCentreCode, b.GLAccountCode;
DROP TABLE #BudgetBase;
GO
"""

TABLES = ["Users", "ChartOfAccounts", "CostCentres", "FiscalPeriods", "Vendors", "BudgetVersions", "BudgetLines", "BudgetTransfers", "PurchaseRequisitions",
          "PurchaseRequisitionLines", "PurchaseOrders", "PurchaseOrderLines", "GoodsReceipts", "VendorInvoices", "Payments", "JournalEntries", "JournalLines", "FixedAssets"]
DROP_ORDER = ["Payments", "VendorInvoices", "GoodsReceipts", "FixedAssets", "JournalLines", "JournalEntries", "BudgetTransfers", "BudgetLines", "BudgetVersions",
              "PurchaseOrderLines", "PurchaseOrders", "PurchaseRequisitionLines", "PurchaseRequisitions", "Vendors", "FiscalPeriods", "CostCentres", "ChartOfAccounts", "Users"]


def emit(f):
    out = [prologue(DB, OWNER, OWNER_PASSWORD, READER, READER_PASSWORD, TITLE, DESCRIPTION).replace("Generated by build_sample_sql.py.", "Generated by wabi-bank/build_finance.py.")]
    out.append(drop_tables(DROP_ORDER))
    out.append(SCHEMA)
    users = bp.local_users(FIN)
    out.append(ins("dbo.Users", ["UserId", "EmployeeNumber", "FullName", "FullNameAr", "DepartmentCode", "SectionCode", "RoleCode", "ApprovalLimitAed", "Status",
                                 "AccessGrantedDate", "AccessRevokedDate"],
                   [(u["UserId"], u["EmployeeNumber"], u["FullName"], u["FullNameAr"], u["DepartmentCode"], u["SectionCode"], u["RoleCode"], u["ApprovalLimitAed"],
                     u["Status"], u["AccessGrantedDate"], u["AccessRevokedDate"]) for u in users]))
    out.append(ins("dbo.ChartOfAccounts", ["GLAccountCode", "AccountName", "AccountNameAr", "AccountType", "AccountCategory", "AccountCategoryAr", "NormalBalance",
                                           "IsBudgeted", "IsActive"],
                   [(c, n, ar, t, cat, CATEGORY_AR[cat], "Debit" if t in ("Asset", "Expense") else "Credit", t == "Expense", True) for c, n, ar, t, cat in COA]))
    ccs = []
    for d in bp.departments():
        h = head(d["DepartmentCode"], Y1)
        ccs.append((d["DepartmentCode"], d["DepartmentName"], d["DepartmentNameAr"], "Department", d["DepartmentCode"], None, d["DivisionCode"], d["LocationCode"],
                    d["DepartmentType"] == "Branch", h["EmployeeNumber"] if h else None, True))
    for s in bp.sections():
        d = DEPT[s["DepartmentCode"]]
        h = head(s["SectionCode"], Y1)
        ccs.append((s["SectionCode"], f"{d['DepartmentName']} - {s['SectionName']}", f"{d['DepartmentNameAr']} - {s['SectionNameAr']}", "Section", d["DepartmentCode"],
                    s["SectionCode"], d["DivisionCode"], d["LocationCode"], d["DepartmentType"] == "Branch", h["EmployeeNumber"] if h else None, True))
    out.append(ins("dbo.CostCentres", ["CostCentreCode", "CostCentreName", "CostCentreNameAr", "CostCentreType", "DepartmentCode", "SectionCode", "DivisionCode",
                                       "LocationCode", "IsBranch", "OwnerEmployeeNumber", "IsActive"], ccs))
    pcols = ["PeriodCode", "FiscalYear", "QuarterNumber", "MonthNumber", "PeriodName", "PeriodNameAr", "StartDate", "EndDate", "Status", "ClosedAt", "ClosedByUserId"]
    out.append(ins("dbo.FiscalPeriods", pcols, rows(f.periods, pcols)))
    vcols = ["VendorCode", "VendorName", "VendorCategory", "Rating", "TaxRegistrationNumber", "IBAN", "CountryCode", "City", "PaymentTermsDays", "ContactEmail",
             "RegisteredDate", "IsActive"]
    out.append(ins("dbo.Vendors", vcols, rows(f.vendors, vcols)))
    out.append(ins("dbo.BudgetVersions", ["BudgetVersionCode", "FiscalYear", "VersionName", "VersionNameAr", "VersionType", "EffectiveFromPeriod", "Status", "IsCurrent",
                                          "PreparedByUserId", "PreparedAt", "ApprovedByUserId", "ApprovedAt", "Notes"],
                   [(v["BudgetVersionCode"], 2025, v["VersionName"], v["VersionNameAr"], v["VersionType"], v["EffectiveFromPeriod"], "Approved", v["IsCurrent"],
                     v["PreparedByUserId"], v["PreparedAt"], v["ApprovedByUserId"], v["ApprovedAt"], v["Notes"]) for v in f.versions]))
    out.append("GO\nCREATE TABLE #BudgetBase (CostCentreCode nvarchar(14) NOT NULL, GLAccountCode nvarchar(10) NOT NULL, OQ1 decimal(18,2), OQ2 decimal(18,2), "
               "OQ3 decimal(18,2), OQ4 decimal(18,2), RQ3 decimal(18,2), RQ4 decimal(18,2), PRIMARY KEY (CostCentreCode, GLAccountCode));")
    out.append(ins("#BudgetBase", ["CostCentreCode", "GLAccountCode", "OQ1", "OQ2", "OQ3", "OQ4", "RQ3", "RQ4"],
                   [(cc, acct, *vals) for (cc, acct), vals in sorted(f.budget_base.items())]))
    out.append(BUDGET_LINES)
    tcols = ["TransferNumber", "BudgetVersionCode", "PeriodCode", "DepartmentCode", "FromCostCentreCode", "FromGLAccountCode", "ToCostCentreCode", "ToGLAccountCode",
             "Amount", "Reason", "Status", "RequestedByUserId", "RequestedAt", "ApprovedByUserId", "ApprovedAt", "RejectedByUserId", "RejectedAt", "RejectionReason"]
    out.append(ins("dbo.BudgetTransfers", tcols, rows(sorted(f.transfers, key=lambda t: t["TransferNumber"]), tcols)))
    out.append("GO")
    prs = sorted(f.prs, key=lambda p: p["RequisitionNumber"])
    prcols = ["RequisitionNumber", "RequisitionDate", "DepartmentCode", "SectionCode", "CostCentreCode", "RequestedByEmployeeNumber", "CreatedByUserId", "CreatedAt",
              "SpendCategory", "Description", "Priority", "NeedByDate", "EstimatedAmount", "Status", "SubmittedAt", "ApprovedByUserId", "ApprovedAt", "ApproverRoleCode",
              "RejectedByUserId", "RejectedAt", "RejectionReason"]
    out.append(ins("dbo.PurchaseRequisitions", prcols, rows(prs, prcols)))
    out.append(ins("dbo.PurchaseRequisitionLines", ["RequisitionNumber", "LineNumber", "ItemDescription", "UnitOfMeasure", "GLAccountCode", "Quantity", "EstimatedUnitPrice",
                                                    "EstimatedAmount"],
                   [(p["RequisitionNumber"], ln["LineNumber"], ln["ItemDescription"], ln["UnitOfMeasure"], ln["GLAccountCode"], ln["Quantity"], ln["EstimatedUnitPrice"],
                     ln["EstimatedAmount"]) for p in prs for ln in p["lines"]]))
    out.append("GO")
    pos = sorted(f.pos, key=lambda p: p["PONumber"])
    out.append(ins("dbo.PurchaseOrders", ["PONumber", "RequisitionNumber", "VendorCode", "DepartmentCode", "SectionCode", "CostCentreCode", "RequestedByEmployeeNumber",
                                          "SpendCategory", "Description", "CreatedByUserId", "CreatedAt", "ApprovedByUserId", "ApprovedAt", "ApproverRoleCode", "OrderDate",
                                          "ExpectedDeliveryDate", "PaymentTermsDays", "CurrencyCode", "SubTotal", "VatAmount", "TotalAmount", "Status", "CancellationReason"],
                   [(p["PONumber"], p["pr"]["RequisitionNumber"], p["VendorCode"], p["DepartmentCode"], p["SectionCode"], p["CostCentreCode"], p["RequestedByEmployeeNumber"],
                     p["SpendCategory"], p["Description"], p["CreatedByUserId"], p["CreatedAt"], p["ApprovedByUserId"], p["ApprovedAt"], p["ApproverRoleCode"], p["OrderDate"],
                     p["ExpectedDeliveryDate"], p["PaymentTermsDays"], "AED", p["SubTotal"], p["VatAmount"], p["TotalAmount"], p["Status"], p["CancellationReason"])
                    for p in pos]))
    out.append(ins("dbo.PurchaseOrderLines", ["PONumber", "LineNumber", "RequisitionLineNumber", "ItemDescription", "UnitOfMeasure", "GLAccountCode", "Quantity", "UnitPrice",
                                              "LineAmount", "QuantityReceived"],
                   [(p["PONumber"], ln["LineNumber"], ln["RequisitionLineNumber"], ln["ItemDescription"], ln["UnitOfMeasure"], ln["GLAccountCode"], ln["Quantity"],
                     ln["UnitPrice"], ln["LineAmount"], ln["QuantityReceived"]) for p in pos for ln in p["lines"]]))
    out.append("GO")
    out.append(ins("dbo.GoodsReceipts", ["ReceiptNumber", "PONumber", "VendorCode", "DepartmentCode", "SectionCode", "CostCentreCode", "ReceiptDate", "ReceivedAt",
                                         "ReceivedByUserId", "ReceiptType", "DeliverySequence", "IsFinalDelivery", "ReceivedAmount", "DeliveryNoteNumber"],
                   [(g["ReceiptNumber"], g["po"]["PONumber"], g["po"]["VendorCode"], g["po"]["DepartmentCode"], g["po"]["SectionCode"], g["po"]["CostCentreCode"],
                     g["ReceiptDate"], g["ReceivedAt"], g["ReceivedByUserId"], g["ReceiptType"], g["DeliverySequence"], g["IsFinalDelivery"], g["ReceivedAmount"],
                     g["DeliveryNoteNumber"]) for g in sorted(f.receipts, key=lambda x: x["ReceiptNumber"])]))
    invs = sorted(f.invoices, key=lambda x: x["InvoiceDocumentNumber"])
    out.append(ins("dbo.VendorInvoices", ["InvoiceDocumentNumber", "VendorCode", "VendorInvoiceNumber", "PONumber", "DepartmentCode", "SectionCode", "InvoiceDate", "EnteredAt",
                                          "EnteredByUserId", "DueDate", "NetAmount", "VatAmount", "TotalAmount", "MatchStatus", "VarianceAmount", "Status"],
                   [(i["InvoiceDocumentNumber"], i["VendorCode"], i["VendorInvoiceNumber"], i["po"]["PONumber"], i["po"]["DepartmentCode"], i["po"]["SectionCode"],
                     i["InvoiceDate"], i["EnteredAt"], i["EnteredByUserId"], i["DueDate"], i["NetAmount"], i["VatAmount"], i["TotalAmount"], i["MatchStatus"],
                     i["VarianceAmount"], i["Status"]) for i in invs]))
    out.append(ins("dbo.Payments", ["PaymentNumber", "InvoiceDocumentNumber", "VendorCode", "DepartmentCode", "SectionCode", "PaymentDate", "Amount", "CurrencyCode",
                                    "PaymentMethod", "BankReference", "CreatedByUserId", "CreatedAt", "ApprovedByUserId", "ApprovedAt", "Status"],
                   [(p["PaymentNumber"], p["invoice"]["InvoiceDocumentNumber"], p["VendorCode"], p["invoice"]["po"]["DepartmentCode"], p["invoice"]["po"]["SectionCode"],
                     p["PaymentDate"], p["Amount"], "AED", "BankTransfer", p["BankReference"], p["CreatedByUserId"], p["CreatedAt"], p["ApprovedByUserId"], p["ApprovedAt"],
                     "Released") for p in sorted(f.payments, key=lambda x: x["PaymentNumber"])]))
    out.append("GO")
    out.append(ins("dbo.FixedAssets", ["AssetNumber", "AssetDescription", "AssetClassCode", "AssetClass", "GLAccountCode", "CostCentreCode", "DepartmentCode", "SectionCode",
                                       "LocationCode", "CustodianEmployeeNumber", "SerialNumber", "VendorCode", "PONumber", "AcquisitionDate", "AcquisitionCost",
                                       "UsefulLifeMonths", "DepreciationStartDate", "MonthlyDepreciation", "AccumulatedDepreciation", "NetBookValue", "Status"],
                   [(a["AssetNumber"], a["AssetDescription"], a["AssetClassCode"], ASSET_CLASSES[a["AssetClassCode"]][0], ASSET_CLASSES[a["AssetClassCode"]][2],
                     a["CostCentreCode"], a["DepartmentCode"], a["SectionCode"], a["LocationCode"], a["CustodianEmployeeNumber"], a["SerialNumber"], a["VendorCode"],
                     a["po"]["PONumber"] if a["po"] else None, a["AcquisitionDate"], a["AcquisitionCost"], a["UsefulLifeMonths"], a["DepreciationStartDate"],
                     a["MonthlyDepreciation"], a["AccumulatedDepreciation"], a["NetBookValue"], a["Status"]) for a in sorted(f.assets, key=lambda x: x["AssetNumber"])]))
    out.append("GO")
    js = sorted(f.journals, key=lambda j: j["JournalNumber"])
    out.append(ins("dbo.JournalEntries", ["JournalNumber", "JournalDate", "PeriodCode", "Source", "JournalType", "Description", "ReferenceNumber", "Status", "CreatedByUserId",
                                          "CreatedAt", "ApprovedByUserId", "ApprovedAt", "PostedAt", "TotalDebit", "TotalCredit", "CurrencyCode"],
                   [(j["JournalNumber"], j["JournalDate"], period(j["JournalDate"]), j["Source"], j["JournalType"], j["Description"], j["ReferenceNumber"], "Posted",
                     j["CreatedByUserId"], j["CreatedAt"], j["ApprovedByUserId"], j["ApprovedAt"], j["PostedAt"], 0, 0, "AED") for j in js]))
    out.append(ins("dbo.JournalLines", ["JournalNumber", "LineNumber", "GLAccountCode", "CostCentreCode", "DebitAmount", "CreditAmount"],
                   [(j["JournalNumber"], n, acct, cc, d, c) for j in js for n, (acct, cc, d, c) in enumerate(j["lines"], 1)]))
    out.append("GO\nCREATE TABLE #Payroll (MonthNumber tinyint NOT NULL, RunDate date NOT NULL, CostCentreCode nvarchar(14) NOT NULL, Salaries decimal(18,2) NOT NULL, "
               "Pension decimal(18,2) NOT NULL, EndOfService decimal(18,2) NOT NULL, Medical decimal(18,2) NOT NULL);")
    out.append(ins("#Payroll", ["MonthNumber", "RunDate", "CostCentreCode", "Salaries", "Pension", "EndOfService", "Medical"], f.payroll))
    out.append("CREATE TABLE #AssetClasses (AssetClassCode nvarchar(4) NOT NULL PRIMARY KEY, CostAccount nvarchar(10), AccumulatedAccount nvarchar(10), ExpenseAccount nvarchar(10));")
    out.append(ins("#AssetClasses", ["AssetClassCode", "CostAccount", "AccumulatedAccount", "ExpenseAccount"], [(k, v[2], v[3], v[4]) for k, v in ASSET_CLASSES.items()]))
    out.append("GO")
    out.append(DERIVED_JOURNALS)
    summary = "\nUNION ALL ".join(f"SELECT N'{t}'{' AS TableName' if i == 0 else ''}, COUNT(*){' AS RowsLoaded' if i == 0 else ''} FROM dbo.{t}" for i, t in enumerate(TABLES)) + ";"
    out.append(epilogue(DB, summary))
    return "\n".join(out)


def build():
    f = Finance().run()
    sql = emit(f)
    stats = {
        "vendors": len(f.vendors), "requisitions": len(f.prs), "requisition_lines": sum(len(p["lines"]) for p in f.prs), "purchase_orders": len(f.pos),
        "order_lines": sum(len(p["lines"]) for p in f.pos), "receipts": len(f.receipts), "invoices": len(f.invoices), "payments": len(f.payments),
        "fixed_assets": len(f.assets), "manual_and_interface_journals": len(f.journals), "budget_pairs": len(f.budget_base), "budget_transfers": len(f.transfers),
        "actor_checks_passed": f.actor_checks, "itd_q3_actual_vs_revised": float(round(f.itd["ratio"], 3)), "split_orders": [float(p["SubTotal"]) for p in f.split_pos], "paid_before_receipt": len(f.prepaid),
    }
    return sql, stats


# ------------------------------------------------------------------------------------------------ catalogue documentation (used by the Wabi Insight seed)
OBJECT_DOCS = [  # object, display EN, display AR, description
    ("Users", "Finance users", "مستخدمو النظام المالي",
     "Everyone who held a role in the finance system during 2025 (copy of Personnel access): user id, employee number, unit, FIN role, approval limit before VAT, status and access dates. SYS_FIN_IF is the interface service account."),
    ("ChartOfAccounts", "Chart of accounts", "دليل الحسابات",
     "General-ledger accounts with type (Asset, Liability, Equity, Income, Expense), category (Staff costs, Premises, IT, Marketing, Professional fees, Depreciation ...) and whether the account is budgeted (operating expenses)."),
    ("CostCentres", "Cost centres", "مراكز التكلفة",
     "One cost centre per department or branch (CostCentreType Department) and per section (Section); the code equals the department or section code, with division, location and the unit head on 31 December 2025."),
    ("FiscalPeriods", "Fiscal periods", "الفترات المالية", "The twelve 2025 accounting months; January to November closed by the Financial Controller, December still open."),
    ("BudgetVersions", "Budget versions", "نسخ الموازنة",
     "FY2025-ORIGINAL (approved December 2024) and FY2025-REVISED (Q2 re-forecast approved June 2025, current from July; its January-June lines equal the original)."),
    ("BudgetLines", "Budget lines", "بنود الموازنة",
     "Monthly operating-expense budget per version, cost centre and GL account (AED). Staff costs and premises are budgeted on department cost centres, purchases and depreciation where they are incurred. Compare with JournalLines on Expense accounts."),
    ("BudgetTransfers", "Budget transfers", "مناقلات الموازنة",
     "Requests by department heads to move budget between cost centres or accounts of their department, approved or rejected by the Budget Manager; recorded separately, not included in BudgetLines."),
    ("Vendors", "Vendors", "الموردون", "Suppliers of goods and services: category, rating (A preferred, B acceptable, C under review), UAE tax registration number, bank account (IBAN), payment terms and status."),
    ("PurchaseRequisitions", "Purchase requisitions", "طلبات الشراء",
     "Purchase requests of 2025 by department and section: requester, creator, spend category, estimated amount before VAT, status (Draft, Submitted, Approved, Rejected, Converted) and the approver (department head up to AED 50,000, CFO up to 2,000,000, CEO above)."),
    ("PurchaseRequisitionLines", "Purchase requisition lines", "بنود طلبات الشراء", "Items or services requested, quantity, estimated unit price and amount, and the GL account to be charged."),
    ("PurchaseOrders", "Purchase orders", "أوامر الشراء",
     "Orders issued by Procurement buyers from approved requisitions: vendor, unit, buyer, approver (Procurement Manager up to AED 100,000 before VAT, CFO up to 2,000,000, CEO above), order and expected delivery dates, subtotal, 5 % VAT, total and status."),
    ("PurchaseOrderLines", "Purchase order lines", "بنود أوامر الشراء", "Ordered items with quantity, unit price, amount before VAT, GL account and quantity received."),
    ("GoodsReceipts", "Goods receipts", "استلام البضائع والخدمات",
     "Deliveries and service acceptances recorded by the ordering unit (or a Head Office facilities coordinator): receipt date, receiver, delivery sequence and value received before VAT."),
    ("VendorInvoices", "Vendor invoices", "فواتير الموردين",
     "Vendor invoices entered by Accounts Payable: invoice and due dates, net, VAT and total, three-way match result (Matched, PriceVariance, QuantityVariance, NoReceipt) and status (Open, OnHold, Paid)."),
    ("Payments", "Vendor payments", "مدفوعات الموردين", "Bank transfers to vendors: prepared by a payables officer (maker) and released by the payables supervisor (checker) on the payment date."),
    ("JournalEntries", "Journal entries", "القيود اليومية",
     "General-ledger journals of 2025: manual accruals and reclassifications (accountant and Financial Controller) and interface batches posted by SYS_FIN_IF (payroll, depreciation, premises and ATM costs, core banking income, vendor invoices and payments)."),
    ("JournalLines", "Journal lines", "بنود القيود",
     "Debit and credit lines per GL account and cost centre (department and section). Actual cost = DebitAmount - CreditAmount on Expense accounts; balance-sheet lines have no cost centre."),
    ("FixedAssets", "Fixed assets", "الأصول الثابتة",
     "Asset register: IT equipment, furniture, vehicles, ATMs and branch equipment, leasehold improvements, with cost centre, custodian, acquisition date and cost, useful life, accumulated depreciation and net book value at 31 December 2025."),
]

COLUMN_DOCS = [  # object, column, display EN, display AR, description, classification
    ("Users", "UserId", "User id", "معرف المستخدم", "Single login across Wabi Bank systems (same as Personnel).", None),
    ("Users", "RoleCode", "Finance role", "الدور المالي", "FIN role: Accountant, FinancialController, BudgetAnalyst, BudgetManager, Buyer, ProcurementManager, PayablesOfficer, PayablesSupervisor, BudgetHolder, Requisitioner, ChiefFinancialOfficer, ExecutiveApprover, ServiceAccount.", None),
    ("Users", "ApprovalLimitAed", "Approval limit (AED)", "حد الاعتماد (درهم)", "Highest amount before VAT the user may approve; NULL means no limit for the CEO (ExecutiveApprover) and no approval limit for other roles.", "Internal"),
    ("ChartOfAccounts", "AccountType", "Account type", "نوع الحساب", "Asset, Liability, Equity, Income or Expense.", None),
    ("ChartOfAccounts", "AccountCategory", "Account category", "فئة الحساب", "Grouping for reporting: Staff costs, Premises, IT, Marketing, Professional fees, Cash and ATM operations, Office and general, Depreciation ...", None),
    ("ChartOfAccounts", "IsBudgeted", "Budgeted", "ضمن الموازنة", "1 for operating-expense accounts that appear in BudgetLines.", None),
    ("CostCentres", "CostCentreType", "Cost centre type", "نوع مركز التكلفة", "Department (a Head Office department or a branch) or Section.", None),
    ("CostCentres", "IsBranch", "Branch", "فرع", "1 for branch cost centres (the branch itself and its sections).", None),
    ("CostCentres", "OwnerEmployeeNumber", "Owner", "المسؤول", "Employee number of the unit head on 31 December 2025.", None),
    ("FiscalPeriods", "PeriodCode", "Period", "الفترة", "Accounting month, yyyy-mm.", None),
    ("FiscalPeriods", "Status", "Period status", "حالة الفترة", "Closed (no more postings) or Open.", None),
    ("BudgetVersions", "IsCurrent", "Current version", "النسخة الحالية", "1 for FY2025-REVISED, the version to compare actuals with.", None),
    ("BudgetLines", "BudgetVersionCode", "Budget version", "نسخة الموازنة", "FY2025-ORIGINAL or FY2025-REVISED; filter on one version.", None),
    ("BudgetLines", "PeriodCode", "Period", "الفترة", "Budget month, yyyy-mm.", None),
    ("BudgetLines", "BudgetAmount", "Budget (AED)", "الموازنة (درهم)", "Budgeted operating expense for the month, AED.", "Internal"),
    ("BudgetTransfers", "Amount", "Amount (AED)", "المبلغ (درهم)", "Budget moved from the From to the To cost centre and account for the period.", "Internal"),
    ("BudgetTransfers", "Status", "Status", "الحالة", "Pending, Approved or Rejected.", None),
    ("Vendors", "Rating", "Vendor rating", "تصنيف المورد", "A = preferred, B = acceptable, C = under review.", None),
    ("Vendors", "TaxRegistrationNumber", "Tax registration number", "رقم التسجيل الضريبي", "UAE VAT registration number (TRN), 15 digits.", "Internal"),
    ("Vendors", "IBAN", "Vendor IBAN", "رقم الحساب المصرفي الدولي للمورد", "Bank account paid by Wabi Bank.", "Restricted"),
    ("Vendors", "ContactEmail", "Contact email", "البريد الإلكتروني", None, "Internal"),
    ("PurchaseRequisitions", "CreatedAt", "Created at", "وقت الإنشاء", "Local time (Asia/Dubai); every timestamp in this database is local time.", None),
    ("PurchaseRequisitions", "RequestedByEmployeeNumber", "Requested by", "مقدم الطلب", "Employee number of the person who needed the purchase.", None),
    ("PurchaseRequisitions", "CreatedByUserId", "Created by", "أنشأه", "User who entered the requisition: a Requisitioner of the section (section head, executive assistant, facilities coordinator) or a branch manager.", None),
    ("PurchaseRequisitions", "EstimatedAmount", "Estimated amount (AED)", "المبلغ التقديري (درهم)", "Before VAT; the approval limit applies to it.", None),
    ("PurchaseRequisitions", "Status", "Status", "الحالة", "Draft, Submitted (awaiting approval), Approved, Rejected or Converted (a purchase order was created).", None),
    ("PurchaseRequisitions", "ApproverRoleCode", "Approved as", "صفة المعتمد", "BudgetHolder (department head), ExecutiveApprover (CEO) or ChiefFinancialOfficer.", None),
    ("PurchaseOrders", "CreatedByUserId", "Buyer", "مسؤول المشتريات", "Procurement buyer who created the order.", None),
    ("PurchaseOrders", "ApprovedByUserId", "Approved by", "اعتمده", "Procurement Manager (up to AED 100,000 before VAT), CFO (up to 2,000,000) or CEO.", None),
    ("PurchaseOrders", "OrderDate", "Order date", "تاريخ الأمر", "Date the order was approved and issued to the vendor.", None),
    ("PurchaseOrders", "SubTotal", "Subtotal (AED)", "المجموع قبل الضريبة (درهم)", "Order value before VAT; approval limits apply to it.", None),
    ("PurchaseOrders", "TotalAmount", "Total (AED)", "الإجمالي (درهم)", "Subtotal plus 5 % VAT.", None),
    ("PurchaseOrders", "Status", "Status", "الحالة", "PendingApproval, Open (nothing received), PartiallyReceived, Received, Closed (received and invoice paid) or Cancelled.", None),
    ("GoodsReceipts", "ReceivedByUserId", "Received by", "استلمه", "Requisitioner of the ordering unit or a Head Office facilities coordinator.", None),
    ("GoodsReceipts", "ReceiptType", "Receipt type", "نوع الاستلام", "Goods or Service (acceptance of a completed service).", None),
    ("GoodsReceipts", "DeliverySequence", "Delivery number", "رقم التسليم", "1 = first or only delivery, 2 = balance of a partial delivery.", None),
    ("VendorInvoices", "MatchStatus", "Three-way match", "المطابقة الثلاثية", "Matched; PriceVariance (invoice above the order price); QuantityVariance (invoiced in full, partly received); NoReceipt (no goods receipt when entered).", None),
    ("VendorInvoices", "VarianceAmount", "Price variance (AED)", "فرق السعر (درهم)", "Invoice net amount minus the order subtotal.", None),
    ("VendorInvoices", "Status", "Status", "الحالة", "Open (approved, not yet paid), OnHold (variance or receipt pending) or Paid.", None),
    ("Payments", "CreatedByUserId", "Prepared by (maker)", "أعده", "Payables officer who prepared the payment.", None),
    ("Payments", "ApprovedByUserId", "Released by (checker)", "اعتمده", "Payables supervisor who released the payment; never the maker.", None),
    ("Payments", "BankReference", "Bank reference", "المرجع المصرفي", None, "Internal"),
    ("JournalEntries", "Source", "Source", "المصدر", "Manual, Payroll, FixedAssets, Premises, CoreBanking or Payables.", None),
    ("JournalEntries", "JournalType", "Journal type", "نوع القيد", "Accrual, Reclassification, Payroll, Depreciation, PremisesCosts, IncomeSummary, VendorInvoices or VendorPayments.", None),
    ("JournalEntries", "PeriodCode", "Period", "الفترة", "Accounting month the journal is posted to.", None),
    ("JournalEntries", "PostedAt", "Posted at", "وقت الترحيل", "Local time (Asia/Dubai) the journal reached the ledger; interface batches post in the evening.", None),
    ("JournalEntries", "ApprovedByUserId", "Approved by", "اعتمده", "Financial Controller for manual journals; NULL for interface journals posted by SYS_FIN_IF.", None),
    ("JournalEntries", "TotalDebit", "Total debit (AED)", "إجمالي المدين (درهم)", None, "Confidential"),
    ("JournalEntries", "TotalCredit", "Total credit (AED)", "إجمالي الدائن (درهم)", None, "Confidential"),
    ("JournalLines", "DebitAmount", "Debit (AED)", "مدين (درهم)", "Expense actuals are DebitAmount - CreditAmount on Expense accounts.", "Confidential"),
    ("JournalLines", "CreditAmount", "Credit (AED)", "دائن (درهم)", None, "Confidential"),
    ("JournalLines", "CostCentreCode", "Cost centre", "مركز التكلفة", "Department or section charged; NULL on balance-sheet lines.", None),
    ("JournalLines", "ReferenceNumber", "Reference", "المرجع", "Vendor invoice or payment number on payables lines.", None),
    ("FixedAssets", "AssetClassCode", "Asset class", "فئة الأصل", "IT (IT equipment), FF (furniture and fittings), MV (motor vehicles), ATM (ATMs and branch equipment), LHI (leasehold improvements).", None),
    ("FixedAssets", "CustodianEmployeeNumber", "Custodian", "عهدة الموظف", "Employee responsible for the asset (an employee of the unit when it was acquired).", None),
    ("FixedAssets", "SerialNumber", "Serial number", "الرقم التسلسلي", "Manufacturer serial, ATM terminal id or vehicle plate.", None),
    ("FixedAssets", "AccumulatedDepreciation", "Accumulated depreciation (AED)", "مجمع الإهلاك (درهم)", "As of 31 December 2025.", None),
    ("FixedAssets", "NetBookValue", "Net book value (AED)", "صافي القيمة الدفترية (درهم)", "Cost minus accumulated depreciation, 31 December 2025.", None),
    ("FixedAssets", "Status", "Status", "الحالة", "InUse, FullyDepreciated (still in use) or Returned (a leaver's device back in the IT store).", None),
]

FIELD_ROLES = [  # object, column, role: 1 Identifier, 2 Person, 3 Account, 4 Device, 5 Date/time, 6 Amount, 7 Quantity, 8 Status, 9 Location
    ("Users", "UserId", 3), ("Users", "EmployeeNumber", 2), ("Users", "Status", 8),
    ("Vendors", "VendorCode", 1), ("Vendors", "IBAN", 3), ("Vendors", "City", 9),
    ("CostCentres", "LocationCode", 9),
    ("BudgetLines", "BudgetAmount", 6),
    ("BudgetTransfers", "RequestedByUserId", 3), ("BudgetTransfers", "RequestedAt", 5), ("BudgetTransfers", "Amount", 6), ("BudgetTransfers", "Status", 8),
    ("PurchaseRequisitions", "RequisitionNumber", 1), ("PurchaseRequisitions", "RequestedByEmployeeNumber", 2), ("PurchaseRequisitions", "CreatedByUserId", 3),
    ("PurchaseRequisitions", "CreatedAt", 5), ("PurchaseRequisitions", "EstimatedAmount", 6), ("PurchaseRequisitions", "Status", 8),
    ("PurchaseRequisitionLines", "Quantity", 7), ("PurchaseRequisitionLines", "EstimatedAmount", 6),
    ("PurchaseOrders", "PONumber", 1), ("PurchaseOrders", "RequestedByEmployeeNumber", 2), ("PurchaseOrders", "CreatedByUserId", 3), ("PurchaseOrders", "ApprovedByUserId", 3),
    ("PurchaseOrders", "OrderDate", 5), ("PurchaseOrders", "SubTotal", 6), ("PurchaseOrders", "TotalAmount", 6), ("PurchaseOrders", "Status", 8),
    ("PurchaseOrderLines", "Quantity", 7), ("PurchaseOrderLines", "LineAmount", 6),
    ("GoodsReceipts", "ReceivedByUserId", 3), ("GoodsReceipts", "ReceivedAt", 5), ("GoodsReceipts", "ReceivedAmount", 6),
    ("VendorInvoices", "InvoiceDocumentNumber", 1), ("VendorInvoices", "EnteredByUserId", 3), ("VendorInvoices", "InvoiceDate", 5), ("VendorInvoices", "TotalAmount", 6),
    ("VendorInvoices", "MatchStatus", 8), ("VendorInvoices", "Status", 8),
    ("Payments", "PaymentNumber", 1), ("Payments", "CreatedByUserId", 3), ("Payments", "ApprovedByUserId", 3), ("Payments", "PaymentDate", 5), ("Payments", "Amount", 6),
    ("Payments", "Status", 8),
    ("JournalEntries", "JournalNumber", 1), ("JournalEntries", "CreatedByUserId", 3), ("JournalEntries", "PostedAt", 5), ("JournalEntries", "TotalDebit", 6),
    ("JournalEntries", "Status", 8),
    ("JournalLines", "DebitAmount", 6), ("JournalLines", "CreditAmount", 6),
    ("FixedAssets", "AssetNumber", 1), ("FixedAssets", "CustodianEmployeeNumber", 2), ("FixedAssets", "SerialNumber", 4), ("FixedAssets", "AcquisitionDate", 5),
    ("FixedAssets", "AcquisitionCost", 6), ("FixedAssets", "NetBookValue", 6), ("FixedAssets", "Status", 8), ("FixedAssets", "LocationCode", 9),
]

ROW_SCOPES = [  # object, employee-number column, department-code column, section-code column
    ("Users", "EmployeeNumber", "DepartmentCode", "SectionCode"),
    ("CostCentres", None, "DepartmentCode", "SectionCode"),
    ("BudgetLines", None, "DepartmentCode", "SectionCode"),
    ("BudgetTransfers", None, "DepartmentCode", None),
    ("PurchaseRequisitions", "RequestedByEmployeeNumber", "DepartmentCode", "SectionCode"),
    ("PurchaseOrders", "RequestedByEmployeeNumber", "DepartmentCode", "SectionCode"),
    ("GoodsReceipts", None, "DepartmentCode", "SectionCode"),
    ("VendorInvoices", None, "DepartmentCode", "SectionCode"),
    ("Payments", None, "DepartmentCode", "SectionCode"),
    ("JournalLines", None, "DepartmentCode", "SectionCode"),
    ("FixedAssets", "CustodianEmployeeNumber", "DepartmentCode", "SectionCode"),
]

FINDINGS = {
    "itd_q3_overrun": """WITH b AS (SELECT DepartmentCode, SUM(BudgetAmount) AS Budget FROM dbo.BudgetLines
           WHERE BudgetVersionCode = N'FY2025-REVISED' AND PeriodCode BETWEEN N'2025-07' AND N'2025-09' GROUP BY DepartmentCode),
     a AS (SELECT jl.DepartmentCode, SUM(jl.DebitAmount - jl.CreditAmount) AS Actual
           FROM dbo.JournalLines jl JOIN dbo.JournalEntries je ON je.JournalNumber = jl.JournalNumber
           JOIN dbo.ChartOfAccounts ga ON ga.GLAccountCode = jl.GLAccountCode
           WHERE ga.AccountType = N'Expense' AND je.PeriodCode BETWEEN N'2025-07' AND N'2025-09' GROUP BY jl.DepartmentCode)
SELECT b.DepartmentCode, b.Budget, a.Actual, CAST(100.0 * (a.Actual - b.Budget) / b.Budget AS decimal(6,1)) AS OverBudgetPct
FROM b JOIN a ON a.DepartmentCode = b.DepartmentCode ORDER BY OverBudgetPct DESC;""",
    "split_orders": """SELECT CreatedByUserId AS Buyer, VendorCode, CostCentreCode, COUNT(*) AS Orders, SUM(SubTotal) AS CombinedSubTotal,
       MIN(OrderDate) AS FirstOrder, MAX(OrderDate) AS LastOrder, MIN(ApprovedByUserId) AS ApprovedBy
FROM dbo.PurchaseOrders
WHERE SubTotal >= 90000 AND SubTotal < 100000
GROUP BY CreatedByUserId, VendorCode, CostCentreCode
HAVING COUNT(*) >= 3 AND DATEDIFF(DAY, MIN(OrderDate), MAX(OrderDate)) <= 7;""",
    "paid_before_receipt": """SELECT vi.InvoiceDocumentNumber, vi.PONumber, vi.VendorCode, vi.MatchStatus, p.PaymentNumber, p.PaymentDate, gr.FirstReceiptDate, p.Amount
FROM dbo.Payments p
JOIN dbo.VendorInvoices vi ON vi.InvoiceDocumentNumber = p.InvoiceDocumentNumber
OUTER APPLY (SELECT MIN(ReceiptDate) AS FirstReceiptDate FROM dbo.GoodsReceipts g WHERE g.PONumber = vi.PONumber) gr
WHERE gr.FirstReceiptDate IS NULL OR p.PaymentDate < gr.FirstReceiptDate;""",
}


if __name__ == "__main__":
    text, stats = build()
    path = os.path.join(HERE, OUTPUT)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(f"{OUTPUT}: {len(text.encode('utf-8')) // 1024} KB")
    for k, v in stats.items():
        print(f"  {k}: {v}")
