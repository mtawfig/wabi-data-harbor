#!/usr/bin/env python3
"""Regenerates the sample source-database scripts in this folder:

  01-create-flightoperationsdb.sql   FlightOperationsDB   (Sharjah airport flight operations, 2025)
  02-create-humanresourcesdb.sql     HumanResourcesDB     (employees, payroll, attendance, leave)
  03-create-procurementstoresdb.sql  ProcurementStoresDB  (suppliers, items, purchasing, receipts, stores)

04-seed-wabiinsightdb.sql is written by hand. Run:  python3 scripts/sql/samples/build_sample_sql.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import build_flights  # noqa: E402

FILES = {"01-create-flightoperationsdb.sql": build_flights}

if __name__ == "__main__":
    try:
        import build_hr  # noqa: F401
        FILES["02-create-humanresourcesdb.sql"] = build_hr
    except ImportError:
        pass
    try:
        import build_procurement  # noqa: F401
        FILES["03-create-procurementstoresdb.sql"] = build_procurement
    except ImportError:
        pass
    for name, module in FILES.items():
        text, *stats = module.build()
        with open(os.path.join(HERE, name), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        print(f"{name}: {len(text) // 1024} KB {stats}")
