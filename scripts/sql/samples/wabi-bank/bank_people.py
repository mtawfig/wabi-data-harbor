"""Wabi Bank (WBK): the shared organization and identity model used by every Wabi Bank sample database.

Personnel is the system of record. Every other system reuses these identifiers unchanged:
  DepartmentCode  a Head Office department (FIN) or a banking branch (BR-DXB01); also the finance cost centre
  SectionCode     a sub-unit of a department or branch (FIN-PRC, BR-DXB01-TLR); IT sections are help-desk groups
  EmployeeNumber  the person (W1001)
  UserId          the person's single login across all systems (CASHBY); service accounts have no employee

Everything is deterministic (seeded random.Random, sorted iteration) so all five scripts agree on every value.
"""
import datetime as dt
import os
import sys
from functools import lru_cache

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from build_common import UAE_HOLIDAYS_2025, rng  # noqa: E402
import bank_names  # noqa: E402

ORG_CODE = "WBK"
ORG_NAME = "Wabi Bank"
ORG_NAME_AR = "بنك وابي"
DOMAIN = "wabibank.example"
Y0, Y1 = dt.date(2025, 1, 1), dt.date(2025, 12, 31)
HOLIDAYS = {dt.date.fromisoformat(d): (en, ar) for d, en, ar in UAE_HOLIDAYS_2025}
DAYS_2025 = [Y0 + dt.timedelta(days=i) for i in range(365)]

# ------------------------------------------------------------------------------------------------ structure
# Board -> CEO and executive management -> divisions -> departments and branches -> sections.
DIVISIONS = [  # code, name, Arabic, head position, reports to
    ("DIV-CEO", "CEO Office", "مكتب الرئيس التنفيذي", "CEO", "Board"),
    ("DIV-RTL", "Retail Banking", "الخدمات المصرفية للأفراد", "CRBO", "CEO"),
    ("DIV-WHL", "Wholesale Banking", "الخدمات المصرفية بالجملة", "CWBO", "CEO"),
    ("DIV-OPT", "Operations and Technology", "العمليات والتقنية", "COO", "CEO"),
    ("DIV-RSK", "Risk", "المخاطر", "CRO", "CEO"),
    ("DIV-FIN", "Finance", "المالية", "CFO", "CEO"),
    ("DIV-HR", "Human Resources", "الموارد البشرية", "CHRO", "CEO"),
    ("DIV-MKT", "Marketing", "التسويق", "CMO", "CEO"),
]

LOCATIONS = [  # code, name, Arabic, type, emirate, address, opened, counters
    ("HO", "Head Office", "المقر الرئيسي", "HeadOffice", "Dubai", "Wabi Bank Tower, Sheikh Zayed Road, Dubai", "2008-01-06", 0),
    ("BR-DXB01", "Deira (Dubai Main)", "ديرة (فرع دبي الرئيسي)", "Branch", "Dubai", "Al Maktoum Road, Deira, Dubai", "2008-03-02", 8),
    ("BR-AUH01", "Abu Dhabi Main", "أبوظبي الرئيسي", "Branch", "Abu Dhabi", "Sheikh Zayed the First Street, Abu Dhabi", "2010-05-09", 7),
    ("BR-SHJ01", "Sharjah", "الشارقة", "Branch", "Sharjah", "Al Wahda Street, Sharjah", "2012-09-16", 6),
    ("BR-AAN01", "Al Ain", "العين", "Branch", "Abu Dhabi", "Khalifa Street, Al Ain", "2015-02-01", 5),
    ("BR-DXB02", "Business Bay", "الخليج التجاري", "Branch", "Dubai", "Bay Square, Business Bay, Dubai", "2018-04-08", 6),
    ("BR-RAK01", "Ras Al Khaimah", "رأس الخيمة", "Branch", "Ras Al Khaimah", "Al Muntasir Road, Ras Al Khaimah", "2021-10-03", 4),
]
BRANCH_CODES = [loc[0] for loc in LOCATIONS if loc[3] == "Branch"]

# Head Office departments: code, name, Arabic, division, head position code, head title, Arabic title, head grade.
# Chiefs of single-department divisions (and of RBD, CBD, OPS) head that department themselves.
HQ_DEPARTMENTS = [
    ("CEO", "CEO Office", "مكتب الرئيس التنفيذي", "DIV-CEO", "CEO", "Chief Executive Officer", "الرئيس التنفيذي", "G10"),
    ("LGL", "Legal", "الشؤون القانونية", "DIV-CEO", "GC", "General Counsel", "المستشار القانوني العام", "G9"),
    ("CMP", "Compliance", "الالتزام", "DIV-CEO", "CCO", "Chief Compliance Officer", "رئيس الالتزام", "G9"),
    ("IAD", "Internal Audit", "التدقيق الداخلي", "DIV-CEO", "CIA", "Chief Internal Auditor", "رئيس التدقيق الداخلي", "G9"),
    ("RBD", "Retail Banking", "الخدمات المصرفية للأفراد", "DIV-RTL", "CRBO", "Chief Retail Banking Officer", "رئيس الخدمات المصرفية للأفراد", "G10"),
    ("DCC", "Digital Banking and Contact Centre", "الخدمات المصرفية الرقمية ومركز الاتصال", "DIV-RTL", "HDCC", "Head of Digital Banking and Contact Centre", "رئيس الخدمات الرقمية ومركز الاتصال", "G8"),
    ("CBD", "Corporate and SME Banking", "الخدمات المصرفية للشركات والمؤسسات الصغيرة والمتوسطة", "DIV-WHL", "CWBO", "Chief Wholesale Banking Officer", "رئيس الخدمات المصرفية بالجملة", "G10"),
    ("TRY", "Treasury", "الخزينة", "DIV-WHL", "HTRY", "Treasurer", "أمين الخزينة", "G8"),
    ("OPS", "Central Operations", "العمليات المركزية", "DIV-OPT", "COO", "Chief Operating Officer", "الرئيس التنفيذي للعمليات", "G10"),
    ("RSK", "Risk Management", "إدارة المخاطر", "DIV-RSK", "CRO", "Chief Risk Officer", "رئيس المخاطر", "G10"),
    ("FIN", "Finance", "المالية", "DIV-FIN", "CFO", "Chief Financial Officer", "الرئيس المالي", "G10"),
    ("HRD", "Human Resources", "الموارد البشرية", "DIV-HR", "CHRO", "Chief Human Resources Officer", "رئيس الموارد البشرية", "G10"),
    ("ITD", "Information Technology", "تقنية المعلومات", "DIV-OPT", "CIO", "Chief Information Officer", "رئيس تقنية المعلومات", "G9"),
    ("MKT", "Marketing and Communications", "التسويق والاتصال", "DIV-MKT", "CMO", "Chief Marketing Officer", "رئيس التسويق", "G10"),
    ("ADM", "Administration and Facilities", "الشؤون الإدارية والمرافق", "DIV-OPT", "HADM", "Head of Administration", "رئيس الشؤون الإدارية", "G8"),
]
HQ_DEPT_CODES = [d[0] for d in HQ_DEPARTMENTS]

# Head Office sections: code, department, name, Arabic, size (incl. head), (head title, Arabic, grade),
# staff positions [(title, Arabic, grade, weight)], user accounts? (False only for guards, drivers and messengers).
HQ_SECTIONS = [
    ("CEO-EXO", "CEO", "Executive Office", "المكتب التنفيذي", 4, ("Executive Office Manager", "مدير المكتب التنفيذي", "G6"),
     [("Executive Assistant", "مساعد تنفيذي", "G3", 3)]),
    ("CEO-STR", "CEO", "Strategy and PMO", "الاستراتيجية وإدارة المشاريع", 4, ("Head of Strategy and PMO", "رئيس الاستراتيجية وإدارة المشاريع", "G7"),
     [("Strategy Analyst", "محلل استراتيجي", "G4", 2), ("Project Manager", "مدير مشروع", "G5", 2)]),
    ("CEO-BSC", "CEO", "Board Secretariat", "أمانة سر مجلس الإدارة", 3, ("Board Secretary", "أمين سر مجلس الإدارة", "G6"),
     [("Board Affairs Officer", "مسؤول شؤون المجلس", "G3", 1)]),
    ("LGL-ADV", "LGL", "Legal Advisory", "الاستشارات القانونية", 4, ("Legal Advisory Manager", "مدير الاستشارات القانونية", "G7"),
     [("Legal Counsel", "مستشار قانوني", "G5", 2), ("Legal Officer", "مسؤول قانوني", "G3", 1)]),
    ("LGL-LIT", "LGL", "Litigation", "القضايا والتقاضي", 4, ("Litigation Manager", "مدير القضايا", "G6"),
     [("Litigation Officer", "مسؤول قضايا", "G4", 2), ("Collections Legal Officer", "مسؤول تحصيل قانوني", "G3", 1)]),
    ("CMP-AML", "CMP", "AML and Sanctions", "مكافحة غسل الأموال والعقوبات", 8, ("AML and Sanctions Manager", "مدير مكافحة غسل الأموال والعقوبات", "G7"),
     [("Senior AML Officer", "مسؤول أول مكافحة غسل الأموال", "G5", 2), ("AML Analyst", "محلل مكافحة غسل الأموال", "G4", 3), ("Sanctions Screening Officer", "مسؤول فحص العقوبات", "G3", 2)]),
    ("CMP-KYC", "CMP", "KYC Review", "مراجعة اعرف عميلك", 7, ("KYC Review Manager", "مدير مراجعة اعرف عميلك", "G6"),
     [("KYC Analyst", "محلل اعرف عميلك", "G3", 3), ("Senior KYC Analyst", "محلل أول اعرف عميلك", "G4", 1)]),
    ("CMP-REG", "CMP", "Regulatory Compliance", "الالتزام الرقابي", 4, ("Regulatory Compliance Manager", "مدير الالتزام الرقابي", "G7"),
     [("Regulatory Reporting Officer", "مسؤول التقارير الرقابية", "G4", 2), ("Compliance Officer", "مسؤول التزام", "G4", 1)]),
    ("IAD-BOA", "IAD", "Branch and Operations Audit", "تدقيق الفروع والعمليات", 5, ("Audit Manager, Branches and Operations", "مدير تدقيق الفروع والعمليات", "G7"),
     [("Internal Auditor", "مدقق داخلي", "G4", 3), ("Senior Internal Auditor", "مدقق داخلي أول", "G5", 1)]),
    ("IAD-ITA", "IAD", "IT Audit", "تدقيق تقنية المعلومات", 4, ("IT Audit Manager", "مدير تدقيق تقنية المعلومات", "G7"),
     [("IT Auditor", "مدقق تقنية معلومات", "G4", 1)]),
    ("RBD-BNM", "RBD", "Branch Network Management", "إدارة شبكة الفروع", 4, ("Head of Branch Network", "رئيس شبكة الفروع", "G8"),
     [("Branch Network Analyst", "محلل شبكة الفروع", "G4", 2), ("Service Quality Officer", "مسؤول جودة الخدمة", "G3", 1)]),
    ("RBD-PRD", "RBD", "Retail Products", "منتجات الأفراد", 6, ("Head of Retail Products", "رئيس منتجات الأفراد", "G7"),
     [("Product Manager", "مدير منتج", "G5", 2), ("Product Analyst", "محلل منتجات", "G4", 2)]),
    ("RBD-CRD", "RBD", "Cards", "البطاقات", 6, ("Head of Cards", "رئيس البطاقات", "G7"),
     [("Cards Product Manager", "مدير منتجات البطاقات", "G5", 1), ("Cards Officer", "مسؤول بطاقات", "G3", 3)]),
    ("DCC-DIG", "DCC", "Digital Channels", "القنوات الرقمية", 8, ("Digital Channels Manager", "مدير القنوات الرقمية", "G7"),
     [("Digital Product Owner", "مالك منتج رقمي", "G5", 2), ("Digital Operations Officer", "مسؤول العمليات الرقمية", "G3", 3), ("Fraud Monitoring Analyst", "محلل مراقبة الاحتيال", "G4", 2)]),
    ("DCC-CC", "DCC", "Contact Centre", "مركز الاتصال", 30, ("Contact Centre Manager", "مدير مركز الاتصال", "G6"),
     [("Contact Centre Agent", "موظف مركز اتصال", "G1", 12), ("Senior Contact Centre Agent", "موظف أول مركز اتصال", "G2", 4), ("Team Leader", "قائد فريق", "G3", 2)]),
    ("CBD-CRM", "CBD", "Corporate Relationship", "علاقات الشركات", 9, ("Head of Corporate Relationship", "رئيس علاقات الشركات", "G8"),
     [("Corporate Relationship Manager", "مدير علاقات الشركات", "G5", 3), ("Credit Analyst", "محلل ائتمان", "G4", 1)]),
    ("CBD-SME", "CBD", "SME Banking", "المؤسسات الصغيرة والمتوسطة", 8, ("Head of SME Banking", "رئيس المؤسسات الصغيرة والمتوسطة", "G7"),
     [("SME Relationship Manager", "مدير علاقات المؤسسات الصغيرة", "G4", 3), ("SME Credit Officer", "مسؤول ائتمان المؤسسات الصغيرة", "G3", 1)]),
    ("CBD-TRF", "CBD", "Trade Finance", "تمويل التجارة", 6, ("Trade Finance Manager", "مدير تمويل التجارة", "G6"),
     [("Trade Finance Officer", "مسؤول تمويل التجارة", "G3", 3), ("Senior Trade Finance Officer", "مسؤول أول تمويل التجارة", "G4", 1)]),
    ("TRY-DLR", "TRY", "Dealing Room", "غرفة التداول", 6, ("Chief Dealer", "كبير المتداولين", "G7"),
     [("FX Dealer", "متداول عملات", "G5", 2), ("Money Market Dealer", "متداول أسواق النقد", "G5", 1)]),
    ("TRY-ALM", "TRY", "ALM and Liquidity", "إدارة الأصول والخصوم والسيولة", 4, ("ALM Manager", "مدير إدارة الأصول والخصوم", "G7"),
     [("Liquidity Analyst", "محلل سيولة", "G4", 1)]),
    ("OPS-CLR", "OPS", "Clearing and Cheques", "المقاصة والشيكات", 10, ("Clearing Supervisor", "مشرف المقاصة", "G5"),
     [("Clearing Officer", "مسؤول مقاصة", "G2", 4), ("Senior Clearing Officer", "مسؤول مقاصة أول", "G3", 1)]),
    ("OPS-PAY", "OPS", "Payments and Remittances", "المدفوعات والحوالات", 10, ("Payments Supervisor", "مشرف المدفوعات", "G5"),
     [("Payments Officer", "مسؤول مدفوعات", "G2", 4), ("Senior Payments Officer", "مسؤول مدفوعات أول", "G3", 1)]),
    ("OPS-ACS", "OPS", "Account Services", "خدمات الحسابات", 8, ("Account Services Manager", "مدير خدمات الحسابات", "G5"),
     [("Account Services Officer", "مسؤول خدمات الحسابات", "G2", 3)]),
    ("OPS-LNO", "OPS", "Loan Operations", "عمليات القروض", 8, ("Loan Operations Manager", "مدير عمليات القروض", "G5"),
     [("Loan Operations Officer", "مسؤول عمليات القروض", "G2", 3), ("Collateral Officer", "مسؤول الضمانات", "G3", 1)]),
    ("OPS-CSH", "OPS", "Cash and Vault", "النقد والخزائن", 6, ("Cash Centre Manager", "مدير مركز النقد", "G5"),
     [("Vault Officer", "مسؤول خزينة نقدية", "G2", 2), ("ATM Replenishment Officer", "مسؤول تعبئة الصرافات", "G2", 1)]),
    ("RSK-CRA", "RSK", "Credit Risk and Approvals", "مخاطر الائتمان والموافقات", 9, ("Head of Credit Risk", "رئيس مخاطر الائتمان", "G8"),
     [("Credit Risk Officer", "مسؤول مخاطر الائتمان", "G4", 3), ("Senior Credit Risk Officer", "مسؤول أول مخاطر الائتمان", "G5", 2)]),
    ("RSK-MLR", "RSK", "Market and Liquidity Risk", "مخاطر السوق والسيولة", 4, ("Market Risk Manager", "مدير مخاطر السوق", "G7"),
     [("Market Risk Analyst", "محلل مخاطر السوق", "G4", 1)]),
    ("RSK-OPR", "RSK", "Operational Risk", "المخاطر التشغيلية", 4, ("Operational Risk Manager", "مدير المخاطر التشغيلية", "G7"),
     [("Operational Risk Analyst", "محلل المخاطر التشغيلية", "G4", 1)]),
    ("FIN-FCR", "FIN", "Financial Control and Reporting", "الرقابة المالية والتقارير", 7, ("Financial Controller", "المراقب المالي", "G8"),
     [("Accountant", "محاسب", "G3", 3), ("Senior Accountant", "محاسب أول", "G4", 2)]),
    ("FIN-BGP", "FIN", "Budgeting and Planning", "الميزانية والتخطيط", 4, ("Head of Budgeting and Planning", "رئيس الميزانية والتخطيط", "G7"),
     [("Budget Controller", "مراقب الميزانية", "G5", 1), ("Budget Analyst", "محلل ميزانية", "G4", 1)]),
    ("FIN-PRC", "FIN", "Procurement", "المشتريات", 5, ("Procurement Manager", "مدير المشتريات", "G6"),
     [("Buyer", "مسؤول مشتريات", "G3", 3)]),
    ("FIN-AP", "FIN", "Accounts Payable", "الحسابات الدائنة", 5, ("Accounts Payable Supervisor", "مشرف الحسابات الدائنة", "G5"),
     [("Accounts Payable Officer", "مسؤول الحسابات الدائنة", "G2", 3)]),
    ("HRD-REC", "HRD", "Recruitment", "التوظيف", 4, ("Recruitment Manager", "مدير التوظيف", "G6"),
     [("Recruitment Officer", "مسؤول توظيف", "G3", 2), ("Emiratisation Officer", "مسؤول التوطين", "G3", 1)]),
    ("HRD-PSV", "HRD", "Personnel Services", "خدمات الموظفين", 6, ("Personnel Services Manager", "مدير خدمات الموظفين", "G6"),
     [("HR Officer", "مسؤول موارد بشرية", "G3", 3), ("Time and Attendance Officer", "مسؤول الحضور والانصراف", "G2", 1)]),
    ("HRD-LND", "HRD", "Learning and Development", "التعلم والتطوير", 4, ("Learning and Development Manager", "مدير التعلم والتطوير", "G6"),
     [("Training Officer", "مسؤول تدريب", "G3", 2)]),
    ("ITD-SD", "ITD", "Service Desk", "مكتب الخدمة", 10, ("IT Service Desk Manager", "مدير مكتب خدمة تقنية المعلومات", "G6"),
     [("Service Desk Analyst", "محلل مكتب الخدمة", "G2", 5), ("Desktop Support Engineer", "مهندس دعم الأجهزة", "G3", 3)]),
    ("ITD-INF", "ITD", "Infrastructure", "البنية التحتية", 8, ("Infrastructure Manager", "مدير البنية التحتية", "G7"),
     [("Systems Engineer", "مهندس أنظمة", "G4", 3), ("Database Administrator", "مدير قواعد بيانات", "G5", 2)]),
    ("ITD-APP", "ITD", "Applications Support", "دعم التطبيقات", 9, ("Applications Support Manager", "مدير دعم التطبيقات", "G7"),
     [("Applications Support Analyst", "محلل دعم التطبيقات", "G4", 4), ("Core Banking Specialist", "أخصائي النظام المصرفي الأساسي", "G5", 2)]),
    ("ITD-NET", "ITD", "Network", "الشبكات", 6, ("Network Manager", "مدير الشبكات", "G6"),
     [("Network Engineer", "مهندس شبكات", "G4", 3)]),
    ("ITD-SEC", "ITD", "Information Security", "أمن المعلومات", 5, ("Information Security Manager", "مدير أمن المعلومات", "G7"),
     [("Information Security Analyst", "محلل أمن المعلومات", "G4", 2), ("Identity and Access Administrator", "مسؤول الهويات والصلاحيات", "G3", 1)]),
    ("MKT-MKT", "MKT", "Marketing", "التسويق", 5, ("Marketing Manager", "مدير التسويق", "G6"),
     [("Marketing Executive", "تنفيذي تسويق", "G3", 3)]),
    ("MKT-CX", "MKT", "Customer Experience", "تجربة العملاء", 4, ("Customer Experience Manager", "مدير تجربة العملاء", "G6"),
     [("Complaints Officer", "مسؤول الشكاوى", "G3", 2)]),
    ("MKT-COM", "MKT", "Corporate Communications", "الاتصال المؤسسي", 3, ("Corporate Communications Manager", "مدير الاتصال المؤسسي", "G6"),
     [("Communications Officer", "مسؤول اتصال", "G3", 1)]),
    ("ADM-FAC", "ADM", "Facilities", "المرافق", 6, ("Facilities Manager", "مدير المرافق", "G5"),
     [("Facilities Coordinator", "منسق مرافق", "G2", 2), ("Driver", "سائق", "G1", 1), ("Messenger", "مراسل", "G1", 1)]),
    ("ADM-PSC", "ADM", "Physical Security", "الأمن المادي", 8, ("Security Supervisor", "مشرف الأمن", "G4"),
     [("Security Guard", "حارس أمن", "G1", 1)]),
]
NO_USER_TITLES = {"Driver", "Messenger", "Security Guard"}

# The four standard sections of every banking branch: suffix, name, Arabic, size (incl. head), head, staff.
BRANCH_SECTIONS = [
    ("CSV", "Customer Service", "خدمة العملاء", 6, ("Customer Service Manager", "مدير خدمة العملاء", "G5"),
     [("Customer Service Officer", "مسؤول خدمة العملاء", "G2", 3), ("Senior Customer Service Officer", "مسؤول أول خدمة العملاء", "G3", 1)]),
    ("TLR", "Teller and Cash", "الصرافة والنقد", 6, ("Head Teller", "كبير الصرافين", "G4"),
     [("Teller", "صراف", "G1", 4), ("Senior Teller", "صراف أول", "G2", 1)]),
    ("SLS", "Personal Banking and Sales", "الخدمات المصرفية الشخصية والمبيعات", 5, ("Sales Manager", "مدير المبيعات", "G5"),
     [("Personal Banking Officer", "مسؤول الخدمات المصرفية الشخصية", "G3", 2), ("Relationship Manager", "مدير علاقات", "G4", 1)]),
    ("OPS", "Branch Operations", "عمليات الفرع", 4, ("Branch Operations Manager", "مدير عمليات الفرع", "G5"),
     [("Branch Operations Officer", "مسؤول عمليات الفرع", "G2", 1)]),
]
BRANCH_MANAGER = ("BR-BM", "Branch Manager", "مدير الفرع", "G6")

GRADES = [  # code, name, Arabic, monthly salary band (AED)
    ("G1", "Associate", "مساعد", 6000, 9000), ("G2", "Officer", "موظف", 9000, 13000), ("G3", "Senior Officer", "موظف أول", 12000, 18000),
    ("G4", "Assistant Manager", "مساعد مدير", 16000, 24000), ("G5", "Manager", "مدير", 22000, 32000), ("G6", "Senior Manager", "مدير أول", 30000, 42000),
    ("G7", "Assistant Vice President", "مساعد نائب الرئيس", 40000, 55000), ("G8", "Vice President", "نائب الرئيس", 52000, 70000),
    ("G9", "Senior Vice President", "نائب الرئيس الأول", 68000, 90000), ("G10", "Executive", "تنفيذي", 90000, 150000),
]

SHIFTS = [  # code, name, Arabic, start, end, works weekends, works public holidays
    ("HO", "Head Office hours", "دوام المقر الرئيسي", "08:00", "16:30", 0, 0),
    ("BR", "Branch hours", "دوام الفروع", "07:45", "15:45", 0, 0),
    ("CC1", "Contact Centre morning", "مركز الاتصال الصباحي", "07:00", "15:00", 1, 1),
    ("CC2", "Contact Centre evening", "مركز الاتصال المسائي", "15:00", "23:00", 1, 1),
    ("CC3", "Contact Centre night", "مركز الاتصال الليلي", "23:00", "07:00", 1, 1),
    ("SEC", "Security rota", "مناوبات الأمن", "06:00", "18:00", 1, 1),
]

COUNTRIES = [  # code, country, Arabic, nationality, weight, name group
    ("AE", "United Arab Emirates", "الإمارات العربية المتحدة", "Emirati", 24, "emirati"), ("IN", "India", "الهند", "Indian", 24, "indian"),
    ("PK", "Pakistan", "باكستان", "Pakistani", 8, "pakistani"), ("PH", "Philippines", "الفلبين", "Filipino", 7, "filipino"),
    ("EG", "Egypt", "مصر", "Egyptian", 8, "arab"), ("JO", "Jordan", "الأردن", "Jordanian", 6, "arab"), ("LB", "Lebanon", "لبنان", "Lebanese", 5, "arab"),
    ("SY", "Syria", "سوريا", "Syrian", 3, "arab"), ("SD", "Sudan", "السودان", "Sudanese", 2, "arab"), ("LK", "Sri Lanka", "سريلانكا", "Sri Lankan", 3, "southasia"),
    ("BD", "Bangladesh", "بنغلاديش", "Bangladeshi", 2, "southasia"), ("GB", "United Kingdom", "المملكة المتحدة", "British", 3, "western"),
]

SYSTEMS = [  # code, name, Arabic, go-live, owner department
    ("HRMS", "Personnel and Attendance", "شؤون الموظفين والحضور", "2016-01-03", "HRD"),
    ("CBS", "Core Banking", "النظام المصرفي الأساسي", "2019-03-03", "OPS"),
    ("FIN", "Finance", "النظام المالي", "2017-01-01", "FIN"),
    ("DIGITAL", "Digital Banking", "الخدمات المصرفية الرقمية", "2020-06-01", "DCC"),
    ("ITSM", "IT Help Desk", "مكتب خدمة تقنية المعلومات", "2018-01-07", "ITD"),
]
GO_LIVE = {s[0]: dt.date.fromisoformat(s[3]) for s in SYSTEMS}

SERVICE_ACCOUNTS = [  # user id, description, systems
    ("SYS_EOD", "Core banking end-of-day batch", ["CBS"]),
    ("SYS_CLEARING", "UAE clearing and payment-system files", ["CBS"]),
    ("SYS_ATM", "ATM and card switch postings", ["CBS"]),
    ("SYS_DIGITAL", "Digital banking channel", ["CBS", "DIGITAL"]),
    ("SYS_MIGRATION", "Legacy core banking migration (March 2019)", ["CBS"]),
    ("SYS_FIN_IF", "Finance interfaces (GL and payments)", ["FIN"]),
    ("SYS_MONITOR", "IT monitoring tools that open incidents automatically", ["ITSM"]),
]

# The eight people who also get Wabi Insight accounts in 06-seed-wabiinsightdb.sql (keep in sync).
SEED_PEOPLE = [  # number, first, last, first Ar, last Ar, gender, nationality, unit, position, birth, hire, wabi role, language
    ("W1001", "Clara", "Ashby", "كلارا", "آشبي", "F", "AE", "ITD-SEC", "head", "1986-04-17", "2014-09-07", "OrganizationAdmin", "en"),
    ("W1002", "Victor", "Lindley", "فيكتور", "ليندلي", "M", "AE", "CEO", "chief", "1969-02-08", "2008-01-06", "Management", "ar"),
    ("W1003", "Daniel", "Hartwell", "دانيال", "هارتويل", "M", "JO", "BR-DXB01", "chief", "1981-10-23", "2011-05-15", "BranchManager", "en"),
    ("W1004", "Emily", "Carver", "إيميلي", "كارفر", "F", "IN", "BR-DXB01-TLR", "Teller", "1996-07-02", "2021-02-14", "Teller", "en"),
    ("W1005", "Sophie", "Langley", "صوفي", "لانغلي", "F", "AE", "HRD-PSV", "HR Officer", "1993-12-11", "2018-10-07", "HROfficer", "ar"),
    ("W1006", "Martin", "Calder", "مارتن", "كالدر", "M", "PK", "FIN-BGP", "Budget Controller", "1985-03-29", "2015-06-21", "BudgetController", "en"),
    ("W1007", "Laura", "Bexley", "لورا", "بيكسلي", "F", "LB", "CMP-AML", "Senior AML Officer", "1990-01-19", "2017-03-12", "ComplianceOfficer", "en"),
    ("W1008", "Simon", "Varley", "سايمون", "فارلي", "M", "PH", "ITD-SD", "head", "1984-08-05", "2016-11-20", "ServiceDeskManager", "en"),
]


def d(s):
    return dt.date.fromisoformat(s) if isinstance(s, str) else s


LOC_OPENED = {loc[0]: d(loc[6]) for loc in LOCATIONS}


# ------------------------------------------------------------------------------------------------ units
@lru_cache(maxsize=None)
def departments():
    """Every department-level unit: Head Office departments and banking branches."""
    out = []
    for code, en, ar, div, pos, title, title_ar, grade in HQ_DEPARTMENTS:
        out.append({"DepartmentCode": code, "DepartmentName": en, "DepartmentNameAr": ar, "DepartmentType": "HeadOffice", "DivisionCode": div,
                    "LocationCode": "HO", "HeadPositionCode": pos})
    for loc in LOCATIONS[1:]:
        out.append({"DepartmentCode": loc[0], "DepartmentName": f"{loc[1]} Branch", "DepartmentNameAr": f"فرع {loc[2]}", "DepartmentType": "Branch",
                    "DivisionCode": "DIV-RTL", "LocationCode": loc[0], "HeadPositionCode": BRANCH_MANAGER[0]})
    return out


@lru_cache(maxsize=None)
def sections():
    out = []
    for s in HQ_SECTIONS:
        out.append({"SectionCode": s[0], "DepartmentCode": s[1], "SectionName": s[2], "SectionNameAr": s[3], "Size": s[4], "Head": s[5], "Staff": s[6], "SectionType": s[0].split("-")[1]})
    for b in BRANCH_CODES:
        for suffix, en, ar, size, head, staff in BRANCH_SECTIONS:
            out.append({"SectionCode": f"{b}-{suffix}", "DepartmentCode": b, "SectionName": en, "SectionNameAr": ar, "Size": size, "Head": head, "Staff": staff, "SectionType": suffix})
    return out


DEPT = {}
SECT = {}


def _index():
    if not DEPT:
        DEPT.update({x["DepartmentCode"]: x for x in departments()})
        SECT.update({x["SectionCode"]: x for x in sections()})


def is_branch(code):
    return code.startswith("BR-")


@lru_cache(maxsize=None)
def positions():
    """Position catalogue: department heads, branch manager, and per-section heads and staff (branch positions shared by all branches)."""
    out = []
    for code, en, ar, div, pos, title, title_ar, grade in HQ_DEPARTMENTS:
        out.append({"PositionCode": pos, "Title": title, "TitleAr": title_ar, "GradeCode": grade, "DepartmentCode": code, "SectionType": None, "Level": "DepartmentHead"})
    out.append({"PositionCode": BRANCH_MANAGER[0], "Title": BRANCH_MANAGER[1], "TitleAr": BRANCH_MANAGER[2], "GradeCode": BRANCH_MANAGER[3], "DepartmentCode": None, "SectionType": None, "Level": "DepartmentHead"})
    for s in HQ_SECTIONS:
        out.append({"PositionCode": f"{s[0]}-H", "Title": s[5][0], "TitleAr": s[5][1], "GradeCode": s[5][2], "DepartmentCode": s[1], "SectionType": s[0], "Level": "SectionHead"})
        for i, (t, tar, g, _) in enumerate(s[6], 1):
            out.append({"PositionCode": f"{s[0]}-{i}", "Title": t, "TitleAr": tar, "GradeCode": g, "DepartmentCode": s[1], "SectionType": s[0], "Level": "Staff"})
    for suffix, en, ar, size, head, staff in BRANCH_SECTIONS:
        out.append({"PositionCode": f"BR-{suffix}-H", "Title": head[0], "TitleAr": head[1], "GradeCode": head[2], "DepartmentCode": None, "SectionType": suffix, "Level": "SectionHead"})
        for i, (t, tar, g, _) in enumerate(staff, 1):
            out.append({"PositionCode": f"BR-{suffix}-{i}", "Title": t, "TitleAr": tar, "GradeCode": g, "DepartmentCode": None, "SectionType": suffix, "Level": "Staff"})
    return out


def _position_code(section_code, title):
    if is_branch(section_code):
        suffix = section_code.split("-")[-1]
        prefix = f"BR-{suffix}"
    else:
        prefix = section_code
    for p in positions():
        if p["PositionCode"].startswith(prefix + "-") and p["Title"] == title:
            return p["PositionCode"]
    raise KeyError((section_code, title))


# ------------------------------------------------------------------------------------------------ people
def _pick(r, weighted):
    total = sum(w for _, w in weighted)
    x = r.random() * total
    for item, w in weighted:
        x -= w
        if x <= 0:
            return item
    return weighted[-1][0]


def _clean(last):
    for prefix in ("Al ", "El ", "Dela "):
        if last.startswith(prefix):
            last = last[len(prefix):]
    return "".join(ch for ch in last.upper() if ch.isalpha())


@lru_cache(maxsize=None)
def _people():
    """Builds employees, movements and planted facts. Returns (employees, movements, facts)."""
    _index()
    r = rng(20250101)
    nat_weights = [(c, c[4]) for c in COUNTRIES]
    used_emails, used_users = set(), set()
    slots = []  # one per job: dept, section, position code, grade, level, has_user

    for dep in HQ_DEPARTMENTS:
        slots.append({"dept": dep[0], "section": None, "pos": dep[4], "grade": dep[7], "level": "DepartmentHead", "title": dep[5]})
    for b in BRANCH_CODES:
        slots.append({"dept": b, "section": None, "pos": BRANCH_MANAGER[0], "grade": BRANCH_MANAGER[3], "level": "DepartmentHead", "title": BRANCH_MANAGER[1]})
    for s in sections():
        slots.append({"dept": s["DepartmentCode"], "section": s["SectionCode"], "pos": _position_code(s["SectionCode"], s["Head"][0]), "grade": s["Head"][2],
                      "level": "SectionHead", "title": s["Head"][0]})
        weighted = [((t, g), w) for t, _, g, w in s["Staff"]]
        titles = [t for t, _, _, _ in s["Staff"]]
        for i in range(s["Size"] - 1):
            # Every staff title appears at least once, then by weight.
            t, g = (titles[i], next(x[2] for x in s["Staff"] if x[0] == titles[i])) if i < len(titles) else _pick(r, weighted)
            slots.append({"dept": s["DepartmentCode"], "section": s["SectionCode"], "pos": _position_code(s["SectionCode"], t), "grade": g, "level": "Staff", "title": t})

    def name_for(nat, female):
        (first, first_ar), (last, last_ar) = bank_names.pick(r, nat[5], female)
        return first, last, first_ar, last_ar

    def unique_email(first, last):
        base = f"{first}.{last}".lower().replace(" ", "").replace("'", "")
        e, n = f"{base}@{DOMAIN}", 2
        while e in used_emails:
            e, n = f"{base}{n}@{DOMAIN}", n + 1
        used_emails.add(e)
        return e

    def unique_user(first, last):
        base = (first[0] + _clean(last))[:10]
        u, n = base, 2
        while u in used_users:
            u, n = f"{base[:9]}{n}", n + 1
        used_users.add(u)
        return u

    def hire_for(slot, dob):
        loc = "HO" if not is_branch(slot["dept"]) else slot["dept"]
        opened = LOC_OPENED[loc]
        adult = dob.replace(year=dob.year + 22)
        if slot["level"] == "DepartmentHead":
            lo, hi = max(opened, adult, dt.date(2008, 1, 6)), max(opened, dt.date(2016, 12, 31))
        elif slot["level"] == "SectionHead":
            lo, hi = max(opened, adult, dt.date(2008, 3, 1)), max(opened + dt.timedelta(days=60), dt.date(2019, 12, 31))
        else:
            lo, hi = max(opened, adult, dt.date(2012, 1, 1)), dt.date(2024, 11, 30)
        if lo >= hi:
            return lo
        span = (hi - lo).days
        return lo + dt.timedelta(days=int(span * (r.random() ** (0.6 if slot["level"] == "Staff" else 1.2))))

    def make(slot, number=None, seed=None, hire=None):
        if seed:
            _, first, last, first_ar, last_ar, gender, nat_code, _, _, dob, hire_s, _, _ = seed
            nat = next(c for c in COUNTRIES if c[0] == nat_code)
            dob, hire = d(dob), d(hire_s)
        else:
            nat = _pick(r, nat_weights)
            female = r.random() < (0.55 if slot["section"] and slot["section"].split("-")[-1] in ("CSV", "TLR", "CC", "PSV", "REC", "CX") else 0.38)
            if slot["title"] in NO_USER_TITLES:
                female = False
            first, last, first_ar, last_ar = name_for(nat, female)
            gender = "F" if female else "M"
            gn = int(slot["grade"][1:])
            age = min(60, max(22, int(23 + gn * 2.6 + r.gauss(0, 4))))
            dob = dt.date(2025 - age, r.randint(1, 12), r.randint(1, 28))
            hire = hire or hire_for(slot, dob)
        loc = "HO" if not is_branch(slot["dept"]) else slot["dept"]
        sect = slot["section"]
        if sect == "DCC-CC" and slot["level"] == "Staff":
            shift = r.choice(["CC1", "CC1", "CC2", "CC2", "CC3"])
            off = r.randint(1, 7)
            off_days = f"{off}{off % 7 + 1}"
        elif sect == "ADM-PSC" and slot["level"] == "Staff":
            shift, off = "SEC", r.randint(1, 7)
            off_days = f"{off}{off % 7 + 1}"
        else:
            shift, off_days = ("BR" if is_branch(slot["dept"]) else "HO"), "67"
        has_user = slot["title"] not in NO_USER_TITLES
        e = {
            "EmployeeNumber": number, "FirstName": first, "LastName": last, "FullName": f"{first} {last}",
            "FullNameAr": f"{first_ar} {last_ar}" if first_ar and last_ar else None, "Gender": gender, "DateOfBirth": dob,
            "NationalityCode": nat[0], "MaritalStatus": r.choice(["Single", "Married", "Married", "Married"]),
            "HireDate": hire, "TerminationDate": None, "TerminationReason": None,
            "DivisionCode": DEPT[slot["dept"]]["DivisionCode"], "DepartmentCode": slot["dept"], "SectionCode": sect, "LocationCode": loc,
            "PositionCode": slot["pos"], "GradeCode": slot["grade"], "Level": slot["level"], "Title": slot["title"],
            "ShiftCode": shift, "WeeklyOffDays": off_days, "WorkEmail": unique_email(first, last) if has_user else None,
            "MobileNumber": f"+971 5{r.choice('024568')} {r.randint(1000000, 9999999)}",
            "EmiratesIdNumber": f"784-{dob.year}-{r.randint(1000000, 9999999)}-{r.randint(0, 9)}",
            "PassportNumber": None if nat[0] == "AE" else f"{r.choice('ABCDEFGHJKLMNPRSTUVWXYZ')}{r.choice('ABCDEFGHJKLMNPRSTUVWXYZ')}{r.randint(1000000, 9999999)}",
            "UserId": unique_user(first, last) if has_user else None,
        }
        return e

    people = []
    # Seed people take their slots first.
    taken = set()
    for s in SEED_PEOPLE:
        unit, kind = s[7], s[8]
        if kind == "chief":
            idx = next(i for i, x in enumerate(slots) if x["dept"] == unit and x["level"] == "DepartmentHead")
        elif kind == "head":
            idx = next(i for i, x in enumerate(slots) if x["section"] == unit and x["level"] == "SectionHead")
        else:
            idx = next(i for i, x in enumerate(slots) if x["section"] == unit and x["title"] == kind and i not in taken)
        taken.add(idx)
        e = make(slots[idx], s[0], seed=s)
        e["Slot"] = idx
        e["WabiRole"], e["PreferredLanguage"] = s[11], s[12]
        people.append(e)
    for i, slot in enumerate(slots):
        if i in taken:
            continue
        e = make(slot)
        e["Slot"] = i
        people.append(e)

    # Turnover in 2025: staff-level leavers, most replaced 3-8 weeks later; plus growth hires in the Contact Centre and IT.
    staff_idx = [p for p in people if p["Level"] == "Staff" and p["EmployeeNumber"] is None and p["HireDate"] < dt.date(2024, 6, 1)]
    leavers = [staff_idx[i] for i in sorted(r.sample(range(len(staff_idx)), 40))]
    facts = {}
    joiners = []
    for n, p in enumerate(leavers):
        term = dt.date(2025, 1, 15) + dt.timedelta(days=int(r.random() * 300))
        p["TerminationDate"] = term
        p["TerminationReason"] = r.choice(["Resignation", "Resignation", "Resignation", "End of contract", "Termination"])
        if n < 34:
            hire = term + dt.timedelta(days=r.randint(21, 56))
            if hire <= dt.date(2025, 12, 15):
                j = make(slots[p["Slot"]], hire=hire)
                j["Slot"] = p["Slot"]
                joiners.append(j)
    for sect, count in (("DCC-CC", 4), ("ITD-APP", 1), ("CMP-AML", 1)):
        base = next(i for i, x in enumerate(slots) if x["section"] == sect and x["level"] == "Staff")
        for _ in range(count):
            j = make(slots[base], hire=dt.date(2025, r.randint(2, 10), r.randint(1, 28)))
            j["Slot"] = base
            joiners.append(j)
    people.extend(joiners)
    # The planted access finding: the first branch teller who left keeps Core Banking access three days after termination.
    late = next(p for p in leavers if p["SectionCode"] and p["SectionCode"].endswith("-TLR"))
    facts["late_revocation"] = late

    # Employee numbers follow the hire date (W1001-W1008 are the seed people).
    rest = sorted((p for p in people if p["EmployeeNumber"] is None), key=lambda p: (p["HireDate"], p["Slot"], p["LastName"], p["FirstName"]))
    for n, p in enumerate(rest, 1101):
        p["EmployeeNumber"] = f"W{n}"

    # Movements in 2025: branch-to-branch transfers (same section type) and promotions.
    movements = []
    stable = [p for p in people if p["Level"] == "Staff" and p["TerminationDate"] is None and p["HireDate"] < Y0
              and not p["EmployeeNumber"].startswith("W100") and p["SectionCode"]]
    branch_staff = [p for p in stable if is_branch(p["DepartmentCode"]) and p["SectionCode"].split("-")[-1] in ("TLR", "CSV", "SLS")]
    for p in [branch_staff[i] for i in sorted(r.sample(range(len(branch_staff)), 6))]:
        suffix = p["SectionCode"].split("-")[-1]
        target = r.choice([b for b in BRANCH_CODES if b != p["DepartmentCode"]])
        eff = dt.date(2025, r.randint(3, 9), 1)
        movements.append({"EmployeeNumber": p["EmployeeNumber"], "MovementType": "Transfer", "EffectiveDate": eff,
                          "FromDepartmentCode": p["DepartmentCode"], "FromSectionCode": p["SectionCode"], "FromLocationCode": p["LocationCode"],
                          "FromPositionCode": p["PositionCode"], "FromGradeCode": p["GradeCode"],
                          "ToDepartmentCode": target, "ToSectionCode": f"{target}-{suffix}", "ToLocationCode": target,
                          "ToPositionCode": p["PositionCode"], "ToGradeCode": p["GradeCode"], "Reason": "Branch staffing rebalance"})
        p.update({"DepartmentCode": target, "SectionCode": f"{target}-{suffix}", "LocationCode": target})
    hq_staff = [p for p in stable if not is_branch(p["DepartmentCode"]) and int(p["GradeCode"][1:]) <= 4 and p not in branch_staff]
    for p in [hq_staff[i] for i in sorted(r.sample(range(len(hq_staff)), 5))]:
        new_grade = f"G{int(p['GradeCode'][1:]) + 1}"
        eff = dt.date(2025, r.choice([4, 7, 10]), 1)
        movements.append({"EmployeeNumber": p["EmployeeNumber"], "MovementType": "Promotion", "EffectiveDate": eff,
                          "FromDepartmentCode": p["DepartmentCode"], "FromSectionCode": p["SectionCode"], "FromLocationCode": p["LocationCode"],
                          "FromPositionCode": p["PositionCode"], "FromGradeCode": p["GradeCode"],
                          "ToDepartmentCode": p["DepartmentCode"], "ToSectionCode": p["SectionCode"], "ToLocationCode": p["LocationCode"],
                          "ToPositionCode": p["PositionCode"], "ToGradeCode": new_grade, "Reason": "Annual promotion"})
        p["GradeCode"] = new_grade
    movements.sort(key=lambda m: (m["EffectiveDate"], m["EmployeeNumber"]))
    for i, m in enumerate(movements, 1):
        m["MovementId"] = i

    for p in people:
        p["EmploymentStatus"] = "Active" if p["TerminationDate"] is None else ("Resigned" if p["TerminationReason"] == "Resignation" else "Terminated")
    people.sort(key=lambda p: p["EmployeeNumber"])
    return people, movements, facts


def employees():
    return _people()[0]


def movements():
    return _people()[1]


@lru_cache(maxsize=None)
def by_number():
    return {p["EmployeeNumber"]: p for p in employees()}


@lru_cache(maxsize=None)
def by_user():
    return {p["UserId"]: p for p in employees() if p["UserId"]}


def employed(p, day):
    return p["HireDate"] <= day and (p["TerminationDate"] is None or day <= p["TerminationDate"])


def unit_on(p, day):
    """(DepartmentCode, SectionCode, LocationCode, GradeCode) on a date, following EmployeeMovements."""
    dept, sect, loc, grade = p["DepartmentCode"], p["SectionCode"], p["LocationCode"], p["GradeCode"]
    for m in sorted((m for m in movements() if m["EmployeeNumber"] == p["EmployeeNumber"]), key=lambda m: m["EffectiveDate"], reverse=True):
        if day < m["EffectiveDate"]:
            dept, sect, loc, grade = m["FromDepartmentCode"], m["FromSectionCode"], m["FromLocationCode"], m["FromGradeCode"]
    return dept, sect, loc, grade


def head_of(unit_code, day):
    """The active head of a department or section on a date (None when vacant)."""
    _index()
    level = "SectionHead" if unit_code in SECT else "DepartmentHead"
    for p in employees():
        if p["Level"] == level and employed(p, day):
            dept, sect, _, _ = unit_on(p, day)
            if (level == "SectionHead" and sect == unit_code) or (level == "DepartmentHead" and dept == unit_code and sect is None):
                return p
    return None


def manager_on(p, day):
    """Line manager on a date: section head -> department head (branch manager) -> division chief -> CEO."""
    _index()
    dept, sect, _, _ = unit_on(p, day)
    ceo = by_number()["W1002"]
    if p["Level"] == "Staff":
        h = head_of(sect, day)
        if h and h is not p:
            return h
    if p["Level"] in ("Staff", "SectionHead"):
        h = head_of(dept, day)
        if h and h is not p:
            return h
    if is_branch(dept):
        return head_of("RBD-BNM", day) or ceo
    if p["EmployeeNumber"] == ceo["EmployeeNumber"]:
        return None
    div = DEPT[dept]["DivisionCode"]
    chief_pos = next(x[3] for x in DIVISIONS if x[0] == div)
    chief = next((e for e in employees() if e["PositionCode"] == chief_pos and employed(e, day)), None)
    if chief and chief is not p and dept not in ("LGL", "CMP", "IAD"):
        return chief
    return ceo


# ------------------------------------------------------------------------------------------------ working time
def is_workday(p, day):
    """The employee's rostered working day (shift pattern; branch and head office close on UAE public holidays)."""
    dow = day.isoweekday()
    if str(dow) in p["WeeklyOffDays"]:
        return False
    if p["ShiftCode"] in ("HO", "BR") and day in HOLIDAYS:
        return False
    return True


@lru_cache(maxsize=None)
def _leave_and_absence():
    """Leave requests (approved, rejected, cancelled) and unauthorised absences for 2025."""
    r = rng(777)
    requests, absences = [], set()
    on_leave = {}
    for p in employees():
        start = max(Y0, p["HireDate"])
        end = min(Y1, p["TerminationDate"] or Y1)
        if start > end:
            continue
        days = [x for x in DAYS_2025 if start <= x <= end and is_workday(p, x)]
        if len(days) < 20:
            continue
        busy = set()
        plan = []
        months = max(1, (end - start).days // 30)
        for _ in range(min(3, max(1, months // 4))):
            plan.append(("AL", r.randint(4, 12)))
        for _ in range(r.choice([0, 0, 1, 1, 2, 3])):
            plan.append(("SL", r.randint(1, 2)))
        if r.random() < 0.08:
            plan.append(("EL", 1))
        if p["Gender"] == "F" and r.random() < 0.03 and len(days) > 120:
            plan.append(("ML", 45))
        for code, length in plan:
            for _ in range(10):
                i = r.randint(0, max(0, len(days) - length - 1))
                block = days[i:i + length]
                if block and not any(b in busy for b in block):
                    break
            else:
                continue
            status = "Approved"
            x = r.random()
            if code == "AL" and x < 0.05:
                status = "Rejected"
            elif code == "AL" and x < 0.08:
                status = "Cancelled"
            requests.append({"EmployeeNumber": p["EmployeeNumber"], "LeaveTypeCode": code, "StartDate": block[0], "EndDate": block[-1],
                             "WorkingDays": len(block), "Status": status, "Blocks": block})
            if status == "Approved":
                busy.update(block)
        on_leave[p["EmployeeNumber"]] = busy
        for x in days:
            if x not in busy and r.random() < 0.004:
                absences.add((p["EmployeeNumber"], x))
    requests.sort(key=lambda q: (q["StartDate"], q["EmployeeNumber"]))
    for i, q in enumerate(requests, 1):
        q["LeaveRequestId"] = i
        lead = 3 if q["LeaveTypeCode"] == "SL" else 14
        q["RequestedAt"] = dt.datetime.combine(max(q["StartDate"] - dt.timedelta(days=lead), by_number()[q["EmployeeNumber"]]["HireDate"]), dt.time(9, 15))
        if q["LeaveTypeCode"] in ("SL", "EL"):
            q["RequestedAt"] = dt.datetime.combine(q["StartDate"], dt.time(8, 5))
    return requests, absences, on_leave


@lru_cache(maxsize=None)
def _pending_leave():
    """Annual leave for January and February 2026 requested in the second half of December 2025 and not decided yet. Its own
    random stream, so the 2025 model above is unchanged; it never affects 2025 attendance (only approved leave does)."""
    r = rng(778)
    people = [p for p in employees() if p["TerminationDate"] is None and p["UserId"]]
    out = []
    next_id = len(_leave_and_absence()[0]) + 1
    for p in sorted(r.sample(people, 28), key=lambda x: x["EmployeeNumber"]):
        first = dt.date(2026, 1, 4) + dt.timedelta(days=r.randint(0, 50))
        while first.weekday() >= 5:
            first += dt.timedelta(days=1)
        days = [first + dt.timedelta(days=k) for k in range(r.randint(5, 16))]
        block = [x for x in days if x.weekday() < 5]
        asked = dt.date(2025, 12, 15) + dt.timedelta(days=r.randint(0, 16))
        while not present(p, asked) and asked < dt.date(2025, 12, 31):
            asked += dt.timedelta(days=1)
        out.append({"LeaveRequestId": next_id, "EmployeeNumber": p["EmployeeNumber"], "LeaveTypeCode": "AL", "StartDate": block[0], "EndDate": days[-1],
                    "WorkingDays": len(block), "Status": "Pending", "Blocks": block, "RequestedAt": dt.datetime.combine(asked, dt.time(r.randint(8, 15), r.randint(0, 59)))})
        next_id += 1
    return out


def leave_requests():
    """2025 leave (approved, rejected, cancelled) and the requests still pending at the year end."""
    return _leave_and_absence()[0] + _pending_leave()


def absences():
    return _leave_and_absence()[1]


def on_leave(p, day):
    return day in _leave_and_absence()[2].get(p["EmployeeNumber"], ())


def present(p, day):
    """At work on that date: employed, rostered, not on approved leave, not absent."""
    return employed(p, day) and is_workday(p, day) and not on_leave(p, day) and (p["EmployeeNumber"], day) not in absences()


def available(p, day):
    """Employed and neither on leave nor absent (on-call work outside the roster)."""
    return employed(p, day) and not on_leave(p, day) and (p["EmployeeNumber"], day) not in absences()


# ------------------------------------------------------------------------------------------------ identity and access
CBS_ROLES = {  # section type -> (staff role, staff limit, head role, head limit)
    "TLR": ("Teller", 50000, "HeadTeller", 200000), "CSV": ("CustomerService", 0, "CustomerServiceManager", 0),
    "SLS": ("SalesOfficer", 0, "SalesManager", 0), "OPS": ("BranchOperations", 0, "BranchOperationsManager", 1000000),
    "CLR": ("ClearingOfficer", 0, "ClearingSupervisor", 0), "PAY": ("PaymentsOfficer", 0, "PaymentsSupervisor", 5000000),
    "ACS": ("AccountServices", 0, "AccountServicesManager", 0), "LNO": ("LoanOperations", 0, "LoanOperationsManager", 0),
    "CSH": ("CashVault", 0, "CashCentreManager", 0), "CRA": ("CreditOfficer", 250000, "HeadOfCredit", 3000000),
    "CRM": ("CorporateRM", 0, "CorporateRMHead", 0), "SME": ("SmeRM", 0, "SmeHead", 0), "TRF": ("TradeFinance", 0, "TradeFinanceManager", 0),
    "DLR": ("Dealer", 0, "ChiefDealer", 0), "AML": ("ComplianceInvestigator", 0, "ComplianceInvestigator", 0),
    "KYC": ("KycReviewer", 0, "KycReviewer", 0), "BOA": ("Auditor", 0, "Auditor", 0), "ITA": ("Auditor", 0, "Auditor", 0),
    "CC": ("ContactCentre", 0, "ContactCentre", 0), "APP": ("ApplicationSupport", 0, "ApplicationSupport", 0),
}
DEPT_HEAD_CBS = {"BR-BM": ("BranchManager", 2000000), "COO": ("OperationsHead", 10000000), "CRO": ("ChiefRiskOfficer", 10000000),
                 "CWBO": ("WholesaleHead", 0), "CCO": ("ComplianceInvestigator", 0), "CRBO": ("RetailHead", 0)}
DIGITAL_ROLES = {"CC": ("ContactCentreAgent", "ContactCentreSupervisor"), "DIG": ("DigitalOperations", "DigitalOperationsManager"),
                 "CSV": ("BranchEnrolment", "BranchEnrolment"), "AML": ("FraudInvestigator", "FraudInvestigator"), "APP": ("ApplicationSupport", "ApplicationSupport")}
FIN_ROLES = {"FCR": ("Accountant", 0, "FinancialController", 0), "BGP": ("BudgetAnalyst", 0, "BudgetManager", 0),
             "PRC": ("Buyer", 0, "ProcurementManager", 100000), "AP": ("PayablesOfficer", 0, "PayablesSupervisor", 0)}


def _roles_for(p):
    """System access rows (system, role, limit) an employee gets with their job."""
    if not p["UserId"]:
        return []
    out = []
    sect_type = p["SectionCode"].split("-")[-1] if p["SectionCode"] else None
    head = p["Level"] in ("SectionHead", "DepartmentHead")
    hrms = "HRAdministrator" if p["DepartmentCode"] == "HRD" else ("ManagerSelfService" if head else "EmployeeSelfService")
    out.append(("HRMS", hrms, None))
    out.append(("ITSM", "Agent" if p["DepartmentCode"] == "ITD" and not head else ("ServiceDeskManager" if p["SectionCode"] == "ITD-SD" and head
                                                                                  else ("GroupLead" if p["DepartmentCode"] == "ITD" else "Requester")), None))
    # Core banking
    if p["Level"] == "DepartmentHead" and p["PositionCode"] in DEPT_HEAD_CBS:
        role, limit = DEPT_HEAD_CBS[p["PositionCode"]]
        out.append(("CBS", role, limit or None))
    elif sect_type in CBS_ROLES and not (sect_type == "OPS" and not is_branch(p["DepartmentCode"])):
        staff_role, staff_limit, head_role, head_limit = CBS_ROLES[sect_type]
        role, limit = (head_role, head_limit) if p["Level"] == "SectionHead" else (staff_role, staff_limit)
        if p["Title"] in ("Senior Teller",):
            limit = 75000
        out.append(("CBS", role, limit or None))
    # Digital banking
    if sect_type in DIGITAL_ROLES and (sect_type != "AML" or p["Title"] in ("AML Analyst", "AML and Sanctions Manager", "Senior AML Officer")):
        staff_role, head_role = DIGITAL_ROLES[sect_type]
        out.append(("DIGITAL", head_role if p["Level"] == "SectionHead" else staff_role, None))
    # Finance: finance staff, requisitioners (section heads) and approvers (department heads, CFO, CEO).
    if p["DepartmentCode"] == "FIN" and sect_type in FIN_ROLES:
        staff_role, staff_limit, head_role, head_limit = FIN_ROLES[sect_type]
        role, limit = (head_role, head_limit) if p["Level"] == "SectionHead" else (staff_role, staff_limit)
        out.append(("FIN", role, limit or None))
    elif p["PositionCode"] == "CFO":
        out.append(("FIN", "ChiefFinancialOfficer", 2000000))
    elif p["PositionCode"] == "CEO":
        out.append(("FIN", "ExecutiveApprover", None))
    elif p["Level"] == "DepartmentHead":
        out.append(("FIN", "BudgetHolder", 50000))
    elif p["Level"] == "SectionHead" or p["Title"] in ("Executive Assistant", "Facilities Coordinator"):
        out.append(("FIN", "Requisitioner", None))
    return out


@lru_cache(maxsize=None)
def access():
    """Personnel.UserSystemAccess: one row per user, system and role, with granted and revoked dates."""
    rows = []
    facts = _people()[2]
    for p in employees():
        for system, role, limit in _roles_for(p):
            granted = max(p["HireDate"], GO_LIVE[system])
            revoked = p["TerminationDate"]
            if system == "CBS" and p is facts["late_revocation"]:
                revoked = p["TerminationDate"] + dt.timedelta(days=3)
            rows.append({"UserId": p["UserId"], "SystemCode": system, "RoleCode": role, "ApprovalLimitAed": limit, "GrantedDate": granted, "RevokedDate": revoked})
    # Planted finding: two users with access their job does not need.
    odd = [p for p in employees() if p["SectionCode"] in ("MKT-MKT", "HRD-REC") and p["Level"] == "Staff" and p["TerminationDate"] is None and p["UserId"]]
    mkt = next(p for p in odd if p["SectionCode"] == "MKT-MKT")
    rec = next(p for p in odd if p["SectionCode"] == "HRD-REC")
    rows.append({"UserId": mkt["UserId"], "SystemCode": "CBS", "RoleCode": "AccountServices", "ApprovalLimitAed": None,
                 "GrantedDate": dt.date(2025, 2, 10), "RevokedDate": None, "Finding": "Marketing user with core banking account-services access"})
    rows.append({"UserId": rec["UserId"], "SystemCode": "FIN", "RoleCode": "Buyer", "ApprovalLimitAed": None,
                 "GrantedDate": dt.date(2025, 5, 18), "RevokedDate": None, "Finding": "Recruitment user with finance buyer access"})
    for uid, _, systems in SERVICE_ACCOUNTS:
        for s in systems:
            rows.append({"UserId": uid, "SystemCode": s, "RoleCode": "ServiceAccount", "ApprovalLimitAed": None, "GrantedDate": GO_LIVE[s] if uid != "SYS_MIGRATION" else dt.date(2019, 1, 6),
                         "RevokedDate": None})
    rows.sort(key=lambda a: (a["UserId"], a["SystemCode"]))
    for i, a in enumerate(rows, 1):
        a["AccessId"] = i
    return rows


@lru_cache(maxsize=None)
def _access_by_user():
    out = {}
    for a in access():
        out.setdefault(a["UserId"], []).append(a)
    return out


def access_row(user_id, system, day):
    for a in _access_by_user().get(user_id, ()):
        if a["SystemCode"] == system and a["GrantedDate"] <= day and (a["RevokedDate"] is None or day <= a["RevokedDate"]):
            return a
    return None


def has_access(user_id, system, day, roles=None):
    a = access_row(user_id, system, day)
    return a is not None and (roles is None or a["RoleCode"] in roles)


@lru_cache(maxsize=None)
def _unit_members(unit_code):
    """Employees who belong to a department or section at any time in 2025 (movements included)."""
    _index()
    out = []
    for p in employees():
        units = {p["DepartmentCode"], p["SectionCode"]}
        for m in movements():
            if m["EmployeeNumber"] == p["EmployeeNumber"]:
                units |= {m["FromDepartmentCode"], m["FromSectionCode"]}
        if unit_code in units:
            out.append(p)
    return out


def staff_on(day, unit_code, system=None, roles=None, levels=None, presence=True):
    """People of a section or department on a date (present at work unless presence=False), optionally with system access and roles."""
    out = []
    for p in _unit_members(unit_code):
        if presence and not present(p, day):
            continue
        if not presence and not available(p, day):
            continue
        dept, sect, _, _ = unit_on(p, day)
        if unit_code not in (dept, sect):
            continue
        if levels and p["Level"] not in levels:
            continue
        if system and not has_access(p["UserId"], system, day, roles):
            continue
        out.append(p)
    return out


def system_users():
    """Personnel.SystemUsers: employee accounts and service accounts."""
    rows = []
    for p in employees():
        if p["UserId"]:
            rows.append({"UserId": p["UserId"], "EmployeeNumber": p["EmployeeNumber"], "AccountType": "Employee", "DisplayName": p["FullName"],
                         "Email": p["WorkEmail"], "Status": "Active" if p["TerminationDate"] is None else "Disabled", "CreatedDate": p["HireDate"],
                         "DisabledDate": None if p["TerminationDate"] is None else max(a["RevokedDate"] for a in _access_by_user()[p["UserId"]] if a["RevokedDate"])})
    for uid, desc, systems in SERVICE_ACCOUNTS:
        rows.append({"UserId": uid, "EmployeeNumber": None, "AccountType": "Service", "DisplayName": desc, "Email": None, "Status": "Active",
                     "CreatedDate": min(GO_LIVE[s] for s in systems) if uid != "SYS_MIGRATION" else dt.date(2019, 1, 6), "DisabledDate": None})
    rows.sort(key=lambda u: u["UserId"])
    return rows


def local_users(system):
    """The system's own Users table: everyone with access to it during 2025, plus its service accounts (values identical to Personnel)."""
    rows = []
    for a in access():
        if a["SystemCode"] != system or a["GrantedDate"] > Y1 or (a["RevokedDate"] and a["RevokedDate"] < Y0):
            continue
        p = by_user().get(a["UserId"])
        if p is None:
            desc = next(s[1] for s in SERVICE_ACCOUNTS if s[0] == a["UserId"])
            rows.append({"UserId": a["UserId"], "EmployeeNumber": None, "FullName": desc, "FullNameAr": None, "DepartmentCode": None, "SectionCode": None,
                         "RoleCode": a["RoleCode"], "ApprovalLimitAed": None, "Status": "Active", "AccessGrantedDate": a["GrantedDate"], "AccessRevokedDate": None})
            continue
        as_of = min(Y1, a["RevokedDate"] or Y1, p["TerminationDate"] or Y1)
        dept, sect, _, _ = unit_on(p, as_of)
        rows.append({"UserId": a["UserId"], "EmployeeNumber": p["EmployeeNumber"], "FullName": p["FullName"], "FullNameAr": p["FullNameAr"],
                     "DepartmentCode": dept, "SectionCode": sect, "RoleCode": a["RoleCode"], "ApprovalLimitAed": a["ApprovalLimitAed"],
                     "Status": "Active" if a["RevokedDate"] is None else "Disabled", "AccessGrantedDate": a["GrantedDate"], "AccessRevokedDate": a["RevokedDate"]})
    rows.sort(key=lambda u: u["UserId"])
    return rows


def facts():
    return _people()[2]


def validate():
    """Structural assertions; every generator calls this first."""
    _index()
    emps = employees()
    assert len({p["EmployeeNumber"] for p in emps}) == len(emps)
    assert len({p["UserId"] for p in emps if p["UserId"]}) == len([p for p in emps if p["UserId"]])
    for s in sections():
        active = [p for p in emps if p["SectionCode"] == s["SectionCode"] and p["TerminationDate"] is None]
        assert len(active) >= 3, (s["SectionCode"], len(active))
    for dpt in departments():
        assert head_of(dpt["DepartmentCode"], Y1) is not None, dpt["DepartmentCode"]
    for p in emps:
        assert p["HireDate"] >= LOC_OPENED["HO" if not is_branch(unit_on(p, p["HireDate"])[0]) else unit_on(p, max(p["HireDate"], Y0))[0]] or p["HireDate"] < Y0
    return True


if __name__ == "__main__":
    validate()
    emps = employees()
    active = [p for p in emps if p["TerminationDate"] is None]
    print(f"employees {len(emps)}, active at year end {len(active)}, users {sum(1 for p in emps if p['UserId'])}, access rows {len(access())}, "
          f"movements {len(movements())}, leave requests {len(leave_requests())}, absences {len(absences())}")
    print("late revocation:", facts()["late_revocation"]["EmployeeNumber"], facts()["late_revocation"]["UserId"])
    for p in emps[:8]:
        print(p["EmployeeNumber"], p["FullName"], p["UserId"], p["DepartmentCode"], p["SectionCode"], p["PositionCode"], p["HireDate"])
