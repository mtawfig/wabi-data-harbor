"""Employee-portal sign-ins for HumanResourcesDB (Phase 14 sample): one month of sign-ins of office staff, December 2025.

Normal behaviour: one device per person per day (the work laptop, sometimes the phone), from Sharjah, a few failed
attempts. Planted anomalies: on 31 December three accounts are used from three or four devices in different cities,
and one account uses a laptop and a phone; on 17 December one account uses two devices. The view
vw_EmployeePortalSignInsLive shifts the dates so 31 December is always "today": a daily Monitoring Rule over the last
24 hours finds the planted cases whenever the sample is loaded.
"""
import datetime as dt
from build_common import insert, rng, UAE_HOLIDAYS_2025

CITIES_ABROAD = ["Istanbul", "Lagos", "Kuala Lumpur", "Tbilisi"]


def portal_signins(people):
    r = rng(2512)
    office = sorted((p for p in people if p["EmploymentStatus"] == "Active" and p["ShiftCode"] == "OFF"), key=lambda p: p["EmployeeNumber"])[:90]
    holidays = {h[0] for h in UAE_HOLIDAYS_2025}
    rows = []

    def add(p, device, device_type, channel, ip, city, at, ok=True):
        rows.append((len(rows) + 1, f"GAS\\{p['EmployeeNumber']}", p["EmployeeNumber"], device, device_type, channel, ip, city, at.strftime("%Y-%m-%d %H:%M:%S"), ok))

    for day in (dt.date(2025, 12, 1) + dt.timedelta(days=i) for i in range(31)):
        if day.weekday() >= 5 or day.isoformat() in holidays:
            continue
        for i, p in enumerate(office):
            if r.random() > 0.92:
                continue
            # The last days before "today" stay on the work laptop, so only the planted accounts show several devices in any 24-hour window.
            laptop = r.random() < 0.82 or day >= dt.date(2025, 12, 29)
            device = f"LT-{p['EmployeeNumber']}" if laptop else f"MB-{p['EmployeeNumber']}"
            ip = f"10.20.{i % 12}.{20 + i}" if laptop else f"94.200.{r.randint(1, 254)}.{r.randint(1, 254)}"
            city = "Sharjah" if laptop or r.random() < 0.8 else "Dubai"
            first = dt.datetime.combine(day, dt.time(7, 30)) + dt.timedelta(minutes=r.randint(0, 120))
            if r.random() < 0.03:
                add(p, device, "Laptop" if laptop else "Mobile", "Web portal" if laptop else "Mobile app", ip, city, first - dt.timedelta(minutes=1), ok=False)
            add(p, device, "Laptop" if laptop else "Mobile", "Web portal" if laptop else "Mobile app", ip, city, first)
            if r.random() < 0.35:
                add(p, device, "Laptop" if laptop else "Mobile", "Web portal" if laptop else "Mobile app", ip, city, first + dt.timedelta(hours=5, minutes=r.randint(0, 50)))

    # Planted anomalies (deterministic): accounts used from several devices on the same day.
    def planted(p, day, devices):
        for n, (device, device_type, city, hour, minute) in enumerate(devices):
            ip = f"10.20.0.{50 + n}" if device_type == "Laptop" else f"{185 + n}.{40 + n}.{r.randint(1, 254)}.{r.randint(1, 254)}"
            add(p, device, device_type, "Mobile app" if device_type == "Mobile" else "Web portal", ip, city, dt.datetime(2025, 12, day, hour, minute))

    a, b, c, d, e = office[5], office[17], office[42], office[60], office[33]
    planted(a, 31, [(f"LT-{a['EmployeeNumber']}", "Laptop", "Sharjah", 8, 5), ("UNK-7F3A21", "Laptop", "Istanbul", 2, 14), ("UNK-7F3A22", "Mobile", "Istanbul", 2, 40)])
    planted(b, 31, [(f"LT-{b['EmployeeNumber']}", "Laptop", "Sharjah", 7, 55), ("UNK-19C0B4", "Laptop", "Lagos", 1, 12), ("UNK-19C0B5", "Tablet", "Lagos", 1, 30), ("UNK-19C0B6", "Mobile", "Kuala Lumpur", 4, 2)])
    planted(c, 31, [(f"LT-{c['EmployeeNumber']}", "Laptop", "Sharjah", 8, 20), ("UNK-A04E9D", "Laptop", "Tbilisi", 3, 3), (f"MB-{c['EmployeeNumber']}", "Mobile", "Sharjah", 12, 45)])
    planted(d, 31, [(f"LT-{d['EmployeeNumber']}", "Laptop", "Sharjah", 8, 0), (f"MB-{d['EmployeeNumber']}", "Mobile", "Dubai", 19, 30)])
    planted(e, 17, [(f"LT-{e['EmployeeNumber']}", "Laptop", "Sharjah", 8, 10), ("UNK-5B77E0", "Laptop", "Dubai", 22, 45)])
    rows.sort(key=lambda x: (x[8], x[2]))
    rows = [(i + 1,) + row[1:] for i, row in enumerate(rows)]

    columns = ["SignInId", "AccountName", "EmployeeNumber", "DeviceId", "DeviceType", "Channel", "IpAddress", "City", "LoginAt", "Succeeded"]
    sql = """-- Employee-portal sign-ins (Phase 14 sample for Monitoring Rules): office staff, December 2025, one row per sign-in attempt.
IF OBJECT_ID(N'dbo.vw_EmployeePortalSignInsLive', N'V') IS NOT NULL DROP VIEW dbo.vw_EmployeePortalSignInsLive;
IF OBJECT_ID(N'dbo.EmployeePortalSignIns', N'U') IS NOT NULL DROP TABLE dbo.EmployeePortalSignIns;
GO
CREATE TABLE dbo.EmployeePortalSignIns (
    SignInId       int           NOT NULL CONSTRAINT PK_EmployeePortalSignIns PRIMARY KEY,
    AccountName    nvarchar(40)  NOT NULL,   -- portal account (GAS\\<employee number>)
    EmployeeNumber nvarchar(10)  NOT NULL,
    DeviceId       nvarchar(40)  NOT NULL,   -- LT-… work laptop, MB-… registered phone, UNK-… unregistered device
    DeviceType     nvarchar(20)  NOT NULL,   -- Laptop | Mobile | Tablet
    Channel        nvarchar(20)  NOT NULL,   -- Web portal | Mobile app
    IpAddress      varchar(45)   NOT NULL,
    City           nvarchar(60)  NOT NULL,
    LoginAt        datetime2(0)  NOT NULL,   -- local time (Asia/Dubai)
    Succeeded      bit           NOT NULL
);
""" + insert("dbo.EmployeePortalSignIns", columns, rows) + """
CREATE INDEX IX_EmployeePortalSignIns_LoginAt ON dbo.EmployeePortalSignIns (LoginAt);
CREATE INDEX IX_EmployeePortalSignIns_Account ON dbo.EmployeePortalSignIns (AccountName, LoginAt);
GO
-- The same sign-ins with dates moved forward so that 31 December 2025 is today in the organization's time zone (Asia/Dubai,
-- "Arabian Standard Time" in SQL Server): lets a "last 24 hours" Monitoring Rule find the planted cases on any day and at any
-- hour the sample is used, whatever the server's own time zone. Analyses of December 2025 use the table itself.
CREATE VIEW dbo.vw_EmployeePortalSignInsLive AS
SELECT SignInId, AccountName, EmployeeNumber, DeviceId, DeviceType, Channel, IpAddress, City,
       DATEADD(day, DATEDIFF(day, CONVERT(date, '20251231'), CONVERT(date, SYSDATETIMEOFFSET() AT TIME ZONE 'Arabian Standard Time')), LoginAt) AS LoginAt,
       Succeeded
FROM dbo.EmployeePortalSignIns;
GO
"""
    return sql, len(rows)
