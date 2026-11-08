"""WabiBankDigitalDB: Wabi Bank's internet and mobile banking (digital profiles of Core Banking customers, devices, sign-ins,
beneficiaries, channel transfers, contact-centre service requests and profile actions) for 2025.

Profiles reuse Core Banking's CIF and account numbers; every completed channel transfer has a matching Core Banking posting
(channel Digital, PostingUnitCode DCC, PostedByUserId SYS_DIGITAL, reference DT0000000). Service requests are handled by
Contact Centre agents (DCC-CC) whose shift covers the request time and who had digital access that day.

Planted findings: one Priority customer's profile signs in from three new devices on 30-31 December, adds a payee at
night and transfers money twice (the live view puts those days at today, so "more than two devices in 24 hours" fires);
a burst of failed sign-ins from foreign IP addresses against many profiles within one hour on 6 October.
"""
import datetime as dt
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bank_customers as bc  # noqa: E402
import bank_people as bp  # noqa: E402
from build_common import epilogue, insert, prologue, rng  # noqa: E402
from build_personnel import DROP_FOREIGN_KEYS  # noqa: E402

DB = "WabiBankDigitalDB"
TABLES = ["ProfileActions", "ServiceRequests", "ChannelTransfers", "Beneficiaries", "SignIns", "Devices", "DigitalProfiles", "Users", "Branches"]
VIEWS = ["vw_SignInsLive", "vw_ChannelTransfersLive"]

OBJECT_DOCS = [
    ("Branches", "Branches", "الفروع", "The six banking branches (codes shared with every Wabi Bank system)."),
    ("Users", "Digital banking staff users", "مستخدمو الخدمات الرقمية", "Staff with digital-banking access in 2025 (Contact Centre, Digital Channels, branch customer service for enrolment, fraud investigators) and the channel service account; same UserId and units as Personnel."),
    ("DigitalProfiles", "Digital profiles", "الملفات الرقمية", "Internet and mobile banking profiles of individual Core Banking customers (CIF): username, enrolment date and channel (branch staff or self-service), home branch and status."),
    ("Devices", "Devices", "الأجهزة", "Devices registered to each profile: platform, model, first and last seen, trusted flag."),
    ("SignIns", "Sign-ins", "عمليات تسجيل الدخول", "Every sign-in attempt in 2025: profile, device, time, channel (mobile or web), IP country, success or the failure reason."),
    ("Beneficiaries", "Beneficiaries", "المستفيدون", "Payees added by customers: within Wabi Bank, local (other UAE banks) or international, with the cooling-off activation time."),
    ("ChannelTransfers", "Channel transfers", "التحويلات الرقمية", "Transfers made in the app or online banking in 2025 with amount, payee, status and the Core Banking reference of the posting."),
    ("ServiceRequests", "Contact Centre service requests", "طلبات خدمة مركز الاتصال", "Customer requests (card block, limit change, device reset, password reset, complaint, dispute) handled by Contact Centre agents, with SLA and resolution."),
    ("ProfileActions", "Profile actions", "إجراءات الملفات الرقمية", "Locks, unlocks, device resets and limit changes made by staff on customers' profiles."),
    ("vw_SignInsLive", "Sign-ins (live demo window)", "تسجيلات الدخول (نافذة العرض المباشر)", "December 2025 sign-ins shifted so that 31 December 2025 is today (Asia/Dubai), for Monitoring Rules demos."),
    ("vw_ChannelTransfersLive", "Channel transfers (live demo window)", "التحويلات الرقمية (نافذة العرض المباشر)", "December 2025 channel transfers shifted so that 31 December 2025 is today (Asia/Dubai)."),
]
COLUMN_DOCS = [
    ("SignIns", "IpAddress", "IP address", "عنوان IP", None, "Confidential"), ("SignIns", "Succeeded", "Succeeded", "ناجح", "1 = signed in, 0 = failed attempt.", None),
    ("SignIns", "FailureReason", "Failure reason", "سبب الفشل", None, None),
    ("Beneficiaries", "AccountOrIban", "Payee account or IBAN", "حساب أو IBAN المستفيد", None, "Restricted"),
    ("ChannelTransfers", "ToAccount", "Payee account", "حساب المستفيد", None, "Restricted"), ("ChannelTransfers", "Amount", "Amount (AED)", "المبلغ (درهم)", None, "Confidential"),
    ("ChannelTransfers", "CoreBankingReference", "Core banking reference", "مرجع النظام المصرفي", "SourceReference of the posting in Core Banking (Transactions).", None),
    ("DigitalProfiles", "CIF", "Customer number (CIF)", "رقم العميل", "Core Banking customer number.", None),
    ("ServiceRequests", "HandledByUserId", "Handled by", "تمت المعالجة بواسطة", "Contact Centre agent's UserId.", None),
]
FIELD_ROLES = [
    ("SignIns", "Username", 3), ("SignIns", "DeviceId", 4), ("SignIns", "SignInAt", 5), ("SignIns", "Succeeded", 8), ("SignIns", "IpCountry", 9),
    ("vw_SignInsLive", "Username", 3), ("vw_SignInsLive", "DeviceId", 4), ("vw_SignInsLive", "SignInAt", 5), ("vw_SignInsLive", "Succeeded", 8), ("vw_SignInsLive", "IpCountry", 9),
    ("ChannelTransfers", "Username", 3), ("ChannelTransfers", "Amount", 6), ("ChannelTransfers", "InitiatedAt", 5), ("ChannelTransfers", "Status", 8),
    ("vw_ChannelTransfersLive", "Username", 3), ("vw_ChannelTransfersLive", "Amount", 6), ("vw_ChannelTransfersLive", "InitiatedAt", 5),
    ("DigitalProfiles", "Username", 3), ("DigitalProfiles", "CIF", 2), ("Devices", "DeviceId", 4), ("ServiceRequests", "OpenedAt", 5), ("ServiceRequests", "Status", 8),
]
ROW_SCOPES = [
    ("DigitalProfiles", None, "HomeBranchCode", None), ("ChannelTransfers", None, "HomeBranchCode", None),
    ("ServiceRequests", "HandledByEmployeeNumber", "DepartmentCode", "SectionCode"), ("ProfileActions", "PerformedByEmployeeNumber", "DepartmentCode", "SectionCode"),
]

PLATFORMS = [("iOS", ["iPhone 13", "iPhone 14", "iPhone 15", "iPhone 15 Pro", "iPhone 16"]), ("Android", ["Galaxy S23", "Galaxy A54", "Pixel 8", "Redmi Note 13", "Galaxy S24"]),
             ("Web", ["Chrome on Windows", "Safari on macOS", "Edge on Windows"])]
REQUEST_TYPES = [("CardBlock", 0.18, 2), ("LimitChange", 0.12, 8), ("DeviceReset", 0.2, 4), ("PasswordReset", 0.25, 2), ("Complaint", 0.15, 72), ("Dispute", 0.1, 120)]


def _agent(r, at, cache, exclude=None):
    """A Contact Centre agent with digital access whose shift covers `at` and who is at work that day (never the customer)."""
    hour = at.hour
    day = at.date() if hour >= 7 else at.date() - dt.timedelta(days=1)
    shift = "CC1" if 7 <= hour < 15 else ("CC2" if 15 <= hour < 23 else "CC3")
    key = (day, shift)
    if key not in cache:
        people = bp.staff_on(day, "DCC-CC", "DIGITAL", {"ContactCentreAgent", "ContactCentreSupervisor"})
        cache[key] = [p for p in people if p["ShiftCode"] == shift] or [p for p in people if p["ShiftCode"] in ("CC1", "CC2", "CC3")]
    people = [p for p in cache[key] if p["EmployeeNumber"] != exclude]
    return r.choice(people) if people else None


def _rows():
    bp.validate()
    r = rng(1212)
    profiles, beneficiaries, transfers, victim = bc.digital()
    by_cif = {c["CIF"]: c for c in bc.customers()}
    out = {}
    out["Branches"] = [(loc[0], loc[1], loc[2], loc[4]) for loc in bp.LOCATIONS[1:]]
    out["Users"] = [(u["UserId"], u["EmployeeNumber"], u["FullName"], u["FullNameAr"], u["DepartmentCode"], u["SectionCode"], u["RoleCode"], u["Status"],
                     u["AccessGrantedDate"], u["AccessRevokedDate"]) for u in bp.local_users("DIGITAL")]
    out["DigitalProfiles"] = [(p["ProfileId"], p["CIF"], p["Username"], p["Customer"]["FullName"], p["Customer"]["HomeBranchCode"], p["EnrolledAt"], p["EnrolmentChannel"],
                               p["EnrolledAtBranchCode"], p["CreatedByUserId"], "Active") for p in profiles]
    devices = []
    dev_by_profile = {}
    for p in profiles:
        for k in range(1, p["Devices"] + 1):
            plat, models = r.choice(PLATFORMS[:2]) if k == 1 or r.random() < 0.7 else PLATFORMS[2]
            first = p["EnrolledAt"] + dt.timedelta(days=0 if k == 1 else r.randint(30, 700))
            first = max(p["EnrolledAt"], min(first, dt.datetime(2025, 11, 30, 12)))
            did = f"D{p['ProfileId']:05d}{k}{r.randint(1000, 9999)}"
            devices.append([did, p["ProfileId"], k, plat, r.choice(models), first, None, 1 if k == 1 else (1 if r.random() < 0.6 else 0)])
            dev_by_profile.setdefault(p["ProfileId"], []).append(did)
    # Planted: three new devices for the victim on 30 December, used from abroad at night.
    new_devices = []
    for k, (plat, model) in enumerate((("Android", "Galaxy A15"), ("Android", "Redmi 13C"), ("Web", "Chrome on Linux")), start=8):
        did = f"D{victim['ProfileId']:05d}{k}9{k}{k}{k}"
        devices.append([did, victim["ProfileId"], k, plat, model, dt.datetime(2025, 12, 30, 0, 20 + k), None, 0])
        new_devices.append(did)
    out["Devices"] = devices
    out["Beneficiaries"] = [(b["BeneficiaryId"], b["ProfileId"], b["BeneficiaryType"], b["BeneficiaryName"], b["AccountOrIban"], b["BankName"], b["CountryCode"],
                             b["AddedAt"], b["ActivatedAt"], b["Status"]) for b in beneficiaries]
    prof = {p["ProfileId"]: p for p in profiles}
    out["ChannelTransfers"] = [(t["TransferId"], t["ProfileId"], prof[t["ProfileId"]]["Username"], prof[t["ProfileId"]]["Customer"]["HomeBranchCode"], t["FromAccountNumber"],
                                t["TransferType"], t["BeneficiaryId"], t["ToAccount"], t["Amount"], t["CurrencyCode"], t["InitiatedAt"], t["Status"], t["FailureReason"],
                                t["CoreBankingReference"]) for t in transfers]
    # Sign-ins that open each transfer's session (on the profile's first device, or a new device for the planted ones).
    session = []
    for t in transfers:
        did = new_devices[0] if t.get("Planted") else dev_by_profile[t["ProfileId"]][0]
        country = "RO" if t.get("Planted") else "AE"
        session.append((t["ProfileId"], did, t["InitiatedAt"] - dt.timedelta(minutes=2 + t["TransferId"] % 4), "Mobile", country, 1, None))
    # Planted multi-device pattern: every few hours on 30 and 31 December from three new devices and the usual one.
    usual = dev_by_profile[victim["ProfileId"]][0]
    for day in (30, 31):
        for hh, mm, did, country in ((0, 40, new_devices[0], "RO"), (2, 30, new_devices[1], "RO"), (4, 10, new_devices[2], "NL"), (7, 15, usual, "AE"),
                                     (10, 5, new_devices[0], "RO"), (13, 20, new_devices[1], "RO"), (16, 45, new_devices[2], "NL"), (19, 30, usual, "AE"),
                                     (22, 10, new_devices[0], "RO"), (23, 40, new_devices[2], "NL")):
            session.append((victim["ProfileId"], did, dt.datetime(2025, 12, day, hh, mm), "Web" if did == new_devices[2] else "Mobile", country, 1, None))
    # Planted burst of failed sign-ins from foreign IP addresses (credential stuffing) on 6 October.
    targets = [profiles[i] for i in sorted(r.sample(range(len(profiles)), 45))]
    for k, p in enumerate(targets):
        at = dt.datetime(2025, 10, 6, 3, 2) + dt.timedelta(seconds=k * 71)
        for attempt in range(r.randint(2, 5)):
            session.append((p["ProfileId"], f"X{k:03d}{attempt}", at + dt.timedelta(seconds=attempt * 9), "Web", r.choice(["RU", "VN", "NG", "BR", "ID"]), 0, "Wrong password"))
    out["#Session"] = session
    # Contact Centre service requests and the profile actions they lead to.
    requests, actions = [], []
    cache = {}
    sid = aid = 0
    for p in profiles:
        n = sum(1 for _ in range(12) if r.random() < 0.11)
        for _ in range(n):
            at = dt.datetime(2025, 1, 1) + dt.timedelta(days=int(r.random() * 364), minutes=int(r.random() * 1440))
            if at < p["EnrolledAt"]:
                continue
            kind = bp._pick(r, [((k, sla), w) for k, w, sla in REQUEST_TYPES])
            agent = _agent(r, at, cache, exclude=p["Customer"].get("StaffEmployeeNumber"))
            if agent is None:
                continue
            sid += 1
            hours = kind[1] * (0.2 + r.random() * (1.6 if r.random() < 0.9 else 3.5))
            resolved = at + dt.timedelta(hours=hours)
            status = "Resolved" if resolved < dt.datetime(2025, 12, 31, 23, 59) else "Open"
            requests.append((sid, p["ProfileId"], p["Username"], kind[0], r.choice(["Phone", "Phone", "Chat", "Email"]), at, agent["UserId"], agent["EmployeeNumber"],
                             "DCC", "DCC-CC", resolved if status == "Resolved" else None, at + dt.timedelta(hours=kind[1]), int(hours > kind[1]) if status == "Resolved" else 0, status))
            if kind[0] in ("CardBlock", "DeviceReset", "PasswordReset", "LimitChange") and status == "Resolved":
                aid += 1
                action = {"CardBlock": "CardBlocked", "DeviceReset": "DevicesReset", "PasswordReset": "ProfileUnlocked", "LimitChange": "LimitChanged"}[kind[0]]
                actions.append((aid, p["ProfileId"], action, at + dt.timedelta(minutes=8 + aid % 30), agent["UserId"], agent["EmployeeNumber"], "DCC", "DCC-CC", sid))
    out["ServiceRequests"] = requests
    out["ProfileActions"] = actions
    # Consistency: every staff actor had digital access and was at work that day.
    for q in requests:
        day = q[5].date() if q[5].hour >= 7 else q[5].date() - dt.timedelta(days=1)
        assert bp.has_access(q[6], "DIGITAL", day), q
        assert bp.present(bp.by_user()[q[6]], day), q
    first_seen = {d[0]: d[5] for d in devices}
    enrolled = {p["ProfileId"]: p["EnrolledAt"] for p in profiles}
    assert all(enrolled[t["ProfileId"]] < t["InitiatedAt"] <= dt.datetime(2025, 12, 31, 23, 59) for t in transfers)
    assert all(enrolled[b["ProfileId"]] < b["AddedAt"] for b in beneficiaries)
    assert all(first_seen[s[1]] <= s[2] for s in session if s[5] == 1), "sign-in before the device was first seen"
    staff_of = {p["ProfileId"]: p["Customer"].get("StaffEmployeeNumber") for p in profiles}
    assert all(staff_of[q[1]] != q[7] for q in requests), "a Contact Centre agent handled their own profile"
    for p in profiles:
        if p["CreatedByUserId"] != "SYS_DIGITAL":
            assert bp.has_access(p["CreatedByUserId"], "DIGITAL", p["EnrolledAt"].date()), p["ProfileId"]
    return out, {"victim": victim["Username"], "new_devices": new_devices}


SCHEMA = """CREATE TABLE dbo.Branches (BranchCode nvarchar(10) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY, BranchName nvarchar(80) NOT NULL, BranchNameAr nvarchar(80) NOT NULL,
    Emirate nvarchar(30) NOT NULL);
CREATE TABLE dbo.Users (UserId nvarchar(20) NOT NULL CONSTRAINT PK_Users PRIMARY KEY, EmployeeNumber nvarchar(10) NULL, FullName nvarchar(120) NOT NULL, FullNameAr nvarchar(120) NULL,
    DepartmentCode nvarchar(10) NULL, SectionCode nvarchar(20) NULL, RoleCode nvarchar(40) NOT NULL, Status nvarchar(10) NOT NULL, AccessGrantedDate date NOT NULL, AccessRevokedDate date NULL);
CREATE TABLE dbo.DigitalProfiles (ProfileId int NOT NULL CONSTRAINT PK_DigitalProfiles PRIMARY KEY, CIF nvarchar(10) NOT NULL, Username nvarchar(40) NOT NULL CONSTRAINT UQ_DigitalProfiles_Username UNIQUE,
    CustomerName nvarchar(150) NOT NULL, HomeBranchCode nvarchar(10) NOT NULL CONSTRAINT FK_DigitalProfiles_Branches REFERENCES dbo.Branches, EnrolledAt datetime2(0) NOT NULL,
    EnrolmentChannel nvarchar(12) NOT NULL, EnrolledAtBranchCode nvarchar(10) NULL, CreatedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_DigitalProfiles_Users REFERENCES dbo.Users,
    Status nvarchar(10) NOT NULL);
CREATE TABLE dbo.Devices (DeviceId nvarchar(20) NOT NULL CONSTRAINT PK_Devices PRIMARY KEY, ProfileId int NOT NULL CONSTRAINT FK_Devices_Profiles REFERENCES dbo.DigitalProfiles,
    DeviceSeq tinyint NOT NULL, Platform nvarchar(10) NOT NULL, Model nvarchar(40) NOT NULL, FirstSeenAt datetime2(0) NOT NULL, LastSeenAt datetime2(0) NULL, IsTrusted bit NOT NULL);
CREATE TABLE dbo.SignIns (SignInId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SignIns PRIMARY KEY, ProfileId int NOT NULL CONSTRAINT FK_SignIns_Profiles REFERENCES dbo.DigitalProfiles,
    Username nvarchar(40) NOT NULL, DeviceId nvarchar(20) NOT NULL, SignInAt datetime2(0) NOT NULL, Channel nvarchar(10) NOT NULL, IpAddress nvarchar(40) NOT NULL, IpCountry char(2) NOT NULL,
    Succeeded bit NOT NULL, FailureReason nvarchar(40) NULL);
CREATE TABLE dbo.Beneficiaries (BeneficiaryId int NOT NULL CONSTRAINT PK_Beneficiaries PRIMARY KEY, ProfileId int NOT NULL CONSTRAINT FK_Beneficiaries_Profiles REFERENCES dbo.DigitalProfiles,
    BeneficiaryType nvarchar(16) NOT NULL, BeneficiaryName nvarchar(120) NOT NULL, AccountOrIban nvarchar(40) NOT NULL, BankName nvarchar(80) NOT NULL, CountryCode char(2) NOT NULL,
    AddedAt datetime2(0) NOT NULL, ActivatedAt datetime2(0) NOT NULL, Status nvarchar(10) NOT NULL);
CREATE TABLE dbo.ChannelTransfers (TransferId int NOT NULL CONSTRAINT PK_ChannelTransfers PRIMARY KEY, ProfileId int NOT NULL CONSTRAINT FK_ChannelTransfers_Profiles REFERENCES dbo.DigitalProfiles,
    Username nvarchar(40) NOT NULL, HomeBranchCode nvarchar(10) NOT NULL, FromAccountNumber char(12) NOT NULL, TransferType nvarchar(16) NOT NULL,
    BeneficiaryId int NULL CONSTRAINT FK_ChannelTransfers_Beneficiaries REFERENCES dbo.Beneficiaries, ToAccount nvarchar(40) NOT NULL, Amount decimal(18,2) NOT NULL, CurrencyCode char(3) NOT NULL,
    InitiatedAt datetime2(0) NOT NULL, Status nvarchar(10) NOT NULL, FailureReason nvarchar(60) NULL, CoreBankingReference nvarchar(20) NULL);
CREATE TABLE dbo.ServiceRequests (RequestId int NOT NULL CONSTRAINT PK_ServiceRequests PRIMARY KEY, ProfileId int NOT NULL CONSTRAINT FK_ServiceRequests_Profiles REFERENCES dbo.DigitalProfiles,
    Username nvarchar(40) NOT NULL, RequestType nvarchar(16) NOT NULL, Channel nvarchar(10) NOT NULL, OpenedAt datetime2(0) NOT NULL,
    HandledByUserId nvarchar(20) NOT NULL CONSTRAINT FK_ServiceRequests_Users REFERENCES dbo.Users, HandledByEmployeeNumber nvarchar(10) NOT NULL, DepartmentCode nvarchar(10) NOT NULL,
    SectionCode nvarchar(20) NOT NULL, ResolvedAt datetime2(0) NULL, SlaDueAt datetime2(0) NOT NULL, SlaBreached bit NOT NULL, Status nvarchar(10) NOT NULL);
CREATE TABLE dbo.ProfileActions (ActionId int NOT NULL CONSTRAINT PK_ProfileActions PRIMARY KEY, ProfileId int NOT NULL CONSTRAINT FK_ProfileActions_Profiles REFERENCES dbo.DigitalProfiles,
    ActionType nvarchar(20) NOT NULL, PerformedAt datetime2(0) NOT NULL, PerformedByUserId nvarchar(20) NOT NULL CONSTRAINT FK_ProfileActions_Users REFERENCES dbo.Users,
    PerformedByEmployeeNumber nvarchar(10) NOT NULL, DepartmentCode nvarchar(10) NOT NULL, SectionCode nvarchar(20) NOT NULL,
    RequestId int NULL CONSTRAINT FK_ProfileActions_Requests REFERENCES dbo.ServiceRequests);
GO
"""
COLUMNS = {
    "Branches": ["BranchCode", "BranchName", "BranchNameAr", "Emirate"],
    "Users": ["UserId", "EmployeeNumber", "FullName", "FullNameAr", "DepartmentCode", "SectionCode", "RoleCode", "Status", "AccessGrantedDate", "AccessRevokedDate"],
    "DigitalProfiles": ["ProfileId", "CIF", "Username", "CustomerName", "HomeBranchCode", "EnrolledAt", "EnrolmentChannel", "EnrolledAtBranchCode", "CreatedByUserId", "Status"],
    "Devices": ["DeviceId", "ProfileId", "DeviceSeq", "Platform", "Model", "FirstSeenAt", "LastSeenAt", "IsTrusted"],
    "Beneficiaries": ["BeneficiaryId", "ProfileId", "BeneficiaryType", "BeneficiaryName", "AccountOrIban", "BankName", "CountryCode", "AddedAt", "ActivatedAt", "Status"],
    "ChannelTransfers": ["TransferId", "ProfileId", "Username", "HomeBranchCode", "FromAccountNumber", "TransferType", "BeneficiaryId", "ToAccount", "Amount", "CurrencyCode",
                         "InitiatedAt", "Status", "FailureReason", "CoreBankingReference"],
    "ServiceRequests": ["RequestId", "ProfileId", "Username", "RequestType", "Channel", "OpenedAt", "HandledByUserId", "HandledByEmployeeNumber", "DepartmentCode", "SectionCode",
                        "ResolvedAt", "SlaDueAt", "SlaBreached", "Status"],
    "ProfileActions": ["ActionId", "ProfileId", "ActionType", "PerformedAt", "PerformedByUserId", "PerformedByEmployeeNumber", "DepartmentCode", "SectionCode", "RequestId"],
}
LOAD_ORDER = ["Branches", "Users", "DigitalProfiles", "Devices", "Beneficiaries", "ChannelTransfers", "ServiceRequests", "ProfileActions"]

GENERATE = """-- Routine sign-ins, set-based: a few per month per profile after enrolment, mostly from the first device, mostly from the UAE.
CREATE TABLE #Session (ProfileId int, DeviceId nvarchar(20), SignInAt datetime2(0), Channel nvarchar(10), IpCountry char(2), Succeeded bit, FailureReason nvarchar(40));
{session}
CREATE TABLE #N (N int PRIMARY KEY); INSERT INTO #N SELECT TOP (12) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects;
SELECT p.ProfileId, p.Username, p.EnrolledAt, m.N AS M, CAST(FLOOR(1 + rc.R1 * rc.R2 * 9) AS int) AS Cnt INTO #PM
FROM dbo.DigitalProfiles p JOIN #N m ON m.N <= 12 CROSS APPLY gen.Rand(p.ProfileId * 100 + m.N) rc;
SELECT pm.ProfileId, pm.Username, pm.EnrolledAt, pm.M, n.N, r.R1, r.R2, r.R3, r.R4, r.R5, r.R6 INTO #SE
FROM #PM pm JOIN #N n ON n.N <= pm.Cnt CROSS APPLY gen.Rand(pm.ProfileId * 10000 + pm.M * 100 + n.N + 7) r;
INSERT INTO #Session (ProfileId, DeviceId, SignInAt, Channel, IpCountry, Succeeded, FailureReason)
SELECT e.ProfileId, d.DeviceId, x.At, CASE d.Platform WHEN N'Web' THEN N'Web' ELSE N'Mobile' END,
       CASE WHEN e.R4 < 0.955 THEN 'AE' WHEN e.R4 < 0.97 THEN 'SA' WHEN e.R4 < 0.98 THEN 'IN' WHEN e.R4 < 0.99 THEN 'GB' ELSE 'OM' END,
       CASE WHEN e.R5 < 0.965 THEN 1 ELSE 0 END, CASE WHEN e.R5 >= 0.965 THEN CASE WHEN e.R6 < 0.6 THEN N'Wrong password' ELSE N'Wrong OTP' END END
FROM #SE e
CROSS APPLY (SELECT DATEADD(MINUTE, 360 + CAST(e.R2 * 1070 AS int), CAST(DATEADD(DAY, CAST(e.R1 * DAY(EOMONTH(DATEFROMPARTS(2025, e.M, 1))) AS int), DATEFROMPARTS(2025, e.M, 1)) AS datetime2(0))) AS At) x
CROSS APPLY (SELECT COUNT(*) AS Cnt FROM dbo.Devices dv WHERE dv.ProfileId = e.ProfileId AND dv.DeviceSeq < 8 AND dv.FirstSeenAt <= x.At) dc
JOIN dbo.Devices d ON d.ProfileId = e.ProfileId AND d.DeviceSeq = CASE WHEN e.R3 < 0.9 OR dc.Cnt < 2 THEN 1 ELSE 1 + CAST(e.R3 * dc.Cnt AS int) % dc.Cnt + CASE WHEN dc.Cnt > 1 THEN 0 ELSE 0 END END
WHERE x.At >= e.EnrolledAt AND x.At >= d.FirstSeenAt AND dc.Cnt >= 1;
INSERT INTO dbo.SignIns (ProfileId, Username, DeviceId, SignInAt, Channel, IpAddress, IpCountry, Succeeded, FailureReason)
SELECT s.ProfileId, p.Username, s.DeviceId, s.SignInAt, s.Channel,
       CASE s.IpCountry WHEN 'AE' THEN CONCAT('94.', 200 + ABS(CHECKSUM(s.DeviceId)) % 50, '.', ABS(CHECKSUM(s.SignInAt)) % 250, '.', ABS(CHECKSUM(s.ProfileId, s.SignInAt)) % 250)
                        ELSE CONCAT(45 + ABS(CHECKSUM(s.IpCountry)) % 150, '.', ABS(CHECKSUM(s.DeviceId)) % 250, '.', ABS(CHECKSUM(s.SignInAt)) % 250, '.', 1 + ABS(CHECKSUM(s.ProfileId)) % 250) END,
       s.IpCountry, s.Succeeded, s.FailureReason
FROM #Session s JOIN dbo.DigitalProfiles p ON p.ProfileId = s.ProfileId
ORDER BY s.SignInAt, s.ProfileId;
UPDATE d SET LastSeenAt = x.LastAt FROM dbo.Devices d CROSS APPLY (SELECT MAX(SignInAt) AS LastAt FROM dbo.SignIns s WHERE s.DeviceId = d.DeviceId AND s.Succeeded = 1) x;
CREATE INDEX IX_SignIns_At ON dbo.SignIns (SignInAt) INCLUDE (Username, DeviceId, Succeeded);
GO
"""

VIEWS_SQL = """CREATE VIEW dbo.vw_SignInsLive AS
SELECT s.SignInId, s.ProfileId, s.Username, s.DeviceId, DATEADD(DAY, x.Shift, s.SignInAt) AS SignInAt, s.Channel, s.IpAddress, s.IpCountry, s.Succeeded, s.FailureReason
FROM dbo.SignIns s CROSS JOIN (SELECT DATEDIFF(DAY, CAST('2025-12-31' AS date), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time' AS date)) AS Shift) x
WHERE s.SignInAt >= '2025-12-01';
GO
CREATE VIEW dbo.vw_ChannelTransfersLive AS
SELECT t.TransferId, t.ProfileId, t.Username, t.HomeBranchCode, t.FromAccountNumber, t.TransferType, t.BeneficiaryId, t.Amount, t.CurrencyCode,
       DATEADD(DAY, x.Shift, t.InitiatedAt) AS InitiatedAt, t.Status, t.CoreBankingReference
FROM dbo.ChannelTransfers t CROSS JOIN (SELECT DATEDIFF(DAY, CAST('2025-12-31' AS date), CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time' AS date)) AS Shift) x
WHERE t.InitiatedAt >= '2025-12-01';
GO
"""


def build():
    rows, facts = _rows()
    parts = [prologue(DB, "wbdigital_owner", "WbDigital#Owner#2026", "wbdigital_reader", "WbDigital#Reader#2026",
                      "WabiBankDigitalDB — Wabi Bank internet and mobile banking (sample)",
                      "Digital profiles, devices, sign-ins, beneficiaries, channel transfers, Contact Centre requests and profile actions, 2025."),
             DROP_FOREIGN_KEYS, "\n".join(f"IF OBJECT_ID(N'dbo.{v}', N'V') IS NOT NULL DROP VIEW dbo.{v};" for v in VIEWS) + "\nGO\n",
             "\n".join(f"IF OBJECT_ID(N'dbo.{t}', N'U') IS NOT NULL DROP TABLE dbo.{t};" for t in TABLES) + "\nGO\n", SCHEMA]
    for t in LOAD_ORDER:
        parts.append(insert(f"dbo.{t}", COLUMNS[t], rows[t]) + "\nGO\n")
    parts.append(GENERATE.replace("{session}", insert("#Session", ["ProfileId", "DeviceId", "SignInAt", "Channel", "IpCountry", "Succeeded", "FailureReason"], rows["#Session"])))
    parts.append(VIEWS_SQL)
    summary = """SELECT N'DigitalProfiles' AS TableName, COUNT(*) AS Rows FROM dbo.DigitalProfiles UNION ALL SELECT N'Devices', COUNT(*) FROM dbo.Devices
UNION ALL SELECT N'SignIns', COUNT(*) FROM dbo.SignIns UNION ALL SELECT N'Beneficiaries', COUNT(*) FROM dbo.Beneficiaries
UNION ALL SELECT N'ChannelTransfers', COUNT(*) FROM dbo.ChannelTransfers UNION ALL SELECT N'ServiceRequests', COUNT(*) FROM dbo.ServiceRequests
UNION ALL SELECT N'ProfileActions', COUNT(*) FROM dbo.ProfileActions;
GO"""
    parts.append(epilogue(DB, summary))
    stats = {k: len(v) for k, v in rows.items()}
    stats["facts"] = facts
    return "\n".join(parts), stats


if __name__ == "__main__":
    text, stats = build()
    path = os.path.join(HERE, "04-create-wabibankdigitaldb.sql")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)
    print(f"{os.path.basename(path)}: {len(text) // 1024} KB {stats}")
