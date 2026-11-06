"""ProcurementStoresDB: suppliers, items, purchase requisitions and orders, goods receipts, invoices and stores of Gulf Aviation Services, 2025."""
from build_common import insert, prologue, epilogue, drop_tables, calendar_table, UAE_HOLIDAYS_2025, rng
from build_people import DEPARTMENTS, staff

DB, OWNER, READER = "ProcurementStoresDB", "procurement_owner", "procurement_reader"

# CategoryCode, English, Arabic, default warehouse, lead time days, typical order quantity
CATEGORIES = [
    ("AIRPART", "Aircraft spares and rotables", "قطع غيار الطائرات", "LINE", 21, 4), ("AIRCONS", "Aircraft consumables", "مستهلكات الطائرات", "LINE", 10, 24),
    ("GSE", "Ground support equipment parts", "قطع معدات الدعم الأرضي", "GSE", 14, 6), ("FUELLUB", "Fuels, oils and lubricants", "الوقود والزيوت ومواد التشحيم", "MAIN", 5, 40),
    ("UNIFORM", "Uniforms and workwear", "الزي الرسمي وملابس العمل", "UNI", 30, 40), ("CATER", "Catering and pantry supplies", "مستلزمات الضيافة", "MAIN", 3, 30),
    ("ITEQ", "IT equipment and peripherals", "معدات تقنية المعلومات", "ITS", 12, 5), ("OFFICE", "Office supplies", "اللوازم المكتبية", "MAIN", 4, 30),
    ("CLEAN", "Cleaning and hygiene", "مواد التنظيف والنظافة", "MAIN", 4, 30), ("SAFETY", "Safety and PPE", "معدات السلامة والوقاية", "MAIN", 8, 20),
    ("FACIL", "Facilities and electrical", "المرافق والكهرباء", "MAIN", 7, 10),
]
# Category -> [(item, uom, standard cost AED)], variants are added below
ITEMS = {
    "AIRPART": [("Main wheel assembly A320", "EA", 18500), ("Nose wheel assembly A320", "EA", 9200), ("Brake assembly A320", "EA", 41000),
                ("Brake assembly B737", "EA", 39000), ("Main landing gear tyre 46x17R20", "EA", 6800), ("Nose landing gear tyre 30x8.8R15", "EA", 2900),
                ("Starter valve A320", "EA", 23500), ("Pitot probe", "EA", 14800), ("Landing light assembly", "EA", 5200), ("Navigation light assembly", "EA", 3100),
                ("Oxygen mask assembly (passenger)", "EA", 640), ("Cabin pressure controller", "EA", 61000), ("Windshield wiper motor", "EA", 12500),
                ("Hydraulic pump (engine driven)", "EA", 88000), ("Fuel quantity probe", "EA", 7600), ("Lavatory flush motor", "EA", 4300),
                ("Galley coffee maker", "EA", 9800), ("Passenger seat belt assembly", "EA", 410), ("Cabin reading light unit", "EA", 760), ("Smoke detector (cargo)", "EA", 8900)],
    "AIRCONS": [("Engine oil turbine type II (1 qt)", "QT", 64), ("Hydraulic fluid type IV (1 gal)", "GAL", 310), ("Oil filter element", "EA", 520),
                ("Hydraulic filter element", "EA", 690), ("Fuel filter element", "EA", 840), ("Cabin air recirculation filter", "EA", 1450), ("O-ring kit (assorted)", "KIT", 380),
                ("Sealant cartridge (fuel tank)", "EA", 210), ("Safety wire 0.032 (roll)", "ROLL", 95), ("Rivet assortment kit", "KIT", 260), ("Cleaning solvent (5 L)", "CAN", 120),
                ("Lockwire pliers", "EA", 180), ("Aircraft grease (14 oz)", "EA", 75), ("Speed tape (roll)", "ROLL", 140)],
    "GSE": [("Tow tractor tyre", "EA", 1900), ("Belt loader conveyor belt", "EA", 4200), ("GPU output cable 28V DC", "EA", 3600), ("Baggage cart wheel", "EA", 240),
            ("Heavy-duty battery 12V", "EA", 820), ("Hydraulic hose assembly", "EA", 360), ("GSE brake pad set", "SET", 290), ("GSE air filter", "EA", 140),
            ("GSE diesel filter", "EA", 115), ("Headlight assembly (GSE)", "EA", 310), ("Tow bar shear pin", "EA", 85), ("Passenger stairs handrail kit", "KIT", 980),
            ("Hi-loader roller set", "SET", 2100), ("Pushback tug alternator", "EA", 2600)],
    "FUELLUB": [("Engine oil 15W-40 (20 L)", "PAIL", 260), ("Hydraulic oil ISO 46 (20 L)", "PAIL", 230), ("Multipurpose grease (18 kg)", "PAIL", 340),
                ("AdBlue (10 L)", "CAN", 55), ("Coolant (20 L)", "PAIL", 180), ("Gear oil 80W-90 (20 L)", "PAIL", 270)],
    "UNIFORM": [("Ramp coverall", "PCS", 165), ("High-visibility vest", "PCS", 28), ("Safety shoes", "PAIR", 210), ("Customer service blazer", "PCS", 420),
                ("Customer service shirt", "PCS", 95), ("Uniform trousers", "PCS", 120), ("Uniform cap", "PCS", 35), ("Rain jacket", "PCS", 150),
                ("Security officer shirt", "PCS", 90), ("Technician polo shirt", "PCS", 70)],
    "CATER": [("Bottled water 500 ml (case of 24)", "CASE", 18), ("Coffee beans (1 kg)", "KG", 85), ("Tea bags (box of 100)", "BOX", 26), ("Disposable cups (carton)", "CTN", 95),
              ("Sugar sticks (box of 1000)", "BOX", 70), ("Milk portions (box of 120)", "BOX", 60), ("Dates (5 kg)", "BOX", 140)],
    "ITEQ": [("Laptop 14-inch", "EA", 4200), ("Desktop computer", "EA", 3300), ("Monitor 24-inch", "EA", 820), ("Barcode scanner", "EA", 690), ("Boarding pass printer", "EA", 5600),
             ("Bag tag printer", "EA", 5900), ("Thermal paper rolls (box of 50)", "BOX", 260), ("Network switch 24-port", "EA", 3900), ("Wi-Fi access point", "EA", 1450),
             ("Toner cartridge", "EA", 340), ("Headset (contact centre)", "EA", 260), ("Rugged tablet", "EA", 5200), ("Two-way radio handset", "EA", 1750),
             ("Two-way radio battery", "EA", 210), ("UPS 1500 VA", "EA", 1150)],
    "OFFICE": [("A4 copy paper (box of 5 reams)", "BOX", 85), ("Ballpoint pens (box of 50)", "BOX", 45), ("Box files", "PCS", 12), ("Whiteboard markers (pack of 4)", "PACK", 18),
               ("Envelopes A4 (box of 250)", "BOX", 75), ("Notebooks A5 (pack of 10)", "PACK", 55), ("Stapler", "PCS", 35), ("Printer labels (box)", "BOX", 120)],
    "CLEAN": [("Floor cleaner (5 L)", "CAN", 38), ("Disinfectant (5 L)", "CAN", 52), ("Hand soap (5 L)", "CAN", 34), ("Garbage bags (roll of 50)", "ROLL", 22),
              ("Microfibre cloths (pack of 10)", "PACK", 30), ("Aircraft cabin cleaning kit", "KIT", 160), ("Paper towels (case)", "CASE", 95), ("Glass cleaner (5 L)", "CAN", 40)],
    "SAFETY": [("Ear defenders", "EA", 65), ("Safety gloves", "PAIR", 18), ("Safety goggles", "EA", 22), ("Fire extinguisher 6 kg", "EA", 240), ("First aid kit", "KIT", 180),
               ("Spill kit", "KIT", 420), ("Traffic cone", "EA", 45), ("Wheel chocks (pair)", "PAIR", 310), ("Hard hat", "EA", 38), ("Reflective tape (roll)", "ROLL", 55)],
    "FACIL": [("LED tube light", "EA", 32), ("Air-conditioning filter", "EA", 85), ("Door closer", "EA", 140), ("Padlock", "EA", 45), ("Electrical cable 2.5 mm (100 m)", "ROLL", 260),
              ("Circuit breaker 32 A", "EA", 75), ("Extension socket", "EA", 40), ("Water pump seal kit", "KIT", 320)],
}
VARIANTS = {"UNIFORM": ["S", "M", "L", "XL"], "AIRPART": ["", "(exchange)"]}
UOM_NAMES = {"EA": "Each", "QT": "Quart", "GAL": "Gallon", "KIT": "Kit", "ROLL": "Roll", "CAN": "Can", "SET": "Set", "PAIL": "Pail", "PCS": "Piece", "PAIR": "Pair",
             "CASE": "Case", "KG": "Kilogram", "BOX": "Box", "CTN": "Carton", "PACK": "Pack"}
WAREHOUSES = [("MAIN", "Main Store", "المخزن الرئيسي", "Head Office compound", "Main"), ("LINE", "Line Maintenance Store", "مخزن الصيانة الخطية", "Hangar 2", "Technical"),
              ("GSE", "Ground Equipment Store", "مخزن المعدات الأرضية", "GSE workshop", "Technical"), ("UNI", "Uniform Store", "مخزن الزي الرسمي", "Terminal 1 basement", "Satellite"),
              ("ITS", "IT Store", "مخزن تقنية المعلومات", "Head Office level 1", "Satellite")]
SUPPLIER_PREFIX = ["Falcon", "Desert Line", "Gulf Star", "Al Noor", "Horizon", "Skyway", "Pearl Coast", "Oasis", "Crescent", "Summit", "Atlas", "Al Reef", "Blue Sky",
                   "Jebel", "Marina", "Sahara", "Dune", "Palm", "Saqr", "Bayt"]
SUPPLIER_SUFFIX = {"AIRPART": "Aero Parts Trading LLC", "AIRCONS": "Aviation Supplies FZE", "GSE": "Ground Equipment Co.", "FUELLUB": "Lubricants Trading LLC",
                   "UNIFORM": "Uniform Factory LLC", "CATER": "Food Supplies LLC", "ITEQ": "Technology Solutions LLC", "OFFICE": "Stationery Trading",
                   "CLEAN": "Hygiene Products LLC", "SAFETY": "Safety Equipment Trading", "FACIL": "Facility Supplies LLC"}
SUPPLIER_COUNTRIES = {"AIRPART": ["AE", "AE", "GB", "FR", "US", "SG"], "AIRCONS": ["AE", "AE", "GB", "US"], "GSE": ["AE", "AE", "DE", "TR"], "ITEQ": ["AE", "AE", "AE", "SG"]}
COUNTRY_NAMES = {"AE": "United Arab Emirates", "GB": "United Kingdom", "FR": "France", "US": "United States", "SG": "Singapore", "DE": "Germany", "TR": "Türkiye"}
# Department -> category weights for requisitions and store issues
DEPT_CATEGORIES = {
    "MNT": [("AIRPART", 5), ("AIRCONS", 5), ("SAFETY", 1), ("CLEAN", 1)], "GND": [("GSE", 6), ("FUELLUB", 2), ("UNIFORM", 2), ("SAFETY", 2)],
    "CS": [("UNIFORM", 3), ("OFFICE", 2), ("ITEQ", 2), ("CATER", 2)], "OPS": [("OFFICE", 3), ("ITEQ", 3), ("CATER", 2)],
    "SEC": [("SAFETY", 3), ("UNIFORM", 2), ("ITEQ", 2)], "PRC": [("OFFICE", 2), ("CLEAN", 3), ("FACIL", 3)], "FIN": [("OFFICE", 5), ("ITEQ", 2)],
    "HR": [("OFFICE", 4), ("UNIFORM", 2), ("CATER", 2)], "IT": [("ITEQ", 8), ("OFFICE", 1)], "EXEC": [("OFFICE", 3), ("CATER", 3), ("ITEQ", 2)],
}
DEPT_WEIGHT = {"MNT": 25, "GND": 22, "CS": 10, "OPS": 8, "SEC": 7, "PRC": 8, "FIN": 4, "HR": 5, "IT": 8, "EXEC": 3}


def build():
    r = rng(909)
    people, heads = staff()
    active = [p for p in people if p["EmploymentStatus"] == "Active"]
    buyers = [p["EmployeeNumber"] for p in active if p["PositionCode"] in ("BUY", "SBY")]
    storekeepers = [p["EmployeeNumber"] for p in active if p["PositionCode"] in ("STK", "STS")]

    suppliers, sup_by_cat, n = [], {}, 1
    for cat in CATEGORIES:
        count = 6 if cat[0] in ("AIRPART", "AIRCONS", "ITEQ", "GSE") else 4
        prefixes = r.sample(SUPPLIER_PREFIX, count)
        for pre in prefixes:
            code = f"SUP-{n:03d}"
            country = r.choice(SUPPLIER_COUNTRIES.get(cat[0], ["AE"]))
            rating = r.choice(["A", "A", "B", "B", "B", "C"])
            late_factor = {"A": 2, "B": 6, "C": 14}[rating]
            suppliers.append((code, f"{pre} {SUPPLIER_SUFFIX[cat[0]]}", cat[0], country, COUNTRY_NAMES[country],
                              "Sharjah" if country == "AE" and r.random() < 0.5 else ("Dubai" if country == "AE" else None),
                              r.choice([30, 30, 45, 60]), rating, f"1003{r.randint(10000000000, 99999999999)}" if country == "AE" else None,
                              f"sales@{pre.lower().replace(' ', '')}-{cat[0].lower()}.example", 1, late_factor))
            sup_by_cat.setdefault(cat[0], []).append(code)
            n += 1

    items, n = [], 1
    for cat in CATEGORIES:
        for name, uom, cost in ITEMS[cat[0]]:
            for v in VARIANTS.get(cat[0], [""]):
                if cat[0] == "AIRPART" and v == "(exchange)" and r.random() < 0.6:
                    continue
                label = f"{name} ({v})" if v and cat[0] == "UNIFORM" else (f"{name} {v}" if v else name)
                c = round(cost * (0.55 if v == "(exchange)" else 1) * r.uniform(0.95, 1.05), 2)
                is_rotable = cat[0] == "AIRPART"
                items.append((f"ITM-{n:04d}", label, cat[0], uom, c, max(1, int(cat[5] * r.uniform(0.3, 0.7))), max(2, int(cat[5] * r.uniform(1.0, 2.0))),
                              cat[4] + r.randint(-2, 5), cat[3], r.choice(sup_by_cat[cat[0]]), 1, 1 if is_rotable else 0))
                n += 1

    cost_centres = [(d[3], f"{d[1]} cost centre", d[0]) for d in DEPARTMENTS]
    staff_rows = [(p["EmployeeNumber"], p["DepartmentCode"], p["SectionCode"]) for p in active]

    sql = [prologue(DB, OWNER, "Procure#Owner#2026", READER, "Procure#Reader#2026",
                    "ProcurementStoresDB — purchasing, receiving, invoices and stores of Gulf Aviation Services, 2025 (sample)",
                    "Suppliers, items, warehouses, requisitions, purchase orders, goods receipts, supplier invoices, store issues, stock movements and balances.")]
    sql.append(drop_tables(["StockBalances", "StockMovements", "StoreIssueLines", "StoreIssues", "SupplierInvoices", "GoodsReceiptLines", "GoodsReceipts",
                            "PurchaseOrderLines", "PurchaseOrders", "PurchaseRequisitionLines", "PurchaseRequisitions", "Items", "Suppliers", "Warehouses",
                            "UnitsOfMeasure", "ItemCategories", "CostCenters", "Calendar"]))
    sql.append("""CREATE TABLE dbo.CostCenters (
    CostCenterCode nvarchar(10) NOT NULL CONSTRAINT PK_CostCenters PRIMARY KEY,
    CostCenterName nvarchar(80) NOT NULL,
    DepartmentCode nvarchar(10) NOT NULL   -- same codes as HumanResourcesDB.Departments
);
CREATE TABLE dbo.ItemCategories (
    CategoryCode        nvarchar(10) NOT NULL CONSTRAINT PK_ItemCategories PRIMARY KEY,
    CategoryName        nvarchar(60) NOT NULL,
    CategoryNameAr      nvarchar(60) NOT NULL,
    DefaultWarehouseCode nvarchar(6) NOT NULL,
    TypicalLeadTimeDays int          NOT NULL
);
CREATE TABLE dbo.UnitsOfMeasure (
    UomCode nvarchar(6)  NOT NULL CONSTRAINT PK_UnitsOfMeasure PRIMARY KEY,
    UomName nvarchar(20) NOT NULL
);
CREATE TABLE dbo.Warehouses (
    WarehouseCode nvarchar(6)  NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
    WarehouseName nvarchar(60) NOT NULL,
    WarehouseNameAr nvarchar(60) NOT NULL,
    Location      nvarchar(60) NOT NULL,
    WarehouseType nvarchar(12) NOT NULL   -- Main | Technical | Satellite
);
CREATE TABLE dbo.Suppliers (
    SupplierCode           nvarchar(10)  NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY,
    SupplierName           nvarchar(100) NOT NULL,
    CategoryCode           nvarchar(10)  NOT NULL CONSTRAINT FK_Suppliers_ItemCategories REFERENCES dbo.ItemCategories (CategoryCode),
    CountryCode            char(2)       NOT NULL,
    CountryName            nvarchar(60)  NOT NULL,
    City                   nvarchar(40)  NULL,
    PaymentTermsDays       int           NOT NULL,
    Rating                 char(1)       NOT NULL,   -- A (preferred) | B | C (under review)
    TaxRegistrationNumber  nvarchar(20)  NULL,       -- UAE TRN
    ContactEmail           nvarchar(120) NOT NULL,
    IsActive               bit           NOT NULL
);
CREATE TABLE dbo.Items (
    ItemCode              nvarchar(10)  NOT NULL CONSTRAINT PK_Items PRIMARY KEY,
    ItemDescription       nvarchar(120) NOT NULL,
    CategoryCode          nvarchar(10)  NOT NULL CONSTRAINT FK_Items_ItemCategories REFERENCES dbo.ItemCategories (CategoryCode),
    UomCode               nvarchar(6)   NOT NULL CONSTRAINT FK_Items_UnitsOfMeasure REFERENCES dbo.UnitsOfMeasure (UomCode),
    StandardCost          decimal(12,2) NOT NULL,   -- AED per unit
    ReorderLevel          int           NOT NULL,
    ReorderQuantity       int           NOT NULL,
    LeadTimeDays          int           NOT NULL,
    DefaultWarehouseCode  nvarchar(6)   NOT NULL CONSTRAINT FK_Items_Warehouses REFERENCES dbo.Warehouses (WarehouseCode),
    PreferredSupplierCode nvarchar(10)  NOT NULL CONSTRAINT FK_Items_Suppliers REFERENCES dbo.Suppliers (SupplierCode),
    IsStockItem           bit           NOT NULL,
    IsRotable             bit           NOT NULL    -- repairable aircraft component
);
GO
""")
    sql.append(calendar_table(UAE_HOLIDAYS_2025))
    sql.append(insert("dbo.CostCenters", ["CostCenterCode", "CostCenterName", "DepartmentCode"], cost_centres))
    sql.append(insert("dbo.ItemCategories", ["CategoryCode", "CategoryName", "CategoryNameAr", "DefaultWarehouseCode", "TypicalLeadTimeDays"], [c[:5] for c in CATEGORIES]))
    sql.append(insert("dbo.UnitsOfMeasure", ["UomCode", "UomName"], sorted(UOM_NAMES.items())))
    sql.append(insert("dbo.Warehouses", ["WarehouseCode", "WarehouseName", "WarehouseNameAr", "Location", "WarehouseType"], WAREHOUSES))
    sql.append(insert("dbo.Suppliers", ["SupplierCode", "SupplierName", "CategoryCode", "CountryCode", "CountryName", "City", "PaymentTermsDays", "Rating",
                                        "TaxRegistrationNumber", "ContactEmail", "IsActive"], [s[:11] for s in suppliers]))
    sql.append(insert("dbo.Items", ["ItemCode", "ItemDescription", "CategoryCode", "UomCode", "StandardCost", "ReorderLevel", "ReorderQuantity", "LeadTimeDays",
                                    "DefaultWarehouseCode", "PreferredSupplierCode", "IsStockItem", "IsRotable"], items))
    sql.append("GO\n")
    # Generation-only helpers (temporary tables, dropped at the end): staff, weights, supplier lateness.
    weights = []
    for dept, cats in DEPT_CATEGORIES.items():
        total = sum(w for _, w in cats)
        acc = 0.0
        for cat, w in cats:
            weights.append((dept, cat, acc / total, (acc + w) / total))
            acc += w
    dept_ranges, acc, total = [], 0.0, sum(DEPT_WEIGHT.values())
    for dept, w in DEPT_WEIGHT.items():
        dept_ranges.append((dept, acc / total, (acc + w) / total))
        acc += w
    sql.append("CREATE TABLE #Staff (Seq int IDENTITY PRIMARY KEY, EmployeeNumber nvarchar(10), DepartmentCode nvarchar(10), SectionCode nvarchar(10));")
    sql.append(insert("#Staff", ["EmployeeNumber", "DepartmentCode", "SectionCode"], staff_rows))
    sql.append("CREATE TABLE #DeptRange (DepartmentCode nvarchar(10) PRIMARY KEY, Lo float, Hi float);")
    sql.append(insert("#DeptRange", ["DepartmentCode", "Lo", "Hi"], dept_ranges))
    sql.append("CREATE TABLE #CatRange (DepartmentCode nvarchar(10), CategoryCode nvarchar(10), Lo float, Hi float);")
    sql.append(insert("#CatRange", ["DepartmentCode", "CategoryCode", "Lo", "Hi"], weights))
    sql.append("CREATE TABLE #SupplierLateness (SupplierCode nvarchar(10) PRIMARY KEY, LateFactor int);")
    sql.append(insert("#SupplierLateness", ["SupplierCode", "LateFactor"], [(s[0], s[11]) for s in suppliers]))
    sql.append(f"CREATE TABLE #People (Role nvarchar(10), Seq int IDENTITY, EmployeeNumber nvarchar(10));")
    sql.append(insert("#People", ["Role", "EmployeeNumber"], [("BUYER", b) for b in buyers] + [("STORE", s) for s in storekeepers]))
    sql.append(GENERATED_SQL)
    sql.append("DROP TABLE #Staff; DROP TABLE #DeptRange; DROP TABLE #CatRange; DROP TABLE #SupplierLateness; DROP TABLE #People;\n" + epilogue(DB, """SELECT N'PurchaseRequisitions' AS TableName, COUNT(*) AS RowsLoaded FROM dbo.PurchaseRequisitions
UNION ALL SELECT N'PurchaseOrders', COUNT(*) FROM dbo.PurchaseOrders
UNION ALL SELECT N'PurchaseOrderLines', COUNT(*) FROM dbo.PurchaseOrderLines
UNION ALL SELECT N'GoodsReceipts', COUNT(*) FROM dbo.GoodsReceipts
UNION ALL SELECT N'SupplierInvoices', COUNT(*) FROM dbo.SupplierInvoices
UNION ALL SELECT N'StoreIssues', COUNT(*) FROM dbo.StoreIssues
UNION ALL SELECT N'StockMovements', COUNT(*) FROM dbo.StockMovements
UNION ALL SELECT N'StockBalances', COUNT(*) FROM dbo.StockBalances;"""))
    # The temp tables live in the session; the GO batches above keep them, so the script must run in one connection (SSMS/sqlcmd do).
    return "\n".join(sql), len(suppliers), len(items)


GENERATED_SQL = r"""GO
-- Item numbering inside each category, used by the generators to pick items.
SELECT ItemCode, CategoryCode, StandardCost, DefaultWarehouseCode, ROW_NUMBER() OVER (PARTITION BY CategoryCode ORDER BY ItemCode) AS Seq,
       COUNT(*) OVER (PARTITION BY CategoryCode) AS CatCount,
       CAST(CASE WHEN UomCode IN (N'EA', N'SET', N'KIT') AND StandardCost > 1000 THEN 2 WHEN UomCode IN (N'EA', N'SET', N'KIT', N'PAIR') THEN 6 ELSE 20 END AS int) AS TypicalQty
INTO #ItemPick FROM dbo.Items;
SELECT SupplierCode, CategoryCode, ROW_NUMBER() OVER (PARTITION BY CategoryCode ORDER BY SupplierCode) AS Seq, COUNT(*) OVER (PARTITION BY CategoryCode) AS CatCount
INTO #SupplierPick FROM dbo.Suppliers;
SELECT EmployeeNumber, DepartmentCode, SectionCode, ROW_NUMBER() OVER (PARTITION BY DepartmentCode ORDER BY EmployeeNumber) AS Seq,
       COUNT(*) OVER (PARTITION BY DepartmentCode) AS DeptCount
INTO #StaffPick FROM #Staff;
GO
CREATE TABLE dbo.PurchaseRequisitions (
    RequisitionNumber          nvarchar(14)  NOT NULL CONSTRAINT PK_PurchaseRequisitions PRIMARY KEY,
    RequestDate                date          NOT NULL,
    DepartmentCode             nvarchar(10)  NOT NULL,
    SectionCode                nvarchar(10)  NOT NULL,
    CostCenterCode             nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseRequisitions_CostCenters REFERENCES dbo.CostCenters (CostCenterCode),
    RequestedByEmployeeNumber  nvarchar(10)  NOT NULL,   -- HumanResourcesDB.Employees
    CategoryCode               nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseRequisitions_ItemCategories REFERENCES dbo.ItemCategories (CategoryCode),
    Priority                   nvarchar(10)  NOT NULL,   -- Routine | Urgent | AOG (aircraft on ground)
    NeedByDate                 date          NOT NULL,
    Status                     nvarchar(12)  NOT NULL,   -- Submitted | Approved | Rejected | Converted (to a purchase order)
    ApprovedDate               date          NULL,
    EstimatedAmount            decimal(14,2) NOT NULL    -- AED
);
CREATE TABLE dbo.PurchaseRequisitionLines (
    RequisitionNumber nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseRequisitionLines_PurchaseRequisitions REFERENCES dbo.PurchaseRequisitions (RequisitionNumber),
    LineNumber        int           NOT NULL,
    ItemCode          nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseRequisitionLines_Items REFERENCES dbo.Items (ItemCode),
    Quantity          int           NOT NULL,
    EstimatedUnitCost decimal(12,2) NOT NULL,
    CONSTRAINT PK_PurchaseRequisitionLines PRIMARY KEY (RequisitionNumber, LineNumber)
);
WITH H AS (
    SELECT t.n, r.*, dr.DepartmentCode
    FROM (SELECT TOP (1800) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) t
    CROSS APPLY gen.Rand(5000000000 + t.n) r
    JOIN #DeptRange dr ON r.R1 >= dr.Lo AND r.R1 < dr.Hi
)
SELECT H.n, H.DepartmentCode, sp.SectionCode, sp.EmployeeNumber, cr.CategoryCode,
       DATEADD(DAY, CAST(H.R3 * 360 AS int), CAST('2025-01-01' AS date)) AS RequestDate,
       CASE WHEN cr.CategoryCode = N'AIRPART' AND H.R4 < 0.12 THEN N'AOG' WHEN H.R4 < 0.25 THEN N'Urgent' ELSE N'Routine' END AS Priority,
       1 + CAST(H.R5 * 4 AS int) AS LineCount, H.R6, H.R7, H.R8
INTO #Req
FROM H
JOIN #CatRange cr ON cr.DepartmentCode = H.DepartmentCode AND H.R2 >= cr.Lo AND H.R2 < cr.Hi
JOIN (SELECT DISTINCT DepartmentCode, DeptCount FROM #StaffPick) dc ON dc.DepartmentCode = H.DepartmentCode
JOIN #StaffPick sp ON sp.DepartmentCode = H.DepartmentCode AND sp.Seq = 1 + CAST(H.R9 * dc.DeptCount AS int);

SELECT CONCAT(N'PR-25-', RIGHT(CONCAT('0000', q.n), 5)) AS RequisitionNumber, l.l AS LineNumber, ip.ItemCode,
       CASE WHEN ip.TypicalQty = 2 THEN 1 + CAST(r.R2 * 2 AS int) ELSE CAST(ip.TypicalQty * (0.5 + r.R2) AS int) + 1 END AS Quantity,
       ip.StandardCost AS EstimatedUnitCost
INTO #ReqLines
FROM #Req q
CROSS JOIN (VALUES (1), (2), (3), (4), (5)) l(l)
CROSS APPLY gen.Rand(5100000000 + q.n * 10 + l.l) r
JOIN (SELECT DISTINCT CategoryCode, CatCount FROM #ItemPick) cc ON cc.CategoryCode = q.CategoryCode
JOIN #ItemPick ip ON ip.CategoryCode = q.CategoryCode AND ip.Seq = 1 + CAST(r.R1 * cc.CatCount AS int)
WHERE l.l <= q.LineCount;
-- An item appears once per requisition.
WITH Dup AS (SELECT ROW_NUMBER() OVER (PARTITION BY RequisitionNumber, ItemCode ORDER BY LineNumber) AS rn FROM #ReqLines) DELETE FROM Dup WHERE rn > 1;

INSERT INTO dbo.PurchaseRequisitions
SELECT CONCAT(N'PR-25-', RIGHT(CONCAT('0000', q.n), 5)), q.RequestDate, q.DepartmentCode, q.SectionCode, d.CostCenterCode, q.EmployeeNumber, q.CategoryCode, q.Priority,
       DATEADD(DAY, CASE q.Priority WHEN N'AOG' THEN 1 WHEN N'Urgent' THEN 7 ELSE 21 + CAST(q.R6 * 20 AS int) END, q.RequestDate),
       st.Status, CASE WHEN st.Status IN (N'Approved', N'Converted', N'Rejected') THEN DATEADD(DAY, CASE q.Priority WHEN N'AOG' THEN 0 ELSE 1 + CAST(q.R8 * 3 AS int) END, q.RequestDate) END,
       ISNULL((SELECT SUM(Quantity * EstimatedUnitCost) FROM #ReqLines pl WHERE pl.RequisitionNumber = CONCAT(N'PR-25-', RIGHT(CONCAT('0000', q.n), 5))), 0)
FROM #Req q
JOIN (SELECT DISTINCT DepartmentCode, CostCenterCode FROM dbo.CostCenters) d ON d.DepartmentCode = q.DepartmentCode
CROSS APPLY (SELECT CASE WHEN q.RequestDate > '2025-12-22' THEN N'Submitted' WHEN q.R7 < 0.06 THEN N'Rejected' WHEN q.RequestDate > '2025-12-15' AND q.R7 < 0.5 THEN N'Approved'
                         ELSE N'Converted' END AS Status) st;
INSERT INTO dbo.PurchaseRequisitionLines SELECT RequisitionNumber, LineNumber, ItemCode, Quantity, EstimatedUnitCost FROM #ReqLines;
CREATE INDEX IX_PurchaseRequisitions_Department ON dbo.PurchaseRequisitions (DepartmentCode, RequestDate);
GO
-- One purchase order per converted requisition, from a supplier of the requisition's category; prices vary around the standard cost.
CREATE TABLE dbo.PurchaseOrders (
    PONumber              nvarchar(14)  NOT NULL CONSTRAINT PK_PurchaseOrders PRIMARY KEY,
    OrderDate             date          NOT NULL,
    SupplierCode          nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseOrders_Suppliers REFERENCES dbo.Suppliers (SupplierCode),
    RequisitionNumber     nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseOrders_PurchaseRequisitions REFERENCES dbo.PurchaseRequisitions (RequisitionNumber),
    DepartmentCode        nvarchar(10)  NOT NULL,
    CategoryCode          nvarchar(10)  NOT NULL,
    BuyerEmployeeNumber   nvarchar(10)  NOT NULL,
    Priority              nvarchar(10)  NOT NULL,
    ExpectedDeliveryDate  date          NOT NULL,
    Status                nvarchar(20)  NOT NULL,   -- Open | Partially received | Received | Closed | Cancelled
    CurrencyCode          char(3)       NOT NULL,
    SubTotal              decimal(14,2) NOT NULL,
    VatAmount             decimal(14,2) NOT NULL,   -- 5 % UAE VAT
    TotalAmount           decimal(14,2) NOT NULL,
    PaymentTermsDays      int           NOT NULL
);
CREATE TABLE dbo.PurchaseOrderLines (
    PONumber         nvarchar(14)  NOT NULL CONSTRAINT FK_PurchaseOrderLines_PurchaseOrders REFERENCES dbo.PurchaseOrders (PONumber),
    LineNumber       int           NOT NULL,
    ItemCode         nvarchar(10)  NOT NULL CONSTRAINT FK_PurchaseOrderLines_Items REFERENCES dbo.Items (ItemCode),
    Quantity         int           NOT NULL,
    UnitPrice        decimal(12,2) NOT NULL,
    LineAmount       decimal(14,2) NOT NULL,
    QuantityReceived int           NOT NULL,
    WarehouseCode    nvarchar(6)   NOT NULL CONSTRAINT FK_PurchaseOrderLines_Warehouses REFERENCES dbo.Warehouses (WarehouseCode),
    CONSTRAINT PK_PurchaseOrderLines PRIMARY KEY (PONumber, LineNumber)
);
SELECT ROW_NUMBER() OVER (ORDER BY pr.ApprovedDate, pr.RequisitionNumber) AS n, pr.RequisitionNumber, pr.DepartmentCode, pr.CategoryCode, pr.Priority, pr.ApprovedDate,
       sp.SupplierCode, s.PaymentTermsDays, sl.LateFactor,
       DATEADD(DAY, CASE pr.Priority WHEN N'AOG' THEN 0 WHEN N'Urgent' THEN 1 ELSE 1 + CAST(r.R2 * 4 AS int) END, pr.ApprovedDate) AS OrderDate,
       r.R3, r.R4, r.R5, r.R6
INTO #Po
FROM dbo.PurchaseRequisitions pr
CROSS APPLY gen.Rand(6000000000 + CAST(RIGHT(pr.RequisitionNumber, 5) AS bigint)) r
JOIN (SELECT DISTINCT CategoryCode, CatCount FROM #SupplierPick) sc ON sc.CategoryCode = pr.CategoryCode
JOIN #SupplierPick sp ON sp.CategoryCode = pr.CategoryCode AND sp.Seq = 1 + CAST(r.R1 * sc.CatCount AS int)
JOIN dbo.Suppliers s ON s.SupplierCode = sp.SupplierCode
JOIN #SupplierLateness sl ON sl.SupplierCode = sp.SupplierCode
WHERE pr.Status = N'Converted';

SELECT CONCAT(N'PO-25-', RIGHT(CONCAT('0000', p.n), 5)) AS PONumber, l.LineNumber, l.ItemCode, l.Quantity, x.UnitPrice, x.UnitPrice * l.Quantity AS LineAmount,
       0 AS QuantityReceived, i.DefaultWarehouseCode AS WarehouseCode
INTO #PoLines
FROM #Po p
JOIN dbo.PurchaseRequisitionLines l ON l.RequisitionNumber = p.RequisitionNumber
JOIN dbo.Items i ON i.ItemCode = l.ItemCode
CROSS APPLY gen.Rand(6100000000 + p.n * 10 + l.LineNumber) r
CROSS APPLY (SELECT CAST(ROUND(l.EstimatedUnitCost * (0.9 + 0.22 * r.R1) * CASE WHEN p.Priority = N'AOG' THEN 1.25 ELSE 1 END, 2) AS decimal(12,2)) AS UnitPrice) x;

INSERT INTO dbo.PurchaseOrders
SELECT CONCAT(N'PO-25-', RIGHT(CONCAT('0000', p.n), 5)), p.OrderDate, p.SupplierCode, p.RequisitionNumber, p.DepartmentCode, p.CategoryCode, b.EmployeeNumber, p.Priority,
       DATEADD(DAY, CASE p.Priority WHEN N'AOG' THEN 2 ELSE c.TypicalLeadTimeDays END, p.OrderDate),
       N'Open', 'AED', t.SubTotal, CAST(ROUND(t.SubTotal * 0.05, 2) AS decimal(14,2)), t.SubTotal + CAST(ROUND(t.SubTotal * 0.05, 2) AS decimal(14,2)), p.PaymentTermsDays
FROM #Po p
JOIN dbo.ItemCategories c ON c.CategoryCode = p.CategoryCode
CROSS APPLY (SELECT SUM(LineAmount) AS SubTotal FROM #PoLines ol WHERE ol.PONumber = CONCAT(N'PO-25-', RIGHT(CONCAT('0000', p.n), 5))) t
JOIN (SELECT COUNT(*) AS N FROM #People WHERE Role = N'BUYER') bc ON 1 = 1
JOIN (SELECT EmployeeNumber, ROW_NUMBER() OVER (ORDER BY Seq) AS Seq FROM #People WHERE Role = N'BUYER') b ON b.Seq = 1 + CAST(p.R6 * bc.N AS int);
INSERT INTO dbo.PurchaseOrderLines SELECT * FROM #PoLines;
CREATE INDEX IX_PurchaseOrders_Supplier ON dbo.PurchaseOrders (SupplierCode, OrderDate);
GO
-- Goods receipts: on time for A-rated suppliers, later for B and C; occasional partial deliveries and rejected quantities.
CREATE TABLE dbo.GoodsReceipts (
    GRNNumber                nvarchar(14) NOT NULL CONSTRAINT PK_GoodsReceipts PRIMARY KEY,
    PONumber                 nvarchar(14) NOT NULL CONSTRAINT FK_GoodsReceipts_PurchaseOrders REFERENCES dbo.PurchaseOrders (PONumber),
    SupplierCode             nvarchar(10) NOT NULL CONSTRAINT FK_GoodsReceipts_Suppliers REFERENCES dbo.Suppliers (SupplierCode),
    ReceiptDate              date         NOT NULL,
    WarehouseCode            nvarchar(6)  NOT NULL CONSTRAINT FK_GoodsReceipts_Warehouses REFERENCES dbo.Warehouses (WarehouseCode),
    ReceivedByEmployeeNumber nvarchar(10) NOT NULL,
    DeliverySequence         int          NOT NULL,   -- 1 = first (or only) delivery, 2 = balance of a partial delivery
    DaysLate                 int          NOT NULL    -- against the PO expected delivery date; 0 when on time or early
);
CREATE TABLE dbo.GoodsReceiptLines (
    GRNNumber        nvarchar(14) NOT NULL CONSTRAINT FK_GoodsReceiptLines_GoodsReceipts REFERENCES dbo.GoodsReceipts (GRNNumber),
    LineNumber       int          NOT NULL,
    PONumber         nvarchar(14) NOT NULL,
    POLineNumber     int          NOT NULL,
    ItemCode         nvarchar(10) NOT NULL CONSTRAINT FK_GoodsReceiptLines_Items REFERENCES dbo.Items (ItemCode),
    QuantityReceived int          NOT NULL,
    QuantityAccepted int          NOT NULL,
    QuantityRejected int          NOT NULL,
    RejectionReason  nvarchar(40) NULL,
    CONSTRAINT PK_GoodsReceiptLines PRIMARY KEY (GRNNumber, LineNumber)
);
SELECT po.PONumber, po.SupplierCode, po.ExpectedDeliveryDate, po.Priority, ROW_NUMBER() OVER (ORDER BY po.PONumber) AS n,
       DATEADD(DAY, CASE WHEN po.Priority = N'AOG' THEN CAST(r.R1 * 2 AS int) ELSE CAST(r.R1 * sl.LateFactor AS int) - 2 END, po.ExpectedDeliveryDate) AS ReceiptDate,
       CASE WHEN r.R2 < 0.15 THEN 1 ELSE 0 END AS IsPartial, r.R3, r.R4, r.R5, r.R6
INTO #Grn
FROM dbo.PurchaseOrders po
JOIN #SupplierLateness sl ON sl.SupplierCode = po.SupplierCode
CROSS APPLY gen.Rand(7000000000 + CAST(RIGHT(po.PONumber, 5) AS bigint)) r
WHERE r.R7 >= 0.02;   -- 2 % of orders are cancelled and never received

INSERT INTO dbo.GoodsReceipts
SELECT CONCAT(N'GRN-25-', RIGHT(CONCAT('0000', g.n), 5), N'-', d.Seq), g.PONumber, g.SupplierCode, x.ReceiptDate, MIN(ol.WarehouseCode), st.EmployeeNumber, d.Seq,
       CASE WHEN x.ReceiptDate > g.ExpectedDeliveryDate THEN DATEDIFF(DAY, g.ExpectedDeliveryDate, x.ReceiptDate) ELSE 0 END
FROM #Grn g
CROSS JOIN (VALUES (1), (2)) d(Seq)
CROSS APPLY (SELECT CASE WHEN d.Seq = 1 THEN g.ReceiptDate ELSE DATEADD(DAY, 7 + CAST(g.R6 * 14 AS int), g.ReceiptDate) END AS ReceiptDate) x
JOIN dbo.PurchaseOrderLines ol ON ol.PONumber = g.PONumber
JOIN (SELECT COUNT(*) AS N FROM #People WHERE Role = N'STORE') sc ON 1 = 1
JOIN (SELECT EmployeeNumber, ROW_NUMBER() OVER (ORDER BY Seq) AS Seq FROM #People WHERE Role = N'STORE') st ON st.Seq = 1 + CAST(g.R5 * sc.N AS int)
WHERE x.ReceiptDate <= '2025-12-31' AND (d.Seq = 1 OR g.IsPartial = 1)
GROUP BY g.n, d.Seq, g.PONumber, g.SupplierCode, x.ReceiptDate, st.EmployeeNumber, g.ExpectedDeliveryDate;

INSERT INTO dbo.GoodsReceiptLines
SELECT gr.GRNNumber, ol.LineNumber, ol.PONumber, ol.LineNumber, ol.ItemCode, q.Received,
       q.Received - q.Rejected, q.Rejected,
       CASE WHEN q.Rejected > 0 THEN CHOOSE(1 + CAST(r.R2 * 4 AS int), N'Damaged in transit', N'Wrong part number', N'Missing certificate of conformity', N'Expired shelf life') END
FROM dbo.GoodsReceipts gr
JOIN #Grn g ON g.PONumber = gr.PONumber
JOIN dbo.PurchaseOrderLines ol ON ol.PONumber = gr.PONumber
CROSS APPLY gen.Rand(7100000000 + g.n * 100 + ol.LineNumber * 10 + gr.DeliverySequence) r
CROSS APPLY (SELECT CASE WHEN g.IsPartial = 0 THEN ol.Quantity
                         WHEN gr.DeliverySequence = 1 THEN CASE WHEN ol.Quantity > 1 THEN ol.Quantity / 2 ELSE ol.Quantity END
                         ELSE ol.Quantity - CASE WHEN ol.Quantity > 1 THEN ol.Quantity / 2 ELSE ol.Quantity END END AS Received) q0
CROSS APPLY (SELECT q0.Received AS Received, CASE WHEN q0.Received > 0 AND r.R1 < 0.04 THEN 1 + CAST(r.R3 * CASE WHEN q0.Received > 3 THEN 2 ELSE 0 END AS int) ELSE 0 END AS Rejected) q
WHERE q.Received > 0;

UPDATE ol SET QuantityReceived = ISNULL(x.Accepted, 0)
FROM dbo.PurchaseOrderLines ol
OUTER APPLY (SELECT SUM(QuantityAccepted) AS Accepted FROM dbo.GoodsReceiptLines gl WHERE gl.PONumber = ol.PONumber AND gl.POLineNumber = ol.LineNumber) x;
UPDATE po SET Status = CASE WHEN NOT EXISTS (SELECT 1 FROM #Grn g WHERE g.PONumber = po.PONumber) THEN N'Cancelled'
                            WHEN NOT EXISTS (SELECT 1 FROM dbo.GoodsReceipts gr WHERE gr.PONumber = po.PONumber) THEN N'Open'
                            WHEN EXISTS (SELECT 1 FROM dbo.PurchaseOrderLines ol WHERE ol.PONumber = po.PONumber AND ol.QuantityReceived < ol.Quantity) THEN N'Partially received'
                            ELSE N'Received' END
FROM dbo.PurchaseOrders po;
CREATE INDEX IX_GoodsReceipts_PO ON dbo.GoodsReceipts (PONumber);
GO
-- Supplier invoices: one per purchase order after its first receipt; paid around the due date, a few disputed.
CREATE TABLE dbo.SupplierInvoices (
    InvoiceNumber  nvarchar(20)  NOT NULL CONSTRAINT PK_SupplierInvoices PRIMARY KEY,
    SupplierCode   nvarchar(10)  NOT NULL CONSTRAINT FK_SupplierInvoices_Suppliers REFERENCES dbo.Suppliers (SupplierCode),
    PONumber       nvarchar(14)  NOT NULL CONSTRAINT FK_SupplierInvoices_PurchaseOrders REFERENCES dbo.PurchaseOrders (PONumber),
    InvoiceDate    date          NOT NULL,
    DueDate        date          NOT NULL,
    NetAmount      decimal(14,2) NOT NULL,
    VatAmount      decimal(14,2) NOT NULL,
    TotalAmount    decimal(14,2) NOT NULL,
    Status         nvarchar(10)  NOT NULL,   -- Pending | Approved | Paid | Disputed
    PaidDate       date          NULL,
    DaysToPay      int           NULL        -- invoice date to payment date
);
INSERT INTO dbo.SupplierInvoices
SELECT CONCAT(N'INV-', po.SupplierCode, N'-', RIGHT(po.PONumber, 5)), po.SupplierCode, po.PONumber, x.InvoiceDate, DATEADD(DAY, po.PaymentTermsDays, x.InvoiceDate),
       po.SubTotal, po.VatAmount, po.TotalAmount, st.Status,
       CASE WHEN st.Status = N'Paid' THEN pd.PaidDate END,
       CASE WHEN st.Status = N'Paid' THEN DATEDIFF(DAY, x.InvoiceDate, pd.PaidDate) END
FROM dbo.PurchaseOrders po
CROSS APPLY (SELECT MIN(ReceiptDate) AS FirstReceipt FROM dbo.GoodsReceipts gr WHERE gr.PONumber = po.PONumber) f
CROSS APPLY gen.Rand(8000000000 + CAST(RIGHT(po.PONumber, 5) AS bigint)) r
CROSS APPLY (SELECT DATEADD(DAY, CAST(r.R1 * 8 AS int), f.FirstReceipt) AS InvoiceDate) x
CROSS APPLY (SELECT DATEADD(DAY, po.PaymentTermsDays - 5 + CAST(r.R2 * 25 AS int), x.InvoiceDate) AS PaidDate) pd
CROSS APPLY (SELECT CASE WHEN r.R3 < 0.03 THEN N'Disputed' WHEN pd.PaidDate <= '2025-12-31' THEN N'Paid' WHEN r.R4 < 0.7 THEN N'Approved' ELSE N'Pending' END AS Status) st
WHERE f.FirstReceipt IS NOT NULL AND x.InvoiceDate <= '2025-12-31';
UPDATE po SET Status = N'Closed' FROM dbo.PurchaseOrders po
WHERE po.Status = N'Received' AND EXISTS (SELECT 1 FROM dbo.SupplierInvoices si WHERE si.PONumber = po.PONumber AND si.Status = N'Paid');
GO
-- Store issues: departments draw stock from the warehouses throughout the year.
CREATE TABLE dbo.StoreIssues (
    IssueNumber               nvarchar(14) NOT NULL CONSTRAINT PK_StoreIssues PRIMARY KEY,
    IssueDate                 date         NOT NULL,
    WarehouseCode             nvarchar(6)  NOT NULL CONSTRAINT FK_StoreIssues_Warehouses REFERENCES dbo.Warehouses (WarehouseCode),
    DepartmentCode            nvarchar(10) NOT NULL,
    SectionCode               nvarchar(10) NOT NULL,
    CostCenterCode            nvarchar(10) NOT NULL CONSTRAINT FK_StoreIssues_CostCenters REFERENCES dbo.CostCenters (CostCenterCode),
    RequestedByEmployeeNumber nvarchar(10) NOT NULL,
    IssuedByEmployeeNumber    nvarchar(10) NOT NULL,
    CategoryCode              nvarchar(10) NOT NULL
);
CREATE TABLE dbo.StoreIssueLines (
    IssueNumber nvarchar(14)  NOT NULL CONSTRAINT FK_StoreIssueLines_StoreIssues REFERENCES dbo.StoreIssues (IssueNumber),
    LineNumber  int           NOT NULL,
    ItemCode    nvarchar(10)  NOT NULL CONSTRAINT FK_StoreIssueLines_Items REFERENCES dbo.Items (ItemCode),
    Quantity    int           NOT NULL,
    UnitCost    decimal(12,2) NOT NULL,
    LineValue   decimal(14,2) NOT NULL,
    CONSTRAINT PK_StoreIssueLines PRIMARY KEY (IssueNumber, LineNumber)
);
WITH H AS (
    SELECT t.n, r.*, dr.DepartmentCode
    FROM (SELECT TOP (4200) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) t
    CROSS APPLY gen.Rand(9000000000 + t.n) r
    JOIN #DeptRange dr ON r.R1 >= dr.Lo AND r.R1 < dr.Hi
)
SELECT H.n, H.DepartmentCode, sp.SectionCode, sp.EmployeeNumber, cr.CategoryCode, c.DefaultWarehouseCode,
       DATEADD(DAY, CAST(H.R3 * 365 AS int), CAST('2025-01-01' AS date)) AS IssueDate, 1 + CAST(H.R4 * 3 AS int) AS LineCount, H.R5
INTO #Iss
FROM H
JOIN #CatRange cr ON cr.DepartmentCode = H.DepartmentCode AND H.R2 >= cr.Lo AND H.R2 < cr.Hi
JOIN dbo.ItemCategories c ON c.CategoryCode = cr.CategoryCode
JOIN (SELECT DISTINCT DepartmentCode, DeptCount FROM #StaffPick) dc ON dc.DepartmentCode = H.DepartmentCode
JOIN #StaffPick sp ON sp.DepartmentCode = H.DepartmentCode AND sp.Seq = 1 + CAST(H.R9 * dc.DeptCount AS int);

INSERT INTO dbo.StoreIssues
SELECT CONCAT(N'SI-25-', RIGHT(CONCAT('0000', i.n), 5)), i.IssueDate, i.DefaultWarehouseCode, i.DepartmentCode, i.SectionCode, d.CostCenterCode, i.EmployeeNumber, st.EmployeeNumber, i.CategoryCode
FROM #Iss i
JOIN dbo.CostCenters d ON d.DepartmentCode = i.DepartmentCode
JOIN (SELECT COUNT(*) AS N FROM #People WHERE Role = N'STORE') sc ON 1 = 1
JOIN (SELECT EmployeeNumber, ROW_NUMBER() OVER (ORDER BY Seq) AS Seq FROM #People WHERE Role = N'STORE') st ON st.Seq = 1 + CAST(i.R5 * sc.N AS int);

INSERT INTO dbo.StoreIssueLines
SELECT CONCAT(N'SI-25-', RIGHT(CONCAT('0000', i.n), 5)), l.l, ip.ItemCode, q.Qty, ip.StandardCost, q.Qty * ip.StandardCost
FROM #Iss i
CROSS JOIN (VALUES (1), (2), (3)) l(l)
CROSS APPLY gen.Rand(9100000000 + i.n * 10 + l.l) r
JOIN (SELECT DISTINCT CategoryCode, CatCount FROM #ItemPick) cc ON cc.CategoryCode = i.CategoryCode
JOIN #ItemPick ip ON ip.CategoryCode = i.CategoryCode AND ip.Seq = 1 + CAST(r.R1 * cc.CatCount AS int)
CROSS APPLY (SELECT CASE WHEN ip.TypicalQty = 2 THEN 1 ELSE 1 + CAST(r.R2 * ip.TypicalQty / 2.0 AS int) END AS Qty) q
WHERE l.l <= i.LineCount;
-- An item appears once per issue: keep the first line of duplicates.
WITH Dup AS (SELECT IssueNumber, LineNumber, ROW_NUMBER() OVER (PARTITION BY IssueNumber, ItemCode ORDER BY LineNumber) AS rn FROM dbo.StoreIssueLines)
DELETE FROM Dup WHERE rn > 1;
CREATE INDEX IX_StoreIssues_Department ON dbo.StoreIssues (DepartmentCode, IssueDate);
GO
-- Stock ledger: an opening balance on 1 January sized so no warehouse ever goes negative, then receipts (+) and issues (-).
CREATE TABLE dbo.StockMovements (
    MovementId      int           NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY,
    MovementDate    date          NOT NULL,
    MovementType    nvarchar(16)  NOT NULL,   -- Opening balance | Goods receipt | Store issue
    WarehouseCode   nvarchar(6)   NOT NULL CONSTRAINT FK_StockMovements_Warehouses REFERENCES dbo.Warehouses (WarehouseCode),
    ItemCode        nvarchar(10)  NOT NULL CONSTRAINT FK_StockMovements_Items REFERENCES dbo.Items (ItemCode),
    Quantity        int           NOT NULL,   -- positive in, negative out
    UnitCost        decimal(12,2) NOT NULL,
    MovementValue   decimal(14,2) NOT NULL,
    ReferenceNumber nvarchar(20)  NOT NULL,   -- GRN or store issue number
    DepartmentCode  nvarchar(10)  NULL        -- consuming department for store issues
);
WITH Flow AS (
    SELECT gr.ReceiptDate AS D, N'Goods receipt' AS T, gr.WarehouseCode AS W, gl.ItemCode AS I, gl.QuantityAccepted AS Q, ol.UnitPrice AS C, gr.GRNNumber AS Ref, CAST(NULL AS nvarchar(10)) AS Dept
    FROM dbo.GoodsReceiptLines gl JOIN dbo.GoodsReceipts gr ON gr.GRNNumber = gl.GRNNumber
    JOIN dbo.PurchaseOrderLines ol ON ol.PONumber = gl.PONumber AND ol.LineNumber = gl.POLineNumber
    WHERE gl.QuantityAccepted > 0
    UNION ALL
    SELECT si.IssueDate, N'Store issue', si.WarehouseCode, sl.ItemCode, -sl.Quantity, sl.UnitCost, si.IssueNumber, si.DepartmentCode
    FROM dbo.StoreIssueLines sl JOIN dbo.StoreIssues si ON si.IssueNumber = sl.IssueNumber
), Running AS (
    SELECT W, I, MIN(RunningQty) AS Lowest FROM (SELECT W, I, SUM(Q) OVER (PARTITION BY W, I ORDER BY D, Ref ROWS UNBOUNDED PRECEDING) AS RunningQty FROM Flow) x GROUP BY W, I
), Opening AS (
    SELECT it.DefaultWarehouseCode AS W, it.ItemCode AS I,
           CASE WHEN ISNULL(-ru.Lowest, 0) > 0 THEN -ru.Lowest ELSE 0 END + CAST(it.ReorderLevel * (0.5 + r.R1 * 1.5) AS int) AS Q, it.StandardCost AS C
    FROM dbo.Items it
    LEFT JOIN Running ru ON ru.W = it.DefaultWarehouseCode AND ru.I = it.ItemCode
    CROSS APPLY gen.Rand(9500000000 + CAST(RIGHT(it.ItemCode, 4) AS bigint)) r
), AllMoves AS (
    SELECT CAST('2025-01-01' AS date) AS D, N'Opening balance' AS T, W, I, Q, C, N'OPENING-2025' AS Ref, CAST(NULL AS nvarchar(10)) AS Dept, 0 AS Ord FROM Opening WHERE Q > 0
    UNION ALL SELECT D, T, W, I, Q, C, Ref, Dept, 1 FROM Flow
)
INSERT INTO dbo.StockMovements
SELECT ROW_NUMBER() OVER (ORDER BY D, Ord, Ref, I), D, T, W, I, Q, C, Q * C, Ref, Dept FROM AllMoves;
CREATE INDEX IX_StockMovements_Item ON dbo.StockMovements (ItemCode, WarehouseCode, MovementDate);
GO
CREATE TABLE dbo.StockBalances (
    WarehouseCode    nvarchar(6)   NOT NULL CONSTRAINT FK_StockBalances_Warehouses REFERENCES dbo.Warehouses (WarehouseCode),
    ItemCode         nvarchar(10)  NOT NULL CONSTRAINT FK_StockBalances_Items REFERENCES dbo.Items (ItemCode),
    QuantityOnHand   int           NOT NULL,   -- as of 31 Dec 2025
    StockValue       decimal(14,2) NOT NULL,   -- at standard cost
    ReorderLevel     int           NOT NULL,
    BelowReorderLevel bit          NOT NULL,
    LastReceiptDate  date          NULL,
    LastIssueDate    date          NULL,
    BinLocation      nvarchar(10)  NOT NULL,
    CONSTRAINT PK_StockBalances PRIMARY KEY (WarehouseCode, ItemCode)
);
INSERT INTO dbo.StockBalances
SELECT m.WarehouseCode, m.ItemCode, SUM(m.Quantity), SUM(m.Quantity) * it.StandardCost, it.ReorderLevel,
       CASE WHEN SUM(m.Quantity) < it.ReorderLevel THEN 1 ELSE 0 END,
       MAX(CASE WHEN m.MovementType = N'Goods receipt' THEN m.MovementDate END), MAX(CASE WHEN m.MovementType = N'Store issue' THEN m.MovementDate END),
       CONCAT(LEFT(m.WarehouseCode, 1), N'-', RIGHT(CONCAT('0', 1 + CAST(RIGHT(m.ItemCode, 2) AS int) % 20), 2), N'-', CHAR(65 + CAST(RIGHT(m.ItemCode, 3) AS int) % 6))
FROM dbo.StockMovements m JOIN dbo.Items it ON it.ItemCode = m.ItemCode
GROUP BY m.WarehouseCode, m.ItemCode, it.StandardCost, it.ReorderLevel;
DROP TABLE #ItemPick; DROP TABLE #SupplierPick; DROP TABLE #StaffPick; DROP TABLE #Req; DROP TABLE #ReqLines; DROP TABLE #Po; DROP TABLE #PoLines; DROP TABLE #Grn; DROP TABLE #Iss;
GO
"""
