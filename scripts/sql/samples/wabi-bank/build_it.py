#!/usr/bin/env python3
"""WabiBankITDB: the IT Help Desk of Wabi Bank (WBK), calendar year 2025 (sample source database, Microsoft SQL Server).

A normal IT service desk limited to two modules:
  * Requests: a service catalogue (new joiner setup, system access per system and role, access removal for leavers,
    password reset or unlock, laptop or peripheral, software installation, VPN, mailbox or distribution list) with
    line-manager and system-owner approvals and fulfilment tasks worked by the IT sections;
  * Incidents: user reports (Portal, Phone, Email) and automatic Monitoring alerts, priority from impact x urgency,
    SLA targets, parent/child links and an activity log (status changes, reassignments, work notes).
Every department, section, location, employee and login comes from bank_people.py (Personnel is the system of record);
the database holds no links to the other Wabi Bank systems' data.

Who does what (enforced in Python before the SQL is written, and checked again in SQL at the end of the script):
  * Requesters: every employee with a UserId (ITSM role Requester) uses the self-service Portal on days they are at work
    and within their shift. Drivers, messengers and security guards have no login: they phone or walk in, and an ITD-SD
    agent logs the ticket. Phone, Email and WalkIn tickets are always created by an ITD-SD agent at work at that time.
  * Agents are ITD staff (Agent); the ITD section heads are GroupLead and W1008 Simon Varley is ServiceDeskManager. The
    assignment groups are the five ITD sections. Staff act only on days they are at work, within Head Office hours
    (Mon-Fri 08:00-16:30, closed on UAE public holidays) or their own shift; outside those hours only on-call engineers
    of ITD-INF, ITD-NET and ITD-APP act, on P1/P2 work, on days they are not on leave or absent.
  * SYS_MONITOR opens Monitoring incidents around the clock and routes them to the owning group.
  * Approvals: the requested-for person's line manager on that day, then for system-access requests the head of the
    department that owns the system (Personnel SYSTEMS). A step whose approver would be the requester is skipped; an
    approver away for a week is replaced by their own manager. Approvers act on days they are at work.
  * Joiners: each 2025 hire gets a New joiner setup request, raised before the first day by the line manager or an
    HRD-PSV HR officer. Leavers: each 2025 termination gets an Access removal request raised by an HRD-PSV HR officer
    and fulfilled on or before the last working day (due by the end of that day).

Planted findings (each one is findable with the SQL shown):
  1. Major P1 core-banking outage, Tuesday 13 May 2025 10:12-13:13 (about 3 hours), opened by monitoring on the core
     banking database cluster CBS-DB-01, with eight child incidents phoned in from the six branches (two from Deira) and
     Central Operations, all resolved by the parent. It is the only P1 on core banking.
       SELECT i.IncidentNumber, i.OpenedAt, i.ResolvedAt, DATEDIFF(MINUTE, i.OpenedAt, i.ResolvedAt) AS OutageMinutes,
              (SELECT COUNT(*) FROM dbo.Incidents c WHERE c.ParentIncidentNumber = i.IncidentNumber) AS LinkedIncidents
       FROM dbo.Incidents i WHERE i.IsMajorIncident = 1 AND i.Priority = N'P1' AND i.ServiceCode = N'CBS';
  2. Phishing campaign, Wednesday 17 September 2025: an ITD-SEC P2 security incident with nine user reports linked to it.
       SELECT p.IncidentNumber, p.Priority, p.AssignmentGroupCode, p.OpenedAt, p.ResolvedAt, COUNT(*) AS LinkedUserReports
       FROM dbo.Incidents p JOIN dbo.Incidents c ON c.ParentIncidentNumber = p.IncidentNumber
       WHERE p.Category = N'Security' AND p.Subcategory = N'Phishing'
       GROUP BY p.IncidentNumber, p.Priority, p.AssignmentGroupCode, p.OpenedAt, p.ResolvedAt;
  3. ATM-SHJ01-02 (Sharjah branch) fails every 2-3 days from 27 October (28 incidents), four times in the last seven
     days of December (25, 27, 29 and 31 December). Apart from it and the major-incident cluster on CBS-DB-01 in May, no
     configuration item ever has more than three incidents within eight days. On the live view (31 December = today)
     the seeded rule "more than three incidents on the same configuration item in 7 days" fires for it alone:
       SELECT CiCode, COUNT(*) AS Incidents FROM dbo.vw_IncidentsLive
       WHERE OpenedAt >= DATEADD(HOUR, -168, CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time' AS datetime2(0)))
       GROUP BY CiCode HAVING COUNT(*) > 3;
  4. ITD-NET breaches its SLA far more often than the other groups in Q3 (two network engineers left in the summer):
       SELECT AssignmentGroupCode, DATEPART(QUARTER, OpenedAt) AS Quarter, COUNT(*) AS Incidents,
              CAST(100.0 * SUM(CAST(SlaBreached AS int)) / COUNT(*) AS decimal(5, 1)) AS BreachPercent
       FROM dbo.Incidents GROUP BY AssignmentGroupCode, DATEPART(QUARTER, OpenedAt) ORDER BY 1, 2;
  5. Late access removal: the access-removal request of bank_people.facts()["late_revocation"] (W1396, Sharjah teller,
     last working day Thursday 24 July 2025) is fulfilled on Sunday 27 July, three days after termination, when the
     on-call core-banking specialist removes the Core Banking user (Personnel revokes CBS on the same day). Every other
     access removal is fulfilled on or before the last working day.
       SELECT sr.RequestNumber, sr.RequestedForEmployeeNumber, e.FullName, e.TerminationDate, sr.OpenedAt, sr.FulfilledAt,
              DATEDIFF(DAY, e.TerminationDate, sr.FulfilledAt) AS DaysAfterTermination
       FROM dbo.ServiceRequests sr JOIN dbo.EmployeeDirectory e ON e.EmployeeNumber = sr.RequestedForEmployeeNumber
       WHERE sr.CatalogItemCode = N'ACC-REMOVE' AND CAST(sr.FulfilledAt AS date) > e.TerminationDate;

Generation: Python decides every ticket, actor and timestamp (so the rules above can be asserted exactly) and writes
them as compact integer-coded rows into temporary tables; set-based T-SQL decodes them into the final tables and expands
the incident activity log (work-note texts are drawn with gen.Rand). Deterministic: the same script on every run.
Run:  python3 build_it.py   (writes 05-create-wabibankitdb.sql next to this file)
"""
import datetime as dt
import os
import sys
from collections import defaultdict
from functools import lru_cache

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))
import bank_people as bp  # noqa: E402
from build_common import insert, prologue, epilogue, drop_tables, TALLY_CTE, rng  # noqa: E402

DB, OWNER, READER = "WabiBankITDB", "wbit_owner", "wbit_reader"
OUTPUT = "05-create-wabibankitdb.sql"
TITLE = "WabiBankITDB — IT Help Desk (service requests and incidents) of Wabi Bank, 2025 (sample)"
DESCRIPTION = ("Employee directory, help-desk users, locations, assignment groups, services, configuration items, categories, "
               "SLA policies, business hours, service catalogue, requests with approvals and tasks, incidents with activities.")

T0 = dt.datetime(2025, 1, 1)
END = dt.datetime(2025, 12, 31, 23, 59)
BH = (8 * 60, 16 * 60 + 30)  # Head Office business hours, minutes of the day
SHIFT_WINDOWS = {"HO": (480, 990), "BR": (465, 945), "CC1": (420, 900), "CC2": (900, 1380), "CC3": (1380, 1439), "SEC": (360, 1080)}
GROUP_CODES = ["ITD-SD", "ITD-INF", "ITD-APP", "ITD-NET", "ITD-SEC"]
ONCALL_GROUPS = {"ITD-INF", "ITD-NET", "ITD-APP"}
RESP_TARGET = {1: 15, 2: 30, 3: 240, 4: 510}        # P1 calendar minutes, others business minutes
RES_TARGET = {1: 240, 2: 480, 3: 1530, 4: 2550}
PLANTED_ATM = "ATM-SHJ01-02"
MAJOR_DAY, PHISH_DAY = dt.date(2025, 5, 13), dt.date(2025, 9, 17)
N_REQUESTS, N_INCIDENTS = 6000, 4500

GROUPS = {  # code -> (email, description)
    "ITD-SD": ("servicedesk@wabibank.example", "First line: calls, walk-ins and portal triage; laptops, desktops, printers, peripherals, office software and password resets."),
    "ITD-INF": ("itd-infrastructure@wabibank.example", "Servers, databases, storage and backup, the email platform and the ATM fleet."),
    "ITD-APP": ("itd-applications@wabibank.example", "Core banking, digital banking, finance, HR and ITSM applications; core-banking user administration."),
    "ITD-NET": ("itd-network@wabibank.example", "Branch WAN links, Head Office LAN and Wi-Fi, VPN and firewalls."),
    "ITD-SEC": ("itd-security@wabibank.example", "Security incidents and phishing, identity and access administration (accounts, access requests and removals)."),
}

SERVICES = [  # code, name, Arabic, owning group, criticality, description
    ("CBS", "Core Banking", "النظام المصرفي الأساسي", "ITD-APP", "Critical", "Customers, accounts, teller, loans, payments, clearing and the end-of-day batch."),
    ("DIGITAL", "Digital Banking", "الخدمات المصرفية الرقمية", "ITD-APP", "Critical", "Mobile and internet banking, digital onboarding, OTP gateway and the contact-centre console."),
    ("FIN", "Finance", "النظام المالي", "ITD-APP", "High", "General ledger, payables, procurement and budgeting."),
    ("HRMS", "Personnel and Attendance", "شؤون الموظفين والحضور", "ITD-APP", "Medium", "Employee self-service, time and attendance and payroll."),
    ("EMAIL", "Email and collaboration", "البريد الإلكتروني والتعاون", "ITD-INF", "High", "Mailboxes, distribution lists, Outlook and the email security gateway."),
    ("NETWORK", "Network", "الشبكة", "ITD-NET", "Critical", "Head Office LAN and Wi-Fi, branch WAN and backup links, VPN and firewalls."),
    ("ATM", "ATM network", "شبكة الصرافات الآلية", "ITD-INF", "Critical", "Branch ATMs and the ATM switch."),
    ("EUD", "End-user devices", "أجهزة المستخدمين", "ITD-SD", "Medium", "Laptops, desktops, printers, peripherals, office software, file and print servers and the directory."),
    ("ITSM", "IT self-service portal", "بوابة الخدمة الذاتية لتقنية المعلومات", "ITD-APP", "Low", "The IT Help Desk portal used by every employee to raise requests and incidents."),
]

CATEGORIES = [  # category, subcategory, Arabic category, Arabic subcategory, ticket type, default group
    ("Hardware", "Laptop", "الأجهزة", "الحاسوب المحمول", "Incident", "ITD-SD"),
    ("Hardware", "Desktop", "الأجهزة", "الحاسوب المكتبي", "Incident", "ITD-SD"),
    ("Hardware", "Printer", "الأجهزة", "الطابعة", "Incident", "ITD-SD"),
    ("Hardware", "Peripheral", "الأجهزة", "الملحقات", "Incident", "ITD-SD"),
    ("Software", "Office and Outlook", "البرمجيات", "أوفيس وأوتلوك", "Incident", "ITD-SD"),
    ("Software", "Operating system", "البرمجيات", "نظام التشغيل", "Incident", "ITD-SD"),
    ("Software", "Browser", "البرمجيات", "المتصفح", "Incident", "ITD-SD"),
    ("Application", "Core banking", "التطبيقات", "النظام المصرفي الأساسي", "Incident", "ITD-APP"),
    ("Application", "Digital banking", "التطبيقات", "الخدمات المصرفية الرقمية", "Incident", "ITD-APP"),
    ("Application", "Finance system", "التطبيقات", "النظام المالي", "Incident", "ITD-APP"),
    ("Application", "HR system", "التطبيقات", "نظام الموارد البشرية", "Incident", "ITD-APP"),
    ("Application", "ITSM portal", "التطبيقات", "بوابة الخدمة الذاتية", "Incident", "ITD-APP"),
    ("Network", "Branch link", "الشبكات", "ربط الفرع", "Incident", "ITD-NET"),
    ("Network", "Wi-Fi", "الشبكات", "الشبكة اللاسلكية", "Incident", "ITD-NET"),
    ("Network", "VPN", "الشبكات", "الشبكة الافتراضية الخاصة", "Incident", "ITD-NET"),
    ("Network", "LAN and switch", "الشبكات", "الشبكة المحلية والمحولات", "Incident", "ITD-NET"),
    ("Network", "Firewall", "الشبكات", "جدار الحماية", "Incident", "ITD-NET"),
    ("Infrastructure", "Server", "البنية التحتية", "الخادم", "Incident", "ITD-INF"),
    ("Infrastructure", "Database", "البنية التحتية", "قاعدة البيانات", "Incident", "ITD-INF"),
    ("Infrastructure", "Storage and backup", "البنية التحتية", "التخزين والنسخ الاحتياطي", "Incident", "ITD-INF"),
    ("Infrastructure", "Email delivery", "البنية التحتية", "توصيل البريد الإلكتروني", "Incident", "ITD-INF"),
    ("ATM", "Cash dispenser", "الصرافات الآلية", "موزع النقد", "Incident", "ITD-INF"),
    ("ATM", "Card reader", "الصرافات الآلية", "قارئ البطاقات", "Incident", "ITD-INF"),
    ("ATM", "Communication", "الصرافات الآلية", "الاتصال", "Incident", "ITD-INF"),
    ("ATM", "Receipt printer", "الصرافات الآلية", "طابعة الإيصالات", "Incident", "ITD-INF"),
    ("Security", "Phishing", "الأمن", "التصيد الاحتيالي", "Incident", "ITD-SEC"),
    ("Security", "Malware", "الأمن", "البرمجيات الخبيثة", "Incident", "ITD-SEC"),
    ("Security", "Suspicious sign-in", "الأمن", "تسجيل دخول مشبوه", "Incident", "ITD-SEC"),
    ("Security", "Lost or stolen device", "الأمن", "جهاز مفقود أو مسروق", "Incident", "ITD-SEC"),
    ("Onboarding", "New joiner", "التعيين", "موظف جديد", "Request", "ITD-SD"),
    ("Access", "System access", "الصلاحيات", "صلاحية نظام", "Request", "ITD-SEC"),
    ("Access", "Access removal", "الصلاحيات", "إلغاء الصلاحيات", "Request", "ITD-SEC"),
    ("Access", "Password reset", "الصلاحيات", "إعادة تعيين كلمة المرور", "Request", "ITD-SD"),
    ("Hardware", "Laptop or peripheral", "الأجهزة", "حاسوب محمول أو ملحقات", "Request", "ITD-SD"),
    ("Software", "Installation", "البرمجيات", "تثبيت البرامج", "Request", "ITD-SD"),
    ("Network", "Remote access", "الشبكات", "الوصول عن بعد", "Request", "ITD-NET"),
    ("Email", "Mailbox or distribution list", "البريد الإلكتروني", "صندوق بريد أو قائمة توزيع", "Request", "ITD-INF"),
]

SLA_POLICIES = [  # code, name, ticket type, priority, schedule, response minutes, resolution minutes, response text, resolution text
    ("INC-P1", "Incident P1 - critical", "Incident", "P1", "24X7", 15, 240, "15 minutes", "4 hours, around the clock"),
    ("INC-P2", "Incident P2 - high", "Incident", "P2", "HO", 30, 480, "30 business minutes", "8 business hours"),
    ("INC-P3", "Incident P3 - moderate", "Incident", "P3", "HO", 240, 1530, "4 business hours", "3 business days"),
    ("INC-P4", "Incident P4 - low", "Incident", "P4", "HO", 510, 2550, "1 business day", "5 business days"),
    ("REQ-1D", "Request - 1 business day", "Request", None, "HO", 240, 510, "4 business hours", "1 business day"),
    ("REQ-2D", "Request - 2 business days", "Request", None, "HO", 240, 1020, "4 business hours", "2 business days"),
    ("REQ-3D", "Request - 3 business days", "Request", None, "HO", 240, 1530, "4 business hours", "3 business days"),
    ("REQ-5D", "Request - 5 business days", "Request", None, "HO", 240, 2550, "4 business hours", "5 business days"),
]

# Catalogue: code, name, Arabic, category, subcategory, system, fulfilment group, manager approval, owner approval,
# target business days, default priority, fulfilment tasks [(name, group, minutes min, minutes max)], description.
SYSTEM_OWNER = {s[0]: s[4] for s in bp.SYSTEMS}
SYSTEM_NAME = {s[0]: s[1] for s in bp.SYSTEMS}
CATALOG = [
    ("NEW-JOINER", "New joiner setup", "تجهيز موظف جديد", "Onboarding", "New joiner", None, "ITD-SD", 0, 0, 3, "P3",
     [("Create user account and standard access", "ITD-SEC", 15, 45), ("Create mailbox and home drive", "ITD-INF", 10, 30), ("Prepare and deliver computer", "ITD-SD", 30, 120)],
     "Account, mailbox, standard access and computer for a new employee, ready before the first day. Raised by the line manager or HR."),
    ("ACC-CBS", "System access - Core Banking", "طلب صلاحية - النظام المصرفي الأساسي", "Access", "System access", "CBS", "ITD-SEC", 1, 1, 3, "P4",
     [("Grant role in Core Banking", "ITD-SEC", 10, 40)], "A Core Banking role; approved by the line manager and the head of Central Operations (system owner)."),
    ("ACC-DIGITAL", "System access - Digital Banking", "طلب صلاحية - الخدمات المصرفية الرقمية", "Access", "System access", "DIGITAL", "ITD-SEC", 1, 1, 3, "P4",
     [("Grant role in Digital Banking", "ITD-SEC", 10, 40)], "A Digital Banking back-office role; approved by the line manager and the head of Digital Banking and Contact Centre."),
    ("ACC-FIN", "System access - Finance", "طلب صلاحية - النظام المالي", "Access", "System access", "FIN", "ITD-SEC", 1, 1, 3, "P4",
     [("Grant role in Finance system", "ITD-SEC", 10, 40)], "A Finance system role; approved by the line manager and the CFO (system owner)."),
    ("ACC-HRMS", "System access - Personnel and Attendance", "طلب صلاحية - شؤون الموظفين والحضور", "Access", "System access", "HRMS", "ITD-SEC", 1, 1, 3, "P4",
     [("Grant role in Personnel and Attendance", "ITD-SEC", 10, 40)], "A Personnel and Attendance role; approved by the line manager and the CHRO (system owner)."),
    ("ACC-ITSM", "System access - IT Help Desk", "طلب صلاحية - مكتب خدمة تقنية المعلومات", "Access", "System access", "ITSM", "ITD-SEC", 1, 1, 3, "P4",
     [("Grant role in IT Help Desk", "ITD-SEC", 10, 30)], "An IT Help Desk agent role; approved by the line manager and the CIO (system owner)."),
    ("ACC-REMOVE", "Access removal (leaver)", "إلغاء صلاحيات موظف مغادر", "Access", "Access removal", None, "ITD-SEC", 0, 0, 1, "P2",
     [("Collect computer and accessories", "ITD-SD", 10, 30), ("Disable network account, email and system access", "ITD-SEC", 10, 30), ("Remove Core Banking user", "ITD-APP", 5, 15)],
     "Raised by an HR officer (Personnel Services) for every leaver; due by the end of the last working day."),
    ("PWD-RESET", "Password reset or account unlock", "إعادة تعيين كلمة المرور أو فتح الحساب", "Access", "Password reset", None, "ITD-SD", 0, 0, 1, "P3",
     [("Verify identity and reset password", "ITD-SD", 3, 12)], "Network, email or application password reset or account unlock after identity verification."),
    ("HW-REQUEST", "Laptop or peripheral", "حاسوب محمول أو ملحقات", "Hardware", "Laptop or peripheral", None, "ITD-SD", 1, 0, 5, "P4",
     [("Issue item from IT store", "ITD-SD", 10, 40), ("Configure and hand over", "ITD-SD", 15, 75)], "A laptop, monitor, docking station, headset, scanner or other peripheral; approved by the line manager."),
    ("SW-INSTALL", "Software installation", "تثبيت برنامج", "Software", "Installation", None, "ITD-SD", 1, 0, 3, "P4",
     [("Check licence and install software", "ITD-SD", 15, 60)], "Licensed or approved software on the requester's computer; approved by the line manager."),
    ("VPN-ACCESS", "Remote access (VPN)", "الوصول عن بعد (VPN)", "Network", "Remote access", None, "ITD-NET", 1, 0, 2, "P4",
     [("Create VPN profile and token", "ITD-NET", 10, 40), ("Install VPN client", "ITD-SD", 10, 30)], "VPN profile and token for working from home or travelling; approved by the line manager."),
    ("MAILBOX", "Mailbox or distribution list", "صندوق بريد أو قائمة توزيع", "Email", "Mailbox or distribution list", None, "ITD-INF", 0, 0, 2, "P4",
     [("Create or change mailbox or distribution list", "ITD-INF", 10, 40)], "Shared mailbox, distribution list, membership or mailbox size change."),
]
CAT = {c[0]: c for c in CATALOG}
ITEM_WEIGHTS = {"PWD-RESET": 30, "SW-INSTALL": 14, "HW-REQUEST": 16, "ACC-CBS": 7, "ACC-DIGITAL": 1.5, "ACC-FIN": 1.2, "ACC-HRMS": 3, "ACC-ITSM": 0.3,
                "VPN-ACCESS": 5, "MAILBOX": 10}

ACCESS_TEMPLATES = ["{sys} access - role {role}", "Additional {sys} functions for role {role}", "Reinstate {sys} access after extended leave - role {role}",
                    "Temporary {sys} access to cover a colleague - role {role}"]
REQUEST_DESCS = {  # item -> [(text, condition)]; condition: None, system code, 'laptop', 'branch', 'itd', department code
    "PWD-RESET": [("Network (Windows) password reset", None), ("Account locked after too many failed sign-ins", None), ("Password expired while on leave", None),
                  ("Core Banking user locked - please unlock", "CBS"), ("Digital Banking back-office password reset", "DIGITAL"), ("Finance system password reset", "FIN"),
                  ("Personnel self-service password reset", "HRMS")],
    "HW-REQUEST": [("Replacement laptop - current one out of warranty", "laptop"), ("Second monitor for the workstation", None), ("Headset for calls", None),
                   ("Docking station", "laptop"), ("Wireless keyboard and mouse", None), ("Replacement laptop charger", "laptop"), ("Cheque scanner for the counter", "branch"),
                   ("Replacement keyboard and mouse for the shared desk computer", "nouser")],
    "SW-INSTALL": [("Install Microsoft Visio", None), ("Install Power BI Desktop", None), ("Install Adobe Acrobat Pro", None), ("Install Microsoft Project", None),
                   ("Install SQL Server Management Studio", "itd"), ("Install Python and Jupyter for data analysis", "RSK"), ("Install Bloomberg Terminal client", "TRY"),
                   ("Install 7-Zip", None), ("Update Java runtime for the regulator portal", "CMP")],
    "VPN-ACCESS": [("VPN access to work from home", None), ("VPN access for business travel", None), ("Renew VPN certificate and token", None)],
    "MAILBOX": [("Create a shared mailbox for the team", None), ("Add me to a distribution list", None), ("Increase mailbox size limit", None),
                ("Create a distribution list for a project", None), ("Access to the department shared mailbox", None)],
}

# Impact x urgency weights per incident family (priority = impact + urgency: 2 -> P1, 3 -> P2, 4 -> P3, 5-6 -> P4).
IU_EUD = [((3, 3), 30), ((3, 2), 35), ((3, 1), 15), ((2, 2), 15), ((2, 3), 5)]
IU_APP = [((3, 2), 25), ((3, 1), 25), ((2, 2), 25), ((2, 1), 15), ((3, 3), 10)]
IU_NET = [((2, 1), 25), ((2, 2), 30), ((3, 2), 25), ((3, 1), 10), ((3, 3), 10)]
IU_INF = [((3, 2), 40), ((2, 2), 30), ((2, 1), 15), ((3, 3), 15)]
IU_ATM = [((3, 1), 40), ((3, 2), 35), ((2, 1), 10), ((3, 3), 15)]
IU_SEC = [((3, 2), 40), ((3, 1), 30), ((2, 2), 20), ((2, 1), 10)]
RES_HW = ["Hardware replaced", "Fixed", "No fault found", "User guidance"]
RES_SW = ["Fixed", "Configuration change", "User guidance", "Workaround provided"]
RES_APP = ["Fixed", "Configuration change", "Workaround provided", "User guidance", "Vendor fix"]
RES_NET = ["Fixed", "Configuration change", "Vendor fix", "Service restarted"]
RES_INF = ["Service restarted", "Configuration change", "Fixed", "No fault found"]
RES_ATM = ["Hardware replaced", "Fixed", "Vendor fix", "Service restarted"]
RES_SEC = ["Fixed", "User guidance", "Configuration change"]

# Incident types: category, subcategory, group, service (None = the CI's service), weight, monitoring share, user CI mode,
# monitoring CI mode, reporter filter, impact/urgency, user descriptions, monitoring descriptions, resolution codes.
INCIDENT_TYPES = [
    ("Hardware", "Laptop", "ITD-SD", "EUD", 9, 0, "device", None, "laptop", IU_EUD,
     ["Laptop running very slowly", "Laptop does not start", "Laptop battery not charging", "Laptop screen flickering", "Docking station does not detect the monitors"], [], RES_HW),
    ("Hardware", "Desktop", "ITD-SD", "EUD", 7, 0, "device", None, "desktop", IU_EUD,
     ["Desktop computer freezes several times a day", "Desktop computer does not power on", "Blue screen error on the desktop computer", "Desktop computer very slow to start"], [], RES_HW),
    ("Hardware", "Printer", "ITD-SD", "EUD", 8, 0.1, "printer", "printer_any", "any", IU_EUD,
     ["Printer {ci} not printing", "Paper jam on {ci}", "Printer {ci} shows offline", "Poor print quality on {ci}"], ["Printer {ci} offline (print server alert)"], RES_HW),
    ("Hardware", "Peripheral", "ITD-SD", "EUD", 6, 0, "device", None, "user", IU_EUD,
     ["Headset not working", "Second monitor not detected", "Keyboard or mouse not working", "Cheque scanner not detected", "Passbook printer not responding"], [], RES_HW),
    ("Software", "Office and Outlook", "ITD-SD", "EUD", 9, 0, "device", None, "user", IU_EUD,
     ["Outlook keeps crashing", "Outlook not receiving new emails", "Excel workbook will not open", "Teams meeting audio not working", "Cannot open the shared mailbox"], [], RES_SW),
    ("Software", "Operating system", "ITD-SD", "EUD", 4, 0, "device", None, "user", IU_EUD,
     ["Windows update stuck at restart", "Cannot sign in to Windows: user profile error", "C: drive full", "Computer very slow after the latest update"], [], RES_SW),
    ("Software", "Browser", "ITD-SD", "EUD", 2, 0, "device", None, "user", IU_EUD,
     ["Intranet page not loading in the browser", "Browser shows a certificate warning on an internal site"], [], RES_SW),
    ("Application", "Core banking", "ITD-APP", "CBS", 9, 0.08, "mod:CBS", "mod:CBS", "cbs", IU_APP,
     ["Error posting a cash deposit in {ci}", "Cannot print an account statement from {ci}", "Core banking session times out repeatedly", "Customer search very slow in {ci}",
      "Cheque deposit not posting in {ci}", "Till balancing difference shown in {ci}", "Standing order not executed ({ci})"],
     ["Core banking interface queue backlog on {ci}", "Core banking batch job failed on {ci}"], RES_APP),
    ("Application", "Digital banking", "ITD-APP", "DIGITAL", 5, 0.25, "mod:DIGITAL", "mod:DIGITAL", "digital", IU_APP,
     ["Customer cannot log in to mobile banking", "Customer did not receive the OTP SMS", "Internet banking transfer stuck in pending", "Digital onboarding document upload failing",
      "Contact-centre console not showing the customer profile"], ["Error rate above threshold on {ci}", "Response time above 3 seconds on {ci}"], RES_APP),
    ("Application", "Finance system", "ITD-APP", "FIN", 2, 0, "mod:FIN", None, "fin", IU_APP,
     ["Cannot post a journal in {ci}", "Purchase order approval not routed in {ci}", "Supplier payment file rejected", "Budget report not generating in {ci}"], [], RES_APP),
    ("Application", "HR system", "ITD-APP", "HRMS", 3, 0, "mod:HRMS", None, "user", IU_APP,
     ["Leave balance not updated in self-service", "Attendance punch missing for yesterday", "Payslip not visible in self-service", "Cannot submit a leave request"], [], RES_APP),
    ("Application", "ITSM portal", "ITD-APP", "ITSM", 0.6, 0, "mod:ITSM", None, "user", IU_APP,
     ["Self-service portal page not loading", "Approval email link does not open the request"], [], RES_APP),
    ("Network", "Branch link", "ITD-NET", "NETWORK", 3, 0.6, "link", "link_any", "branch", IU_NET,
     ["Branch network very slow at {loc}", "Branch systems keep disconnecting at {loc}"],
     ["WAN link {ci} down", "Packet loss above 5% on {ci}", "Branch failed over to the backup link ({ci})"], RES_NET),
    ("Network", "Wi-Fi", "ITD-NET", "NETWORK", 2.5, 0.1, "wifi", "wifi", "ho", IU_NET,
     ["Wi-Fi drops frequently on this floor", "Cannot connect to the staff Wi-Fi"], ["Access point {ci} not responding"], RES_NET),
    ("Network", "VPN", "ITD-NET", "NETWORK", 2, 0.15, "device", "vpn", "remote", IU_NET,
     ["VPN not connecting from home", "VPN disconnects every few minutes"], ["VPN gateway {ci} tunnel flapping"], RES_NET),
    ("Network", "LAN and switch", "ITD-NET", "NETWORK", 2, 0.4, "lan", "switch_any", "user", IU_NET,
     ["Network port at the desk not working", "No network on several desks ({ci})"], ["Switch {ci} not responding", "Port errors above threshold on {ci}"], RES_NET),
    ("Network", "Firewall", "ITD-NET", "NETWORK", 1, 0.9, "fw", "fw_any", "user", IU_NET,
     ["Website needed for work is blocked"], ["Firewall {ci} CPU above 90%", "Firewall {ci} failover event"], RES_NET),
    ("Infrastructure", "Server", "ITD-INF", None, 5, 0.9, "server_user", "server_any", "user", IU_INF,
     ["Shared drive on {ci} not accessible", "Cannot add a network printer ({ci})"],
     ["Disk space above 90% on {ci}", "CPU above 95% for 15 minutes on {ci}", "Windows service stopped on {ci}", "Server {ci} not responding to ping", "Memory above 90% on {ci}"], RES_INF),
    ("Infrastructure", "Database", "ITD-INF", None, 2, 0.95, "db_any", "db_any", "user", IU_INF,
     ["Reports from {ci} very slow"], ["Long-running blocking sessions on {ci}", "Transaction log space low on {ci}", "Database {ci} failed over to the secondary node"], RES_INF),
    ("Infrastructure", "Storage and backup", "ITD-INF", None, 2, 1.0, None, "backup_any", "user", IU_INF,
     [], ["Nightly backup failed for {ci}", "Backup job for {ci} exceeded its window"], RES_INF),
    ("Infrastructure", "Email delivery", "ITD-INF", "EMAIL", 3, 0.3, "device", "exch", "user", IU_INF,
     ["Emails to external recipients delayed", "Mailbox full - cannot send", "Emails from a client going to junk"],
     ["Mail queue above threshold on {ci}", "Certificate on {ci} expires in 14 days"], RES_INF),
    ("ATM", "Cash dispenser", "ITD-INF", "ATM", 3, 0.3, "atm", "atm_any", "branch", IU_ATM,
     ["Cash dispenser jammed on {ci}", "{ci} dispensed less cash than requested", "{ci} out of cash before replenishment"], ["Cash dispenser fault reported by {ci}"], RES_ATM),
    ("ATM", "Card reader", "ITD-INF", "ATM", 2, 0.3, "atm", "atm_any", "branch", IU_ATM,
     ["Card reader on {ci} retaining cards", "{ci} cannot read chip cards"], ["Card reader fault reported by {ci}"], RES_ATM),
    ("ATM", "Communication", "ITD-INF", "ATM", 2, 0.9, "atm", "atm_any", "branch", IU_ATM,
     ["{ci} shows Out of service"], ["{ci} offline: host not responding", "{ci} lost communication with the ATM switch"], RES_ATM),
    ("ATM", "Receipt printer", "ITD-INF", "ATM", 1.5, 0.7, "atm", "atm_any", "branch", IU_ATM,
     ["Receipt printer fault on {ci}"], ["Receipt printer paper low on {ci}"], RES_ATM),
    ("Security", "Phishing", "ITD-SEC", "EMAIL", 1, 0, "gw", None, "user", IU_SEC,
     ["Suspicious email asking to verify my account", "Email with an unexpected invoice attachment", "Email pretending to be from the CEO asking for gift cards"], [], RES_SEC),
    ("Security", "Malware", "ITD-SEC", "EUD", 1, 0.8, "device", "device_any", "user", IU_SEC,
     ["Pop-ups and unknown programs on my computer"], ["Endpoint protection quarantined malware on {ci}", "Endpoint protection blocked a suspicious script on {ci}"], RES_SEC),
    ("Security", "Suspicious sign-in", "ITD-SEC", "EUD", 0.8, 0.9, "dc", "dc", "user", IU_SEC,
     ["Received sign-in approval prompts I did not request"], ["Multiple failed sign-ins for one account ({ci})", "Sign-in from an unusual country blocked ({ci})"], RES_SEC),
    ("Security", "Lost or stolen device", "ITD-SEC", "EUD", 0.2, 0, "device", None, "laptop", IU_SEC,
     ["Laptop lost while travelling", "Laptop stolen from a car"], [], RES_SEC),
]
WORK_NOTES = {  # category -> four work-note texts (picked with gen.Rand in T-SQL)
    "Hardware": ["Remote session taken; diagnostics running.", "Spare unit reserved in the IT store.", "Visited the desk; hardware checked.", "Vendor warranty case opened."],
    "Software": ["Remote session taken; profile repaired.", "Application reinstalled; waiting for user confirmation.", "Known issue; workaround shared with the user.", "Cache cleared and settings reset."],
    "Application": ["Reproduced in the test environment.", "Application logs collected and reviewed.", "Vendor support ticket raised.", "Data correction prepared for approval."],
    "Network": ["Link tested with the carrier; ticket raised.", "Interface counters cleared and monitored.", "Port configuration checked.", "Traffic captured for analysis."],
    "Infrastructure": ["Disk cleanup and log rotation done.", "Service restarted; monitoring.", "Capacity increase requested.", "Root cause under investigation."],
    "ATM": ["Branch staff asked to check the device.", "Vendor engineer dispatched.", "Remote reset sent from the ATM switch.", "Cash-in-transit team informed."],
    "Security": ["Indicators checked against the threat feed.", "User contacted; credentials reset as a precaution.", "Endpoint isolated and scanned.", "Sender and URLs blocked at the gateway."],
}
RANDOM_P1 = [  # category, subcategory, CI, description (monitoring, not core banking)
    ("Application", "Digital banking", "DIG-MOB", "Mobile banking unavailable for all customers"),
    ("Infrastructure", "Email delivery", "EXCH-MBX-01", "Email service unavailable for all staff"),
    ("ATM", "Communication", "ATM-SW-01", "ATM switch not responding - all ATMs out of service"),
    ("Network", "LAN and switch", "NET-CORE-01", "Head Office core switch failure - several floors without network"),
    ("Application", "Digital banking", "DIG-IB", "Internet banking sign-in failing for all customers"),
    ("Network", "Firewall", "FW-HO-01", "Perimeter firewall failure - internet and digital banking unreachable"),
]

# ------------------------------------------------------------------------------------------------ documentation lists
OBJECT_DOCS = [
    ("Locations", "Locations", "المواقع", "Head Office and the six banking branches (location codes shared with Personnel) with emirate, address, opening date and teller counters."),
    ("EmployeeDirectory", "Employee directory", "دليل الموظفين",
     "Every Wabi Bank employee of 2025, leavers included, as synchronised from Personnel: employee number, names, department, section and location (current at year end, or at termination), work email, login (UserId; none for drivers, messengers and guards), active flag, hire and termination dates."),
    ("Users", "Help-desk users", "مستخدمو مكتب الخدمة",
     "Logins with IT Help Desk access during 2025 and their role: Requester (self-service), Agent (IT staff), GroupLead (IT section heads), ServiceDeskManager, and the SYS_MONITOR service account that opens monitoring incidents; access granted and revoked dates."),
    ("AssignmentGroups", "Assignment groups", "مجموعات الإسناد", "The five IT sections that work tickets (Service Desk, Infrastructure, Applications Support, Network, Information Security) with their group lead."),
    ("Services", "IT services", "خدمات تقنية المعلومات", "Business and technical services supported by IT (core banking, digital banking, finance, HR, email, network, ATMs, end-user devices, the self-service portal) with owning group and criticality."),
    ("ConfigurationItems", "Configuration items", "عناصر التهيئة",
     "The CMDB: servers, databases, application modules, branch WAN and backup links, switches, Wi-Fi, VPN, firewalls, printers, ATMs (2-4 per branch) and staff laptops and desktops, with service, location, owning group, assigned employee, asset tag, model and status."),
    ("Categories", "Ticket categories", "تصنيفات التذاكر", "Category and subcategory of incidents and requests with the default assignment group."),
    ("SlaPolicies", "SLA policies", "سياسات مستوى الخدمة",
     "Response and resolution targets: incidents by priority (P1 15 minutes / 4 hours around the clock; P2 30 minutes / 8 business hours; P3 4 business hours / 3 business days; P4 1 / 5 business days) and requests by catalogue target."),
    ("BusinessHours", "Business hours", "ساعات العمل", "One row per day of 2025 for each schedule: HO (Monday-Friday 08:00-16:30, closed on UAE public holidays) used by SLAs, and 24X7 used by P1 incidents."),
    ("ServiceCatalogItems", "Service catalogue", "كتالوج الخدمات", "What employees can request: new joiner setup, system access per system, access removal, password reset, laptop or peripheral, software, VPN, mailbox or distribution list; fulfilment group, approvals needed and target days."),
    ("ServiceRequests", "Service requests", "طلبات الخدمة",
     "Requests raised in 2025: catalogue item, requested-for and requested-by employee, the requested-for person's department, section and location at logging time, system and role for access requests, channel, creator, assignment group and agent, priority, status, opened, approved, fulfilled and closed times, SLA due time and breach flag."),
    ("RequestApprovals", "Request approvals", "موافقات الطلبات", "Approval steps of requests: line manager, then the system owner's department head for system access; approver, requested and decided times and decision (Approved, Rejected, Pending)."),
    ("RequestTasks", "Request fulfilment tasks", "مهام تنفيذ الطلبات", "Fulfilment steps of each request with the IT group and agent who did them, start and completion times."),
    ("Incidents", "Incidents", "الحوادث",
     "Incidents of 2025 from users (Portal, Phone, Email) and monitoring: service, configuration item, category, impact, urgency, priority (P1-P4), reporter and their department, section and location, creator, channel, assignment group and agent, status, opened, responded, resolved and closed times with SLA due times and breach flag, resolution code, major-incident flag and parent incident."),
    ("IncidentActivities", "Incident activity log", "سجل نشاط الحوادث", "Every change on an incident in time order: creation, group and agent assignments and reassignments, status changes, work notes and links to a parent incident, each by a user at a time."),
    ("vw_IncidentsLive", "Incidents (live December)", "الحوادث (ديسمبر مباشر)",
     "The incidents opened in December 2025 with every date moved forward so that 31 December 2025 is today in Asia/Dubai; lets rolling-window Monitoring Rules find the planted cases whenever the sample is used. Analyses of 2025 use Incidents."),
]
COLUMN_DOCS = [
    ("EmployeeDirectory", "EmployeeNumber", "Employee number", "الرقم الوظيفي", "Personnel employee number (W1001 ...), also used by Wabi Insight user accounts.", None),
    ("EmployeeDirectory", "UserId", "Login", "اسم الدخول", "Single sign-on login shared by all bank systems; empty for drivers, messengers and security guards.", None),
    ("EmployeeDirectory", "Email", "Work email", "البريد الإلكتروني للعمل", None, "Internal"),
    ("EmployeeDirectory", "IsActive", "Active", "نشط", "0 for employees who left during 2025.", None),
    ("EmployeeDirectory", "DepartmentCode", "Department", "الإدارة", "Head Office department or branch (BR-...) at year end, or at termination for leavers.", None),
    ("Users", "RoleCode", "Help-desk role", "الدور في مكتب الخدمة", "Requester, Agent, GroupLead, ServiceDeskManager or ServiceAccount.", None),
    ("Users", "AccessRevokedDate", "Access revoked", "تاريخ إلغاء الصلاحية", "Last day the login could use the help desk; empty while active.", None),
    ("ConfigurationItems", "CiCode", "Configuration item", "عنصر التهيئة", "CMDB code, e.g. ATM-SHJ01-02, CBS-DB-01, LT0123 (laptop), DT0045 (desktop).", None),
    ("ConfigurationItems", "CiType", "CI type", "نوع العنصر", "Server, Database, Application, Network link, Switch, Wi-Fi, VPN gateway, Firewall, Printer, ATM, Laptop or Desktop.", None),
    ("ConfigurationItems", "AssignedToEmployeeNumber", "Assigned to", "مسند إلى", "Employee who uses the laptop or desktop; empty for shared equipment and returned devices.", None),
    ("ConfigurationItems", "Status", "CI status", "حالة العنصر", "Operational (shared equipment), In use (assigned device) or In stock (returned by a leaver).", None),
    ("ServiceCatalogItems", "TargetDays", "Target (business days)", "المدة المستهدفة (أيام عمل)", "Fulfilment target counted in Head Office business days from approval (or from opening when no approval is needed).", None),
    ("ServiceRequests", "RequestNumber", "Request number", "رقم الطلب", "REQ followed by seven digits, numbered in the order requests were opened.", None),
    ("ServiceRequests", "RequestedForEmployeeNumber", "Requested for", "مطلوب لصالح", "The employee who receives the service (a new joiner, a leaver, or the requester themself).", None),
    ("ServiceRequests", "RequestedByEmployeeNumber", "Requested by", "مقدم الطلب", "The employee who asked: the requester, or the line manager / HR officer for joiners and leavers.", None),
    ("ServiceRequests", "DepartmentCode", "Department", "الإدارة", "Department of the requested-for employee when the request was logged.", None),
    ("ServiceRequests", "SectionCode", "Section", "القسم", "Section of the requested-for employee when the request was logged.", None),
    ("ServiceRequests", "LocationCode", "Location", "الموقع", "Location of the requested-for employee when the request was logged.", None),
    ("ServiceRequests", "Channel", "Channel", "القناة", "Portal (self-service by the requester), Phone, Email or WalkIn (logged by a Service Desk agent).", None),
    ("ServiceRequests", "CreatedByUserId", "Created by", "أنشئ بواسطة", "Login that created the request: the requester on the Portal, otherwise the Service Desk agent.", None),
    ("ServiceRequests", "Status", "Status", "الحالة", "New (in the queue), Awaiting approval, In progress, Fulfilled, Rejected or Cancelled, as of 31 December 2025.", None),
    ("ServiceRequests", "ApprovedAt", "Approved at", "وقت الموافقة", "Last approval; the SLA clock starts here (or at opening when no approval is needed).", None),
    ("ServiceRequests", "FulfilledAt", "Fulfilled at", "وقت التنفيذ", "Completion of the last fulfilment task.", None),
    ("ServiceRequests", "ClosedAt", "Closed at", "وقت الإغلاق", "Automatic closure two business days after fulfilment, or the rejection or cancellation time.", None),
    ("ServiceRequests", "SlaDueAt", "SLA due", "موعد اتفاقية الخدمة", "Approval (or opening) plus the catalogue target in business days; access removals are due no earlier than the end of the leaver's last working day, joiner setups no earlier than the day before the start date.", None),
    ("ServiceRequests", "SlaBreached", "SLA breached", "تجاوز اتفاقية الخدمة", "1 when fulfilled after the due time, or still open after it at year end.", None),
    ("ServiceRequests", "ShortDescription", "Short description", "وصف مختصر", None, "Confidential"),
    ("RequestApprovals", "ApprovalType", "Approval type", "نوع الموافقة", "LineManager or SystemOwner (head of the department that owns the system).", None),
    ("RequestApprovals", "Decision", "Decision", "القرار", "Approved, Rejected or Pending (not decided by 31 December 2025).", None),
    ("RequestApprovals", "Comments", "Comments", "ملاحظات", None, "Confidential"),
    ("Incidents", "IncidentNumber", "Incident number", "رقم الحادث", "INC followed by seven digits, numbered in the order incidents were opened.", None),
    ("Incidents", "CiCode", "Configuration item", "عنصر التهيئة", "The affected configuration item (device, server, application module, link, ATM ...).", None),
    ("Incidents", "Impact", "Impact", "الأثر", "1 = bank-wide or many users, 2 = a branch, department or group of users, 3 = one user or device.", None),
    ("Incidents", "Urgency", "Urgency", "الاستعجال", "1 = work stopped, 2 = work degraded, 3 = can wait.", None),
    ("Incidents", "Priority", "Priority", "الأولوية", "From impact x urgency: P1 critical, P2 high, P3 moderate, P4 low.", None),
    ("Incidents", "ReportedByEmployeeNumber", "Reported by", "المبلغ", "Employee who reported it; empty for monitoring alerts.", None),
    ("Incidents", "DepartmentCode", "Department", "الإدارة", "Reporter's department when the incident was opened; empty for monitoring alerts.", None),
    ("Incidents", "SectionCode", "Section", "القسم", "Reporter's section when the incident was opened.", None),
    ("Incidents", "LocationCode", "Location", "الموقع", "Reporter's location when opened; for monitoring alerts the location of the configuration item.", None),
    ("Incidents", "Channel", "Channel", "القناة", "Portal, Phone, Email (logged by a Service Desk agent) or Monitoring (SYS_MONITOR).", None),
    ("Incidents", "Status", "Status", "الحالة", "New, In Progress, Resolved or Closed, as of 31 December 2025.", None),
    ("Incidents", "RespondedAt", "Responded at", "وقت الاستجابة", "When an agent took the incident (status In Progress).", None),
    ("Incidents", "ResponseDueAt", "Response due", "موعد الاستجابة", "Response target of the priority (P1 around the clock, others in Head Office business hours).", None),
    ("Incidents", "ResolutionDueAt", "Resolution due", "موعد الحل", "Resolution target of the priority (P1 around the clock, others in Head Office business hours).", None),
    ("Incidents", "SlaBreached", "SLA breached", "تجاوز اتفاقية الخدمة", "1 when the response or the resolution was later than due, or the incident was still open after the due time at year end.", None),
    ("Incidents", "IsMajorIncident", "Major incident", "حادث كبير", "1 for a declared major incident.", None),
    ("Incidents", "ParentIncidentNumber", "Parent incident", "الحادث الرئيسي", "Set on user reports linked to a parent (major or security) incident.", None),
    ("Incidents", "ShortDescription", "Short description", "وصف مختصر", None, "Confidential"),
    ("IncidentActivities", "ActivityType", "Activity type", "نوع النشاط", "Created, Assignment, StatusChange, WorkNote or Link.", None),
    ("IncidentActivities", "FieldName", "Field", "الحقل", "Status, AssignmentGroup, AssignedTo or ParentIncident for changes; empty for work notes.", None),
    ("IncidentActivities", "Note", "Note", "ملاحظة", None, "Confidential"),
    ("vw_IncidentsLive", "OpenedAt", "Opened at", "وقت الفتح", "Local time, shifted so that 31 December 2025 is today.", None),
    ("vw_IncidentsLive", "CiCode", "Configuration item", "عنصر التهيئة", "The affected configuration item.", None),
    ("vw_IncidentsLive", "ShortDescription", "Short description", "وصف مختصر", None, "Confidential"),
]
FIELD_ROLES = [
    ("EmployeeDirectory", "EmployeeNumber", 2), ("EmployeeDirectory", "UserId", 3), ("EmployeeDirectory", "LocationCode", 9),
    ("EmployeeDirectory", "HireDate", 5), ("EmployeeDirectory", "TerminationDate", 5),
    ("Users", "UserId", 3), ("Users", "EmployeeNumber", 2), ("Users", "Status", 8), ("Users", "AccessGrantedDate", 5), ("Users", "AccessRevokedDate", 5),
    ("Locations", "LocationCode", 9),
    ("ConfigurationItems", "CiCode", 1), ("ConfigurationItems", "AssetTag", 4), ("ConfigurationItems", "AssignedToEmployeeNumber", 2),
    ("ConfigurationItems", "LocationCode", 9), ("ConfigurationItems", "Status", 8), ("ConfigurationItems", "InstalledDate", 5),
    ("ServiceCatalogItems", "TargetDays", 7),
    ("ServiceRequests", "RequestNumber", 1), ("ServiceRequests", "RequestedForEmployeeNumber", 2), ("ServiceRequests", "RequestedByEmployeeNumber", 2),
    ("ServiceRequests", "CreatedByUserId", 3), ("ServiceRequests", "AssignedToUserId", 3), ("ServiceRequests", "LocationCode", 9),
    ("ServiceRequests", "OpenedAt", 5), ("ServiceRequests", "ApprovedAt", 5), ("ServiceRequests", "FulfilledAt", 5), ("ServiceRequests", "ClosedAt", 5),
    ("ServiceRequests", "SlaDueAt", 5), ("ServiceRequests", "Priority", 8), ("ServiceRequests", "Status", 8), ("ServiceRequests", "SlaBreached", 8),
    ("RequestApprovals", "RequestNumber", 1), ("RequestApprovals", "ApproverUserId", 3), ("RequestApprovals", "RequestedAt", 5), ("RequestApprovals", "DecidedAt", 5),
    ("RequestApprovals", "Decision", 8),
    ("RequestTasks", "RequestNumber", 1), ("RequestTasks", "AssignedToUserId", 3), ("RequestTasks", "StartedAt", 5), ("RequestTasks", "CompletedAt", 5), ("RequestTasks", "Status", 8),
    ("Incidents", "IncidentNumber", 1), ("Incidents", "CiCode", 1), ("Incidents", "ReportedByEmployeeNumber", 2), ("Incidents", "CreatedByUserId", 3),
    ("Incidents", "AssignedToUserId", 3), ("Incidents", "ResolvedByUserId", 3), ("Incidents", "LocationCode", 9),
    ("Incidents", "OpenedAt", 5), ("Incidents", "RespondedAt", 5), ("Incidents", "ResolvedAt", 5), ("Incidents", "ClosedAt", 5),
    ("Incidents", "Priority", 8), ("Incidents", "Status", 8), ("Incidents", "SlaBreached", 8),
    ("IncidentActivities", "IncidentNumber", 1), ("IncidentActivities", "UserId", 3), ("IncidentActivities", "ActivityAt", 5), ("IncidentActivities", "ActivityType", 8),
    ("vw_IncidentsLive", "IncidentNumber", 1), ("vw_IncidentsLive", "CiCode", 1), ("vw_IncidentsLive", "ReportedByEmployeeNumber", 2),
    ("vw_IncidentsLive", "CreatedByUserId", 3), ("vw_IncidentsLive", "AssignedToUserId", 3), ("vw_IncidentsLive", "ResolvedByUserId", 3), ("vw_IncidentsLive", "LocationCode", 9),
    ("vw_IncidentsLive", "OpenedAt", 5), ("vw_IncidentsLive", "RespondedAt", 5), ("vw_IncidentsLive", "ResolvedAt", 5), ("vw_IncidentsLive", "ClosedAt", 5),
    ("vw_IncidentsLive", "Priority", 8), ("vw_IncidentsLive", "Status", 8), ("vw_IncidentsLive", "SlaBreached", 8),
]
ROW_SCOPES = [
    ("ServiceRequests", "RequestedForEmployeeNumber", "DepartmentCode", "SectionCode"),
    ("Incidents", "ReportedByEmployeeNumber", "DepartmentCode", "SectionCode"),
    ("vw_IncidentsLive", "ReportedByEmployeeNumber", "DepartmentCode", "SectionCode"),
    ("EmployeeDirectory", "EmployeeNumber", "DepartmentCode", "SectionCode"),
    ("Users", "EmployeeNumber", "DepartmentCode", "SectionCode"),
    ("ConfigurationItems", "AssignedToEmployeeNumber", None, None),
]


# ------------------------------------------------------------------------------------------------ time helpers
def mod(t):
    return t.hour * 60 + t.minute


def at(d, minutes):
    return dt.datetime.combine(d, dt.time()) + dt.timedelta(minutes=minutes)


def mins(n):
    return dt.timedelta(minutes=n)


def is_bday(d):
    return d.weekday() < 5 and d not in bp.HOLIDAYS


def next_bday(d):
    d += dt.timedelta(days=1)
    while not is_bday(d):
        d += dt.timedelta(days=1)
    return d


def prev_bday(d):
    d -= dt.timedelta(days=1)
    while not is_bday(d):
        d -= dt.timedelta(days=1)
    return d


def in_bh(t):
    return is_bday(t.date()) and BH[0] <= mod(t) <= BH[1]


def next_bh(t):
    d = t.date()
    if is_bday(d) and mod(t) <= BH[1]:
        return max(t, at(d, BH[0]))
    return at(next_bday(d), BH[0])


def add_bmin(t, n):
    """t plus n Head Office business minutes (Mon-Fri 08:00-16:30 without UAE public holidays)."""
    t, n = next_bh(t), int(n)
    while True:
        rem = BH[1] - mod(t)
        if n <= rem:
            return t + mins(n)
        n -= rem
        t = at(next_bday(t.date()), BH[0])


def window_start(t):
    return at(t.date(), BH[0]) if in_bh(t) else at(t.date(), 0)


def window_end(t):
    return at(t.date(), BH[1]) if in_bh(t) else at(t.date(), 1439)


def minute_of_year(t):
    return int((t - T0).total_seconds() // 60)


def priority(impact, urgency):
    return {2: 1, 3: 2, 4: 3}.get(impact + urgency, 4)


# ------------------------------------------------------------------------------------------------ people helpers
EMP = bp.by_number()


@lru_cache(maxsize=None)
def present(no, d):
    return bp.present(EMP[no], d)


@lru_cache(maxsize=None)
def available(no, d):
    return bp.available(EMP[no], d)


@lru_cache(maxsize=None)
def itsm(uid, d):
    return bp.has_access(uid, "ITSM", d)


@lru_cache(maxsize=None)
def unit(no, d):
    return bp.unit_on(EMP[no], d)[:3]


@lru_cache(maxsize=None)
def manager(no, d):
    m = bp.manager_on(EMP[no], d)
    return m["EmployeeNumber"] if m else None


@lru_cache(maxsize=None)
def head(unit_code, d):
    h = bp.head_of(unit_code, d)
    return h["EmployeeNumber"] if h else None


@lru_cache(maxsize=None)
def roster(group, d, oncall):
    """Members of an IT group with help-desk access: at work that day, or (oncall) not on leave or absent."""
    return tuple(p["EmployeeNumber"] for p in bp.staff_on(d, group, system="ITSM", presence=not oncall))


@lru_cache(maxsize=None)
def present_people(d):
    return tuple(p["EmployeeNumber"] for p in bp.employees() if present(p["EmployeeNumber"], d))


def can_act(no, t, oncall_group=None):
    """The staff-action rule: help-desk access that day and at work within the shift, or on-call (P1/P2 work of
    ITD-INF/ITD-NET/ITD-APP) by a member of that group who is not on leave or absent."""
    p = EMP[no]
    d = t.date()
    if not p["UserId"] or not itsm(p["UserId"], d) or not (T0 <= t <= END):
        return False
    s0, s1 = SHIFT_WINDOWS[p["ShiftCode"]]
    if present(no, d) and s0 <= mod(t) <= s1:
        return True
    return oncall_group in ONCALL_GROUPS and available(no, d) and unit(no, d)[1] == oncall_group


def employed(no, d):
    return bp.employed(EMP[no], d)


# ------------------------------------------------------------------------------------------------ generator
class Ticket(dict):
    __getattr__ = dict.get

    def __setattr__(self, k, v):
        self[k] = v


class Generator:
    def __init__(self):
        self.r = rng(20250525)
        self.descs, self.desc_index = [], {}
        self.cis, self.ci = [], {}
        self.ci_days = defaultdict(list)
        self.device = {}
        self.requests, self.incidents = [], []
        self.units, self.unit_index = [], {}
        self.users = bp.local_users("ITSM")
        self.user_index = {u["UserId"]: i for i, u in enumerate(self.users, 1)}
        days = [d for d in bp.DAYS_2025]
        month_factor = {1: 1.1, 2: 1.0, 3: 0.95, 4: 1.0, 5: 1.0, 6: 0.95, 7: 0.85, 8: 0.8, 9: 1.1, 10: 1.05, 11: 1.0, 12: 0.9}
        weights = [month_factor[d.month] * (1.15 if d.weekday() == 0 else 1.0) if is_bday(d) else 0.07 for d in days]
        self.days, self.day_cum = days, []
        acc = 0
        for w in weights:
            acc += w
            self.day_cum.append(acc)

    # ---------------------------------------------------------------- small helpers
    def desc(self, text):
        if text not in self.desc_index:
            self.descs.append(text)
            self.desc_index[text] = len(self.descs)
        return self.desc_index[text]

    def unit_id(self, u):
        if u not in self.unit_index:
            self.units.append(u)
            self.unit_index[u] = len(self.units)
        return self.unit_index[u]

    def pick_day(self):
        return self.r.choices(self.days, cum_weights=self.day_cum)[0]

    def pick_agent(self, cands):
        return self.r.choices(cands, weights=[1.0 if EMP[c]["Level"] == "Staff" else 0.12 for c in cands])[0]

    def act(self, group, t, oncall=False, prefer=None):
        """Earliest moment >= t at which a member of the group can act; returns (time, employee number)."""
        og = group if oncall and group in ONCALL_GROUPS else None
        for _ in range(400):
            d = t.date()
            if in_bh(t):
                cands = roster(group, d, False)
            elif og:
                cands = roster(group, d, True)
            else:
                cands = ()
            if cands:
                return t, (prefer if prefer in cands else self.pick_agent(cands))
            t = (at(next_bday(d), BH[0]) if in_bh(t) else next_bh(t)) + mins(self.r.randint(0, 20))
        raise AssertionError(("nobody can act", group, t))

    def schedule(self, group, t, duration, oncall=False, prefer=None):
        """A task of `duration` minutes inside one working window, starting at or after t: (start, end, agent)."""
        for _ in range(100):
            s, agent = self.act(group, t, oncall, prefer)
            e = s + mins(duration)
            if in_bh(s):
                if e.date() == s.date() and mod(e) <= BH[1]:
                    return s, e, agent
                t = at(next_bday(s.date()), BH[0]) + mins(self.r.randint(0, 30))
                continue
            return s, min(e, at(s.date(), 1439)), agent
        raise AssertionError(("cannot schedule", group, t))

    def time_in_shift(self, no, d, after=None, span=None):
        s0, s1 = SHIFT_WINDOWS[EMP[no]["ShiftCode"]]
        lo = s0 if after is None or after.date() != d else max(s0, mod(after) + 1)
        hi = s1 if span is None else min(s1, lo + span)
        if lo > hi:
            return None
        return at(d, int(self.r.triangular(lo, hi, lo + (hi - lo) * 0.25)))

    def has_device(self, no, d, kind=None):
        code = self.device.get(no)
        if not code:
            return False
        c = self.ci[code]
        return c["InstalledDate"] <= d and (kind is None or c["CiType"] == kind)

    def ci_free(self, code, d):
        if code == PLANTED_ATM and d >= dt.date(2025, 10, 1):
            return False
        o = d.toordinal()
        return sum(1 for x in self.ci_days[code] if abs(x - o) <= 7) <= 2

    def use_ci(self, code, d):
        self.ci_days[code].append(d.toordinal())

    # ---------------------------------------------------------------- configuration items
    def build_cis(self):
        r = self.r
        tags = iter(range(10001, 99999))

        def add(code, name, ci_type, svc, loc, group, model, installed, status="Operational", emp=None):
            c = {"CiCode": code, "CiName": name, "CiType": ci_type, "ServiceCode": svc, "LocationCode": loc, "OwningGroupCode": group,
                 "AssignedToEmployeeNumber": emp, "AssetTag": f"WBK-{next(tags)}", "Model": model, "Status": status, "InstalledDate": installed}
            self.cis.append(c)
            self.ci[code] = c
            return code

        def when(lo="2019-01-01", hi="2024-11-30"):
            a, b = dt.date.fromisoformat(lo), dt.date.fromisoformat(hi)
            return a + dt.timedelta(days=r.randint(0, (b - a).days))

        self.modes = defaultdict(list)
        servers = [("CBS-APP-01", "Core banking application server 1", "CBS"), ("CBS-APP-02", "Core banking application server 2", "CBS"),
                   ("DIG-WEB-01", "Digital banking web server 1", "DIGITAL"), ("DIG-WEB-02", "Digital banking web server 2", "DIGITAL"),
                   ("DIG-API-01", "Digital banking API server", "DIGITAL"), ("FIN-APP-01", "Finance application server", "FIN"),
                   ("HRMS-APP-01", "Personnel and attendance application server", "HRMS"), ("ITSM-APP-01", "IT Help Desk application server", "ITSM"),
                   ("EXCH-MBX-01", "Exchange mailbox server 1", "EMAIL"), ("EXCH-MBX-02", "Exchange mailbox server 2", "EMAIL"),
                   ("EXCH-GW-01", "Email security gateway", "EMAIL"), ("AD-DC-01", "Domain controller 1", "EUD"), ("AD-DC-02", "Domain controller 2", "EUD"),
                   ("FS-01", "File server", "EUD"), ("PRN-SRV-01", "Print server", "EUD"), ("ATM-SW-01", "ATM switch server", "ATM")]
        for code, name, svc in servers:
            add(code, name, "Server", svc, "HO", "ITD-INF", "HPE ProLiant DL380 Gen10" if not code.startswith("EXCH-GW") else "Email security appliance", when())
            self.modes["server_any"].append(code)
        for code, name, svc in [("CBS-DB-01", "Core banking database cluster", "CBS"), ("DIG-DB-01", "Digital banking database", "DIGITAL"),
                                ("FIN-DB-01", "Finance database", "FIN"), ("HRMS-DB-01", "Personnel and attendance database", "HRMS"),
                                ("ITSM-DB-01", "IT Help Desk database", "ITSM")]:
            add(code, name, "Database", svc, "HO", "ITD-INF", "Microsoft SQL Server 2019 cluster" if code != "CBS-DB-01" else "Oracle Database 19c RAC", when())
            self.modes["db_any"].append(code)
        self.modes["backup_any"] = self.modes["server_any"] + self.modes["db_any"]
        self.modes["server_user"] = ["FS-01", "PRN-SRV-01"]
        self.modes["exch"] = ["EXCH-MBX-01", "EXCH-MBX-02", "EXCH-GW-01"]
        self.modes["gw"] = ["EXCH-GW-01"]
        self.modes["dc"] = ["AD-DC-01", "AD-DC-02"]
        modules = {"CBS": [("CBS-TLR", "Core banking - Teller"), ("CBS-CIF", "Core banking - Customer information"), ("CBS-DEP", "Core banking - Deposits and accounts"),
                           ("CBS-LNS", "Core banking - Loans"), ("CBS-PAY", "Core banking - Payments and remittances"), ("CBS-TRF", "Core banking - Trade finance"),
                           ("CBS-CLR", "Core banking - Clearing"), ("CBS-GLI", "Core banking - General ledger interface"), ("CBS-RPT", "Core banking - Reports and statements"),
                           ("CBS-EOD", "Core banking - End-of-day batch")],
                   "DIGITAL": [("DIG-MOB", "Mobile banking app"), ("DIG-IB", "Internet banking"), ("DIG-ONB", "Digital onboarding"), ("DIG-OTP", "OTP and SMS gateway"),
                               ("DIG-CCC", "Contact-centre console")],
                   "FIN": [("FIN-GL", "Finance - General ledger"), ("FIN-AP", "Finance - Accounts payable"), ("FIN-PRC", "Finance - Procurement"), ("FIN-BGT", "Finance - Budgeting")],
                   "HRMS": [("HRMS-ESS", "Employee self-service"), ("HRMS-ATT", "Time and attendance"), ("HRMS-PAY", "Payroll")],
                   "ITSM": [("ITSM-PRT", "IT self-service portal")]}
        for svc, mods in modules.items():
            for code, name in mods:
                add(code, name, "Application", svc, "HO", "ITD-APP", {"CBS": "Core banking suite R19", "DIGITAL": "Digital banking platform 7"}.get(svc, "Packaged application"),
                    when("2019-03-03" if svc == "CBS" else "2018-01-07"))
                self.modes[f"mod:{svc}"].append(code)
        # Network
        for code, name in [("NET-CORE-01", "Head Office core switch 1"), ("NET-CORE-02", "Head Office core switch 2")]:
            add(code, name, "Switch", "NETWORK", "HO", "ITD-NET", "Cisco Catalyst 9500", when())
            self.modes["switch_any"].append(code)
        for f in range(1, 9):
            code = add(f"NET-SW-HO-F{f:02d}", f"Head Office floor {f} access switch", "Switch", "NETWORK", "HO", "ITD-NET", "Cisco Catalyst 9300", when())
            self.modes["switch_any"].append(code)
            self.modes["lan:HO"].append(code)
            code = add(f"NET-AP-HO-F{f:02d}", f"Head Office floor {f} Wi-Fi access points", "Wi-Fi", "NETWORK", "HO", "ITD-NET", "Cisco Catalyst 9120 AP", when())
            self.modes["wifi"].append(code)
        self.modes["wifi"].append(add("NET-WLC-01", "Wi-Fi controller", "Wi-Fi", "NETWORK", "HO", "ITD-NET", "Cisco Catalyst 9800", when()))
        self.modes["vpn"].append(add("NET-VPN-01", "Remote access VPN gateway", "VPN gateway", "NETWORK", "HO", "ITD-NET", "Palo Alto GlobalProtect", when()))
        for code, name in [("FW-HO-01", "Head Office perimeter firewall 1"), ("FW-HO-02", "Head Office perimeter firewall 2")]:
            add(code, name, "Firewall", "NETWORK", "HO", "ITD-NET", "Palo Alto PA-5450", when())
            self.modes["fw_any"].append(code)
            self.modes["fw:HO"].append(code)
        self.modes["link:HO"] = ["NET-CORE-01", "NET-CORE-02"]
        loc_names = {loc[0]: loc[1] for loc in bp.LOCATIONS}
        atm_count = {"BR-DXB01": 4, "BR-AUH01": 3, "BR-SHJ01": 3, "BR-AAN01": 2, "BR-DXB02": 3, "BR-RAK01": 2}
        for b in bp.BRANCH_CODES:
            short = b[3:]
            opened = max(bp.LOC_OPENED[b], dt.date(2019, 1, 1))
            for code, name, ci_type, model, mode in [(f"NET-WAN-{short}", f"{loc_names[b]} branch WAN link (MPLS)", "Network link", "Etisalat MPLS 100 Mbps", "link"),
                                                     (f"NET-LTE-{short}", f"{loc_names[b]} branch backup link (4G)", "Network link", "du 4G router", "link"),
                                                     (f"NET-SW-{short}", f"{loc_names[b]} branch switch", "Switch", "Cisco Catalyst 9200", "lan"),
                                                     (f"FW-{short}", f"{loc_names[b]} branch firewall", "Firewall", "Palo Alto PA-440", "fw")]:
                add(code, name, ci_type, "NETWORK", b, "ITD-NET", model, when(opened.isoformat()))
                self.modes[f"{mode}:{b}"].append(code)
                if mode == "link":
                    self.modes["link_any"].append(code)
                    self.modes[f"link:{b}"].append(code)
                if mode == "lan":
                    self.modes["switch_any"].append(code)
                    self.modes[f"link:{b}"].append(code)
                if mode == "fw":
                    self.modes["fw_any"].append(code)
            for k in range(1, atm_count[b] + 1):
                code = add(f"ATM-{short}-{k:02d}", f"{loc_names[b]} ATM {k}", "ATM", "ATM", b, "ITD-INF", r.choice(["NCR SelfServ 84", "Diebold Nixdorf DN Series 200"]),
                           when(opened.isoformat()))
                self.modes[f"atm:{b}"].append(code)
                self.modes["atm_any"].append(code)
            for k in (1, 2):
                code = add(f"PRN-{short}-{k:02d}", f"{loc_names[b]} branch printer {k}", "Printer", "EUD", b, "ITD-SD", "HP LaserJet Enterprise M611", when(opened.isoformat()))
                self.modes[f"printer:{b}"].append(code)
                self.modes["printer_any"].append(code)
        for f in range(1, 9):
            for k in ((1,) if f % 2 else (1, 2)):
                code = add(f"PRN-HO-F{f:02d}-{k}", f"Head Office floor {f} printer {k}", "Printer", "EUD", "HO", "ITD-SD", "HP LaserJet Enterprise MFP M635", when())
                self.modes["printer:HO"].append(code)
                self.modes["printer_any"].append(code)
        # Staff laptops and desktops: laptops at Head Office (except the contact centre) and for branch managers and section heads.
        n_lt = n_dt = 0
        for p in bp.employees():
            if not p["UserId"]:
                continue
            laptop = (p["LocationCode"] == "HO" and p["SectionCode"] != "DCC-CC") or p["Level"] != "Staff"
            first = p["HireDate"]
            installed = when(max(first, dt.date(2022, 1, 1)).isoformat(), "2024-12-31") if first < bp.Y0 else first  # joiners: set by the joiner request
            left = p["TerminationDate"] is not None
            as_of = min(bp.Y1, p["TerminationDate"] or bp.Y1)
            if laptop:
                n_lt += 1
                code, model = f"LT{n_lt:04d}", r.choice(["Dell Latitude 7440", "HP EliteBook 840 G10", "Lenovo ThinkPad T14 Gen 4"])
            else:
                n_dt += 1
                code, model = f"DT{n_dt:04d}", r.choice(["Dell OptiPlex 7010", "HP EliteDesk 800 G9"])
            add(code, f"{'Laptop' if laptop else 'Desktop'} {p['UserId']}", "Laptop" if laptop else "Desktop", "EUD", "HO" if left else unit(p["EmployeeNumber"], as_of)[2],
                "ITD-SD", model, installed, status="In stock" if left else "In use", emp=None if left else p["EmployeeNumber"])
            self.device[p["EmployeeNumber"]] = code

    def ci_location(self, code, d):
        c = self.ci[code]
        if c["CiType"] in ("Laptop", "Desktop"):
            owner = next(no for no, cc in self.device.items() if cc == code)
            return unit(owner, d)[2]
        return c["LocationCode"]

    def ci_candidates(self, mode, no, d, loc):
        if mode == "device":
            return [self.device[no]] if no and self.has_device(no, d) else []
        if mode == "device_any":
            pool = [x for x in present_people(d) if self.has_device(x, d)]
            return [self.device[x] for x in self.r.sample(pool, min(10, len(pool)))]
        if mode in ("printer", "lan", "fw", "link", "atm"):
            return list(self.modes.get(f"{mode}:{loc}", []))
        return list(self.modes[mode])

    def pick_ci(self, mode, no, d, loc):
        cands = self.ci_candidates(mode, no, d, loc)
        self.r.shuffle(cands)
        for c in cands:
            if self.ci_free(c, d):
                return c
        if no and mode != "device" and self.has_device(no, d) and self.ci_free(self.device[no], d):
            return self.device[no]
        return None

    # ---------------------------------------------------------------- opening a ticket for a person
    def user_open(self, no, d, portal_share, channels):
        """(opened at, channel, creator employee number) for a person at work that day, or None."""
        r = self.r
        p = EMP[no]
        portal_ok = bool(p["UserId"]) and itsm(p["UserId"], d)
        if not is_bday(d) or (portal_ok and r.random() < portal_share):
            if not portal_ok:
                return None
            t = self.time_in_shift(no, d)
            return (t, "Portal", no) if t else None
        options = [(c, w) for c, w in channels if (c != "Email" or p["UserId"]) and (c != "WalkIn" or unit(no, d)[2] == "HO")]
        ch = r.choices([c for c, _ in options], weights=[w for _, w in options])[0]
        s0, s1 = SHIFT_WINDOWS[p["ShiftCode"]]
        lo, hi = max(BH[0], s0), min(BH[1] - 15, s1)
        if lo > hi:
            return None
        t = at(d, int(r.triangular(lo, hi, lo + (hi - lo) * 0.3)))
        tt, agent = self.act("ITD-SD", t)
        if tt != t or agent == no:
            return None
        return t, ch, agent

    # ---------------------------------------------------------------- requests
    def new_request(self, item, for_no, by_no, open_t, channel, creator_no, desc_text, system=None, role=None):
        rq = Ticket(item=item, for_no=for_no, by_no=by_no, open_t=open_t, channel=channel, creator=creator_no, group=CAT[item][6],
                    priority=int(CAT[item][10][1]), unit=unit(for_no, open_t.date()), system=system, role=role, desc=self.desc(desc_text),
                    approvals=[], tasks=[], status=None, approved=None, fulfilled=None, closed=None, due=None)
        if channel == "Portal":
            assert creator_no == by_no
        self.requests.append(rq)
        return rq

    def decide(self, approver, t_after, exclude, depth=0):
        """(decided at, approver) when the approver (or, after a week away, their manager) is at work; (None, approver) if pending at year end."""
        d0 = t_after.date()
        for k in range(8):
            d = d0 + dt.timedelta(days=k)
            if d > bp.Y1:
                return None, approver
            if present(approver, d) and itsm(EMP[approver]["UserId"], d):
                tt = self.time_in_shift(approver, d, after=t_after if k == 0 else None, span=150 if k == 0 else 300)
                if tt and tt > t_after:
                    return (tt, approver) if tt <= END else (None, approver)
        m = manager(approver, d0)
        if depth < 3 and m and m not in exclude:
            return self.decide(m, t_after, exclude, depth + 1)
        return None, approver

    def approvals_and_tasks(self, rq, tasks=None, owner_dept=None, breach_scale=1.0):
        r = self.r
        item = CAT[rq.item]
        t = rq.open_t
        exclude = {rq.for_no, rq.by_no}
        steps = []
        if item[7]:
            m = manager(rq.for_no, t.date())
            if m and m not in exclude:
                steps.append(("LineManager", m))
        owner_dept = owner_dept or (SYSTEM_OWNER.get(item[5]) if item[8] else None)
        if owner_dept:
            h = head(owner_dept, t.date())
            if h and h not in exclude and all(h != s[1] for s in steps):
                steps.append(("SystemOwner", h))
        for kind, approver in steps:
            dec, who = self.decide(approver, t, exclude)
            if dec is None:
                rq.approvals.append((kind, who, t, None, "Pending"))
                rq.status = "Awaiting approval"
                return
            rejected = r.random() < (0.045 if kind == "LineManager" else 0.03)
            rq.approvals.append((kind, who, t, dec, "Rejected" if rejected else "Approved"))
            t = dec
            if rejected:
                rq.status, rq.closed = "Rejected", dec
                return
        if steps:
            rq.approved = t
        target = item[9] * 510
        rq.due = add_bmin(t, target)
        if rq.item == "ACC-REMOVE":
            rq.due = max(rq.due, at(EMP[rq.for_no]["TerminationDate"], 1439))
        if rq.item == "NEW-JOINER":
            rq.due = max(rq.due, at(EMP[rq.for_no]["HireDate"] - dt.timedelta(days=1), 1439))
        if tasks is None:
            if r.random() < 0.015:
                cancel = add_bmin(t, r.randint(5, 300))
                if cancel <= END:
                    rq.status, rq.closed = "Cancelled", cancel
                    return
            q3_net = rq.group == "ITD-NET" and t.month in (7, 8, 9)
            breach = r.random() < (0.35 if q3_net else 0.05 * breach_scale)
            if rq.item == "PWD-RESET" and rq.channel in ("Phone", "WalkIn"):
                wt, prefer = rq.open_t + mins(r.randint(1, 4)), rq.creator   # first-call resolution by the agent on the phone
            else:
                frac = r.uniform(1.05, 1.9) if breach else r.uniform(0.01, 0.35) if rq.item == "PWD-RESET" else r.uniform(0.02, 0.6)
                wt, prefer = add_bmin(t, int(frac * target)), None
            tasks = []
            for name, group, d0, d1 in item[11]:
                tasks.append((name, group, wt, r.randint(d0, d1), prefer))
                wt = None
                prefer = None
        self.run_tasks(rq, tasks, target)

    def run_tasks(self, rq, tasks, target):
        """tasks: [(name, group, earliest start or None = after the previous task, minutes, preferred agent)]."""
        r = self.r
        prev_end = None
        oncall_ok = rq.priority <= 2
        for name, group, start, duration, prefer in tasks:
            if start is None:
                start = add_bmin(prev_end, r.randint(5, max(10, int(target * 0.12))))
            elif prev_end is not None:
                start = max(start, prev_end + mins(r.randint(2, 10)))
            if start > END:
                break
            s, e, agent = self.schedule(group, start, duration, oncall=oncall_ok and group in ONCALL_GROUPS and not in_bh(start), prefer=prefer)
            if s > END:
                break
            rq.tasks.append((name, group, agent, s, e))
            prev_end = e
        done = len(rq.tasks) == len(tasks)
        if rq.tasks:
            in_group = [tk for tk in rq.tasks if tk[1] == rq.group]
            rq.assignee = (in_group or rq.tasks)[0][2]
        if done:
            rq.status, rq.fulfilled = "Fulfilled", rq.tasks[-1][4]
            close = add_bmin(rq.fulfilled, 1020)
            rq.closed = close if close <= END else None
        else:
            rq.status = "In progress" if rq.tasks else "New"

    def gen_joiners(self):
        r = self.r
        officers = [p["EmployeeNumber"] for p in bp.employees() if p["SectionCode"] == "HRD-PSV" and p["Title"] == "HR Officer"]
        hires = sorted((p for p in bp.employees() if p["HireDate"] >= bp.Y0), key=lambda p: (p["HireDate"], p["EmployeeNumber"]))
        for j in hires:
            no, hire = j["EmployeeNumber"], j["HireDate"]
            d = hire
            for _ in range(r.randint(3, 7)):
                d = prev_bday(d)
            first = next_bday(bp.Y0)   # the data covers 2025 only: an early-January hire is raised on the first working day
            d = max(d, first)
            tried = [d] + [x for x in (prev_bday(d), prev_bday(prev_bday(d))) if x >= first] + [next_bday(d), next_bday(next_bday(d))]
            raiser = None
            for d in tried:
                mgr = manager(no, hire)
                cands = ([mgr] if mgr and r.random() < 0.6 else []) + r.sample(officers, len(officers))
                raiser = next((c for c in cands if c and present(c, d) and itsm(EMP[c]["UserId"], d)), None)
                if raiser and d < hire:
                    break
            assert raiser, no
            open_t = self.time_in_shift(raiser, d, span=None)
            text = "New joiner setup for {for} (starts {start})" if j["UserId"] else "New joiner setup for {for} (starts {start}) - desk phone only, no system account"
            rq = self.new_request("NEW-JOINER", no, raiser, open_t, "Portal", raiser, text)
            rq.unit = unit(no, hire)
            if j["UserId"]:
                deliver = prev_bday(hire)
                tasks = [("Create user account and standard access", "ITD-SEC", add_bmin(open_t, r.randint(20, 400)), r.randint(15, 45), None),
                         ("Create mailbox and home drive", "ITD-INF", None, r.randint(10, 30), None),
                         ("Prepare and deliver computer", "ITD-SD", at(deliver, r.randint(BH[0], BH[0] + 300)), r.randint(30, 120), None)]
            else:
                tasks = [("Set up desk phone extension", "ITD-SD", add_bmin(open_t, r.randint(20, 600)), r.randint(10, 30), None)]
            self.approvals_and_tasks(rq, tasks=tasks)
            assert rq.status == "Fulfilled", (no, rq.status)
            if j["UserId"]:
                self.ci[self.device[no]]["InstalledDate"] = rq.tasks[-1][3].date()

    def gen_leavers(self):
        r = self.r
        late = bp.facts()["late_revocation"]["EmployeeNumber"]
        officers = [p["EmployeeNumber"] for p in bp.employees() if p["SectionCode"] == "HRD-PSV" and p["Title"] == "HR Officer"]
        leavers = sorted((p for p in bp.employees() if p["TerminationDate"]), key=lambda p: (p["TerminationDate"], p["EmployeeNumber"]))
        for p in leavers:
            no, td = p["EmployeeNumber"], p["TerminationDate"]
            has_cbs = bool(p["UserId"]) and bp.has_access(p["UserId"], "CBS", td)
            fulfil_day = td if is_bday(td) else prev_bday(td)
            for _ in range(6):
                raise_day = fulfil_day
                if no != late:
                    for _ in range(r.choice([0, 1, 1, 2])):
                        raise_day = prev_bday(raise_day)
                officer = None
                for _ in range(6):
                    officer = next((o for o in r.sample(officers, len(officers)) if o != no and present(o, raise_day) and itsm(EMP[o]["UserId"], raise_day)), None)
                    if officer:
                        break
                    raise_day = prev_bday(raise_day)
                assert officer, no
                s0 = SHIFT_WINDOWS[EMP[officer]["ShiftCode"]][0]
                open_t = at(raise_day, r.randint(s0, 12 * 60 + 15))
                text = "Remove all system access for {for} - last working day {last}" if p["UserId"] else "Leaver {for} (last working day {last}) - confirm no system accounts"
                rq = self.new_request("ACC-REMOVE", no, officer, open_t, "Portal", officer, text)
                base = max(open_t + mins(5), at(fulfil_day, BH[0]))
                tasks = []
                if p["UserId"]:
                    tasks.append(("Collect computer and accessories", "ITD-SD", base + mins(r.randint(60, 240)), r.randint(10, 30), None))
                    tasks.append(("Disable network account, email and system access", "ITD-SEC", at(fulfil_day, r.randint(13 * 60, 14 * 60 + 30)), r.randint(10, 30), None))
                    if has_cbs:
                        when = at(td + dt.timedelta(days=3), r.randint(10 * 60, 11 * 60 + 30)) if no == late else at(fulfil_day, r.randint(15 * 60, 15 * 60 + 40))
                        tasks.append(("Remove Core Banking user", "ITD-APP", when, r.randint(5, 15), None))
                else:
                    tasks.append(("Confirm the leaver has no system accounts", "ITD-SEC", at(fulfil_day, r.randint(13 * 60, 15 * 60)), r.randint(5, 15), None))
                self.approvals_and_tasks(rq, tasks=tasks)
                ok = rq.status == "Fulfilled" and (rq.fulfilled.date() <= td) == (no != late)
                if ok:
                    break
                assert self.requests.pop() is rq
                fulfil_day = prev_bday(fulfil_day)
            assert ok, (no, rq.status, rq.fulfilled)
            if no == late:
                assert rq.fulfilled.date() == td + dt.timedelta(days=3)
                self.late_request = rq

    def gen_transfers(self):
        r = self.r
        for m in bp.movements():
            if m["MovementType"] != "Transfer":
                continue
            no = m["EmployeeNumber"]
            p = EMP[no]
            d = m["EffectiveDate"] if is_bday(m["EffectiveDate"]) else next_bday(m["EffectiveDate"])
            while not (present(no, d) and itsm(p["UserId"], d)):
                d = next_bday(d)
            a = bp.access_row(p["UserId"], "CBS", d)
            open_t = self.time_in_shift(no, d)
            rq = self.new_request("ACC-CBS", no, no, open_t, "Portal", no, "Move Core Banking user to the new branch after transfer - role {role}", "CBS", a["RoleCode"])
            self.approvals_and_tasks(rq)

    def request_text(self, item, no, d):
        """(description, system, role) for a generic request, or None when the person does not fit."""
        r = self.r
        p = EMP[no]
        uid = p["UserId"]
        dept, sect, loc = unit(no, d)
        if item.startswith("ACC-"):
            system = CAT[item][5]
            a = bp.access_row(uid, system, d) if uid else None
            if not a or (system == "ITSM" and a["RoleCode"] == "Requester"):
                return None
            return r.choice(ACCESS_TEMPLATES).replace("{sys}", SYSTEM_NAME[system]), system, a["RoleCode"]
        options = []
        for text, cond in REQUEST_DESCS[item]:
            if cond is None and uid:
                options.append((text, None))
            elif cond == "nouser" and not uid:
                options.append((text, None))
            elif cond in SYSTEM_NAME and uid and bp.access_row(uid, cond, d):
                options.append((text, cond))
            elif cond == "laptop" and self.has_device(no, d, "Laptop"):
                options.append((text, None))
            elif cond == "branch" and loc != "HO":
                options.append((text, None))
            elif cond == "itd" and dept == "ITD":
                options.append((text, None))
            elif cond == dept:
                options.append((text, None))
        if not options:
            return None
        text, system = r.choice(options)
        return text, system, None

    def eligible_requester(self, item, no, d):
        p = EMP[no]
        uid = p["UserId"]
        loc = unit(no, d)[2]
        if item == "HW-REQUEST":
            return bool(uid) or self.r.random() < 0.3
        if not uid:
            return False
        if item == "PWD-RESET":
            return p["ShiftCode"] != "HO" or self.r.random() < 0.6
        if item in ("SW-INSTALL", "MAILBOX"):
            return loc == "HO" and p["SectionCode"] != "DCC-CC" or p["Level"] != "Staff"
        if item == "VPN-ACCESS":
            return self.has_device(no, d, "Laptop")
        if item.startswith("ACC-"):
            a = bp.access_row(uid, CAT[item][5], d)
            return a is not None and a["RoleCode"] != "Requester"
        return True

    def gen_generic_requests(self, n):
        r = self.r
        items, weights = list(ITEM_WEIGHTS), list(ITEM_WEIGHTS.values())
        channels = [("Phone", 55), ("Email", 30), ("WalkIn", 15)]
        made = 0
        while made < n:
            d = self.pick_day()
            item = r.choices(items, weights=weights)[0]
            pool = present_people(d)
            no = None
            for _ in range(30):
                c = r.choice(pool)
                if self.eligible_requester(item, c, d):
                    no = c
                    break
            if no is None:
                continue
            spec = self.request_text(item, no, d)
            if spec is None:
                continue
            o = self.user_open(no, d, 0.25 if item == "PWD-RESET" else 0.85, channels)
            if not o:
                continue
            t, ch, creator = o
            rq = self.new_request(item, no, no, t, ch, creator, spec[0], spec[1], spec[2])
            self.approvals_and_tasks(rq)
            made += 1

    # ---------------------------------------------------------------- incidents
    def new_incident(self, typ, svc, ci, imp, urg, rep, open_t, channel, creator, desc_text, loc=None):
        inc = Ticket(cat=(typ[0], typ[1]), group=typ[2], svc=svc, ci=ci, imp=imp, urg=urg, rep=rep, open_t=open_t, channel=channel, creator=creator,
                     unit=unit(rep, open_t.date()) if rep else None, loc=loc or (unit(rep, open_t.date())[2] if rep else self.ci_location(ci, open_t.date())),
                     desc=self.desc(desc_text), res_codes=typ[12], major=0, parent=None, extra=[], link=None)
        self.use_ci(ci, open_t.date())
        self.incidents.append(inc)
        return inc

    def run_incident(self, inc, breach=None, resolve_at=None, respond_in=None, resolver=None, res_code=None, assignee=None):
        r = self.r
        pri = priority(inc.imp, inc.urg)
        inc.priority = pri
        g = inc.group
        oc = g if pri <= 2 and g in ONCALL_GROUPS else None
        t = inc.open_t
        inc.triage = None
        if inc.channel in ("Phone", "Email"):
            inc.init_group = "ITD-SD"
            if g != "ITD-SD":
                tt = min(t + mins(r.randint(1, 8)), at(t.date(), BH[1]))
                inc.triage = (inc.creator, tt)
                t = tt
        elif inc.channel == "Portal" and unit(inc.creator, t.date())[1] == g:
            inc.init_group = g   # raised by a member of the resolving group itself
        elif inc.channel == "Portal":
            inc.init_group = "ITD-SD"
            if g != "ITD-SD":
                tt, agent = self.act("ITD-SD", add_bmin(t, r.randint(2, 40)))
                inc.triage = (agent, tt)
                t = tt
        else:
            inc.init_group = g
        if pri == 1:
            inc.resp_due, inc.res_due = inc.open_t + mins(15), inc.open_t + mins(240)
        else:
            inc.resp_due, inc.res_due = add_bmin(inc.open_t, RESP_TARGET[pri]), add_bmin(inc.open_t, RES_TARGET[pri])
        # response
        if respond_in is not None:
            resp, asg = self.act(g, t + mins(respond_in), oncall=bool(oc), prefer=assignee)
        elif pri == 1 or (oc and not in_bh(t)):
            resp, asg = self.act(g, t + mins(r.randint(2, 12) if pri == 1 else r.randint(6, 35)), oncall=True)
        else:
            late = r.random() < 0.03
            delay = int(RESP_TARGET[pri] * (r.uniform(1.05, 1.8) if late else r.uniform(0.02, 0.7)))
            resp, asg = self.act(g, add_bmin(t, max(1, delay)), oncall=bool(oc))
        inc.resp = inc.asg = inc.res = inc.rsl = inc.reassign = inc.close = inc.closer = inc.wn1 = inc.wn2 = None
        if resp > END:
            return
        inc.resp, inc.asg = resp, asg
        # resolution
        if breach is None:
            quarter = (inc.open_t.month - 1) // 3 + 1
            breach = r.random() < (0.42 if g == "ITD-NET" and quarter == 3 else {1: 0.05, 2: 0.08, 3: 0.07, 4: 0.06}[pri])
        if resolve_at is not None:
            res_t = max(resolve_at, resp + mins(1))
        elif pri == 1:
            res_t = max(inc.open_t + mins(r.randint(250, 400) if breach else r.randint(40, 200)), resp + mins(5))
        elif oc and not in_bh(resp):
            res_t = resp + mins(r.randint(30, 300))
        else:
            frac = r.uniform(1.05, 2.3) if breach else r.betavariate(1.3, 3.0) * 0.95
            res_t = add_bmin(inc.open_t, int(frac * RES_TARGET[pri]))
            if res_t <= resp:
                res_t = resp + mins(r.randint(3, 30))
        res_t, rsl = self.act(g, res_t, oncall=bool(oc), prefer=resolver or asg)
        if res_t <= END:
            inc.res, inc.rsl = res_t, rsl
            inc.res_code = res_code or r.choice(inc.res_codes)
            if rsl != asg:
                lo = max(window_start(res_t), resp + mins(1))
                inc.reassign = at(lo.date(), r.randint(mod(lo), mod(res_t))) if lo <= res_t else res_t
            close = add_bmin(res_t, r.randint(240, 1020))
            ct, closer = self.act("ITD-SD", close)
            if ct <= END:
                inc.close, inc.closer = ct, closer
        # work notes: the first by the assignee on the response day, the second by the resolver on the resolution day
        limit = inc.reassign or inc.res or END
        if r.random() < 0.65:
            lo, hi = resp + mins(1), min(window_end(resp), limit - mins(1))
            if lo <= hi:
                inc.wn1 = (asg, lo + mins(r.randint(0, int((hi - lo).total_seconds() // 60))))
        if inc.res and r.random() < 0.5:
            lo = max(window_start(inc.res), resp + mins(1), (inc.wn1[1] if inc.wn1 else resp) + mins(1), (inc.reassign or resp) + mins(1))
            hi = inc.res - mins(1)
            if lo <= hi:
                inc.wn2 = (inc.rsl, lo + mins(r.randint(0, int((hi - lo).total_seconds() // 60))))

    def gen_major_incident(self):
        """Planted 1: core banking database cluster outage on Tuesday 13 May, about three hours, with branch reports linked."""
        r = self.r
        d = MAJOR_DAY
        assert is_bday(d)
        typ = next(t for t in INCIDENT_TYPES if t[1] == "Core banking")
        specialists = [x for x in roster("ITD-APP", d, False) if EMP[x]["Title"] == "Core Banking Specialist"]
        lead = head("ITD-APP", d)
        assert specialists and lead in roster("ITD-APP", d, False)
        major = self.new_incident(typ, "CBS", "CBS-DB-01", 1, 1, None, at(d, 10 * 60 + 12), "Monitoring", "SYS_MONITOR",
                                  "Core banking unavailable in all branches: database cluster node hung and failover did not complete", loc="HO")
        major.major = 1
        self.run_incident(major, breach=False, respond_in=3, resolve_at=at(d, 13 * 60 + 13), res_code="Service restarted", assignee=specialists[0], resolver=specialists[0])
        dba = next(x for x in roster("ITD-INF", d, False) if EMP[x]["Title"] == "Database Administrator")
        major.extra = [(lead, at(d, 10 * 60 + 24), "Major incident declared; bridge call opened with Operations, Retail and the database team."),
                       (dba, at(d, 10 * 60 + 41), "Node 1 of the cluster hung during a storage path failover; node 2 did not take over the instance."),
                       (dba, at(d, 12 * 60 + 35), "Storage path restored; cluster restarted in a controlled sequence."),
                       (specialists[0], at(d, 13 * 60 + 9), "Core banking services up; teller, clearing and digital channels confirmed by branches.")]
        children = []
        units = [b for b in bp.BRANCH_CODES] + ["OPS", "BR-DXB01"]
        for k, u in enumerate(units):
            t = at(d, 10 * 60 + 15 + k * 4 + r.randint(0, 3))
            cands = [x for x in present_people(d) if unit(x, d)[0] == u and EMP[x]["UserId"] and x not in children]
            rep = sorted(cands)[r.randint(0, len(cands) - 1)]
            agent_t, agent = self.act("ITD-SD", t)
            assert agent_t == t
            child = self.new_incident(typ, "CBS", "CBS-DB-01", 2, 1, rep, t, "Phone", agent,
                                      "Core banking not responding at {loc} - customers waiting" if u.startswith("BR-") else "Core banking not responding - payments and clearing stopped")
            child.parent = major
            children.append(rep)
            self.run_incident(child, breach=False, respond_in=r.randint(3, 10), resolve_at=at(d, 13 * 60 + 15 + r.randint(0, 10)), res_code="Resolved by parent",
                              assignee=specialists[0], resolver=specialists[0])
            child.link = (child.asg, child.resp + mins(1))
        self.major = major

    def gen_phishing(self):
        """Planted 2: phishing campaign on Wednesday 17 September, ITD-SEC P2 parent with user reports linked."""
        r = self.r
        d = PHISH_DAY
        assert is_bday(d)
        typ = next(t for t in INCIDENT_TYPES if t[1] == "Phishing")
        sec = [x for x in roster("ITD-SEC", d, False) if EMP[x]["Level"] == "Staff"]
        assert len(sec) >= 2
        analyst = sec[0]
        parent = self.new_incident(typ, "EMAIL", "EXCH-GW-01", 1, 2, analyst, at(d, 9 * 60 + 41), "Portal", analyst,
                                   "Phishing campaign: fake 'updated salary certificate' email with a credential-harvesting link sent to staff")
        self.run_incident(parent, breach=False, respond_in=1, resolve_at=at(d, 14 * 60 + 55), res_code="Fixed", assignee=analyst, resolver=analyst)
        parent.extra = [(analyst, at(d, 10 * 60 + 2), "Sender domain and the link blocked at the email gateway."),
                        (sec[1], at(d, 11 * 60 + 30), "Matching emails purged from 143 mailboxes."),
                        (analyst, at(d, 14 * 60 + 20), "Two users who entered credentials: passwords reset and sessions revoked; awareness notice sent.")]
        reporters = []
        for k in range(9):
            t = at(d, 8 * 60 + 52 + k * 17 + r.randint(0, 9))
            cands = sorted(x for x in present_people(d) if EMP[x]["UserId"] and self.has_device(x, d) and unit(x, d)[0] != "ITD" and x not in reporters
                           and self.ci_free(self.device[x], d))
            rep = cands[r.randint(0, len(cands) - 1)]
            reporters.append(rep)
            if k % 3 == 0:
                ch, creator = "Portal", rep
            else:
                tt, creator = self.act("ITD-SD", t)
                assert tt == t
                ch = "Email" if k % 3 == 1 else "Phone"
            child = self.new_incident(typ, "EMAIL", self.device[rep], 3, 1, rep, t, ch, creator, "Suspicious email about an 'updated salary certificate' with a sign-in link")
            child.parent = parent
            self.run_incident(child, breach=False, resolve_at=at(d, 15 * 60 + r.randint(0, 40)), res_code="Resolved by parent", assignee=analyst, resolver=analyst,
                              respond_in=r.randint(5, 25))
            child.link = (child.asg, child.resp + mins(1))
        self.phishing = parent

    def gen_atm_series(self):
        """Planted 3: ATM-SHJ01-02 fails every 2-3 days from late October; four times in the last seven days of December."""
        r = self.r
        branch = "BR-SHJ01"
        days, d, gaps = [], dt.date(2025, 12, 31), [2, 2, 2, 3, 2, 3, 2, 2, 3, 2, 3]
        k = 0
        while d >= dt.date(2025, 10, 27):
            days.append(d)
            d -= dt.timedelta(days=gaps[k % len(gaps)])
            k += 1
        days.sort()
        types = {t[1]: t for t in INCIDENT_TYPES if t[0] == "ATM"}
        cycle = ["Communication", "Cash dispenser", "Card reader", "Communication", "Receipt printer", "Cash dispenser"]
        for k, d in enumerate(days):
            typ = types[cycle[k % len(cycle)]]
            staff = sorted(x for x in present_people(d) if unit(x, d)[0] == branch and EMP[x]["UserId"]) if is_bday(d) else []
            if staff and k % 2 == 1 and d != dt.date(2025, 12, 31):
                t = at(d, r.randint(8 * 60 + 5, 14 * 60))
                tt, agent = self.act("ITD-SD", t)
                if tt == t:
                    rep = staff[r.randint(0, len(staff) - 1)]
                    inc = self.new_incident(typ, "ATM", PLANTED_ATM, 3, 1, rep, t, "Phone", agent, r.choice(typ[10]).replace("{ci}", PLANTED_ATM))
                    self.run_incident(inc)
                    continue
            if d == dt.date(2025, 12, 31):
                # The last one opens at midnight, so a window that ends "now" (31 December = today on the live view) holds all four
                # at any hour. The minute drawn here before is still drawn so every later generated value stays the same.
                r.randint(20, 90)
                t = at(d, 0)
            else:
                t = at(d, r.randint(0, 1439))
            inc = self.new_incident(typ, "ATM", PLANTED_ATM, 3, 1, None, t, "Monitoring", "SYS_MONITOR", (typ[11] or typ[10])[0].replace("{ci}", PLANTED_ATM), loc=branch)
            self.run_incident(inc, res_code="Hardware replaced" if d == dt.date(2025, 12, 10) else None)
        self.atm_days = days

    def gen_random_p1(self):
        r = self.r
        months = [2, 4, 6, 8, 10, 11]
        for (cat, sub, ci, text), month in zip(RANDOM_P1, months):
            typ = next(t for t in INCIDENT_TYPES if t[0] == cat and t[1] == sub)
            for _ in range(50):
                t = at(dt.date(2025, month, r.randint(1, 28)), r.randint(0, 1439))
                if self.ci_free(ci, t.date()):
                    break
            inc = self.new_incident(typ, self.ci[ci]["ServiceCode"], ci, 1, 1, None, t, "Monitoring", "SYS_MONITOR", text)
            self.run_incident(inc)

    def gen_generic_incidents(self, n):
        r = self.r
        weights = [t[4] for t in INCIDENT_TYPES]
        channels = [("Phone", 70), ("Email", 30)]
        made = 0
        while made < n:
            typ = r.choices(INCIDENT_TYPES, weights=weights)[0]
            cat, sub, group, svc, _, mon_share, ci_mode, mon_mode, who, iu, udesc, mdesc, _ = typ
            mon = ci_mode is None or (mon_mode is not None and r.random() < mon_share)
            if mon:
                t = T0 + mins(r.randint(1, 525599))
                ci = self.pick_ci(mon_mode, None, t.date(), None)
                if not ci:
                    continue
                rep, ch, creator, text = None, "Monitoring", "SYS_MONITOR", r.choice(mdesc)
            else:
                d = self.pick_day()
                pool = present_people(d)
                rep = None
                for _ in range(30):
                    c = r.choice(pool)
                    if self.eligible_reporter(who, c, d):
                        rep = c
                        break
                if rep is None:
                    continue
                o = self.user_open(rep, d, 0.5, channels)
                if not o:
                    continue
                t, ch, creator = o
                ci = self.pick_ci(ci_mode, rep, d, unit(rep, d)[2])
                if not ci:
                    continue
                text = r.choice(udesc)
            imp, urg = r.choices([x for x, _ in iu], weights=[w for _, w in iu])[0]
            service = svc or self.ci[ci]["ServiceCode"]
            inc = self.new_incident(typ, service, ci, imp, urg, rep, t, ch, "SYS_MONITOR" if mon else creator, text)
            self.run_incident(inc)
            made += 1

    def eligible_reporter(self, who, no, d):
        p = EMP[no]
        uid = p["UserId"]
        loc = unit(no, d)[2]
        if who == "any":
            return True
        if not uid:
            return False
        if who == "laptop":
            return self.has_device(no, d, "Laptop")
        if who == "desktop":
            return self.has_device(no, d, "Desktop")
        if who == "user":
            return self.has_device(no, d)
        if who in ("cbs", "digital", "fin"):
            return bp.access_row(uid, who.upper(), d) is not None
        if who == "branch":
            return loc != "HO"
        if who == "ho":
            return loc == "HO" and self.has_device(no, d)
        if who == "remote":
            return loc == "HO" and self.has_device(no, d, "Laptop")
        raise KeyError(who)

    # ---------------------------------------------------------------- run and verify
    def run(self):
        bp.validate()
        self.build_cis()
        self.late_request = self.major = self.phishing = None
        self.gen_joiners()
        self.gen_leavers()
        self.gen_transfers()
        self.gen_major_incident()
        self.gen_phishing()
        self.gen_atm_series()
        self.gen_random_p1()
        self.gen_generic_requests(N_REQUESTS - len(self.requests))
        self.gen_generic_incidents(N_INCIDENTS - len(self.incidents))
        self.requests.sort(key=lambda x: (x.open_t, x.item, x.for_no))
        for n, x in enumerate(self.requests, 1):
            x.n = n
        self.incidents.sort(key=lambda x: (x.open_t, x.ci, x.rep or ""))
        for n, x in enumerate(self.incidents, 1):
            x.n = n
        self.verify()

    def verify(self):
        """The consistency rules, asserted on everything that will be written."""
        users = {u["UserId"] for u in self.users}
        units = {(s["DepartmentCode"], s["SectionCode"]) for s in bp.sections()} | {(dpt["DepartmentCode"], None) for dpt in bp.departments()}
        locs = {loc[0] for loc in bp.LOCATIONS}

        def check_actor(no, t, og, what):
            assert EMP[no]["UserId"] in users, (what, no)
            assert can_act(no, t, og), (what, no, t, og)

        for rq in self.requests:
            d = rq.open_t.date()
            assert employed(rq.by_no, d), ("requester not employed", rq.n)
            assert rq.for_no in EMP and (rq.unit[0], rq.unit[1]) in units and rq.unit[2] in locs
            if rq.channel == "Portal":
                assert rq.creator == rq.by_no
                check_actor(rq.creator, rq.open_t, None, "portal creator")
            else:
                assert unit(rq.creator, d)[1] == "ITD-SD"
                check_actor(rq.creator, rq.open_t, None, "agent creator")
            t = rq.open_t
            for kind, ap, req_t, dec_t, decision in rq.approvals:
                assert ap not in (rq.by_no, rq.for_no), ("approver = requester", rq.n)
                assert req_t >= rq.open_t and req_t == t
                if dec_t:
                    assert dec_t > req_t
                    check_actor(ap, dec_t, None, "approver")
                    t = dec_t
            prev = rq.approved or rq.open_t
            for name, group, agent, s, e in rq.tasks:
                assert s >= prev and e >= s, ("task order", rq.n)
                assert unit(agent, s.date())[1] == group
                gog = group if rq.priority <= 2 and group in ONCALL_GROUPS else None
                check_actor(agent, s, gog, "task start")
                check_actor(agent, e, gog, "task end")
                prev = e
            if rq.fulfilled:
                assert rq.fulfilled == rq.tasks[-1][4] and (rq.closed is None or rq.closed >= rq.fulfilled)
            if rq.closed:
                assert rq.closed >= rq.open_t and rq.closed <= END
        removals = [rq for rq in self.requests if rq.item == "ACC-REMOVE"]
        late = [rq for rq in removals if rq.fulfilled and rq.fulfilled.date() > EMP[rq.for_no]["TerminationDate"]]
        assert late == [self.late_request], late
        assert len(removals) == sum(1 for p in bp.employees() if p["TerminationDate"])
        assert sum(1 for rq in self.requests if rq.item == "NEW-JOINER") == sum(1 for p in bp.employees() if p["HireDate"] >= bp.Y0)
        for inc in self.incidents:
            d = inc.open_t.date()
            assert inc.ci in self.ci and inc.loc in locs
            og = inc.group if inc.priority <= 2 and inc.group in ONCALL_GROUPS else None
            if inc.rep:
                assert employed(inc.rep, d), ("reporter not employed", inc.n)
            if inc.channel == "Monitoring":
                assert inc.rep is None and inc.creator == "SYS_MONITOR"
            elif inc.channel == "Portal":
                assert inc.creator == inc.rep
                check_actor(inc.creator, inc.open_t, None, "portal reporter")
            else:
                assert unit(inc.creator, d)[1] == "ITD-SD"
                check_actor(inc.creator, inc.open_t, None, "agent creator")
            seq = [inc.open_t]
            if inc.triage:
                check_actor(inc.triage[0], inc.triage[1], None, "triage")
                assert unit(inc.triage[0], inc.triage[1].date())[1] == "ITD-SD"
                seq.append(inc.triage[1])
            if inc.resp:
                check_actor(inc.asg, inc.resp, og, "response")
                assert unit(inc.asg, inc.resp.date())[1] == inc.group
                seq.append(inc.resp)
            if inc.wn1:
                check_actor(*inc.wn1, og, "work note 1")
                seq.append(inc.wn1[1])
            if inc.reassign:
                check_actor(inc.rsl, inc.reassign, og, "reassign")
                seq.append(inc.reassign)
            if inc.wn2:
                check_actor(*inc.wn2, og, "work note 2")
                seq.append(inc.wn2[1])
            if inc.link:
                check_actor(*inc.link, og, "link")
                assert inc.resp <= inc.link[1] <= (inc.res or END)
            if inc.res:
                check_actor(inc.rsl, inc.res, og, "resolve")
                assert unit(inc.rsl, inc.res.date())[1] == inc.group
                seq.append(inc.res)
            if inc.close:
                check_actor(inc.closer, inc.close, None, "close")
                seq.append(inc.close)
            for who, t, _ in inc.extra:
                check_actor(who, t, og, "extra note")
                assert inc.resp <= t <= inc.res
            assert seq == sorted(seq) and seq[-1] <= END, ("timestamps out of order", inc.n, seq)
            if inc.priority == 1 and inc.svc == "CBS":
                assert inc is self.major
        # Only the planted ATM has more than three incidents on one configuration item within eight days.
        by_ci = defaultdict(list)
        for inc in self.incidents:
            by_ci[inc.ci].append(inc.open_t.date().toordinal())
        planted_clusters = {PLANTED_ATM, "CBS-DB-01", "EXCH-GW-01"}
        for ci, days in by_ci.items():
            days.sort()
            worst = max(sum(1 for y in days if x <= y <= x + 7) for x in days)
            assert worst <= 3 or ci in planted_clusters, (ci, worst)
        last_week = dt.date(2025, 12, 24).toordinal()
        busy = {ci for ci, days in by_ci.items() if sum(1 for x in days if x >= last_week) > 3}
        assert busy == {PLANTED_ATM}, busy
        assert sum(1 for x in by_ci[PLANTED_ATM] if x >= dt.date(2025, 12, 25).toordinal()) >= 4

    # ---------------------------------------------------------------- SQL
    def sql(self):
        out = [prologue(DB, OWNER, "WbIt#Owner#2026", READER, "WbIt#Reader#2026", TITLE, DESCRIPTION)]
        out.append("IF OBJECT_ID(N'dbo.vw_IncidentsLive', N'V') IS NOT NULL DROP VIEW dbo.vw_IncidentsLive;\nGO\n")
        out.append(drop_tables(["IncidentActivities", "Incidents", "RequestTasks", "RequestApprovals", "ServiceRequests", "ServiceCatalogItems", "SlaPolicies",
                                "BusinessHours", "Categories", "ConfigurationItems", "Services", "AssignmentGroups", "Users", "EmployeeDirectory", "Locations"]))
        out.append(SCHEMA_SQL)
        out.append(self.reference_sql())
        out.append(self.coded_sql())
        out.append(DECODE_SQL)
        out.append(CHECK_SQL)
        out.append("DROP TABLE #U; DROP TABLE #Unit; DROP TABLE #Desc; DROP TABLE #Role; DROP TABLE #Ci; DROP TABLE #Cat; DROP TABLE #TaskName; DROP TABLE #Note;\n"
                   "DROP TABLE #Res; DROP TABLE #Item; DROP TABLE #Svc; DROP TABLE #Grp; DROP TABLE #Loc;\n"
                   "DROP TABLE #R; DROP TABLE #A; DROP TABLE #T; DROP TABLE #I; DROP TABLE #X; DROP TABLE #Presence;\nGO\n")
        out.append(epilogue(DB, SUMMARY_SQL))
        return "\n".join(out)

    def reference_sql(self):
        out = []
        out.append(insert("dbo.Locations", ["LocationCode", "LocationName", "LocationNameAr", "LocationType", "Emirate", "Address", "OpenedDate", "TellerCounters"],
                          [(loc[0], loc[1], loc[2], loc[3], loc[4], loc[5], loc[6], loc[7]) for loc in bp.LOCATIONS]))
        rows = []
        for p in bp.employees():
            as_of = min(bp.Y1, p["TerminationDate"] or bp.Y1)
            dept, sect, loc = unit(p["EmployeeNumber"], as_of)
            rows.append((p["EmployeeNumber"], p["FullName"], p["FullNameAr"], dept, sect, loc, p["WorkEmail"], p["UserId"], p["TerminationDate"] is None,
                         p["HireDate"].isoformat(), p["TerminationDate"].isoformat() if p["TerminationDate"] else None))
        out.append(insert("dbo.EmployeeDirectory", ["EmployeeNumber", "FullName", "FullNameAr", "DepartmentCode", "SectionCode", "LocationCode", "Email", "UserId", "IsActive",
                                                    "HireDate", "TerminationDate"], rows))
        out.append(insert("dbo.Users", ["UserId", "EmployeeNumber", "FullName", "FullNameAr", "DepartmentCode", "SectionCode", "RoleCode", "ApprovalLimitAed", "Status",
                                        "AccessGrantedDate", "AccessRevokedDate"],
                          [(u["UserId"], u["EmployeeNumber"], u["FullName"], u["FullNameAr"], u["DepartmentCode"], u["SectionCode"], u["RoleCode"], u["ApprovalLimitAed"],
                            u["Status"], u["AccessGrantedDate"].isoformat(), u["AccessRevokedDate"].isoformat() if u["AccessRevokedDate"] else None) for u in self.users]))
        sect = {s["SectionCode"]: s for s in bp.sections()}
        out.append(insert("dbo.AssignmentGroups", ["GroupCode", "GroupName", "GroupNameAr", "GroupEmail", "GroupLeadEmployeeNumber", "Description"],
                          [(g, sect[g]["SectionName"], sect[g]["SectionNameAr"], GROUPS[g][0], head(g, bp.Y1), GROUPS[g][1]) for g in GROUP_CODES]))
        out.append(insert("dbo.Services", ["ServiceCode", "ServiceName", "ServiceNameAr", "OwningGroupCode", "BusinessCriticality", "Description"], SERVICES))
        out.append(insert("dbo.ConfigurationItems", ["CiCode", "CiName", "CiType", "ServiceCode", "LocationCode", "OwningGroupCode", "AssignedToEmployeeNumber", "AssetTag",
                                                     "Model", "Status", "InstalledDate"],
                          [(c["CiCode"], c["CiName"], c["CiType"], c["ServiceCode"], c["LocationCode"], c["OwningGroupCode"], c["AssignedToEmployeeNumber"], c["AssetTag"],
                            c["Model"], c["Status"], c["InstalledDate"].isoformat()) for c in self.cis]))
        out.append(insert("dbo.Categories", ["Category", "Subcategory", "CategoryNameAr", "SubcategoryNameAr", "TicketType", "DefaultGroupCode"], CATEGORIES))
        out.append(insert("dbo.SlaPolicies", ["SlaPolicyCode", "PolicyName", "TicketType", "Priority", "ScheduleCode", "ResponseTargetMinutes", "ResolutionTargetMinutes",
                                              "ResponseTarget", "ResolutionTarget"], SLA_POLICIES))
        out.append(insert("dbo.ServiceCatalogItems", ["CatalogItemCode", "ItemName", "ItemNameAr", "Category", "Subcategory", "SystemCode", "FulfilmentGroupCode",
                                                      "RequiresManagerApproval", "RequiresOwnerApproval", "OwnerDepartmentCode", "TargetDays", "SlaPolicyCode",
                                                      "DefaultPriority", "Description"],
                          [(c[0], c[1], c[2], c[3], c[4], c[5], c[6], bool(c[7]), bool(c[8]), SYSTEM_OWNER.get(c[5]) if c[8] else None, c[9], f"REQ-{c[9]}D", c[10], c[12])
                           for c in CATALOG]))
        holidays = [(d.isoformat(), en) for d, (en, _) in sorted(bp.HOLIDAYS.items())]
        out.append("GO\nDECLARE @Holidays TABLE (HolidayDate date PRIMARY KEY, HolidayName nvarchar(60));\n" + insert("@Holidays", ["HolidayDate", "HolidayName"], holidays) + f"""
{TALLY_CTE}
INSERT INTO dbo.BusinessHours (ScheduleCode, CalendarDate, DayName, IsWorkingDay, StartTime, EndTime, WorkingMinutes, HolidayName)
SELECT s.ScheduleCode, d.D, CHOOSE(w.Dow, N'Monday', N'Tuesday', N'Wednesday', N'Thursday', N'Friday', N'Saturday', N'Sunday'),
       x.IsWorkingDay, CASE WHEN x.IsWorkingDay = 1 THEN s.StartTime END, CASE WHEN x.IsWorkingDay = 1 THEN s.EndTime END,
       CASE WHEN x.IsWorkingDay = 1 THEN s.Minutes ELSE 0 END, h.HolidayName
FROM Tally t
CROSS APPLY (SELECT DATEADD(DAY, t.n - 1, CAST('2025-01-01' AS date)) AS D) d
CROSS APPLY (SELECT (DATEDIFF(DAY, CAST('1900-01-01' AS date), d.D) % 7) + 1 AS Dow) w   -- 1 = Monday
CROSS JOIN (VALUES (N'HO', CAST('08:00' AS time(0)), CAST('16:30' AS time(0)), 510), (N'24X7', CAST('00:00' AS time(0)), CAST('23:59' AS time(0)), 1440)) s(ScheduleCode, StartTime, EndTime, Minutes)
LEFT JOIN @Holidays h ON h.HolidayDate = d.D
CROSS APPLY (SELECT CAST(CASE WHEN s.ScheduleCode = N'24X7' OR (w.Dow <= 5 AND h.HolidayDate IS NULL) THEN 1 ELSE 0 END AS bit) AS IsWorkingDay) x
WHERE t.n <= 365;
GO
""")
        return "\n".join(out)

    def coded_sql(self):
        """Lookup and integer-coded temporary tables; DECODE_SQL turns them into the final rows."""
        U = self.user_index
        cat_index = {(c[0], c[1]): i for i, c in enumerate(CATEGORIES, 1)}
        ci_index = {c["CiCode"]: i for i, c in enumerate(self.cis, 1)}
        svc_index = {s[0]: i for i, s in enumerate(SERVICES, 1)}
        grp = {g: i for i, g in enumerate(GROUP_CODES, 1)}
        loc_index = {loc[0]: i for i, loc in enumerate(bp.LOCATIONS, 1)}
        item_index = {c[0]: i for i, c in enumerate(CATALOG, 1)}
        res_codes = sorted({x for t in INCIDENT_TYPES for x in t[12]} | {"Resolved by parent"})
        res_index = {x: i for i, x in enumerate(res_codes, 1)}
        roles, role_index = [], {}
        task_names = sorted({t[0] for c in CATALOG for t in c[11]} | {"Set up desk phone extension", "Confirm the leaver has no system accounts"})
        task_index = {x: i for i, x in enumerate(task_names, 1)}

        def uid(no):
            return U[EMP[no]["UserId"]] if no else 0

        def m(t):
            return minute_of_year(t) if t else 0

        def e(no):
            return int(no[1:]) if no else 0

        def role(system, rolecode):
            if not system:
                return 0
            key = (system, rolecode)
            if key not in role_index:
                roles.append(key)
                role_index[key] = len(roles)
            return role_index[key]

        status = {"New": 1, "Awaiting approval": 2, "In progress": 3, "Fulfilled": 4, "Rejected": 5, "Cancelled": 6}
        chan = {"Portal": 1, "Phone": 2, "Email": 3, "WalkIn": 4, "Monitoring": 5}
        R, A, T = [], [], []
        for rq in self.requests:
            breached = (rq.fulfilled > rq.due) if rq.fulfilled else (rq.status in ("New", "In progress") and rq.due is not None and rq.due < END)
            R.append((rq.n, m(rq.open_t), item_index[rq.item], e(rq.for_no), e(rq.by_no), self.unit_id(rq.unit), role(rq.system, rq.role), uid(rq.creator), chan[rq.channel],
                      grp[rq.group], uid(rq.assignee), rq.priority, status[rq.status], m(rq.approved), m(rq.fulfilled), m(rq.closed), m(rq.due), int(bool(breached)), rq.desc))
            for k, (kind, ap, req_t, dec_t, decision) in enumerate(rq.approvals, 1):
                A.append((rq.n, k, 1 if kind == "LineManager" else 2, uid(ap), m(req_t), m(dec_t), {"Approved": 1, "Rejected": 2, "Pending": 3}[decision]))
            for k, (name, group, agent, s, en) in enumerate(rq.tasks, 1):
                T.append((rq.n, k, task_index[name], grp[group], uid(agent), m(s), m(en)))
        I, X = [], []
        for inc in self.incidents:
            pri = inc.priority
            breached = (inc.resp is not None and inc.resp > inc.resp_due) or (inc.resp is None and inc.resp_due < END) \
                or (inc.res is not None and inc.res > inc.res_due) or (inc.res is None and inc.res_due < END)
            I.append((inc.n, m(inc.open_t), svc_index[inc.svc], ci_index[inc.ci], cat_index[inc.cat], inc.imp, inc.urg, e(inc.rep), self.unit_id(inc.unit) if inc.unit else 0,
                      loc_index[inc.loc], U[EMP[inc.creator]["UserId"]] if inc.creator != "SYS_MONITOR" else U["SYS_MONITOR"], chan[inc.channel], grp[inc.init_group],
                      grp[inc.group], uid(inc.triage[0]) if inc.triage else 0, m(inc.triage[1]) if inc.triage else 0, uid(inc.asg), m(inc.resp), m(inc.reassign),
                      uid(inc.rsl), m(inc.res), uid(inc.closer), m(inc.close), uid(inc.wn1[0]) if inc.wn1 else 0, m(inc.wn1[1]) if inc.wn1 else 0,
                      uid(inc.wn2[0]) if inc.wn2 else 0, m(inc.wn2[1]) if inc.wn2 else 0, uid(inc.link[0]) if inc.link else 0, m(inc.link[1]) if inc.link else 0,
                      m(inc.resp_due), m(inc.res_due), int(bool(breached)), res_index[inc.res_code] if inc.res else 0, inc.major, inc.parent.n if inc.parent else 0, inc.desc))
            assert pri == priority(inc.imp, inc.urg)
            for k, (who, t, note) in enumerate(inc.extra, 1):
                X.append((inc.n, k, uid(who), m(t), note))
        presence = []
        for u in self.users:
            no = u["EmployeeNumber"]
            if not no:
                continue
            p = EMP[no]
            days = "".join("P" if present(no, d) else "A" if available(no, d) else "-" for d in bp.DAYS_2025)
            s0, s1 = SHIFT_WINDOWS[p["ShiftCode"]]
            presence.append((u["UserId"], s0, s1, days))

        def compact(table, rows):
            out = []
            for i in range(0, len(rows), 1000):
                out.append(f"INSERT INTO {table} VALUES\n" + ",\n".join("(" + ",".join(str(v) for v in row) + ")" for row in rows[i:i + 1000]) + ";")
            return "\n".join(out)

        self.stats = {"requests": len(R), "approvals": len(A), "tasks": len(T), "incidents": len(I), "configuration items": len(self.cis),
                      "employees": len(bp.employees()), "users": len(self.users)}
        notes = [(cat, k, text) for cat, texts in WORK_NOTES.items() for k, text in enumerate(texts, 1)]
        return "\n".join([
            "-- Lookups and integer-coded rows written by build_it.py (times are minutes after 2025-01-01 00:00 local; 0 = none).",
            "CREATE TABLE #U (i smallint PRIMARY KEY, UserId nvarchar(16) NOT NULL);",
            insert("#U", ["i", "UserId"], [(i, u["UserId"]) for i, u in enumerate(self.users, 1)], chunk=1000),
            "CREATE TABLE #Unit (i smallint PRIMARY KEY, DepartmentCode nvarchar(12) NOT NULL, SectionCode nvarchar(16) NULL, LocationCode nvarchar(10) NOT NULL);",
            insert("#Unit", ["i", "DepartmentCode", "SectionCode", "LocationCode"], [(i,) + u for i, u in enumerate(self.units, 1)], chunk=1000),
            "CREATE TABLE #Desc (i smallint PRIMARY KEY, Text nvarchar(200) NOT NULL);",
            insert("#Desc", ["i", "Text"], list(enumerate(self.descs, 1)), chunk=1000),
            "CREATE TABLE #Role (i smallint PRIMARY KEY, SystemCode nvarchar(10) NOT NULL, RoleCode nvarchar(30) NULL);",
            insert("#Role", ["i", "SystemCode", "RoleCode"], [(i,) + x for i, x in enumerate(roles, 1)] or [(0, "-", None)], chunk=1000),
            "CREATE TABLE #Ci (i smallint PRIMARY KEY, CiCode nvarchar(20) NOT NULL);",
            insert("#Ci", ["i", "CiCode"], [(i, c) for c, i in ci_index.items()], chunk=1000),
            "CREATE TABLE #Cat (i smallint PRIMARY KEY, Category nvarchar(30) NOT NULL, Subcategory nvarchar(40) NOT NULL);",
            insert("#Cat", ["i", "Category", "Subcategory"], [(i, c[0], c[1]) for c, i in cat_index.items()], chunk=1000),
            "CREATE TABLE #TaskName (i smallint PRIMARY KEY, Name nvarchar(100) NOT NULL);",
            insert("#TaskName", ["i", "Name"], list(enumerate(task_names, 1)), chunk=1000),
            "CREATE TABLE #Note (Category nvarchar(30) NOT NULL, Seq int NOT NULL, Text nvarchar(200) NOT NULL, PRIMARY KEY (Category, Seq));",
            insert("#Note", ["Category", "Seq", "Text"], notes, chunk=1000),
            "CREATE TABLE #Res (i smallint PRIMARY KEY, Text nvarchar(40) NOT NULL);",
            insert("#Res", ["i", "Text"], list(enumerate(res_codes, 1)), chunk=1000),
            "CREATE TABLE #Item (i smallint PRIMARY KEY, CatalogItemCode nvarchar(20) NOT NULL);",
            insert("#Item", ["i", "CatalogItemCode"], [(i, c) for c, i in item_index.items()], chunk=1000),
            "CREATE TABLE #Svc (i smallint PRIMARY KEY, ServiceCode nvarchar(10) NOT NULL);",
            insert("#Svc", ["i", "ServiceCode"], [(i, c) for c, i in svc_index.items()], chunk=1000),
            "CREATE TABLE #Grp (i smallint PRIMARY KEY, GroupCode nvarchar(16) NOT NULL);",
            insert("#Grp", ["i", "GroupCode"], [(i, c) for c, i in grp.items()], chunk=1000),
            "CREATE TABLE #Loc (i smallint PRIMARY KEY, LocationCode nvarchar(10) NOT NULL);",
            insert("#Loc", ["i", "LocationCode"], [(i, c) for c, i in loc_index.items()], chunk=1000),
            "GO",
            "CREATE TABLE #R (n int PRIMARY KEY, OpenM int, Item smallint, ForE smallint, ByE smallint, Unit smallint, Role smallint, CreU smallint, Chan tinyint, Grp tinyint,"
            " AsgU smallint, Pri tinyint, St tinyint, ApprM int, FulM int, CloM int, DueM int, Br bit, Dsc smallint);",
            compact("#R", R),
            "GO",
            "CREATE TABLE #A (n int, Step tinyint, Kind tinyint, ApU smallint, ReqM int, DecM int, Dec tinyint, PRIMARY KEY (n, Step));",
            compact("#A", A),
            "CREATE TABLE #T (n int, Seq tinyint, Name smallint, Grp tinyint, AgU smallint, StartM int, EndM int, PRIMARY KEY (n, Seq));",
            compact("#T", T),
            "GO",
            "CREATE TABLE #I (n int PRIMARY KEY, OpenM int, Svc tinyint, Ci smallint, Cat smallint, Imp tinyint, Urg tinyint, RepE smallint, Unit smallint, Loc tinyint,"
            " CreU smallint, Chan tinyint, IGrp tinyint, Grp tinyint, TriU smallint, TriM int, AsgU smallint, RespM int, ReaM int, RslU smallint, RslM int, CloU smallint,"
            " CloM int, W1U smallint, W1M int, W2U smallint, W2M int, LnkU smallint, LnkM int, RDueM int, XDueM int, Br bit, Res tinyint, Maj bit, Par int, Dsc smallint);",
            compact("#I", I),
            "GO",
            "CREATE TABLE #X (n int, k tinyint, U smallint, M int, Note nvarchar(200), PRIMARY KEY (n, k));",
            insert("#X", ["n", "k", "U", "M", "Note"], X, chunk=1000),
            "-- Presence of every help-desk user per day of 2025 (P = at work, A = available on call, - = away) and shift window, for the checks at the end.",
            "CREATE TABLE #Presence (UserId nvarchar(16) PRIMARY KEY, ShiftStart int NOT NULL, ShiftEnd int NOT NULL, Days char(365) NOT NULL);",
            compact("#Presence", [(f"'{u}'", s0, s1, f"'{days}'") for u, s0, s1, days in presence]),
            "GO",
        ])


# ------------------------------------------------------------------------------------------------ static SQL
SCHEMA_SQL = """CREATE TABLE dbo.Locations (
    LocationCode    nvarchar(10)  NOT NULL CONSTRAINT PK_Locations PRIMARY KEY,   -- HO or BR-... (Personnel codes)
    LocationName    nvarchar(60)  NOT NULL,
    LocationNameAr  nvarchar(60)  NOT NULL,
    LocationType    nvarchar(12)  NOT NULL,   -- HeadOffice | Branch
    Emirate         nvarchar(20)  NOT NULL,
    Address         nvarchar(120) NOT NULL,
    OpenedDate      date          NOT NULL,
    TellerCounters  int           NOT NULL
);
CREATE TABLE dbo.EmployeeDirectory (
    EmployeeNumber  nvarchar(10)  NOT NULL CONSTRAINT PK_EmployeeDirectory PRIMARY KEY,
    FullName        nvarchar(100) NOT NULL,
    FullNameAr      nvarchar(100) NULL,
    DepartmentCode  nvarchar(12)  NOT NULL,   -- at year end, or at termination for leavers
    SectionCode     nvarchar(16)  NULL,       -- NULL for department heads and branch managers
    LocationCode    nvarchar(10)  NOT NULL CONSTRAINT FK_EmployeeDirectory_Locations REFERENCES dbo.Locations (LocationCode),
    Email           nvarchar(120) NULL,
    UserId          nvarchar(16)  NULL,       -- NULL for drivers, messengers and security guards (no login)
    IsActive        bit           NOT NULL,
    HireDate        date          NOT NULL,
    TerminationDate date          NULL
);
CREATE TABLE dbo.Users (
    UserId            nvarchar(16)  NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    EmployeeNumber    nvarchar(10)  NULL CONSTRAINT FK_Users_EmployeeDirectory REFERENCES dbo.EmployeeDirectory (EmployeeNumber),   -- NULL for SYS_MONITOR
    FullName          nvarchar(100) NOT NULL,
    FullNameAr        nvarchar(100) NULL,
    DepartmentCode    nvarchar(12)  NULL,
    SectionCode       nvarchar(16)  NULL,
    RoleCode          nvarchar(30)  NOT NULL,   -- Requester | Agent | GroupLead | ServiceDeskManager | ServiceAccount
    ApprovalLimitAed  decimal(14,2) NULL,
    Status            nvarchar(10)  NOT NULL,   -- Active | Disabled
    AccessGrantedDate date          NOT NULL,
    AccessRevokedDate date          NULL
);
CREATE TABLE dbo.AssignmentGroups (
    GroupCode               nvarchar(16)  NOT NULL CONSTRAINT PK_AssignmentGroups PRIMARY KEY,   -- the ITD section code
    GroupName               nvarchar(60)  NOT NULL,
    GroupNameAr             nvarchar(60)  NOT NULL,
    GroupEmail              nvarchar(80)  NOT NULL,
    GroupLeadEmployeeNumber nvarchar(10)  NOT NULL CONSTRAINT FK_AssignmentGroups_EmployeeDirectory REFERENCES dbo.EmployeeDirectory (EmployeeNumber),
    Description             nvarchar(200) NOT NULL
);
CREATE TABLE dbo.Services (
    ServiceCode         nvarchar(10)  NOT NULL CONSTRAINT PK_Services PRIMARY KEY,
    ServiceName         nvarchar(60)  NOT NULL,
    ServiceNameAr       nvarchar(60)  NOT NULL,
    OwningGroupCode     nvarchar(16)  NOT NULL CONSTRAINT FK_Services_AssignmentGroups REFERENCES dbo.AssignmentGroups (GroupCode),
    BusinessCriticality nvarchar(10)  NOT NULL,   -- Critical | High | Medium | Low
    Description         nvarchar(200) NOT NULL
);
CREATE TABLE dbo.ConfigurationItems (
    CiCode                   nvarchar(20)  NOT NULL CONSTRAINT PK_ConfigurationItems PRIMARY KEY,
    CiName                   nvarchar(100) NOT NULL,
    CiType                   nvarchar(20)  NOT NULL,   -- Server | Database | Application | Network link | Switch | Wi-Fi | VPN gateway | Firewall | Printer | ATM | Laptop | Desktop
    ServiceCode              nvarchar(10)  NOT NULL CONSTRAINT FK_ConfigurationItems_Services REFERENCES dbo.Services (ServiceCode),
    LocationCode             nvarchar(10)  NOT NULL CONSTRAINT FK_ConfigurationItems_Locations REFERENCES dbo.Locations (LocationCode),
    OwningGroupCode          nvarchar(16)  NOT NULL CONSTRAINT FK_ConfigurationItems_AssignmentGroups REFERENCES dbo.AssignmentGroups (GroupCode),
    AssignedToEmployeeNumber nvarchar(10)  NULL CONSTRAINT FK_ConfigurationItems_EmployeeDirectory REFERENCES dbo.EmployeeDirectory (EmployeeNumber),
    AssetTag                 nvarchar(20)  NOT NULL CONSTRAINT UQ_ConfigurationItems_AssetTag UNIQUE,
    Model                    nvarchar(60)  NOT NULL,
    Status                   nvarchar(12)  NOT NULL,   -- Operational | In use | In stock
    InstalledDate            date          NOT NULL
);
CREATE TABLE dbo.Categories (
    Category          nvarchar(30) NOT NULL,
    Subcategory       nvarchar(40) NOT NULL,
    CategoryNameAr    nvarchar(40) NOT NULL,
    SubcategoryNameAr nvarchar(60) NOT NULL,
    TicketType        nvarchar(10) NOT NULL,   -- Incident | Request
    DefaultGroupCode  nvarchar(16) NOT NULL CONSTRAINT FK_Categories_AssignmentGroups REFERENCES dbo.AssignmentGroups (GroupCode),
    CONSTRAINT PK_Categories PRIMARY KEY (Category, Subcategory)
);
CREATE TABLE dbo.SlaPolicies (
    SlaPolicyCode           nvarchar(10) NOT NULL CONSTRAINT PK_SlaPolicies PRIMARY KEY,
    PolicyName              nvarchar(60) NOT NULL,
    TicketType              nvarchar(10) NOT NULL,   -- Incident | Request
    Priority                nvarchar(2)  NULL,       -- P1-P4 for incidents
    ScheduleCode            nvarchar(6)  NOT NULL,   -- HO (business hours) | 24X7
    ResponseTargetMinutes   int          NOT NULL,   -- in the schedule's working minutes
    ResolutionTargetMinutes int          NOT NULL,   -- one business day = 510 minutes
    ResponseTarget          nvarchar(40) NOT NULL,
    ResolutionTarget        nvarchar(40) NOT NULL
);
CREATE TABLE dbo.BusinessHours (
    ScheduleCode   nvarchar(6)  NOT NULL,   -- HO | 24X7
    CalendarDate   date         NOT NULL,
    DayName        nvarchar(10) NOT NULL,
    IsWorkingDay   bit          NOT NULL,
    StartTime      time(0)      NULL,
    EndTime        time(0)      NULL,
    WorkingMinutes int          NOT NULL,
    HolidayName    nvarchar(60) NULL,
    CONSTRAINT PK_BusinessHours PRIMARY KEY (ScheduleCode, CalendarDate)
);
CREATE TABLE dbo.ServiceCatalogItems (
    CatalogItemCode         nvarchar(20)  NOT NULL CONSTRAINT PK_ServiceCatalogItems PRIMARY KEY,
    ItemName                nvarchar(80)  NOT NULL,
    ItemNameAr              nvarchar(80)  NOT NULL,
    Category                nvarchar(30)  NOT NULL,
    Subcategory             nvarchar(40)  NOT NULL,
    SystemCode              nvarchar(10)  NULL,       -- CBS | DIGITAL | FIN | HRMS | ITSM for system-access items
    FulfilmentGroupCode     nvarchar(16)  NOT NULL CONSTRAINT FK_ServiceCatalogItems_AssignmentGroups REFERENCES dbo.AssignmentGroups (GroupCode),
    RequiresManagerApproval bit           NOT NULL,
    RequiresOwnerApproval   bit           NOT NULL,   -- head of the department that owns the system
    OwnerDepartmentCode     nvarchar(12)  NULL,
    TargetDays              int           NOT NULL,   -- business days
    SlaPolicyCode           nvarchar(10)  NOT NULL CONSTRAINT FK_ServiceCatalogItems_SlaPolicies REFERENCES dbo.SlaPolicies (SlaPolicyCode),
    DefaultPriority         nvarchar(2)   NOT NULL,
    Description             nvarchar(300) NOT NULL,
    CONSTRAINT FK_ServiceCatalogItems_Categories FOREIGN KEY (Category, Subcategory) REFERENCES dbo.Categories (Category, Subcategory)
);
CREATE TABLE dbo.ServiceRequests (
    RequestNumber              nvarchar(12)  NOT NULL CONSTRAINT PK_ServiceRequests PRIMARY KEY,
    CatalogItemCode            nvarchar(20)  NOT NULL CONSTRAINT FK_ServiceRequests_ServiceCatalogItems REFERENCES dbo.ServiceCatalogItems (CatalogItemCode),
    RequestedForEmployeeNumber nvarchar(10)  NOT NULL CONSTRAINT FK_ServiceRequests_EmployeeDirectory_For REFERENCES dbo.EmployeeDirectory (EmployeeNumber),
    RequestedByEmployeeNumber  nvarchar(10)  NOT NULL CONSTRAINT FK_ServiceRequests_EmployeeDirectory_By REFERENCES dbo.EmployeeDirectory (EmployeeNumber),
    DepartmentCode             nvarchar(12)  NOT NULL,   -- requested-for employee's unit when the request was logged
    SectionCode                nvarchar(16)  NULL,
    LocationCode               nvarchar(10)  NOT NULL CONSTRAINT FK_ServiceRequests_Locations REFERENCES dbo.Locations (LocationCode),
    RequestedSystemCode        nvarchar(10)  NULL,
    RequestedRoleCode          nvarchar(30)  NULL,
    CreatedByUserId            nvarchar(16)  NOT NULL CONSTRAINT FK_ServiceRequests_Users_Created REFERENCES dbo.Users (UserId),
    Channel                    nvarchar(10)  NOT NULL,   -- Portal | Phone | Email | WalkIn
    AssignmentGroupCode        nvarchar(16)  NOT NULL CONSTRAINT FK_ServiceRequests_AssignmentGroups REFERENCES dbo.AssignmentGroups (GroupCode),
    AssignedToUserId           nvarchar(16)  NULL CONSTRAINT FK_ServiceRequests_Users_Assigned REFERENCES dbo.Users (UserId),
    Priority                   nvarchar(2)   NOT NULL,
    Status                     nvarchar(20)  NOT NULL,   -- New | Awaiting approval | In progress | Fulfilled | Rejected | Cancelled
    OpenedAt                   datetime2(0)  NOT NULL,   -- local time (Asia/Dubai)
    ApprovedAt                 datetime2(0)  NULL,
    FulfilledAt                datetime2(0)  NULL,
    ClosedAt                   datetime2(0)  NULL,
    SlaDueAt                   datetime2(0)  NULL,
    SlaBreached                bit           NOT NULL,
    ShortDescription           nvarchar(200) NOT NULL
);
CREATE TABLE dbo.RequestApprovals (
    ApprovalId     int           NOT NULL CONSTRAINT PK_RequestApprovals PRIMARY KEY,
    RequestNumber  nvarchar(12)  NOT NULL CONSTRAINT FK_RequestApprovals_ServiceRequests REFERENCES dbo.ServiceRequests (RequestNumber),
    StepNumber     tinyint       NOT NULL,
    ApprovalType   nvarchar(12)  NOT NULL,   -- LineManager | SystemOwner
    ApproverUserId nvarchar(16)  NOT NULL CONSTRAINT FK_RequestApprovals_Users REFERENCES dbo.Users (UserId),
    RequestedAt    datetime2(0)  NOT NULL,
    DecidedAt      datetime2(0)  NULL,
    Decision       nvarchar(10)  NOT NULL,   -- Approved | Rejected | Pending
    Comments       nvarchar(200) NULL,
    CONSTRAINT UQ_RequestApprovals_Step UNIQUE (RequestNumber, StepNumber)
);
CREATE TABLE dbo.RequestTasks (
    TaskId              int           NOT NULL CONSTRAINT PK_RequestTasks PRIMARY KEY,
    RequestNumber       nvarchar(12)  NOT NULL CONSTRAINT FK_RequestTasks_ServiceRequests REFERENCES dbo.ServiceRequests (RequestNumber),
    TaskNumber          tinyint       NOT NULL,
    TaskName            nvarchar(100) NOT NULL,
    AssignmentGroupCode nvarchar(16)  NOT NULL CONSTRAINT FK_RequestTasks_AssignmentGroups REFERENCES dbo.AssignmentGroups (GroupCode),
    AssignedToUserId    nvarchar(16)  NOT NULL CONSTRAINT FK_RequestTasks_Users REFERENCES dbo.Users (UserId),
    Status              nvarchar(12)  NOT NULL,   -- Completed
    StartedAt           datetime2(0)  NOT NULL,
    CompletedAt         datetime2(0)  NULL,
    CONSTRAINT UQ_RequestTasks_Number UNIQUE (RequestNumber, TaskNumber)
);
CREATE TABLE dbo.Incidents (
    IncidentNumber           nvarchar(12)  NOT NULL CONSTRAINT PK_Incidents PRIMARY KEY,
    ServiceCode              nvarchar(10)  NOT NULL CONSTRAINT FK_Incidents_Services REFERENCES dbo.Services (ServiceCode),
    CiCode                   nvarchar(20)  NOT NULL CONSTRAINT FK_Incidents_ConfigurationItems REFERENCES dbo.ConfigurationItems (CiCode),
    Category                 nvarchar(30)  NOT NULL,
    Subcategory              nvarchar(40)  NOT NULL,
    Impact                   tinyint       NOT NULL,   -- 1 high ... 3 low
    Urgency                  tinyint       NOT NULL,   -- 1 high ... 3 low
    Priority                 nvarchar(2)   NOT NULL,   -- P1-P4 from impact x urgency
    ReportedByEmployeeNumber nvarchar(10)  NULL CONSTRAINT FK_Incidents_EmployeeDirectory REFERENCES dbo.EmployeeDirectory (EmployeeNumber),   -- NULL for monitoring
    DepartmentCode           nvarchar(12)  NULL,       -- reporter's unit when opened
    SectionCode              nvarchar(16)  NULL,
    LocationCode             nvarchar(10)  NOT NULL CONSTRAINT FK_Incidents_Locations REFERENCES dbo.Locations (LocationCode),
    CreatedByUserId          nvarchar(16)  NOT NULL CONSTRAINT FK_Incidents_Users_Created REFERENCES dbo.Users (UserId),
    Channel                  nvarchar(10)  NOT NULL,   -- Portal | Phone | Email | Monitoring
    AssignmentGroupCode      nvarchar(16)  NOT NULL CONSTRAINT FK_Incidents_AssignmentGroups REFERENCES dbo.AssignmentGroups (GroupCode),
    AssignedToUserId         nvarchar(16)  NULL CONSTRAINT FK_Incidents_Users_Assigned REFERENCES dbo.Users (UserId),
    Status                   nvarchar(12)  NOT NULL,   -- New | In Progress | Resolved | Closed
    OpenedAt                 datetime2(0)  NOT NULL,   -- local time (Asia/Dubai)
    ResponseDueAt            datetime2(0)  NOT NULL,
    RespondedAt              datetime2(0)  NULL,
    ResolutionDueAt          datetime2(0)  NOT NULL,
    ResolvedAt               datetime2(0)  NULL,
    ResolvedByUserId         nvarchar(16)  NULL CONSTRAINT FK_Incidents_Users_Resolved REFERENCES dbo.Users (UserId),
    ClosedAt                 datetime2(0)  NULL,
    SlaBreached              bit           NOT NULL,
    ResolutionCode           nvarchar(40)  NULL,
    IsMajorIncident          bit           NOT NULL,
    ParentIncidentNumber     nvarchar(12)  NULL CONSTRAINT FK_Incidents_Parent REFERENCES dbo.Incidents (IncidentNumber),
    ShortDescription         nvarchar(200) NOT NULL,
    CONSTRAINT FK_Incidents_Categories FOREIGN KEY (Category, Subcategory) REFERENCES dbo.Categories (Category, Subcategory)
);
CREATE TABLE dbo.IncidentActivities (
    ActivityId     int           NOT NULL CONSTRAINT PK_IncidentActivities PRIMARY KEY,
    IncidentNumber nvarchar(12)  NOT NULL CONSTRAINT FK_IncidentActivities_Incidents REFERENCES dbo.Incidents (IncidentNumber),
    ActivitySeq    int           NOT NULL,   -- order within the incident
    ActivityType   nvarchar(12)  NOT NULL,   -- Created | Assignment | StatusChange | WorkNote | Link
    FieldName      nvarchar(20)  NULL,       -- Status | AssignmentGroup | AssignedTo | ParentIncident
    FromValue      nvarchar(40)  NULL,
    ToValue        nvarchar(40)  NULL,
    UserId         nvarchar(16)  NOT NULL CONSTRAINT FK_IncidentActivities_Users REFERENCES dbo.Users (UserId),
    ActivityAt     datetime2(0)  NOT NULL,
    Note           nvarchar(200) NULL,
    CONSTRAINT UQ_IncidentActivities_Seq UNIQUE (IncidentNumber, ActivitySeq)
);
GO
"""

DECODE_SQL = r"""-- Final rows from the coded tables.
DECLARE @T0 datetime2(0) = '2025-01-01T00:00:00';
INSERT INTO dbo.ServiceRequests (RequestNumber, CatalogItemCode, RequestedForEmployeeNumber, RequestedByEmployeeNumber, DepartmentCode, SectionCode, LocationCode,
       RequestedSystemCode, RequestedRoleCode, CreatedByUserId, Channel, AssignmentGroupCode, AssignedToUserId, Priority, Status, OpenedAt, ApprovedAt, FulfilledAt,
       ClosedAt, SlaDueAt, SlaBreached, ShortDescription)
SELECT CONCAT(N'REQ', RIGHT(CONCAT(N'0000000', 1000 + r.n), 7)), it.CatalogItemCode, CONCAT(N'W', r.ForE), CONCAT(N'W', r.ByE), un.DepartmentCode, un.SectionCode,
       un.LocationCode, ro.SystemCode, ro.RoleCode, cu.UserId, CHOOSE(r.Chan, N'Portal', N'Phone', N'Email', N'WalkIn'), g.GroupCode, au.UserId, CONCAT(N'P', r.Pri),
       CHOOSE(r.St, N'New', N'Awaiting approval', N'In progress', N'Fulfilled', N'Rejected', N'Cancelled'),
       DATEADD(MINUTE, r.OpenM, @T0), DATEADD(MINUTE, NULLIF(r.ApprM, 0), @T0), DATEADD(MINUTE, NULLIF(r.FulM, 0), @T0), DATEADD(MINUTE, NULLIF(r.CloM, 0), @T0),
       DATEADD(MINUTE, NULLIF(r.DueM, 0), @T0), r.Br,
       LEFT(REPLACE(REPLACE(REPLACE(REPLACE(d.Text, N'{for}', fe.FullName), N'{start}', CONVERT(nvarchar(11), fe.HireDate, 106)),
                            N'{last}', ISNULL(CONVERT(nvarchar(11), fe.TerminationDate, 106), N'')), N'{role}', ISNULL(ro.RoleCode, N'')), 200)
FROM #R r
JOIN #Item it ON it.i = r.Item
JOIN #Unit un ON un.i = r.Unit
LEFT JOIN #Role ro ON ro.i = r.Role
JOIN #U cu ON cu.i = r.CreU
JOIN #Grp g ON g.i = r.Grp
LEFT JOIN #U au ON au.i = r.AsgU
JOIN #Desc d ON d.i = r.Dsc
JOIN dbo.EmployeeDirectory fe ON fe.EmployeeNumber = CONCAT(N'W', r.ForE);

INSERT INTO dbo.RequestApprovals (ApprovalId, RequestNumber, StepNumber, ApprovalType, ApproverUserId, RequestedAt, DecidedAt, Decision, Comments)
SELECT ROW_NUMBER() OVER (ORDER BY a.n, a.Step), CONCAT(N'REQ', RIGHT(CONCAT(N'0000000', 1000 + a.n), 7)), a.Step, CHOOSE(a.Kind, N'LineManager', N'SystemOwner'), u.UserId,
       DATEADD(MINUTE, a.ReqM, @T0), DATEADD(MINUTE, NULLIF(a.DecM, 0), @T0), CHOOSE(a.Dec, N'Approved', N'Rejected', N'Pending'),
       CASE WHEN a.Dec = 2 THEN CHOOSE(1 + CAST(x.R1 * 3 AS int), N'Not needed for the current role.', N'Please use the shared team resource instead.', N'Raise again with a business justification.')
            WHEN a.Dec = 1 AND x.R2 < 0.15 THEN N'Approved.' END
FROM #A a
JOIN #U u ON u.i = a.ApU
CROSS APPLY gen.Rand(3000000 + a.n * 10 + a.Step) x;

INSERT INTO dbo.RequestTasks (TaskId, RequestNumber, TaskNumber, TaskName, AssignmentGroupCode, AssignedToUserId, Status, StartedAt, CompletedAt)
SELECT ROW_NUMBER() OVER (ORDER BY t.n, t.Seq), CONCAT(N'REQ', RIGHT(CONCAT(N'0000000', 1000 + t.n), 7)), t.Seq, tn.Name, g.GroupCode, u.UserId, N'Completed',
       DATEADD(MINUTE, t.StartM, @T0), DATEADD(MINUTE, NULLIF(t.EndM, 0), @T0)
FROM #T t
JOIN #TaskName tn ON tn.i = t.Name
JOIN #Grp g ON g.i = t.Grp
JOIN #U u ON u.i = t.AgU;
CREATE INDEX IX_ServiceRequests_OpenedAt ON dbo.ServiceRequests (OpenedAt);
CREATE INDEX IX_ServiceRequests_RequestedFor ON dbo.ServiceRequests (RequestedForEmployeeNumber);
GO
DECLARE @T0 datetime2(0) = '2025-01-01T00:00:00';
-- Parents first (the self-reference), then the rest.
INSERT INTO dbo.Incidents (IncidentNumber, ServiceCode, CiCode, Category, Subcategory, Impact, Urgency, Priority, ReportedByEmployeeNumber, DepartmentCode, SectionCode,
       LocationCode, CreatedByUserId, Channel, AssignmentGroupCode, AssignedToUserId, Status, OpenedAt, ResponseDueAt, RespondedAt, ResolutionDueAt, ResolvedAt,
       ResolvedByUserId, ClosedAt, SlaBreached, ResolutionCode, IsMajorIncident, ParentIncidentNumber, ShortDescription)
SELECT CONCAT(N'INC', RIGHT(CONCAT(N'0000000', 4000 + i.n), 7)), sv.ServiceCode, ci.CiCode, ca.Category, ca.Subcategory, i.Imp, i.Urg,
       CONCAT(N'P', CASE i.Imp + i.Urg WHEN 2 THEN 1 WHEN 3 THEN 2 WHEN 4 THEN 3 ELSE 4 END),
       CASE WHEN i.RepE > 0 THEN CONCAT(N'W', i.RepE) END, un.DepartmentCode, un.SectionCode, lo.LocationCode, cu.UserId,
       CHOOSE(i.Chan, N'Portal', N'Phone', N'Email', N'WalkIn', N'Monitoring'), g.GroupCode, COALESCE(ru.UserId, au.UserId),
       CASE WHEN i.CloM > 0 THEN N'Closed' WHEN i.RslM > 0 THEN N'Resolved' WHEN i.RespM > 0 THEN N'In Progress' ELSE N'New' END,
       DATEADD(MINUTE, i.OpenM, @T0), DATEADD(MINUTE, i.RDueM, @T0), DATEADD(MINUTE, NULLIF(i.RespM, 0), @T0), DATEADD(MINUTE, i.XDueM, @T0),
       DATEADD(MINUTE, NULLIF(i.RslM, 0), @T0), ru.UserId, DATEADD(MINUTE, NULLIF(i.CloM, 0), @T0), i.Br, rc.Text, i.Maj,
       CASE WHEN i.Par > 0 THEN CONCAT(N'INC', RIGHT(CONCAT(N'0000000', 4000 + i.Par), 7)) END,
       LEFT(REPLACE(REPLACE(d.Text, N'{ci}', ci.CiCode), N'{loc}', lc.LocationName), 200)
FROM #I i
JOIN #Svc sv ON sv.i = i.Svc
JOIN #Ci ci ON ci.i = i.Ci
JOIN #Cat ca ON ca.i = i.Cat
LEFT JOIN #Unit un ON un.i = i.Unit
JOIN #Loc lo ON lo.i = i.Loc
JOIN dbo.Locations lc ON lc.LocationCode = lo.LocationCode
JOIN #U cu ON cu.i = i.CreU
JOIN #Grp g ON g.i = i.Grp
LEFT JOIN #U au ON au.i = i.AsgU
LEFT JOIN #U ru ON ru.i = i.RslU
LEFT JOIN #Res rc ON rc.i = i.Res
JOIN #Desc d ON d.i = i.Dsc
ORDER BY CASE WHEN i.Par > 0 THEN 1 ELSE 0 END, i.n;
CREATE INDEX IX_Incidents_OpenedAt ON dbo.Incidents (OpenedAt);
CREATE INDEX IX_Incidents_Ci ON dbo.Incidents (CiCode, OpenedAt);
CREATE INDEX IX_Incidents_Group ON dbo.Incidents (AssignmentGroupCode, OpenedAt);
GO
-- Activity log: one row per event, in time order within each incident; work-note texts drawn with gen.Rand.
DECLARE @T0 datetime2(0) = '2025-01-01T00:00:00';
WITH Act AS (
    SELECT i.n, v.Ord, 0 AS Sub, v.ActivityType, v.FieldName, v.FromValue, v.ToValue, v.U, v.M, v.Note
    FROM #I i
    JOIN #Grp ig ON ig.i = i.IGrp
    JOIN #Grp fg ON fg.i = i.Grp
    JOIN #Cat ca ON ca.i = i.Cat
    LEFT JOIN #U au ON au.i = i.AsgU
    LEFT JOIN #U ru ON ru.i = i.RslU
    LEFT JOIN #Res rc ON rc.i = i.Res
    CROSS APPLY gen.Rand(5000000 + i.n) x
    JOIN #Note n1 ON n1.Category = ca.Category AND n1.Seq = 1 + CAST(x.R1 * 4 AS int)
    JOIN #Note n2 ON n2.Category = ca.Category AND n2.Seq = 1 + CAST(x.R2 * 4 AS int)
    CROSS APPLY (VALUES
        (1, N'Created', N'Status', CAST(NULL AS nvarchar(40)), N'New', i.CreU, i.OpenM, CAST(NULL AS nvarchar(200))),
        (2, N'Assignment', N'AssignmentGroup', NULL, ig.GroupCode, i.CreU, i.OpenM, NULL),
        (3, N'Assignment', N'AssignmentGroup', ig.GroupCode, fg.GroupCode, i.TriU, i.TriM, N'Triaged by the Service Desk and routed to the resolving group.'),
        (4, N'Assignment', N'AssignedTo', NULL, au.UserId, i.AsgU, i.RespM, NULL),
        (5, N'StatusChange', N'Status', N'New', N'In Progress', i.AsgU, i.RespM, NULL),
        (6, N'WorkNote', NULL, NULL, NULL, i.W1U, i.W1M, n1.Text),
        (7, N'Assignment', N'AssignedTo', au.UserId, ru.UserId, i.RslU, i.ReaM, N'Taken over within the group.'),
        (8, N'WorkNote', NULL, NULL, NULL, i.W2U, i.W2M, n2.Text),
        (9, N'Link', N'ParentIncident', NULL, CASE WHEN i.Par > 0 THEN CONCAT(N'INC', RIGHT(CONCAT(N'0000000', 4000 + i.Par), 7)) END, i.LnkU, i.LnkM,
            N'Linked to the parent incident.'),
        (10, N'StatusChange', N'Status', N'In Progress', N'Resolved', i.RslU, i.RslM, rc.Text),
        (11, N'StatusChange', N'Status', N'Resolved', N'Closed', i.CloU, i.CloM, N'Closed after the resolution review.')
    ) v(Ord, ActivityType, FieldName, FromValue, ToValue, U, M, Note)
    WHERE v.U > 0 AND v.M > 0
    UNION ALL
    SELECT x.n, 6, x.k, N'WorkNote', NULL, NULL, NULL, x.U, x.M, x.Note FROM #X x
)
INSERT INTO dbo.IncidentActivities (ActivityId, IncidentNumber, ActivitySeq, ActivityType, FieldName, FromValue, ToValue, UserId, ActivityAt, Note)
SELECT ROW_NUMBER() OVER (ORDER BY a.n, a.M, a.Ord, a.Sub), CONCAT(N'INC', RIGHT(CONCAT(N'0000000', 4000 + a.n), 7)),
       ROW_NUMBER() OVER (PARTITION BY a.n ORDER BY a.M, a.Ord, a.Sub), a.ActivityType, a.FieldName, a.FromValue, a.ToValue, u.UserId, DATEADD(MINUTE, a.M, @T0), a.Note
FROM Act a
JOIN #U u ON u.i = a.U;
CREATE INDEX IX_IncidentActivities_User ON dbo.IncidentActivities (UserId, ActivityAt);
GO
-- The December incidents with dates moved forward so that 31 December 2025 is today in the organization's time zone (Asia/Dubai,
-- "Arabian Standard Time" in SQL Server): rolling-window Monitoring Rules find the planted cases on any day the sample is used.
-- Analyses of 2025 use dbo.Incidents.
CREATE VIEW dbo.vw_IncidentsLive AS
SELECT i.IncidentNumber, i.ServiceCode, i.CiCode, i.Category, i.Subcategory, i.Impact, i.Urgency, i.Priority, i.ReportedByEmployeeNumber, i.DepartmentCode,
       i.SectionCode, i.LocationCode, i.CreatedByUserId, i.Channel, i.AssignmentGroupCode, i.AssignedToUserId, i.Status,
       DATEADD(DAY, s.Shift, i.OpenedAt) AS OpenedAt, DATEADD(DAY, s.Shift, i.ResponseDueAt) AS ResponseDueAt, DATEADD(DAY, s.Shift, i.RespondedAt) AS RespondedAt,
       DATEADD(DAY, s.Shift, i.ResolutionDueAt) AS ResolutionDueAt, DATEADD(DAY, s.Shift, i.ResolvedAt) AS ResolvedAt, i.ResolvedByUserId,
       DATEADD(DAY, s.Shift, i.ClosedAt) AS ClosedAt, i.SlaBreached, i.ResolutionCode, i.IsMajorIncident, i.ParentIncidentNumber, i.ShortDescription
FROM dbo.Incidents i
CROSS JOIN (SELECT DATEDIFF(DAY, CONVERT(date, '20251231'), CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time')) AS Shift) s
WHERE i.OpenedAt >= '2025-12-01';
GO
"""

CHECK_SQL = r"""-- Consistency checks (the script stops here if any rule is broken).
DECLARE @Bad TABLE (CheckName nvarchar(100), Violations int);
WITH Actions AS (
    SELECT i.Priority, i.AssignmentGroupCode AS GroupCode, a.UserId, a.ActivityAt AS At FROM dbo.IncidentActivities a JOIN dbo.Incidents i ON i.IncidentNumber = a.IncidentNumber
    UNION ALL SELECT r.Priority, NULL, r.CreatedByUserId, r.OpenedAt FROM dbo.ServiceRequests r
    UNION ALL SELECT r.Priority, NULL, a.ApproverUserId, a.DecidedAt FROM dbo.RequestApprovals a JOIN dbo.ServiceRequests r ON r.RequestNumber = a.RequestNumber WHERE a.DecidedAt IS NOT NULL
    UNION ALL SELECT r.Priority, t.AssignmentGroupCode, t.AssignedToUserId, t.StartedAt FROM dbo.RequestTasks t JOIN dbo.ServiceRequests r ON r.RequestNumber = t.RequestNumber
    UNION ALL SELECT r.Priority, t.AssignmentGroupCode, t.AssignedToUserId, t.CompletedAt FROM dbo.RequestTasks t JOIN dbo.ServiceRequests r ON r.RequestNumber = t.RequestNumber
)
INSERT INTO @Bad
SELECT N'Staff action without access, presence or on-call duty', COUNT(*)
FROM Actions x
LEFT JOIN dbo.Users u ON u.UserId = x.UserId
LEFT JOIN #Presence p ON p.UserId = x.UserId
CROSS APPLY (SELECT SUBSTRING(p.Days, DATEPART(DAYOFYEAR, x.At), 1) AS Day, DATEPART(HOUR, x.At) * 60 + DATEPART(MINUTE, x.At) AS MinuteOfDay) d
WHERE x.UserId <> N'SYS_MONITOR'
  AND NOT (u.UserId IS NOT NULL AND CAST(x.At AS date) >= u.AccessGrantedDate AND (u.AccessRevokedDate IS NULL OR CAST(x.At AS date) <= u.AccessRevokedDate)
           AND ((d.Day = 'P' AND d.MinuteOfDay BETWEEN p.ShiftStart AND p.ShiftEnd)
                OR (d.Day IN ('P', 'A') AND x.Priority IN (N'P1', N'P2') AND x.GroupCode IN (N'ITD-INF', N'ITD-NET', N'ITD-APP') AND u.SectionCode = x.GroupCode)));
INSERT INTO @Bad SELECT N'Approver is the requester', COUNT(*) FROM dbo.RequestApprovals a JOIN dbo.ServiceRequests r ON r.RequestNumber = a.RequestNumber
    JOIN dbo.Users u ON u.UserId = a.ApproverUserId WHERE u.EmployeeNumber IN (r.RequestedByEmployeeNumber, r.RequestedForEmployeeNumber);
INSERT INTO @Bad SELECT N'Incident timestamps out of order', COUNT(*) FROM dbo.Incidents
    WHERE RespondedAt < OpenedAt OR ResolvedAt < RespondedAt OR ClosedAt < ResolvedAt OR (ResolvedAt IS NOT NULL AND RespondedAt IS NULL) OR (ClosedAt IS NOT NULL AND ResolvedAt IS NULL);
INSERT INTO @Bad SELECT N'Activity outside its incident', COUNT(*) FROM dbo.IncidentActivities a JOIN dbo.Incidents i ON i.IncidentNumber = a.IncidentNumber
    WHERE a.ActivityAt < i.OpenedAt OR a.ActivityAt > ISNULL(i.ClosedAt, '2025-12-31T23:59:00');
INSERT INTO @Bad SELECT N'Activity sequence not in time order', COUNT(*) FROM dbo.IncidentActivities a JOIN dbo.IncidentActivities b
    ON b.IncidentNumber = a.IncidentNumber AND b.ActivitySeq = a.ActivitySeq + 1 WHERE b.ActivityAt < a.ActivityAt;
INSERT INTO @Bad SELECT N'Request timestamps out of order', COUNT(*) FROM dbo.ServiceRequests
    WHERE ApprovedAt < OpenedAt OR FulfilledAt < ISNULL(ApprovedAt, OpenedAt) OR ClosedAt < OpenedAt OR ClosedAt < FulfilledAt;
INSERT INTO @Bad SELECT N'Task outside its request', COUNT(*) FROM dbo.RequestTasks t JOIN dbo.ServiceRequests r ON r.RequestNumber = t.RequestNumber
    WHERE t.StartedAt < ISNULL(r.ApprovedAt, r.OpenedAt) OR t.CompletedAt < t.StartedAt OR t.CompletedAt > ISNULL(r.FulfilledAt, '2025-12-31T23:59:00');
INSERT INTO @Bad SELECT N'Requester or reporter not employed', COUNT(*) FROM (
    SELECT r.RequestedByEmployeeNumber AS E, CAST(r.OpenedAt AS date) AS D FROM dbo.ServiceRequests r
    UNION ALL SELECT i.ReportedByEmployeeNumber, CAST(i.OpenedAt AS date) FROM dbo.Incidents i WHERE i.ReportedByEmployeeNumber IS NOT NULL) x
    JOIN dbo.EmployeeDirectory e ON e.EmployeeNumber = x.E WHERE x.D < e.HireDate OR x.D > ISNULL(e.TerminationDate, '9999-12-31');
INSERT INTO @Bad SELECT N'Priority does not follow impact x urgency', COUNT(*) FROM dbo.Incidents
    WHERE Priority <> CONCAT(N'P', CASE Impact + Urgency WHEN 2 THEN 1 WHEN 3 THEN 2 WHEN 4 THEN 3 ELSE 4 END);
INSERT INTO @Bad SELECT N'Unit codes not in the employee directory', COUNT(*) FROM (
    SELECT DepartmentCode, SectionCode FROM dbo.ServiceRequests UNION SELECT DepartmentCode, SectionCode FROM dbo.Incidents WHERE DepartmentCode IS NOT NULL) x
    WHERE NOT EXISTS (SELECT 1 FROM dbo.EmployeeDirectory e WHERE e.DepartmentCode = x.DepartmentCode)
      AND NOT EXISTS (SELECT 1 FROM dbo.Users u WHERE u.DepartmentCode = x.DepartmentCode);
SELECT CheckName, Violations FROM @Bad;
IF EXISTS (SELECT 1 FROM @Bad WHERE Violations > 0)
    THROW 50001, N'WabiBankITDB consistency check failed (see the list above).', 1;
GO
"""

SUMMARY_SQL = """SELECT N'EmployeeDirectory' AS TableName, COUNT(*) AS RowsLoaded FROM dbo.EmployeeDirectory
UNION ALL SELECT N'Users', COUNT(*) FROM dbo.Users
UNION ALL SELECT N'ConfigurationItems', COUNT(*) FROM dbo.ConfigurationItems
UNION ALL SELECT N'ServiceRequests', COUNT(*) FROM dbo.ServiceRequests
UNION ALL SELECT N'RequestApprovals', COUNT(*) FROM dbo.RequestApprovals
UNION ALL SELECT N'RequestTasks', COUNT(*) FROM dbo.RequestTasks
UNION ALL SELECT N'Incidents', COUNT(*) FROM dbo.Incidents
UNION ALL SELECT N'IncidentActivities', COUNT(*) FROM dbo.IncidentActivities
UNION ALL SELECT N'vw_IncidentsLive', COUNT(*) FROM dbo.vw_IncidentsLive;"""


def build():
    """Returns (sql_text, stats)."""
    g = Generator()
    g.run()
    text = g.sql()
    return text, g.stats


if __name__ == "__main__":
    sql_text, stats = build()
    path = os.path.join(HERE, OUTPUT)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(sql_text)
    print(f"{OUTPUT}: {len(sql_text.encode('utf-8')) // 1024} KB {stats}")
