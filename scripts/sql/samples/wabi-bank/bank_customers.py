"""Wabi Bank customer domain shared by Core Banking and Digital Banking: customers (CIF), accounts, fixed deposits, loans
and loan applications, digital profiles, beneficiaries and channel transfers. Deterministic; every staff action is taken
by a user who had the right access and was at work that day (bank_people.staff_on)."""
import datetime as dt
import math
from functools import lru_cache

import bank_names
import bank_people as bp
from build_common import rng

BRANCH_NUM = {"BR-DXB01": "101", "BR-DXB02": "102", "BR-AUH01": "201", "BR-AAN01": "202", "BR-SHJ01": "301", "BR-RAK01": "401"}
BANK_CODE = "095"  # fictional
OTHER_BANKS = [  # fictional banks used as counterparties: code, name
    ("021", "Union Coast Bank"), ("033", "Crescent Harbour Bank"), ("044", "Gateway Merchant Bank"), ("046", "Seashore Bank"),
    ("051", "Desert Pearl Bank"), ("057", "Union Trust Bank"), ("062", "Palm Coast Bank"), ("071", "Oasis National Bank"),
]
FOREIGN_BANKS = [("IN", "Bank of Kerala Coast"), ("PK", "Indus Valley Bank"), ("PH", "Manila Harbour Bank"), ("EG", "Nile Delta Bank"),
                 ("GB", "Thames Merchant Bank"), ("JO", "Petra Commercial Bank"), ("LB", "Cedar Trust Bank"), ("US", "Hudson River Bank")]
COMPANY_A = ["Northfield", "Brightstar", "Desert Rose", "Wide Horizon", "Blue Wave", "Falcon", "Oasis", "Pearl Coast", "Sahara", "Silver Palm",
             "Crescent", "Clearwater", "Golden Sands", "Grand Dune", "Marina", "Creekside", "Greenleaf", "Summit", "Bright Path", "Zenith", "Countryside",
             "Coral Bay", "Dune Valley", "Skyline", "Harbour Gate", "Island Line", "Palm Grove", "New Dawn", "Twin Oaks", "Sea Breeze"]
COMPANY_B = ["Trading", "General Trading", "Contracting", "Logistics", "Restaurants", "Technology", "Real Estate", "Auto Parts", "Medical Supplies",
             "Textiles", "Electronics", "Foodstuff", "Building Materials", "Travel and Tourism", "Engineering Consultants", "Facilities Management",
             "Printing and Packaging", "Furniture", "Jewellery", "Laundry Services"]
COMPANY_C = ["LLC", "L.L.C.", "FZE", "FZCO", "Est."]
EXTERNAL_EMPLOYERS = ["Dubai Government Department", "Abu Dhabi Government Department", "Sharjah Government Department", "Federal Government Ministry",
                      "Private Hospital Group", "International School", "Hotel and Resort", "Airline", "Telecom Operator", "Oil and Gas Company",
                      "Construction Group", "Retail Group", "University", "Logistics Group", "Free Zone Company"]
CBS_GO_LIVE = bp.GO_LIVE["CBS"]
DIG_GO_LIVE = bp.GO_LIVE["DIGITAL"]


def is_business_day(day):
    return day.isoweekday() <= 5 and day not in bp.HOLIDAYS


def next_business_day(day):
    while not is_business_day(day):
        day += dt.timedelta(days=1)
    return day


@lru_cache(maxsize=None)
def business_days_2025():
    return [x for x in bp.DAYS_2025 if is_business_day(x)]


def iban(account_number):
    bban = BANK_CODE + account_number.rjust(16, "0")
    digits = "".join(str(int(ch, 36)) for ch in bban + "AE00")
    check = 98 - int(digits) % 97
    return f"AE{check:02d}{bban}"


def _actor(day, unit, roles, r, levels=None):
    """A user of `unit` with CBS `roles` who is at work on `day`; None when nobody is."""
    people = bp.staff_on(day, unit, system="CBS", roles=roles, levels=levels)
    return r.choice(people) if people else None


def actor_on_or_after(day, unit, roles, r, system="CBS", limit=None, exclude=None):
    """The first business day from `day` when someone of the unit with the role is at work; returns (person, day).
    `exclude` is the employee number of the customer when the customer is a member of staff: nobody processes their own
    customer record, account, deposit or loan.

    Before 2025 only today's staff are known, so history falls back to anyone of the section, then of the department,
    with access to the system (the people who worked there earlier and left are not part of the sample)."""
    tries = [(unit, roles)]
    if day < bp.Y0:
        tries += [(unit, None), (unit.rsplit("-", 1)[0] if unit.count("-") >= 2 else unit, None)]
    for u, rl in tries:
        x = day
        for _ in range(30 if day >= bp.Y0 else 400):
            if is_business_day(x):
                people = [p for p in bp.staff_on(x, u, system=system, roles=rl) if p["EmployeeNumber"] != exclude
                          and (limit is None or (bp.access_row(p["UserId"], system, x)["ApprovalLimitAed"] or 0) >= limit)]
                if people:
                    return r.choice(people), x
            x += dt.timedelta(days=1)
    raise RuntimeError((unit, roles, day))


@lru_cache(maxsize=None)
def customers():
    r = rng(4242)
    emps = bp.employees()
    out = []
    n = 100000

    def onboard_actor(branch, day, staff=None):
        if day < CBS_GO_LIVE:
            return "SYS_MIGRATION", day
        p, day = actor_on_or_after(day, f"{branch}-CSV", {"CustomerService", "CustomerServiceManager"}, r, exclude=staff)
        return p["UserId"], day

    def rm_for(branch, segment, day):
        if segment == "Corporate":
            pool = [p for p in emps if p["SectionCode"] == "CBD-CRM" and p["Level"] == "Staff" and bp.employed(p, bp.Y1)]
        elif segment == "SME":
            pool = [p for p in emps if p["SectionCode"] == "CBD-SME" and p["Level"] == "Staff" and bp.employed(p, bp.Y1)]
        elif segment == "Priority":
            pool = [p for p in emps if p["SectionCode"] == f"{branch}-SLS" and p["Title"] == "Relationship Manager" and bp.employed(p, bp.Y1)]
            pool = pool or [p for p in emps if p["SectionCode"] == f"{branch}-SLS" and bp.employed(p, bp.Y1)]
        else:
            return None
        return r.choice(pool)["EmployeeNumber"] if pool else None

    # Staff customers: every employee's salary is paid into a Wabi Bank account.
    for p in emps:
        n += 1
        branch = p["DepartmentCode"] if bp.is_branch(p["DepartmentCode"]) else ("BR-DXB01" if int(p["EmployeeNumber"][1:]) % 2 else "BR-DXB02")
        start = max(p["HireDate"], bp.LOC_OPENED[branch])
        uid, day = onboard_actor(branch, start, staff=p["EmployeeNumber"])
        grade = next(g for g in bp.GRADES if g[0] == p["GradeCode"])
        out.append({"CIF": f"C{n}", "CustomerType": "Individual", "Segment": "Staff", "FullName": p["FullName"], "FullNameAr": p["FullNameAr"],
                    "Gender": p["Gender"], "DateOfBirth": p["DateOfBirth"], "NationalityCode": p["NationalityCode"], "MobileNumber": p["MobileNumber"],
                    "RiskRating": "Low", "IsPep": False, "HomeBranchCode": branch, "RelationshipManagerEmployeeNumber": None, "OnboardedDate": day,
                    "CreatedByUserId": uid, "IsStaff": True, "StaffEmployeeNumber": p["EmployeeNumber"], "EmployerName": "Wabi Bank",
                    "MonthlySalary": round((grade[3] + r.random() * (grade[4] - grade[3])) / 100) * 100, "Employee": p})
    # Retail customers.
    groups = [(c, c[4]) for c in bp.COUNTRIES] + [(("IN", "India", "", "", 0, "indian"), 10), (("PH", "Philippines", "", "", 0, "filipino"), 4)]
    branch_weights = [("BR-DXB01", 26), ("BR-AUH01", 22), ("BR-SHJ01", 18), ("BR-DXB02", 14), ("BR-AAN01", 11), ("BR-RAK01", 9)]
    for _ in range(4800):
        n += 1
        nat = bp._pick(r, groups)
        female = r.random() < 0.38
        (first, first_ar), (last, last_ar) = bank_names.pick(r, nat[5], female)
        name_ar = f"{first_ar} {last_ar}"
        branch = bp._pick(r, branch_weights)
        age = int(22 + (r.random() ** 1.4) * 50)
        dob = dt.date(2025 - age, r.randint(1, 12), r.randint(1, 28))
        lo = max(bp.LOC_OPENED[branch], dob.replace(year=dob.year + 21), dt.date(2008, 3, 2))
        start = dt.date(2025, r.randint(1, 12), r.randint(1, 28)) if r.random() < 0.07 else lo + dt.timedelta(days=int(r.random() ** 0.8 * max(1, (dt.date(2024, 12, 31) - lo).days)))
        start = min(start, dt.date(2025, 12, 20))
        uid, day = onboard_actor(branch, start)
        segment = "Priority" if r.random() < 0.11 else "Mass"
        salaried = r.random() < 0.72
        salary = (round(r.lognormvariate(math.log(14000 if segment == "Mass" else 52000), 0.45) / 100) * 100) if salaried else None
        employer = (r.choice(EXTERNAL_EMPLOYERS) if r.random() < 0.6 else None) if salaried else None
        risk = "High" if r.random() < 0.03 else ("Medium" if r.random() < 0.22 else "Low")
        out.append({"CIF": f"C{n}", "CustomerType": "Individual", "Segment": segment, "FullName": f"{first} {last}", "FullNameAr": name_ar, "Gender": "F" if female else "M",
                    "DateOfBirth": dob, "NationalityCode": nat[0], "MobileNumber": f"+971 5{r.choice('024568')} {r.randint(1000000, 9999999)}",
                    "RiskRating": risk, "IsPep": risk == "High" and r.random() < 0.3, "HomeBranchCode": branch,
                    "RelationshipManagerEmployeeNumber": rm_for(branch, segment, day), "OnboardedDate": day, "CreatedByUserId": uid, "IsStaff": False,
                    "StaffEmployeeNumber": None, "EmployerName": employer, "MonthlySalary": salary})
    # SME and corporate customers.
    used = set()
    for kind, count in (("SME", 520), ("Corporate", 180)):
        for _ in range(count):
            n += 1
            while True:
                name = f"{r.choice(COMPANY_A)} {r.choice(COMPANY_B)} {r.choice(COMPANY_C)}"
                if name not in used:
                    used.add(name)
                    break
            branch = bp._pick(r, [("BR-DXB01", 30), ("BR-DXB02", 25), ("BR-AUH01", 22), ("BR-SHJ01", 13), ("BR-AAN01", 5), ("BR-RAK01", 5)])
            lo = max(bp.LOC_OPENED[branch], dt.date(2008, 3, 2))
            start = lo + dt.timedelta(days=int(r.random() * max(1, (dt.date(2025, 11, 30) - lo).days)))
            uid, day = onboard_actor(branch, start)
            out.append({"CIF": f"C{n}", "CustomerType": kind, "Segment": kind, "FullName": name, "FullNameAr": None, "Gender": None,
                        "DateOfBirth": None, "IncorporationDate": day - dt.timedelta(days=r.randint(200, 4000)), "NationalityCode": "AE",
                        "MobileNumber": f"+971 4 {r.randint(1000000, 9999999)}", "RiskRating": "Medium" if r.random() < 0.35 else ("High" if r.random() < 0.06 else "Low"),
                        "IsPep": False, "HomeBranchCode": branch, "RelationshipManagerEmployeeNumber": rm_for(branch, kind, day), "OnboardedDate": day,
                        "CreatedByUserId": uid, "IsStaff": False, "StaffEmployeeNumber": None, "EmployerName": None, "MonthlySalary": None,
                        "Turnover": round(r.lognormvariate(math.log(4_000_000 if kind == "SME" else 60_000_000), 0.6), -3)})
    corporates = [c["FullName"] for c in out if c["CustomerType"] != "Individual"]
    for c in out:
        if c["CustomerType"] == "Individual" and c["MonthlySalary"] and c["EmployerName"] is None:
            c["EmployerName"] = r.choice(corporates)
    return out


@lru_cache(maxsize=None)
def accounts():
    """Accounts with their 2025 activity profile (the profile drives the T-SQL transaction generator)."""
    r = rng(5151)
    out = []
    seq = {b: 0 for b in bp.BRANCH_CODES}

    def open_account(c, product, currency, opened, kind):
        b = c["HomeBranchCode"]
        seq[b] += 1
        number = f"{BRANCH_NUM[b]}{seq[b]:09d}"
        if opened < CBS_GO_LIVE:
            opener = approver = "SYS_MIGRATION"
            day = opened
        else:
            staff = c.get("StaffEmployeeNumber")
            p, day = actor_on_or_after(opened, f"{b}-CSV", {"CustomerService"}, r, exclude=staff)
            opener = p["UserId"]
            checkers = [q for q in bp.staff_on(day, f"{b}-CSV", "CBS", {"CustomerServiceManager"}) + bp.staff_on(day, f"{b}-OPS", "CBS", {"BranchOperationsManager"})
                        if q["UserId"] != opener and q["EmployeeNumber"] != staff]
            approver = (r.choice(checkers) if checkers else actor_on_or_after(day, f"{b}-OPS", {"BranchOperationsManager", "BranchOperations"}, r, exclude=staff)[0])["UserId"]
        a = {"AccountNumber": number, "IBAN": iban(number), "CIF": c["CIF"], "ProductCode": product, "CurrencyCode": currency, "BranchCode": b,
             "OpenedDate": day, "OpenedByUserId": opener, "ApprovedByUserId": approver, "Status": "Active", "DormantSince": None, "ReactivatedDate": None,
             "ReactivatedByUserId": None, "ClosedDate": None, "Kind": kind, "Customer": c}
        out.append(a)
        return a

    for c in customers():
        start = c["OnboardedDate"]
        if c["CustomerType"] == "Individual":
            salaried = bool(c["MonthlySalary"])
            main = open_account(c, "CA-SAL" if salaried else ("CA-STD" if r.random() < 0.4 else "SA-STD"), "AED", start, "salary" if salaried else "retail")
            if r.random() < (0.35 if c["Segment"] != "Mass" else 0.22):
                open_account(c, "SA-STD", "AED", min(start + dt.timedelta(days=r.randint(30, 900)), dt.date(2025, 12, 15)), "savings")
            if c["Segment"] == "Priority" and r.random() < 0.35:
                open_account(c, "SA-USD", "USD", min(start + dt.timedelta(days=r.randint(30, 900)), dt.date(2025, 12, 15)), "savings")
            if not c["IsStaff"] and not salaried and r.random() < 0.14 and main["OpenedDate"] < dt.date(2023, 6, 1):
                main["Status"], main["DormantSince"] = "Dormant", dt.date(2022, 1, 1) + dt.timedelta(days=r.randint(0, 900))
            elif not c["IsStaff"] and r.random() < 0.03 and main["OpenedDate"] < dt.date(2024, 6, 1):
                main["Status"], main["ClosedDate"] = "Closed", next_business_day(dt.date(2025, r.randint(2, 11), r.randint(1, 28)))
        else:
            open_account(c, "CA-BUS", "AED", start, c["CustomerType"].lower())
            if r.random() < (0.55 if c["CustomerType"] == "Corporate" else 0.25):
                open_account(c, "CA-BUSUSD", "USD", min(start + dt.timedelta(days=r.randint(20, 700)), dt.date(2025, 12, 1)), c["CustomerType"].lower())
            if c["CustomerType"] == "Corporate" and r.random() < 0.4:
                open_account(c, "CA-BUS", "AED", min(start + dt.timedelta(days=r.randint(20, 700)), dt.date(2025, 12, 1)), "corporate")
    # The planted dormant account that is reactivated in November and used to pass a large SWIFT transfer through.
    dormant = next(a for a in out if a["Status"] == "Dormant" and a["CurrencyCode"] == "AED" and a["BranchCode"] == "BR-AUH01" and a["DormantSince"] < dt.date(2023, 6, 1))
    p, day = actor_on_or_after(dt.date(2025, 11, 11), "OPS-ACS", {"AccountServices"}, r)
    dormant.update({"Status": "Active", "ReactivatedDate": day, "ReactivatedByUserId": p["UserId"], "Planted": "dormant_reactivated"})
    # Activity profile per account (monthly rates; amounts in account currency).
    for a in out:
        kind = a["Kind"]
        c = a["Customer"]
        a["Key"] = len(a["AccountNumber"]) and int(a["AccountNumber"])
        a["SalaryAmount"] = c["MonthlySalary"] if kind == "salary" else None
        a["SalaryDay"] = r.choice([25, 26, 27, 28]) if kind == "salary" else None
        a["EmployerName"] = c["EmployerName"] if kind == "salary" else None
        active = a["Status"] in ("Active", "Closed") and not a["ReactivatedDate"]
        # Staff salary accounts receive the salary only while the employee is employed.
        emp = c.get("Employee")
        a["SalaryFrom"] = max(emp["HireDate"], bp.Y0) if emp and kind == "salary" else None
        a["SalaryTo"] = emp["TerminationDate"] if emp and kind == "salary" else None
        a["AtmPerMonth"] = (r.choice([1, 2, 2, 3, 4]) if kind in ("salary", "retail") else 0) if active else 0
        a["PosPerMonth"] = (r.choice([0, 1, 1, 2, 3]) if kind in ("salary", "retail") else 0) if active else 0
        # Branch counters: individuals come in a couple of times a month for small amounts, businesses weekly.
        a["TellerPerMonth"] = ({"salary": 2.0, "retail": 2.0, "savings": 0.8, "sme": 5.0, "corporate": 6.0}[kind]) if active else 0
        a["ChequeInPerMonth"] = ({"sme": 1.2, "corporate": 2.5, "salary": 0.05, "retail": 0.05}.get(kind, 0)) if active and a["CurrencyCode"] == "AED" else 0
        a["ChequeOutPerMonth"] = ({"sme": 1.0, "corporate": 2.0, "salary": 0.08}.get(kind, 0)) if active and a["ProductCode"].startswith("CA") and a["CurrencyCode"] == "AED" else 0
        a["TransferOutPerMonth"] = ({"sme": 0.8, "corporate": 1.6, "salary": 0.04}.get(kind, 0)) if active else 0
        a["TransferInPerMonth"] = ({"sme": 0.9, "corporate": 1.8, "salary": 0.03, "savings": 0.03}.get(kind, 0)) if active else 0
        scale = {"salary": (c["MonthlySalary"] or 10000) / 4, "retail": 3000, "savings": 6000, "sme": 45000, "corporate": 260000}[kind]
        a["AmountScale"] = round(scale if a["CurrencyCode"] == "AED" else scale / 3.6725, 2)
    return out


@lru_cache(maxsize=None)
def fixed_deposits():
    r = rng(6161)
    out = []
    n = 0
    funding = [a for a in accounts() if a["Status"] == "Active" and a["CurrencyCode"] == "AED" and a["Kind"] in ("salary", "savings", "corporate", "sme")]
    for a in [funding[i] for i in sorted(r.sample(range(len(funding)), 900))]:
        n += 1
        tenor = r.choice([3, 6, 6, 12, 12, 24])
        if r.random() < 0.45:
            start = next_business_day(dt.date(2025, r.randint(1, 12), r.randint(1, 26)))
        else:
            start = next_business_day(dt.date(2025, 1, 1) - dt.timedelta(days=r.randint(10, tenor * 30)))
        start = max(start, a["OpenedDate"])
        principal = round(r.lognormvariate(math.log(250000 if a["Kind"] in ("corporate", "sme") else 90000), 0.6), -3)
        rate = round({3: 3.6, 6: 3.9, 12: 4.15, 24: 4.25}[tenor] + r.uniform(-0.2, 0.2), 2)
        maturity = start + dt.timedelta(days=round(tenor * 30.42))
        b = a["BranchCode"]
        if start < CBS_GO_LIVE:
            booker, day = "SYS_MIGRATION", start
        else:
            p, day = actor_on_or_after(start, f"{b}-CSV", {"CustomerService", "CustomerServiceManager"}, r, exclude=a["Customer"].get("StaffEmployeeNumber"))
            booker = p["UserId"]
        out.append({"DepositNumber": f"FD{BRANCH_NUM[b]}{n:06d}", "CIF": a["CIF"], "BranchCode": b, "FundingAccountNumber": a["AccountNumber"],
                    "Principal": principal, "InterestRate": rate, "TenorMonths": tenor, "StartDate": day, "MaturityDate": maturity,
                    "InterestAtMaturity": round(principal * rate / 100 * tenor / 12, 2), "RenewalInstruction": r.choice(["Renew principal", "Renew principal and interest", "Credit to account"]),
                    "Status": "Matured" if maturity <= bp.Y1 else "Active", "BookedByUserId": booker})
    return out


LOAN_PRODUCTS = [  # code, name, Arabic, rate range, tenor months, amount range
    ("PL", "Personal loan", "قرض شخصي", (5.5, 9.5), (12, 60), (20000, 350000)),
    ("AL", "Auto loan", "قرض سيارة", (3.2, 5.9), (24, 60), (40000, 400000)),
    ("ML", "Home loan", "قرض سكني", (3.9, 5.2), (120, 300), (600000, 4500000)),
    ("SL", "SME working capital loan", "قرض رأس مال عامل للمؤسسات", (6.5, 10.5), (12, 48), (100000, 3000000)),
]


@lru_cache(maxsize=None)
def loans():
    """2025 applications (approved, rejected, withdrawn) and the loan book (earlier loans plus 2025 disbursements)."""
    r = rng(7171)
    custs = customers()
    accts = {}
    for a in accounts():
        if a["Status"] == "Active" and a["CurrencyCode"] == "AED" and a["Kind"] in ("salary", "retail", "sme", "corporate"):
            accts.setdefault(a["CIF"], a)
    eligible_ind = [c for c in custs if c["CustomerType"] == "Individual" and c["CIF"] in accts and c["MonthlySalary"]]
    eligible_sme = [c for c in custs if c["CustomerType"] == "SME" and c["CIF"] in accts]
    prod = {p[0]: p for p in LOAN_PRODUCTS}
    apps, book = [], []
    planted_over_limit = 2

    def amount_for(code):
        lo, hi = prod[code][5]
        return round(math.exp(math.log(lo) + r.random() * (math.log(hi) - math.log(lo))), -3)

    def pick_customer(code):
        return r.choice(eligible_sme if code == "SL" else eligible_ind)

    def product_code():
        return bp._pick(r, [("PL", 55), ("AL", 25), ("ML", 7), ("SL", 13)])

    # Earlier loans still on the book in 2025.
    for _ in range(720):
        code = product_code()
        c = pick_customer(code)
        tenor = r.randint(*prod[code][4])
        start = dt.date(2025, 1, 1) - dt.timedelta(days=r.randint(30, min(tenor * 30 - 15, 1800)))
        start = max(start, c["OnboardedDate"] + dt.timedelta(days=30), dt.date(2019, 6, 1))
        if start >= dt.date(2025, 1, 1):
            continue
        book.append({"ApplicationId": None, "CIF": c["CIF"], "ProductCode": code, "BranchCode": c["HomeBranchCode"], "Principal": amount_for(code),
                     "InterestRate": round(r.uniform(*prod[code][3]), 2), "TenorMonths": tenor, "StartDate": start,
                     "RepaymentAccountNumber": accts[c["CIF"]]["AccountNumber"], "DisbursedByUserId": "SYS_MIGRATION" if start < CBS_GO_LIVE else None,
                     "DisbursementApprovedByUserId": None})
    # 2025 applications.
    for i in range(640):
        code = product_code()
        c = pick_customer(code)
        b = c["HomeBranchCode"]
        day = next_business_day(dt.date(2025, 1, 2) + dt.timedelta(days=int(r.random() * 345)))
        amount = amount_for(code)
        staff = c.get("StaffEmployeeNumber")
        if code == "SL":
            src = [p for p in bp.staff_on(day, "CBD-SME", "CBS", {"SmeRM"})]
            sourcer, day = (r.choice(src), day) if src else actor_on_or_after(day, "CBD-SME", {"SmeRM"}, r)
        else:
            sourcer, day = actor_on_or_after(day, f"{b}-SLS", {"SalesOfficer", "SalesManager"}, r, exclude=staff)
        status = "Approved"
        x = r.random()
        if x < 0.22:
            status = "Rejected"
        elif x < 0.27:
            status = "Withdrawn"
        decision_day = next_business_day(day + dt.timedelta(days=r.randint(2, 9)))
        decider = None
        planted = None
        if status != "Withdrawn":
            if status == "Approved" and planted_over_limit and 250000 < amount <= 600000 and code != "ML":
                decider, decision_day = actor_on_or_after(decision_day, "RSK-CRA", {"CreditOfficer"}, r, exclude=staff)
                planted_over_limit -= 1
                planted = "approved_above_limit"
            else:
                decider, decision_day = actor_on_or_after(decision_day, "RSK-CRA", {"CreditOfficer", "HeadOfCredit"}, r, limit=amount if amount <= 3000000 else None,
                                                          exclude=staff)
                if amount > (bp.access_row(decider["UserId"], "CBS", decision_day)["ApprovalLimitAed"] or 0):
                    decider, decision_day = actor_on_or_after(decision_day, "RSK", {"ChiefRiskOfficer"}, r, exclude=staff)
        app = {"ApplicationId": i + 1, "ApplicationNumber": f"LA25{i + 1:05d}", "CIF": c["CIF"], "ProductCode": code, "BranchCode": b,
               "SourcingUnitCode": "CBD" if code == "SL" else b, "SourcedByEmployeeNumber": sourcer["EmployeeNumber"], "CreatedByUserId": sourcer["UserId"],
               "AppliedDate": day, "RequestedAmount": amount, "TenorMonths": r.randint(*prod[code][4]), "Status": status,
               "DecidedByUserId": decider["UserId"] if decider else None, "DecisionDate": decision_day if decider else None,
               "RejectionReason": r.choice(["Debt burden ratio above 50%", "Insufficient salary history", "Negative credit bureau record", "Employer not approved",
                                            "Incomplete documents"]) if status == "Rejected" else None, "Planted": planted}
        apps.append(app)
        if status == "Approved":
            disb_day = next_business_day(decision_day + dt.timedelta(days=r.randint(1, 6)))
            if disb_day > dt.date(2025, 12, 30):
                app["Status"] = "Approved"
                continue
            maker, disb_day = actor_on_or_after(disb_day, "OPS-LNO", {"LoanOperations"}, r, exclude=staff)
            checker, _ = actor_on_or_after(disb_day, "OPS-LNO", {"LoanOperationsManager"}, r, exclude=staff)
            if checker["UserId"] == maker["UserId"] or _ != disb_day:
                checker = bp.staff_on(disb_day, "OPS", "CBS", {"OperationsHead"}) or [checker]
                checker = checker[0]
            book.append({"ApplicationId": app["ApplicationId"], "CIF": c["CIF"], "ProductCode": code, "BranchCode": b, "Principal": amount,
                         "InterestRate": round(r.uniform(*prod[code][3]), 2), "TenorMonths": app["TenorMonths"], "StartDate": disb_day,
                         "RepaymentAccountNumber": accts[c["CIF"]]["AccountNumber"], "DisbursedByUserId": maker["UserId"],
                         "DisbursementApprovedByUserId": checker["UserId"]})
    book.sort(key=lambda x: (x["StartDate"], x["CIF"]))
    for i, ln in enumerate(book, 1):
        ln["LoanNumber"] = f"LN{BRANCH_NUM[ln['BranchCode']]}{i:06d}"
        months = ln["TenorMonths"]
        ln["MaturityDate"] = _add_months(ln["StartDate"], months)
        rate = ln["InterestRate"] / 1200
        ln["InstallmentAmount"] = round(ln["Principal"] * rate / (1 - (1 + rate) ** -months), 2)
    return apps, book


def _add_months(day, months):
    y, m = divmod(day.month - 1 + months, 12)
    return dt.date(day.year + y, m + 1, min(day.day, 28))


# ------------------------------------------------------------------------------------------------ digital banking
@lru_cache(maxsize=None)
def digital():
    """Digital profiles, beneficiaries and 2025 channel transfers (each transfer is also posted in Core Banking by SYS_DIGITAL)."""
    r = rng(8181)
    by_cif = {}
    for a in accounts():
        by_cif.setdefault(a["CIF"], []).append(a)
    profiles, beneficiaries, transfers = [], [], []
    others = [a for a in accounts() if a["Status"] == "Active" and a["CurrencyCode"] == "AED"]
    n = 0
    for c in customers():
        if c["CustomerType"] != "Individual":
            continue
        accs = [a for a in by_cif[c["CIF"]] if a["Status"] == "Active"]
        age = 2025 - c["DateOfBirth"].year
        if not accs or r.random() > (0.78 if age < 40 else 0.55 if age < 55 else 0.3):
            continue
        n += 1
        start = max(c["OnboardedDate"], DIG_GO_LIVE) + dt.timedelta(days=r.randint(0, 400))
        start = min(start, dt.date(2025, 12, 10))
        if r.random() < 0.35 and start >= DIG_GO_LIVE:
            b = c["HomeBranchCode"]
            day = next_business_day(start)
            people = [p for p in bp.staff_on(day, f"{b}-CSV", "DIGITAL", {"BranchEnrolment"}) if p["EmployeeNumber"] != c.get("StaffEmployeeNumber")]
            if people:
                channel, creator = "Branch", r.choice(people)["UserId"]
            else:
                channel, creator = "SelfService", "SYS_DIGITAL"
        else:
            channel, creator, day = "SelfService", "SYS_DIGITAL", start
        first = c["FullName"].split()[0].lower()
        profiles.append({"ProfileId": n, "CIF": c["CIF"], "Username": f"{first}{c['CIF'][-5:]}", "EnrolledAt": dt.datetime.combine(day, dt.time(r.randint(8, 21), r.randint(0, 59))),
                         "EnrolmentChannel": channel, "EnrolledAtBranchCode": c["HomeBranchCode"] if channel == "Branch" else None, "CreatedByUserId": creator,
                         "Status": "Active", "Devices": r.choice([1, 1, 2, 2, 3]), "Accounts": [a["AccountNumber"] for a in accs], "Customer": c})
    # Beneficiaries and transfers.
    bid = 0
    tid = 0
    for p in profiles:
        main = p["Accounts"][0]
        if r.random() < 0.6:
            for _ in range(r.choice([1, 1, 2, 3])):
                kind = bp._pick(r, [("WithinBank", 35), ("Local", 50), ("International", 15)])
                added = max(p["EnrolledAt"] + dt.timedelta(days=r.randint(1, 200)), dt.datetime(2024, 1, 1) + dt.timedelta(days=r.randint(0, 600)))
                added = min(added, dt.datetime(2025, 12, 20, 12))
                if added < p["EnrolledAt"] + dt.timedelta(hours=1):
                    continue
                bid += 1
                if kind == "WithinBank":
                    target = r.choice(others)
                    acct, bank, country = target["AccountNumber"], "Wabi Bank", "AE"
                    name = target["Customer"]["FullName"]
                elif kind == "Local":
                    code, bank = r.choice(OTHER_BANKS)
                    acct, country = f"AE{r.randint(10, 99)}{code}{r.randint(10**15, 10**16 - 1)}", "AE"
                    name = r.choice(["Rent - landlord", "Family", "Car dealer", "Friend", "School fees", "Contractor"])
                else:
                    country, bank = r.choice(FOREIGN_BANKS)
                    acct, name = f"{country}{r.randint(10**11, 10**12 - 1)}", "Family support"
                beneficiaries.append({"BeneficiaryId": bid, "ProfileId": p["ProfileId"], "BeneficiaryType": kind, "BeneficiaryName": name,
                                      "AccountOrIban": acct, "BankName": bank, "CountryCode": country, "AddedAt": added,
                                      "ActivatedAt": added + dt.timedelta(hours=24), "Status": "Active"})
    by_profile = {}
    for b in beneficiaries:
        by_profile.setdefault(b["ProfileId"], []).append(b)
    for p in profiles:
        bens = [b for b in by_profile.get(p["ProfileId"], []) if b["ActivatedAt"] < dt.datetime(2025, 12, 31)]
        own = p["Accounts"][1:]
        rate = r.choice([0, 0, 1, 2, 3])
        if not bens and not own:
            continue
        for _ in range(rate * 1 + (1 if r.random() < 0.5 else 0)):
            at = dt.datetime(2025, 1, 1, 7) + dt.timedelta(days=int(r.random() * 364), minutes=int(r.random() * 15 * 60))
            if at < p["EnrolledAt"] + dt.timedelta(hours=1):
                at = p["EnrolledAt"] + dt.timedelta(hours=r.randint(1, 72))
            if bens and (not own or r.random() < 0.7):
                b = r.choice(bens)
                if b["ActivatedAt"] > at:
                    at = b["ActivatedAt"] + dt.timedelta(hours=r.randint(1, 72))
                kind, to_acct, bid_ = b["BeneficiaryType"], b["AccountOrIban"], b["BeneficiaryId"]
            else:
                kind, to_acct, bid_ = "OwnAccount", r.choice(own), None
            if at > dt.datetime(2025, 12, 31, 22):
                continue
            tid += 1
            amount = round(r.lognormvariate(math.log(2500 if kind != "International" else 1800), 0.8), -1)
            status = "Completed" if r.random() < 0.965 else r.choice(["Failed", "Rejected"])
            transfers.append({"TransferId": tid, "ProfileId": p["ProfileId"], "FromAccountNumber": p["Accounts"][0], "TransferType": kind,
                              "BeneficiaryId": bid_, "ToAccount": to_acct, "Amount": amount, "CurrencyCode": "AED", "InitiatedAt": at,
                              "Status": status, "FailureReason": None if status == "Completed" else ("Insufficient funds" if status == "Failed" else "Daily limit exceeded"),
                              "CoreBankingReference": f"DT{tid:07d}" if status == "Completed" else None})
    # Planted account-takeover pattern on the last two days of December (live views shift them to today).
    victim = next(p for p in profiles if p["Customer"]["Segment"] == "Priority" and p["EnrolledAt"] < dt.datetime(2024, 1, 1))
    bid += 1
    beneficiaries.append({"BeneficiaryId": bid, "ProfileId": victim["ProfileId"], "BeneficiaryType": "Local", "BeneficiaryName": "New payee",
                          "AccountOrIban": f"AE{57}{OTHER_BANKS[3][0]}{4400000000000017}", "BankName": OTHER_BANKS[3][1], "CountryCode": "AE",
                          "AddedAt": dt.datetime(2025, 12, 30, 2, 41), "ActivatedAt": dt.datetime(2025, 12, 30, 2, 41), "Status": "Active", "Planted": True})
    for at, amount in ((dt.datetime(2025, 12, 30, 3, 5), 38500), (dt.datetime(2025, 12, 31, 3, 12), 41200)):
        tid += 1
        transfers.append({"TransferId": tid, "ProfileId": victim["ProfileId"], "FromAccountNumber": victim["Accounts"][0], "TransferType": "Local",
                          "BeneficiaryId": bid, "ToAccount": beneficiaries[-1]["AccountOrIban"], "Amount": amount, "CurrencyCode": "AED", "InitiatedAt": at,
                          "Status": "Completed", "FailureReason": None, "CoreBankingReference": f"DT{tid:07d}", "Planted": True})
    transfers.sort(key=lambda t: (t["InitiatedAt"], t["TransferId"]))
    return profiles, beneficiaries, transfers, victim
