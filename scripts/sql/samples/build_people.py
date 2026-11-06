"""Organization structure and staff shared by HumanResourcesDB, ProcurementStoresDB and the WabiInsightDB seed."""
import datetime as dt
from build_common import rng

ORG_DOMAIN = "gulfaviation.example"

# DepartmentCode, English, Arabic, cost centre, shift-based (24/7 airport operations)
DEPARTMENTS = [
    ("EXEC", "Executive Office", "المكتب التنفيذي", "CC-100", False),
    ("OPS", "Flight Operations", "العمليات الجوية", "CC-200", True),
    ("GND", "Ground Handling", "الخدمات الأرضية", "CC-300", True),
    ("MNT", "Engineering and Maintenance", "الهندسة والصيانة", "CC-400", True),
    ("CS", "Customer Service", "خدمة العملاء", "CC-500", True),
    ("SEC", "Aviation Security", "أمن الطيران", "CC-600", True),
    ("FIN", "Finance", "المالية", "CC-700", False),
    ("HR", "Human Resources", "الموارد البشرية", "CC-800", False),
    ("PRC", "Procurement and Stores", "المشتريات والمخازن", "CC-900", False),
    ("IT", "Information Technology", "تقنية المعلومات", "CC-950", False),
]
SECTIONS = [
    ("EXEC-OFF", "EXEC", "Office of the CEO", "مكتب الرئيس التنفيذي"), ("EXEC-LEG", "EXEC", "Legal and Compliance", "الشؤون القانونية والامتثال"),
    ("EXEC-STR", "EXEC", "Strategy and Performance", "الاستراتيجية والأداء"),
    ("OPS-CTRL", "OPS", "Operations Control Centre", "مركز التحكم بالعمليات"), ("OPS-DSP", "OPS", "Flight Dispatch", "إرسال الرحلات"),
    ("OPS-CRW", "OPS", "Crew Scheduling", "جدولة الطواقم"),
    ("GND-RAMP", "GND", "Ramp Services", "خدمات ساحة الطائرات"), ("GND-LOAD", "GND", "Load Control", "التحكم بالحمولة"),
    ("GND-GSE", "GND", "Ground Support Equipment", "معدات الدعم الأرضي"),
    ("MNT-LINE", "MNT", "Line Maintenance", "الصيانة الخطية"), ("MNT-PLN", "MNT", "Maintenance Planning", "تخطيط الصيانة"),
    ("MNT-AVI", "MNT", "Avionics", "إلكترونيات الطيران"),
    ("CS-CHK", "CS", "Check-in and Boarding", "تسجيل الوصول والصعود"), ("CS-LNG", "CS", "Lounges", "صالات الانتظار"),
    ("CS-CC", "CS", "Contact Centre", "مركز الاتصال"),
    ("SEC-SCR", "SEC", "Passenger Screening", "تفتيش المسافرين"), ("SEC-PAT", "SEC", "Patrol and Access Control", "الدوريات والتحكم بالدخول"),
    ("FIN-ACC", "FIN", "Accounting", "المحاسبة"), ("FIN-PAY", "FIN", "Accounts Payable", "الحسابات الدائنة"), ("FIN-TRS", "FIN", "Treasury", "الخزينة"),
    ("HR-PAY", "HR", "Payroll", "الرواتب"), ("HR-REC", "HR", "Recruitment", "التوظيف"), ("HR-EMP", "HR", "Employee Relations", "علاقات الموظفين"),
    ("PRC-PUR", "PRC", "Purchasing", "المشتريات"), ("PRC-STR", "PRC", "Stores and Inventory", "المخازن والمخزون"),
    ("IT-APP", "IT", "Business Applications", "تطبيقات الأعمال"), ("IT-INF", "IT", "Infrastructure", "البنية التحتية"),
    ("IT-SUP", "IT", "Service Desk", "مكتب الخدمة"),
]
# GradeCode, name, min basic, max basic (AED / month), annual leave days
GRADES = [("G1", "Grade 1 - Entry", 2000, 3000, 22), ("G2", "Grade 2 - Operative", 2500, 4000, 22), ("G3", "Grade 3 - Skilled", 3500, 5500, 22),
          ("G4", "Grade 4 - Officer", 5000, 8000, 22), ("G5", "Grade 5 - Specialist", 7000, 11000, 26), ("G6", "Grade 6 - Senior Specialist", 9500, 14000, 26),
          ("G7", "Grade 7 - Supervisor / Lead", 12000, 18000, 26), ("G8", "Grade 8 - Manager", 16000, 26000, 30),
          ("G9", "Grade 9 - Senior Manager / Head", 24000, 36000, 30), ("G10", "Grade 10 - Executive", 35000, 55000, 30)]
# PositionCode, English, Arabic, department, section, grade, hiring weight
POSITIONS = [
    ("CEO", "Chief Executive Officer", "الرئيس التنفيذي", "EXEC", "EXEC-OFF", "G10", 0), ("EXA", "Executive Assistant", "مساعد تنفيذي", "EXEC", "EXEC-OFF", "G4", 2),
    ("LGC", "Legal Counsel", "مستشار قانوني", "EXEC", "EXEC-LEG", "G8", 2), ("CMP", "Compliance Officer", "مسؤول امتثال", "EXEC", "EXEC-LEG", "G5", 2),
    ("STM", "Strategy Manager", "مدير الاستراتيجية", "EXEC", "EXEC-STR", "G8", 1), ("BAN", "Business Analyst", "محلل أعمال", "EXEC", "EXEC-STR", "G5", 2),
    ("OCO", "Operations Controller", "مراقب عمليات", "OPS", "OPS-CTRL", "G6", 6), ("DMG", "Duty Manager", "مدير مناوب", "OPS", "OPS-CTRL", "G7", 3),
    ("HOO", "Head of Flight Operations", "رئيس العمليات الجوية", "OPS", "OPS-CTRL", "G9", 0.3), ("FDS", "Flight Dispatcher", "مرسل رحلات", "OPS", "OPS-DSP", "G5", 6),
    ("CSC", "Crew Scheduler", "مجدول طواقم", "OPS", "OPS-CRW", "G4", 5), ("CSS", "Crew Scheduling Supervisor", "مشرف جدولة الطواقم", "OPS", "OPS-CRW", "G6", 1),
    ("RMA", "Ramp Agent", "وكيل ساحة", "GND", "GND-RAMP", "G2", 10), ("SRA", "Senior Ramp Agent", "وكيل ساحة أول", "GND", "GND-RAMP", "G3", 4),
    ("RSV", "Ramp Supervisor", "مشرف ساحة", "GND", "GND-RAMP", "G5", 1.5), ("LDC", "Load Controller", "مراقب حمولة", "GND", "GND-LOAD", "G4", 4),
    ("LCS", "Load Control Supervisor", "مشرف التحكم بالحمولة", "GND", "GND-LOAD", "G6", 0.8), ("GSO", "GSE Operator", "مشغل معدات أرضية", "GND", "GND-GSE", "G3", 4),
    ("GSM", "GSE Mechanic", "ميكانيكي معدات أرضية", "GND", "GND-GSE", "G4", 2), ("GOM", "Ground Operations Manager", "مدير العمليات الأرضية", "GND", "GND-RAMP", "G8", 0.3),
    ("TB1", "Aircraft Technician (B1)", "فني طائرات (B1)", "MNT", "MNT-LINE", "G5", 6), ("MEC", "Aircraft Mechanic", "ميكانيكي طائرات", "MNT", "MNT-LINE", "G3", 5),
    ("LMS", "Line Maintenance Supervisor", "مشرف الصيانة الخطية", "MNT", "MNT-LINE", "G7", 1), ("MPL", "Maintenance Planner", "مخطط صيانة", "MNT", "MNT-PLN", "G5", 3),
    ("TRO", "Technical Records Officer", "مسؤول السجلات الفنية", "MNT", "MNT-PLN", "G4", 2), ("AVE", "Avionics Engineer (B2)", "مهندس إلكترونيات طيران (B2)", "MNT", "MNT-AVI", "G6", 3),
    ("MMG", "Maintenance Manager", "مدير الصيانة", "MNT", "MNT-LINE", "G8", 0.3),
    ("CSA", "Customer Service Agent", "وكيل خدمة عملاء", "CS", "CS-CHK", "G3", 10), ("CKS", "Check-in Supervisor", "مشرف تسجيل الوصول", "CS", "CS-CHK", "G5", 2),
    ("LNA", "Lounge Agent", "وكيل صالة", "CS", "CS-LNG", "G3", 3), ("CCA", "Contact Centre Agent", "وكيل مركز اتصال", "CS", "CS-CC", "G3", 4),
    ("CSM", "Customer Service Manager", "مدير خدمة العملاء", "CS", "CS-CHK", "G8", 0.3),
    ("SCO", "Security Officer", "ضابط أمن", "SEC", "SEC-SCR", "G2", 10), ("SCS", "Screening Supervisor", "مشرف تفتيش", "SEC", "SEC-SCR", "G4", 2),
    ("PTO", "Patrol Officer", "ضابط دورية", "SEC", "SEC-PAT", "G2", 4), ("SMG", "Security Manager", "مدير الأمن", "SEC", "SEC-PAT", "G7", 0.4),
    ("ACC", "Accountant", "محاسب", "FIN", "FIN-ACC", "G4", 4), ("SAC", "Senior Accountant", "محاسب أول", "FIN", "FIN-ACC", "G5", 2),
    ("APO", "Payables Officer", "مسؤول الحسابات الدائنة", "FIN", "FIN-PAY", "G4", 3), ("TRA", "Treasury Analyst", "محلل خزينة", "FIN", "FIN-TRS", "G5", 1.5),
    ("FMG", "Finance Manager", "مدير المالية", "FIN", "FIN-ACC", "G8", 0.4), ("CFO", "Chief Financial Officer", "المدير المالي", "FIN", "FIN-ACC", "G10", 0.1),
    ("HRO", "HR Officer", "مسؤول موارد بشرية", "HR", "HR-EMP", "G4", 3), ("PYS", "Payroll Specialist", "أخصائي رواتب", "HR", "HR-PAY", "G5", 2),
    ("PYO", "Payroll Officer", "مسؤول رواتب", "HR", "HR-PAY", "G4", 2), ("REC", "Recruiter", "أخصائي توظيف", "HR", "HR-REC", "G4", 2),
    ("HRM", "HR Manager", "مدير الموارد البشرية", "HR", "HR-EMP", "G8", 0.3),
    ("BUY", "Buyer", "مشتري", "PRC", "PRC-PUR", "G4", 4), ("SBY", "Senior Buyer", "مشتري أول", "PRC", "PRC-PUR", "G5", 1.5),
    ("STK", "Storekeeper", "أمين مخزن", "PRC", "PRC-STR", "G3", 5), ("STS", "Store Supervisor", "مشرف مخازن", "PRC", "PRC-STR", "G5", 1.5),
    ("PMG", "Procurement Manager", "مدير المشتريات", "PRC", "PRC-PUR", "G8", 0.3),
    ("DEV", "Software Developer", "مطور برمجيات", "IT", "IT-APP", "G5", 4), ("BSA", "Business Systems Analyst", "محلل أنظمة أعمال", "IT", "IT-APP", "G5", 2),
    ("SYE", "Systems Engineer", "مهندس أنظمة", "IT", "IT-INF", "G5", 3), ("NWE", "Network Engineer", "مهندس شبكات", "IT", "IT-INF", "G5", 2),
    ("SDA", "Service Desk Analyst", "محلل مكتب الخدمة", "IT", "IT-SUP", "G3", 4), ("ITM", "IT Manager", "مدير تقنية المعلومات", "IT", "IT-APP", "G8", 0),
]
HEADCOUNT = {"GND": 170, "CS": 95, "OPS": 70, "MNT": 75, "SEC": 55, "PRC": 35, "FIN": 30, "HR": 25, "IT": 28, "EXEC": 12}

NATIONALITIES = [  # code, country, Arabic, nationality, weight, name group
    ("IN", "India", "الهند", "Indian", 34, "indian"), ("PK", "Pakistan", "باكستان", "Pakistani", 14, "pakistani"),
    ("PH", "Philippines", "الفلبين", "Filipino", 12, "filipino"), ("EG", "Egypt", "مصر", "Egyptian", 8, "arab"),
    ("AE", "United Arab Emirates", "الإمارات العربية المتحدة", "Emirati", 6, "emirati"), ("BD", "Bangladesh", "بنغلاديش", "Bangladeshi", 6, "southasia"),
    ("JO", "Jordan", "الأردن", "Jordanian", 4, "arab"), ("LK", "Sri Lanka", "سريلانكا", "Sri Lankan", 4, "southasia"),
    ("NP", "Nepal", "نيبال", "Nepalese", 4, "southasia"), ("LB", "Lebanon", "لبنان", "Lebanese", 2, "arab"),
    ("SD", "Sudan", "السودان", "Sudanese", 2, "arab"), ("SY", "Syria", "سوريا", "Syrian", 2, "arab"), ("GB", "United Kingdom", "المملكة المتحدة", "British", 2, "western"),
]
NAMES = {
    "arab_m": [("Ahmed", "أحمد"), ("Mohammed", "محمد"), ("Omar", "عمر"), ("Khalid", "خالد"), ("Yousef", "يوسف"), ("Hassan", "حسن"), ("Ali", "علي"),
               ("Tariq", "طارق"), ("Ibrahim", "إبراهيم"), ("Mahmoud", "محمود"), ("Karim", "كريم"), ("Waleed", "وليد"), ("Nasser", "ناصر"), ("Sami", "سامي")],
    "arab_f": [("Fatima", "فاطمة"), ("Mariam", "مريم"), ("Aisha", "عائشة"), ("Noura", "نورة"), ("Huda", "هدى"), ("Layla", "ليلى"), ("Sara", "سارة"),
               ("Reem", "ريم"), ("Amal", "أمل"), ("Dana", "دانة"), ("Salma", "سلمى"), ("Rania", "رانيا")],
    "arab_l": [("Haddad", "حداد"), ("Khoury", "خوري"), ("Mansour", "منصور"), ("Saleh", "صالح"), ("Abdelaziz", "عبدالعزيز"), ("Farouk", "فاروق"),
               ("El Sayed", "السيد"), ("Mostafa", "مصطفى"), ("Tawfiq", "توفيق"), ("Nasr", "نصر"), ("Hamdan", "حمدان"), ("Youssef", "يوسف"), ("Darwish", "درويش")],
    "emirati_m": [("Rashid", "راشد"), ("Hamad", "حمد"), ("Saeed", "سعيد"), ("Sultan", "سلطان"), ("Majed", "ماجد"), ("Abdullah", "عبدالله"), ("Faisal", "فيصل"), ("Mohammed", "محمد")],
    "emirati_f": [("Hessa", "حصة"), ("Mariam", "مريم"), ("Shamma", "شمة"), ("Moza", "موزة"), ("Alia", "علياء"), ("Noura", "نورة")],
    "emirati_l": [("Al Mansoori", "المنصوري"), ("Al Hammadi", "الحمادي"), ("Al Shamsi", "الشامسي"), ("Al Suwaidi", "السويدي"), ("Al Ali", "العلي"),
                  ("Al Nuaimi", "النعيمي"), ("Al Ketbi", "الكتبي"), ("Al Marzooqi", "المرزوقي")],
    "indian_m": ["Rahul", "Arjun", "Vijay", "Suresh", "Ramesh", "Anil", "Manoj", "Sanjay", "Rajesh", "Deepak", "Ajith", "Nikhil", "Sreejith", "Faizal", "Shafeeq", "Vivek"],
    "indian_f": ["Priya", "Anjali", "Divya", "Lakshmi", "Sneha", "Kavya", "Reshma", "Neha", "Asha", "Meera"],
    "indian_l": ["Nair", "Menon", "Kumar", "Pillai", "Sharma", "Patel", "Reddy", "Iyer", "Thomas", "George", "Varghese", "Joseph", "Rao", "Das"],
    "pakistani_m": ["Imran", "Bilal", "Usman", "Hamza", "Asif", "Kashif", "Adnan", "Zeeshan", "Farhan", "Waqas"],
    "pakistani_f": ["Ayesha", "Sana", "Hina", "Mehwish", "Saima"],
    "pakistani_l": ["Khan", "Malik", "Hussain", "Chaudhry", "Qureshi", "Butt", "Raza", "Iqbal", "Siddiqui"],
    "filipino_m": ["Mark", "John", "Jerome", "Carlo", "Paolo", "Ramon", "Joel", "Christian"],
    "filipino_f": ["Maria", "Jennifer", "Kristine", "Angelica", "Rowena", "Grace", "Joy"],
    "filipino_l": ["Santos", "Reyes", "Cruz", "Bautista", "Garcia", "Mendoza", "Dela Cruz", "Villanueva", "Ramos"],
    "southasia_m": ["Rahim", "Sujan", "Bikash", "Nuwan", "Kasun", "Shahidul", "Prakash", "Roshan"],
    "southasia_f": ["Nasrin", "Sabina", "Dilani", "Sita", "Tharushi"],
    "southasia_l": ["Hossain", "Rahman", "Islam", "Shrestha", "Gurung", "Tamang", "Perera", "Fernando", "Silva"],
    "western_m": ["James", "David", "Michael", "Daniel", "Thomas"], "western_f": ["Sarah", "Emma", "Laura", "Hannah"],
    "western_l": ["Smith", "Wilson", "Taylor", "Brown", "Evans", "Clarke"],
}
SHIFTS = [("DAY", "Day shift", "الوردية الصباحية", "07:00", "15:00"), ("EVE", "Evening shift", "الوردية المسائية", "15:00", "23:00"),
          ("NGT", "Night shift", "الوردية الليلية", "23:00", "07:00"), ("OFF", "Office hours", "ساعات المكتب", "08:00", "17:00")]
BANKS = ["Emirates Islamic Bank", "Sharjah Islamic Bank", "Abu Dhabi Commercial Bank", "Mashreq Bank", "First Abu Dhabi Bank", "RAKBANK", "Commercial Bank of Dubai"]

# The four people who also get Wabi Insight accounts in 04-seed-wabiinsightdb.sql (keep in sync).
SEED_PEOPLE = [
    # number, first, last, first Ar, last Ar, gender, nationality, position, birth, hire, wabi role
    ("E1001", "Faisal", "Al Shamsi", "فيصل", "الشامسي", "M", "AE", "ITM", "1984-05-12", "2016-03-01", "OrganizationAdmin"),
    ("E1002", "Khalid", "Al Suwaidi", "خالد", "السويدي", "M", "AE", "CEO", "1972-11-03", "2012-01-15", "Management"),
    ("E1003", "Arjun", "Nair", None, None, "M", "IN", "OCO", "1990-08-21", "2019-06-02", "OperationsAnalyst"),
    ("E1004", "Sara", "Khoury", "سارة", "خوري", "F", "JO", "PYS", "1992-02-14", "2020-09-13", "HRPayrollOfficer"),
]


def email(first, last, n=None):
    base = f"{first}.{last}".lower().replace(" ", "").replace("'", "")
    return f"{base}{n or ''}@{ORG_DOMAIN}"


def staff():
    """Deterministic staff list: dicts with every attribute both databases need."""
    r = rng(1984)
    pos_by_code = {p[0]: p for p in POSITIONS}
    grade = {g[0]: g for g in GRADES}
    shift_based = {d[0]: d[4] for d in DEPARTMENTS}
    people, used_emails = [], set()

    def salary(grade_code):
        g = grade[grade_code]
        return round((g[2] + r.random() * (g[3] - g[2])) / 50) * 50

    def base_row(number, first, last, first_ar, last_ar, gender, nat, pos_code, dob, hire, term=None, reason=None):
        p = pos_by_code[pos_code]
        e = email(first, last)
        n = 2
        while e in used_emails:
            e = email(first, last, n)
            n += 1
        used_emails.add(e)
        shift = (r.choice(["DAY", "DAY", "EVE", "NGT"]) if shift_based[p[3]] and p[5] in ("G2", "G3", "G4", "G5", "G6") else "OFF")
        off_start = r.randint(1, 7) if shift != "OFF" else 6
        return {
            "EmployeeNumber": number, "FirstName": first, "LastName": last, "FullName": f"{first} {last}",
            "FullNameAr": f"{first_ar} {last_ar}" if first_ar and last_ar else None, "Gender": gender, "DateOfBirth": dob,
            "NationalityCode": nat, "MaritalStatus": r.choice(["Single", "Married", "Married", "Married"]), "HireDate": hire,
            "TerminationDate": term, "TerminationReason": reason, "EmploymentStatus": "Active" if term is None else ("Resigned" if reason == "Resignation" else "Terminated"),
            "EmploymentType": "Fixed-term" if r.random() < 0.15 else "Permanent", "DepartmentCode": p[3], "SectionCode": p[4], "PositionCode": pos_code,
            "GradeCode": p[5], "ShiftCode": shift, "WeeklyOffDays": f"{off_start}{off_start % 7 + 1}", "WorkEmail": e,
            "MobileNumber": f"+971 5{r.choice('024568')} {r.randint(1000000, 9999999)}",
            "WorkLocation": "Head Office" if not shift_based[p[3]] else r.choice(["Terminal 1", "Terminal 1", "Terminal 2", "Airside"]),
            "NationalIdNumber": f"784-{dob[:4]}-{r.randint(1000000, 9999999)}-{r.randint(0, 9)}",
            "PassportNumber": f"{r.choice('ABCDEFGHJKLMNPRSTUVWXYZ')}{r.choice('ABCDEFGHJKLMNPRSTUVWXYZ')}{r.randint(1000000, 9999999)}",
            "BasicSalary": salary(p[5]), "BankName": r.choice(BANKS),
            "IBAN": "AE" + f"{r.randint(10, 99)}" + "".join(str(r.randint(0, 9)) for _ in range(19)),
        }

    for s in SEED_PEOPLE:
        people.append(base_row(s[0], s[1], s[2], s[3], s[4], s[5], s[6], s[7], s[8], s[9]))

    nat_weights = [(n, n[4]) for n in NATIONALITIES]
    number = 1100
    for dept, count in HEADCOUNT.items():
        positions = [(p, p[6]) for p in POSITIONS if p[3] == dept and p[6] > 0]
        for _ in range(count):
            number += 1
            p = _pick(r, positions)
            nat = _pick(r, nat_weights)
            group = nat[5]
            female = r.random() < (0.45 if dept in ("CS", "HR", "FIN") else 0.12 if dept in ("GND", "MNT", "SEC") else 0.3)
            gk = "f" if female else "m"
            if group in ("arab", "emirati"):
                first, first_ar = r.choice(NAMES[f"{group}_{gk}"])
                last, last_ar = r.choice(NAMES[f"{group}_l"])
            else:
                first, first_ar = r.choice(NAMES[f"{group}_{gk}"]), None
                last, last_ar = r.choice(NAMES[f"{group}_l"]), None
            grade_n = int(p[5][1:])
            age = min(60, max(21, int(22 + grade_n * 2.8 + r.gauss(0, 5))))
            dob = dt.date(2025 - age, r.randint(1, 12), r.randint(1, 28))
            earliest = max(dt.date(2008, 1, 1), dob.replace(year=dob.year + 21))
            if r.random() < 0.11:
                hire = dt.date(2025, r.randint(1, 11), r.randint(1, 28))
            else:
                span = (dt.date(2024, 12, 31) - earliest).days
                hire = earliest + dt.timedelta(days=int(span * (r.random() ** 0.7))) if span > 0 else earliest
            term = reason = None
            if r.random() < 0.09 and hire < dt.date(2025, 6, 1):
                term = dt.date(2025, r.randint(2, 12), r.randint(1, 28))
                reason = r.choice(["Resignation", "Resignation", "Resignation", "End of contract", "Termination"])
            people.append(base_row(f"E{number}", first, last, first_ar, last_ar, "F" if female else "M", nat[0], p[0], dob.isoformat(), hire.isoformat(),
                                   term.isoformat() if term else None, reason))
    # Every department gets a manager-grade employee (the first person hired into it becomes its manager position).
    for dept in HEADCOUNT:
        if not any(x["DepartmentCode"] == dept and int(x["GradeCode"][1:]) >= 7 and x["EmploymentStatus"] == "Active" for x in people):
            manager = max((q for q in POSITIONS if q[3] == dept and q[0] not in ("CEO", "CFO")), key=lambda q: int(q[5][1:]))
            first = next(x for x in people if x["DepartmentCode"] == dept and x["EmployeeNumber"] not in [s[0] for s in SEED_PEOPLE] and x["EmploymentStatus"] == "Active")
            first.update({"PositionCode": manager[0], "SectionCode": manager[4], "GradeCode": manager[5], "ShiftCode": "OFF", "WeeklyOffDays": "67", "BasicSalary": salary(manager[5])})
    # Department managers: the highest-graded active employee of each department; everyone else reports to them.
    heads = {}
    for p in people:
        if p["EmploymentStatus"] == "Active":
            cur = heads.get(p["DepartmentCode"])
            if cur is None or int(p["GradeCode"][1:]) > int(cur["GradeCode"][1:]):
                heads[p["DepartmentCode"]] = p
    ceo = people[1]
    for p in people:
        head = heads[p["DepartmentCode"]]
        p["ManagerEmployeeNumber"] = ceo["EmployeeNumber"] if p is head else head["EmployeeNumber"]
    ceo["ManagerEmployeeNumber"] = None
    return people, {d: h["EmployeeNumber"] for d, h in heads.items()}


def _pick(r, weighted):
    total = sum(w for _, w in weighted)
    x = r.random() * total
    for item, w in weighted:
        x -= w
        if x <= 0:
            return item
    return weighted[-1][0]
