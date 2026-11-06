"""FlightOperationsDB: Sharjah International (SHJ) flight operations, calendar year 2025."""
from build_common import insert, prologue, epilogue, drop_tables, calendar_table, UAE_HOLIDAYS_2025, rng, TALLY_CTE

DB, OWNER, READER = "FlightOperationsDB", "flightops_owner", "flightops_reader"

# ISO code, English, Arabic, region
COUNTRIES = [
    ("AE", "United Arab Emirates", "الإمارات العربية المتحدة", "GCC"), ("SA", "Saudi Arabia", "المملكة العربية السعودية", "GCC"),
    ("QA", "Qatar", "قطر", "GCC"), ("KW", "Kuwait", "الكويت", "GCC"), ("BH", "Bahrain", "البحرين", "GCC"), ("OM", "Oman", "عُمان", "GCC"),
    ("EG", "Egypt", "مصر", "Middle East & North Africa"), ("JO", "Jordan", "الأردن", "Middle East & North Africa"),
    ("LB", "Lebanon", "لبنان", "Middle East & North Africa"), ("IQ", "Iraq", "العراق", "Middle East & North Africa"),
    ("IR", "Iran", "إيران", "Middle East & North Africa"), ("MA", "Morocco", "المغرب", "Middle East & North Africa"),
    ("IN", "India", "الهند", "South Asia"), ("PK", "Pakistan", "باكستان", "South Asia"), ("BD", "Bangladesh", "بنغلاديش", "South Asia"),
    ("NP", "Nepal", "نيبال", "South Asia"), ("LK", "Sri Lanka", "سريلانكا", "South Asia"), ("AF", "Afghanistan", "أفغانستان", "South Asia"),
    ("MV", "Maldives", "جزر المالديف", "South Asia"), ("TR", "Türkiye", "تركيا", "Europe"), ("RU", "Russia", "روسيا", "Europe"),
    ("CZ", "Czechia", "التشيك", "Europe"), ("AT", "Austria", "النمسا", "Europe"), ("GR", "Greece", "اليونان", "Europe"),
    ("IT", "Italy", "إيطاليا", "Europe"), ("BA", "Bosnia and Herzegovina", "البوسنة والهرسك", "Europe"),
    ("AZ", "Azerbaijan", "أذربيجان", "Caucasus & Central Asia"), ("GE", "Georgia", "جورجيا", "Caucasus & Central Asia"),
    ("AM", "Armenia", "أرمينيا", "Caucasus & Central Asia"), ("KZ", "Kazakhstan", "كازاخستان", "Caucasus & Central Asia"),
    ("UZ", "Uzbekistan", "أوزبكستان", "Caucasus & Central Asia"), ("KG", "Kyrgyzstan", "قيرغيزستان", "Caucasus & Central Asia"),
    ("KE", "Kenya", "كينيا", "Africa"), ("ET", "Ethiopia", "إثيوبيا", "Africa"), ("TH", "Thailand", "تايلاند", "Asia Pacific"),
    ("MY", "Malaysia", "ماليزيا", "Asia Pacific"),
]
CONTINENT = {"GCC": "Asia", "South Asia": "Asia", "Caucasus & Central Asia": "Asia", "Asia Pacific": "Asia", "Europe": "Europe", "Africa": "Africa", "Middle East & North Africa": "Asia"}
CONTINENT.update({})
AFRICAN_MENA = {"EG", "MA"}

# IATA, airport name, city, city Arabic, country, demand weight per direction, block minutes
AIRPORTS = [
    ("SHJ", "Sharjah International", "Sharjah", "الشارقة", "AE", 0, 0),
    ("CAI", "Cairo International", "Cairo", "القاهرة", "EG", 6, 225), ("HBE", "Borg El Arab", "Alexandria", "الإسكندرية", "EG", 2.5, 240),
    ("BOM", "Chhatrapati Shivaji Maharaj International", "Mumbai", "مومباي", "IN", 5, 195), ("DEL", "Indira Gandhi International", "Delhi", "دلهي", "IN", 4, 215),
    ("COK", "Cochin International", "Kochi", "كوتشي", "IN", 4.5, 255), ("CCJ", "Calicut International", "Kozhikode", "كوزيكود", "IN", 3.5, 250),
    ("TRV", "Trivandrum International", "Thiruvananthapuram", "تيروفانانثابورام", "IN", 3, 260), ("MAA", "Chennai International", "Chennai", "تشيناي", "IN", 2.5, 250),
    ("HYD", "Rajiv Gandhi International", "Hyderabad", "حيدر آباد", "IN", 3, 220), ("BLR", "Kempegowda International", "Bengaluru", "بنغالور", "IN", 2.5, 240),
    ("AMD", "Sardar Vallabhbhai Patel International", "Ahmedabad", "أحمد آباد", "IN", 2, 180), ("LKO", "Chaudhary Charan Singh International", "Lucknow", "لكناو", "IN", 2, 225),
    ("JAI", "Jaipur International", "Jaipur", "جايبور", "IN", 1.5, 200), ("CNN", "Kannur International", "Kannur", "كانور", "IN", 1.2, 245),
    ("KHI", "Jinnah International", "Karachi", "كراتشي", "PK", 4, 125), ("LHE", "Allama Iqbal International", "Lahore", "لاهور", "PK", 3.5, 195),
    ("ISB", "Islamabad International", "Islamabad", "إسلام آباد", "PK", 3, 205), ("PEW", "Bacha Khan International", "Peshawar", "بيشاور", "PK", 2.5, 200),
    ("SKT", "Sialkot International", "Sialkot", "سيالكوت", "PK", 2, 195), ("MUX", "Multan International", "Multan", "ملتان", "PK", 1.5, 170),
    ("DAC", "Hazrat Shahjalal International", "Dhaka", "دكا", "BD", 3.5, 300), ("CGP", "Shah Amanat International", "Chittagong", "شيتاغونغ", "BD", 2, 300),
    ("KTM", "Tribhuvan International", "Kathmandu", "كاتماندو", "NP", 3, 255), ("CMB", "Bandaranaike International", "Colombo", "كولومبو", "LK", 2, 255),
    ("JED", "King Abdulaziz International", "Jeddah", "جدة", "SA", 3.5, 185), ("RUH", "King Khalid International", "Riyadh", "الرياض", "SA", 3, 120),
    ("DMM", "King Fahd International", "Dammam", "الدمام", "SA", 2, 75), ("MED", "Prince Mohammad bin Abdulaziz International", "Medina", "المدينة المنورة", "SA", 1.5, 180),
    ("DOH", "Hamad International", "Doha", "الدوحة", "QA", 2.5, 70), ("KWI", "Kuwait International", "Kuwait City", "مدينة الكويت", "KW", 3, 105),
    ("BAH", "Bahrain International", "Manama", "المنامة", "BH", 2, 70), ("MCT", "Muscat International", "Muscat", "مسقط", "OM", 2.5, 65),
    ("SLL", "Salalah", "Salalah", "صلالة", "OM", 1, 110), ("AMM", "Queen Alia International", "Amman", "عمّان", "JO", 2.5, 190),
    ("BEY", "Beirut-Rafic Hariri International", "Beirut", "بيروت", "LB", 2, 230), ("BGW", "Baghdad International", "Baghdad", "بغداد", "IQ", 2, 150),
    ("BSR", "Basra International", "Basra", "البصرة", "IQ", 1.2, 120), ("NJF", "Al Najaf International", "Najaf", "النجف", "IQ", 1.5, 150),
    ("EBL", "Erbil International", "Erbil", "أربيل", "IQ", 1, 165), ("IKA", "Tehran Imam Khomeini International", "Tehran", "طهران", "IR", 1.5, 120),
    ("SYZ", "Shiraz International", "Shiraz", "شيراز", "IR", 1, 70), ("MHD", "Mashhad International", "Mashhad", "مشهد", "IR", 1, 120),
    ("KBL", "Kabul International", "Kabul", "كابول", "AF", 1, 180), ("IST", "Istanbul Airport", "Istanbul", "إسطنبول", "TR", 2, 280),
    ("SAW", "Istanbul Sabiha Gokcen", "Istanbul", "إسطنبول", "TR", 2, 275), ("AYT", "Antalya", "Antalya", "أنطاليا", "TR", 1, 260),
    ("TZX", "Trabzon", "Trabzon", "طرابزون", "TR", 0.6, 210), ("GYD", "Heydar Aliyev International", "Baku", "باكو", "AZ", 1.5, 175),
    ("TBS", "Tbilisi International", "Tbilisi", "تبليسي", "GE", 1.2, 200), ("EVN", "Zvartnots International", "Yerevan", "يريفان", "AM", 1, 195),
    ("ALA", "Almaty International", "Almaty", "ألماتي", "KZ", 1, 270), ("TAS", "Tashkent International", "Tashkent", "طشقند", "UZ", 1, 215),
    ("FRU", "Manas International", "Bishkek", "بيشكيك", "KG", 0.6, 240), ("SVO", "Sheremetyevo", "Moscow", "موسكو", "RU", 1.5, 330),
    ("DME", "Domodedovo", "Moscow", "موسكو", "RU", 1, 330), ("KZN", "Kazan International", "Kazan", "قازان", "RU", 0.6, 290),
    ("UFA", "Ufa International", "Ufa", "أوفا", "RU", 0.5, 270), ("NBO", "Jomo Kenyatta International", "Nairobi", "نيروبي", "KE", 1, 300),
    ("ADD", "Addis Ababa Bole International", "Addis Ababa", "أديس أبابا", "ET", 0.8, 235), ("CMN", "Mohammed V International", "Casablanca", "الدار البيضاء", "MA", 0.8, 450),
    ("MLE", "Velana International", "Male", "ماليه", "MV", 1, 255), ("PRG", "Vaclav Havel Airport Prague", "Prague", "براغ", "CZ", 0.6, 390),
    ("VIE", "Vienna International", "Vienna", "فيينا", "AT", 0.6, 365), ("ATH", "Athens International", "Athens", "أثينا", "GR", 0.8, 300),
    ("BGY", "Milan Bergamo", "Milan", "ميلانو", "IT", 0.7, 395), ("SJJ", "Sarajevo International", "Sarajevo", "سراييفو", "BA", 0.5, 330),
    ("HKT", "Phuket International", "Phuket", "بوكيت", "TH", 0.8, 390), ("KUL", "Kuala Lumpur International", "Kuala Lumpur", "كوالالمبور", "MY", 0.6, 420),
]

# code, name, Arabic name, country, carrier type, share weight, fleet [(type, seats, aircraft count)]
AIRLINES = [
    ("G9", "Air Arabia", "العربية للطيران", "AE", "Low-cost", 62, [("A320", 168, 24), ("A21N", 232, 12), ("A321", 215, 4)]),
    ("6E", "IndiGo", None, "IN", "Low-cost", 7, [("A20N", 186, 4), ("A21N", 232, 2)]),
    ("IX", "Air India Express", None, "IN", "Low-cost", 5, [("B738", 189, 3), ("B38M", 189, 2)]),
    ("PK", "Pakistan International Airlines", "الخطوط الجوية الباكستانية", "PK", "Full-service", 3, [("A320", 180, 2), ("B772", 329, 1)]),
    ("PA", "airblue", None, "PK", "Low-cost", 2, [("A320", 180, 2), ("A321", 220, 1)]),
    ("BG", "Biman Bangladesh Airlines", None, "BD", "Full-service", 2, [("B738", 162, 1), ("B788", 271, 1)]),
    ("BS", "US-Bangla Airlines", None, "BD", "Full-service", 1.5, [("B738", 189, 2)]),
    ("J9", "Jazeera Airways", "طيران الجزيرة", "KW", "Low-cost", 2, [("A20N", 165, 2)]),
    ("OV", "SalamAir", "طيران السلام", "OM", "Low-cost", 1.5, [("A20N", 180, 1), ("A21N", 212, 1)]),
    ("PC", "Pegasus Airlines", None, "TR", "Low-cost", 1.5, [("A21N", 239, 2)]),
    ("MS", "EgyptAir", "مصر للطيران", "EG", "Full-service", 1.5, [("A20N", 142, 1), ("B738", 154, 1)]),
    ("TK", "Turkish Airlines", "الخطوط الجوية التركية", "TR", "Full-service", 1, [("A321", 180, 1)]),
    ("UL", "SriLankan Airlines", None, "LK", "Full-service", 1, [("A20N", 150, 1)]),
    ("IR", "Iran Air", "إيران للطيران", "IR", "Full-service", 1, [("A320", 150, 1)]),
    ("IF", "Fly Baghdad", "فلاي بغداد", "IQ", "Low-cost", 1, [("B738", 189, 1)]),
    ("RJ", "Royal Jordanian", "الملكية الأردنية", "JO", "Full-service", 1, [("A320", 150, 1), ("E195", 120, 1)]),
    ("ME", "Middle East Airlines", "طيران الشرق الأوسط", "LB", "Full-service", 0.8, [("A320", 150, 1)]),
    ("WY", "Oman Air", "الطيران العُماني", "OM", "Full-service", 0.8, [("B38M", 174, 1)]),
    ("H9", "Himalaya Airlines", None, "NP", "Full-service", 0.6, [("A320", 158, 1)]),
    ("ET", "Ethiopian Airlines", "الخطوط الجوية الإثيوبية", "ET", "Full-service", 0.6, [("B738", 154, 1)]),
    ("UT", "UTair", None, "RU", "Full-service", 0.6, [("B738", 189, 1)]),
    ("HY", "Uzbekistan Airways", None, "UZ", "Full-service", 0.6, [("A20N", 150, 1)]),
    ("Y7", "NordStar", None, "RU", "Full-service", 0.4, [("B738", 189, 1)]),
]
REG_PREFIX = {"AE": "A6-", "IN": "VT-", "PK": "AP-", "BD": "S2-", "KW": "9K-", "OM": "A4O-", "TR": "TC-", "EG": "SU-", "LK": "4R-", "IR": "EP-",
              "IQ": "YI-", "JO": "JY-", "LB": "OD-", "NP": "9N-", "ET": "ET-", "RU": "RA-", "UZ": "UK-"}
AIRCRAFT_TYPES = [
    ("A320", "Airbus", "A320ceo", "Narrow-body", 168, 6100), ("A20N", "Airbus", "A320neo", "Narrow-body", 180, 6500),
    ("A321", "Airbus", "A321ceo", "Narrow-body", 215, 5900), ("A21N", "Airbus", "A321neo", "Narrow-body", 232, 7400),
    ("B738", "Boeing", "737-800", "Narrow-body", 189, 5400), ("B38M", "Boeing", "737 MAX 8", "Narrow-body", 178, 6500),
    ("B772", "Boeing", "777-200ER", "Wide-body", 329, 13000), ("B788", "Boeing", "787-8", "Wide-body", 271, 13600),
    ("E195", "Embraer", "E195", "Regional", 120, 4000),
]
SERVE = {
    "6E": {"BOM", "DEL", "COK", "CCJ", "TRV", "MAA", "HYD", "BLR", "AMD", "LKO", "JAI", "CNN"}, "IX": {"COK", "CCJ", "TRV", "MAA", "BLR", "CNN", "HYD"},
    "PK": {"KHI", "LHE", "ISB", "PEW", "SKT", "MUX"}, "PA": {"KHI", "LHE", "ISB", "PEW", "SKT", "MUX"}, "BG": {"DAC", "CGP"}, "BS": {"DAC", "CGP"},
    "J9": {"KWI"}, "OV": {"MCT", "SLL"}, "PC": {"SAW", "AYT"}, "MS": {"CAI", "HBE"}, "TK": {"IST"}, "UL": {"CMB"}, "IR": {"IKA", "SYZ", "MHD"},
    "IF": {"BGW", "BSR", "NJF"}, "RJ": {"AMM"}, "ME": {"BEY"}, "WY": {"MCT"}, "H9": {"KTM"}, "ET": {"ADD"}, "UT": {"SVO", "UFA"}, "HY": {"TAS"}, "Y7": {"KZN"},
}
DELAY_REASONS = [
    ("11", "Late check-in or check-in congestion", "تأخر إجراءات تسجيل الوصول", "Passenger and baggage"),
    ("15", "Boarding: discrepancies and late passengers", "تأخر صعود الركاب", "Passenger and baggage"),
    ("18", "Baggage processing and sorting", "تأخر معالجة الأمتعة", "Passenger and baggage"),
    ("41", "Aircraft technical defect", "عطل فني في الطائرة", "Technical"),
    ("63", "Late crew boarding or crew shortage", "تأخر الطاقم", "Crew"),
    ("71", "Weather at departure station (fog, sandstorm)", "الطقس في مطار المغادرة (ضباب، عاصفة رملية)", "Weather"),
    ("72", "Weather at destination", "الطقس في مطار الوصول", "Weather"),
    ("81", "Air traffic control restriction en-route", "قيود الملاحة الجوية في الطريق", "Air traffic control"),
    ("87", "Airport facilities (stands, gates, equipment)", "مرافق المطار (المواقف والبوابات والمعدات)", "Airport"),
    ("93", "Late arrival of the inbound aircraft (rotation)", "تأخر وصول الطائرة القادمة", "Reactionary"),
    ("96", "Operations control decision", "قرار مركز التحكم بالعمليات", "Airline operations"),
]
HOURS_DEP = [0.6, 0.9, 1.3, 1.4, 1.0, 0.6, 0.9, 1.3, 1.4, 1.2, 1.0, 0.8, 0.7, 0.9, 1.1, 1.2, 1.0, 0.9, 1.0, 1.1, 1.2, 1.3, 1.1, 0.8]


def pick(r, weighted):
    total = sum(w for _, w in weighted)
    x = r.random() * total
    for item, w in weighted:
        x -= w
        if x <= 0:
            return item
    return weighted[-1][0]


def build():
    r = rng(2025)
    country_names = {c[0]: c for c in COUNTRIES}
    # Cities (one id per distinct city/country)
    cities, city_id = [], {}
    for ap in AIRPORTS:
        key = (ap[2], ap[4])
        if key not in city_id:
            city_id[key] = len(cities) + 1
            cities.append((city_id[key], ap[2], ap[3], ap[4]))
    airports = [(ap[0], ap[1], city_id[(ap[2], ap[4])], ap[4], country_names[ap[4]][3], ap[6] if ap[6] else None, 1 if ap[0] == "SHJ" else 0) for ap in AIRPORTS]

    # Aircraft registrations per airline and type
    aircraft, used_regs = [], set()
    for code, _, _, country, _, _, fleet in AIRLINES:
        for ac_type, seats, count in fleet:
            for i in range(count):
                while True:
                    reg = REG_PREFIX[country] + "".join(r.choice("ABCDEFGHJKLMNPRSTUVWXYZ") for _ in range(3 if country != "OM" else 2))
                    if reg not in used_regs:
                        used_regs.add(reg)
                        break
                aircraft.append((reg, code, ac_type, seats, i + 1, r.randint(2012, 2024), "Active"))

    # Flight schedules (the "flights master"): one departure and its return arrival per slot.
    demand = [(ap, ap[5]) for ap in AIRPORTS if ap[5] > 0]
    k = 176 / sum(w for _, w in demand)
    schedules, sid, used_numbers = [], 1, set()
    served_count = {a[0]: (len(demand) if a[0] == "G9" else len(SERVE[a[0]])) for a in AIRLINES}
    # Demand-weighted: a carrier's share is spread over the airports it serves, weighted by their demand.
    demand_of = {ap[0]: w for ap, w in demand}
    served_count = {a[0]: sum(demand_of[c] for c in (demand_of if a[0] == "G9" else SERVE[a[0]])) / 3.0 for a in AIRLINES}

    def flight_number(code):
        while True:
            n = r.randint(101, 699) if code == "G9" else r.randint(101, 989)
            n -= n % 2  # outbound even, inbound odd
            fn = f"{code}{n}"
            if fn not in used_numbers and f"{code}{n + 1}" not in used_numbers:
                used_numbers.update({fn, f"{code}{n + 1}"})
                return fn, f"{code}{n + 1}"

    def add_pair(ap, days, eff_from, eff_to, season):
        nonlocal sid
        eligible = [(a, a[5] / served_count[a[0]]) for a in AIRLINES if a[0] == "G9" or ap[0] in SERVE.get(a[0], set())]
        al = pick(r, eligible)
        ac_type, seats, _ = r.choice(al[6])
        hour = pick(r, list(enumerate(HOURS_DEP)))
        minute = r.choice(range(0, 60, 5))
        dep_min = hour * 60 + minute
        turnaround = r.choice([50, 55, 60, 65, 70, 75])
        arr_min = (dep_min + 2 * ap[6] + turnaround) % 1440
        out_no, in_no = flight_number(al[0])
        terminal = "T1" if al[0] == "G9" else "T2"
        for direction, number, minutes in (("Departure", out_no, dep_min), ("Arrival", in_no, arr_min)):
            schedules.append((sid, number, al[0], direction, ap[0], f"{minutes // 60:02d}:{minutes % 60:02d}:00", days, ac_type, ap[6], terminal, eff_from, eff_to, season))
            sid += 1

    weekly_patterns = {1: "4", 2: "15", 3: "135", 4: "1357", 5: "12457", 6: "123567"}
    for ap, w in demand:
        f = w * k
        daily = int(f)
        for _ in range(daily):
            add_pair(ap, "1234567", "2025-01-01", "2025-12-31", "Year-round")
        weekly = round((f - daily) * 7)
        if weekly >= 7:
            add_pair(ap, "1234567", "2025-01-01", "2025-12-31", "Year-round")
        elif weekly > 0:
            add_pair(ap, weekly_patterns[weekly], "2025-01-01", "2025-12-31", "Year-round")
    # Peak-season extra rotations on the busiest routes (summer holidays, Eid, year-end)
    top = sorted(demand, key=lambda x: -x[1])[:14]
    for ap, _ in top:
        add_pair(ap, "1234567", "2025-07-01", "2025-08-31", "Summer peak")
        add_pair(ap, "1234567", "2025-12-15", "2025-12-31", "Year-end peak")
    for ap, _ in top[:8]:
        add_pair(ap, "1234567", "2025-03-27", "2025-04-03", "Eid al-Fitr")
        add_pair(ap, "1234567", "2025-06-03", "2025-06-10", "Eid al-Adha")

    gates = [(f"A{i}", "T1", "Contact" if i <= 16 else "Remote", i) for i in range(1, 25)] + \
            [(f"B{i}", "T1", "Remote", 24 + i) for i in range(1, 13)] + \
            [(f"C{i}", "T2", "Contact" if i <= 8 else "Remote", i) for i in range(1, 15)]

    sql = [prologue(DB, OWNER, "FlightOps#Owner#2026", READER, "FlightOps#Reader#2026",
                    "FlightOperationsDB — Sharjah International Airport (SHJ) flight operations, 2025 (sample)",
                    "Countries, cities, airports, airlines, aircraft, gates, schedules (flights master) and every 2025 flight movement.")]
    sql.append(drop_tables(["Flights", "FlightSchedules", "Gates", "Terminals", "Aircraft", "AircraftTypes", "DelayReasons", "Airlines", "Airports", "Cities", "Countries", "Calendar"]))
    sql.append("""CREATE TABLE dbo.Countries (
    CountryCode   char(2)       NOT NULL CONSTRAINT PK_Countries PRIMARY KEY,   -- ISO 3166-1 alpha-2
    CountryName   nvarchar(80)  NOT NULL,
    CountryNameAr nvarchar(80)  NOT NULL,
    Region        nvarchar(40)  NOT NULL,
    Continent     nvarchar(20)  NOT NULL
);
CREATE TABLE dbo.Cities (
    CityId      int           NOT NULL CONSTRAINT PK_Cities PRIMARY KEY,
    CityName    nvarchar(80)  NOT NULL,
    CityNameAr  nvarchar(80)  NOT NULL,
    CountryCode char(2)       NOT NULL CONSTRAINT FK_Cities_Countries REFERENCES dbo.Countries (CountryCode)
);
CREATE TABLE dbo.Airports (
    AirportCode       char(3)       NOT NULL CONSTRAINT PK_Airports PRIMARY KEY,   -- IATA code
    AirportName       nvarchar(120) NOT NULL,
    CityId            int           NOT NULL CONSTRAINT FK_Airports_Cities REFERENCES dbo.Cities (CityId),
    CountryCode       char(2)       NOT NULL CONSTRAINT FK_Airports_Countries REFERENCES dbo.Countries (CountryCode),
    Region            nvarchar(40)  NOT NULL,
    BlockMinutesFromSharjah int     NULL,   -- typical scheduled flight time from SHJ
    IsHomeAirport     bit           NOT NULL
);
CREATE TABLE dbo.Airlines (
    AirlineCode   char(2)       NOT NULL CONSTRAINT PK_Airlines PRIMARY KEY,   -- IATA designator
    AirlineName   nvarchar(100) NOT NULL,
    AirlineNameAr nvarchar(100) NULL,
    CountryCode   char(2)       NOT NULL CONSTRAINT FK_Airlines_Countries REFERENCES dbo.Countries (CountryCode),
    CarrierType   nvarchar(20)  NOT NULL,   -- Low-cost | Full-service
    IsBasedAtSharjah bit        NOT NULL
);
CREATE TABLE dbo.AircraftTypes (
    AircraftTypeCode char(4)      NOT NULL CONSTRAINT PK_AircraftTypes PRIMARY KEY,   -- ICAO type designator
    Manufacturer     nvarchar(40) NOT NULL,
    Model            nvarchar(40) NOT NULL,
    BodyType         nvarchar(20) NOT NULL,   -- Narrow-body | Wide-body | Regional
    TypicalSeats     int          NOT NULL,
    RangeKm          int          NOT NULL
);
CREATE TABLE dbo.Aircraft (
    Registration     nvarchar(10) NOT NULL CONSTRAINT PK_Aircraft PRIMARY KEY,
    AirlineCode      char(2)      NOT NULL CONSTRAINT FK_Aircraft_Airlines REFERENCES dbo.Airlines (AirlineCode),
    AircraftTypeCode char(4)      NOT NULL CONSTRAINT FK_Aircraft_AircraftTypes REFERENCES dbo.AircraftTypes (AircraftTypeCode),
    Seats            int          NOT NULL,   -- this airline's cabin configuration
    FleetSequence    int          NOT NULL,
    YearBuilt        smallint     NOT NULL,
    Status           nvarchar(20) NOT NULL
);
CREATE TABLE dbo.Terminals (
    TerminalCode nvarchar(4)  NOT NULL CONSTRAINT PK_Terminals PRIMARY KEY,
    TerminalName nvarchar(60) NOT NULL,
    TerminalNameAr nvarchar(60) NOT NULL
);
CREATE TABLE dbo.Gates (
    GateCode     nvarchar(4)  NOT NULL CONSTRAINT PK_Gates PRIMARY KEY,
    TerminalCode nvarchar(4)  NOT NULL CONSTRAINT FK_Gates_Terminals REFERENCES dbo.Terminals (TerminalCode),
    StandType    nvarchar(10) NOT NULL,   -- Contact (air bridge) | Remote (bus boarding)
    GateSequence int          NOT NULL
);
CREATE TABLE dbo.DelayReasons (
    DelayCode     char(2)       NOT NULL CONSTRAINT PK_DelayReasons PRIMARY KEY,   -- IATA delay code
    Description   nvarchar(120) NOT NULL,
    DescriptionAr nvarchar(120) NOT NULL,
    Category      nvarchar(40)  NOT NULL
);
CREATE TABLE dbo.FlightSchedules (
    ScheduleId       int          NOT NULL CONSTRAINT PK_FlightSchedules PRIMARY KEY,
    FlightNumber     nvarchar(8)  NOT NULL,
    AirlineCode      char(2)      NOT NULL CONSTRAINT FK_FlightSchedules_Airlines REFERENCES dbo.Airlines (AirlineCode),
    Direction        nvarchar(10) NOT NULL,   -- Departure (from SHJ) | Arrival (to SHJ)
    RouteAirportCode char(3)      NOT NULL CONSTRAINT FK_FlightSchedules_Airports REFERENCES dbo.Airports (AirportCode),
    ScheduledTime    time(0)      NOT NULL,   -- local Sharjah time of departure or arrival
    OperatingDays    varchar(7)   NOT NULL,   -- digits of operating weekdays, 1 = Monday ... 7 = Sunday
    AircraftTypeCode char(4)      NOT NULL CONSTRAINT FK_FlightSchedules_AircraftTypes REFERENCES dbo.AircraftTypes (AircraftTypeCode),
    BlockMinutes     int          NOT NULL,
    TerminalCode     nvarchar(4)  NOT NULL CONSTRAINT FK_FlightSchedules_Terminals REFERENCES dbo.Terminals (TerminalCode),
    EffectiveFrom    date         NOT NULL,
    EffectiveTo      date         NOT NULL,
    Season           nvarchar(20) NOT NULL
);
CREATE INDEX IX_FlightSchedules_Airline ON dbo.FlightSchedules (AirlineCode);
GO
""")
    sql.append(calendar_table(UAE_HOLIDAYS_2025))
    sql.append(insert("dbo.Countries", ["CountryCode", "CountryName", "CountryNameAr", "Region", "Continent"],
                      [(c[0], c[1], c[2], c[3], "Africa" if c[0] in AFRICAN_MENA or c[3] == "Africa" else CONTINENT[c[3]]) for c in COUNTRIES]))
    sql.append(insert("dbo.Cities", ["CityId", "CityName", "CityNameAr", "CountryCode"], cities))
    sql.append(insert("dbo.Airports", ["AirportCode", "AirportName", "CityId", "CountryCode", "Region", "BlockMinutesFromSharjah", "IsHomeAirport"], airports))
    sql.append(insert("dbo.Airlines", ["AirlineCode", "AirlineName", "AirlineNameAr", "CountryCode", "CarrierType", "IsBasedAtSharjah"],
                      [(a[0], a[1], a[2], a[3], a[4], 1 if a[0] == "G9" else 0) for a in AIRLINES]))
    sql.append(insert("dbo.AircraftTypes", ["AircraftTypeCode", "Manufacturer", "Model", "BodyType", "TypicalSeats", "RangeKm"], AIRCRAFT_TYPES))
    sql.append(insert("dbo.Aircraft", ["Registration", "AirlineCode", "AircraftTypeCode", "Seats", "FleetSequence", "YearBuilt", "Status"], aircraft))
    sql.append(insert("dbo.Terminals", ["TerminalCode", "TerminalName", "TerminalNameAr"],
                      [("T1", "Terminal 1 (Air Arabia)", "المبنى 1 (العربية للطيران)"), ("T2", "Terminal 2 (other airlines)", "المبنى 2 (شركات الطيران الأخرى)")]))
    sql.append(insert("dbo.Gates", ["GateCode", "TerminalCode", "StandType", "GateSequence"], gates))
    sql.append(insert("dbo.DelayReasons", ["DelayCode", "Description", "DescriptionAr", "Category"], DELAY_REASONS))
    sql.append(insert("dbo.FlightSchedules", ["ScheduleId", "FlightNumber", "AirlineCode", "Direction", "RouteAirportCode", "ScheduledTime", "OperatingDays",
                                              "AircraftTypeCode", "BlockMinutes", "TerminalCode", "EffectiveFrom", "EffectiveTo", "Season"], schedules))
    sql.append("GO\n")
    sql.append(FLIGHTS_SQL)
    sql.append(epilogue(DB, """SELECT N'Flights' AS TableName, COUNT(*) AS RowsLoaded FROM dbo.Flights
UNION ALL SELECT N'FlightSchedules', COUNT(*) FROM dbo.FlightSchedules
UNION ALL SELECT N'Aircraft', COUNT(*) FROM dbo.Aircraft
UNION ALL SELECT N'Airports', COUNT(*) FROM dbo.Airports;"""))
    return "\n".join(sql), len(schedules), len(aircraft)


FLIGHTS_SQL = r"""CREATE TABLE dbo.Flights (
    FlightId             int           NOT NULL CONSTRAINT PK_Flights PRIMARY KEY,
    ScheduleId           int           NOT NULL CONSTRAINT FK_Flights_FlightSchedules REFERENCES dbo.FlightSchedules (ScheduleId),
    FlightDate           date          NOT NULL CONSTRAINT FK_Flights_Calendar REFERENCES dbo.Calendar (CalendarDate),
    FlightNumber         nvarchar(8)   NOT NULL,
    AirlineCode          char(2)       NOT NULL CONSTRAINT FK_Flights_Airlines REFERENCES dbo.Airlines (AirlineCode),
    Direction            nvarchar(10)  NOT NULL,   -- Departure | Arrival (relative to Sharjah)
    RouteAirportCode     char(3)       NOT NULL CONSTRAINT FK_Flights_Airports REFERENCES dbo.Airports (AirportCode),   -- destination of a departure, origin of an arrival
    ScheduledTime        datetime2(0)  NOT NULL,
    ActualTime           datetime2(0)  NULL,       -- NULL when cancelled
    DelayMinutes         int           NULL,       -- NULL when cancelled
    Status               nvarchar(10)  NOT NULL,   -- On Time | Delayed (> 15 min) | Cancelled | Diverted
    DelayCode            char(2)       NULL CONSTRAINT FK_Flights_DelayReasons REFERENCES dbo.DelayReasons (DelayCode),
    AircraftRegistration nvarchar(10)  NOT NULL CONSTRAINT FK_Flights_Aircraft REFERENCES dbo.Aircraft (Registration),
    AircraftTypeCode     char(4)       NOT NULL CONSTRAINT FK_Flights_AircraftTypes REFERENCES dbo.AircraftTypes (AircraftTypeCode),
    Seats                int           NOT NULL,
    Passengers           int           NOT NULL,
    AdultPassengers      int           NOT NULL,
    ChildPassengers      int           NOT NULL,
    InfantPassengers     int           NOT NULL,
    LoadFactorPercent    decimal(5, 1) NOT NULL,
    BaggagePieces        int           NOT NULL,
    CargoKg              int           NOT NULL,
    TerminalCode         nvarchar(4)   NOT NULL CONSTRAINT FK_Flights_Terminals REFERENCES dbo.Terminals (TerminalCode),
    GateCode             nvarchar(4)   NOT NULL CONSTRAINT FK_Flights_Gates REFERENCES dbo.Gates (GateCode),
    BlockMinutes         int           NOT NULL,
    HourOfDay            tinyint       NOT NULL,   -- scheduled hour, local time
    DayOfWeekNumber      tinyint       NOT NULL,   -- 1 = Monday
    DayName              nvarchar(10)  NOT NULL,
    MonthNumber          tinyint       NOT NULL,
    MonthName            nvarchar(10)  NOT NULL,
    QuarterNumber        tinyint       NOT NULL
);
GO
WITH Fleet AS (
    SELECT AirlineCode, AircraftTypeCode, COUNT(*) AS FleetSize FROM dbo.Aircraft GROUP BY AirlineCode, AircraftTypeCode
), GateCount AS (
    SELECT TerminalCode, COUNT(*) AS Gates FROM dbo.Gates GROUP BY TerminalCode
), Ops AS (
    SELECT s.ScheduleId, s.FlightNumber, s.AirlineCode, s.Direction, s.RouteAirportCode, s.AircraftTypeCode, s.BlockMinutes, s.TerminalCode,
           c.CalendarDate, c.DayOfWeekNumber, c.DayName, c.MonthNumber, c.MonthName, c.QuarterNumber, c.IsPublicHoliday,
           DATEADD(MINUTE, DATEDIFF(MINUTE, CAST('00:00' AS time(0)), s.ScheduledTime), CAST(c.CalendarDate AS datetime2(0))) AS ScheduledAt,
           DATEPART(HOUR, s.ScheduledTime) AS HourOfDay,
           CAST(s.ScheduleId AS bigint) * 1000 + c.DayOfYear AS K,
           f.FleetSize, g.Gates
    FROM dbo.FlightSchedules s
    JOIN dbo.Calendar c ON c.CalendarDate BETWEEN s.EffectiveFrom AND s.EffectiveTo
                       AND CHARINDEX(CAST(c.DayOfWeekNumber AS char(1)), s.OperatingDays) > 0
    JOIN Fleet f ON f.AirlineCode = s.AirlineCode AND f.AircraftTypeCode = s.AircraftTypeCode
    JOIN GateCount g ON g.TerminalCode = s.TerminalCode
)
INSERT INTO dbo.Flights
SELECT ROW_NUMBER() OVER (ORDER BY o.ScheduledAt, o.ScheduleId),
       o.ScheduleId, o.CalendarDate, o.FlightNumber, o.AirlineCode, o.Direction, o.RouteAirportCode, o.ScheduledAt,
       CASE WHEN st.Status = N'Cancelled' THEN NULL
            WHEN st.Status = N'Diverted' THEN DATEADD(MINUTE, d.Delay + 95, o.ScheduledAt)
            ELSE DATEADD(MINUTE, d.Delay, o.ScheduledAt) END,
       CASE WHEN st.Status = N'Cancelled' THEN NULL ELSE d.Delay END,
       st.Status,
       CASE WHEN st.Status = N'On Time' THEN NULL
            WHEN st.Status = N'Cancelled' THEN CASE WHEN r.R6 < 0.4 THEN '41' WHEN r.R6 < 0.7 THEN '71' ELSE '96' END
            WHEN st.Status = N'Diverted' THEN '72'
            WHEN o.MonthNumber IN (1, 2, 12) AND o.HourOfDay BETWEEN 4 AND 9 AND r.R6 < 0.5 THEN '71'
            WHEN r.R6 < 0.30 THEN '93' WHEN r.R6 < 0.45 THEN '81' WHEN r.R6 < 0.55 THEN '41' WHEN r.R6 < 0.65 THEN '15'
            WHEN r.R6 < 0.72 THEN '18' WHEN r.R6 < 0.80 THEN '63' WHEN r.R6 < 0.87 THEN '87' WHEN r.R6 < 0.93 THEN '11' ELSE '96' END,
       a.Registration, o.AircraftTypeCode, a.Seats,
       p.Pax, p.Pax - CAST(p.Pax * 0.10 AS int), CAST(p.Pax * 0.08 AS int), CAST(p.Pax * 0.10 AS int) - CAST(p.Pax * 0.08 AS int),
       CAST(CASE WHEN a.Seats = 0 THEN 0 ELSE 100.0 * p.Pax / a.Seats END AS decimal(5, 1)),
       CAST(p.Pax * (0.9 + 0.5 * r.R7) AS int),
       CASE WHEN st.Status = N'Cancelled' THEN 0 WHEN t.BodyType = N'Wide-body' THEN 3000 + CAST(12000 * r.R8 AS int) ELSE 200 + CAST(2300 * r.R8 AS int) END,
       o.TerminalCode, gt.GateCode, o.BlockMinutes, o.HourOfDay, o.DayOfWeekNumber, o.DayName, o.MonthNumber, o.MonthName, o.QuarterNumber
FROM Ops o
CROSS APPLY gen.Rand(o.K) r
-- Delay model: summer heat and congestion, winter morning fog, evening bank congestion; normal-ish noise from three uniforms.
CROSS APPLY (SELECT 9 + CASE WHEN o.MonthNumber IN (7, 8) THEN 6 ELSE 0 END
                      + CASE WHEN o.MonthNumber IN (1, 2, 12) AND o.HourOfDay BETWEEN 4 AND 9 THEN 10 ELSE 0 END
                      + CASE WHEN o.HourOfDay >= 18 THEN 4 ELSE 0 END
                      + 18 * ((r.R1 + r.R2 + r.R3 - 1.5) * 2) AS Raw) m
CROSS APPLY (SELECT CASE WHEN m.Raw < 0 THEN 0 ELSE CAST(m.Raw AS int) END AS D0) d0
CROSS APPLY (SELECT CASE WHEN r.R4 < 0.6 THEN CASE WHEN d0.D0 > 10 THEN d0.D0 - 10 ELSE 0 END ELSE d0.D0 END AS Delay) d
CROSS APPLY (SELECT CASE WHEN r.R5 < 0.011 THEN N'Cancelled' WHEN r.R5 < 0.0125 THEN N'Diverted' WHEN d.Delay > 15 THEN N'Delayed' ELSE N'On Time' END AS Status) st
JOIN dbo.Aircraft a ON a.AirlineCode = o.AirlineCode AND a.AircraftTypeCode = o.AircraftTypeCode AND a.FleetSequence = 1 + CAST(r.R9 * o.FleetSize AS int)
JOIN dbo.AircraftTypes t ON t.AircraftTypeCode = o.AircraftTypeCode
JOIN dbo.Gates gt ON gt.TerminalCode = o.TerminalCode AND gt.GateSequence = 1 + CAST(r.R10 * o.Gates AS int)
-- Load factor: 78 % base, 83 % in Jul, Aug, Dec and on public holidays; clipped to 35-100 %.
CROSS APPLY (SELECT CASE WHEN o.MonthNumber IN (7, 8, 12) OR o.IsPublicHoliday = 1 THEN 0.83 ELSE 0.78 END + (r.R11 + r.R7 - 1.0) * 0.12 AS Lf) lf
CROSS APPLY (SELECT CASE WHEN st.Status = N'Cancelled' THEN 0
                         ELSE CAST(a.Seats * CASE WHEN lf.Lf < 0.35 THEN 0.35 WHEN lf.Lf > 1 THEN 1 ELSE lf.Lf END AS int) END AS Pax) p
OPTION (MAXDOP 4);
GO
CREATE INDEX IX_Flights_FlightDate ON dbo.Flights (FlightDate, Direction);
CREATE INDEX IX_Flights_Airline ON dbo.Flights (AirlineCode, FlightDate);
CREATE INDEX IX_Flights_RouteAirport ON dbo.Flights (RouteAirportCode);
CREATE INDEX IX_Flights_Status ON dbo.Flights (Status) INCLUDE (DelayMinutes, DelayCode);
GO
"""
