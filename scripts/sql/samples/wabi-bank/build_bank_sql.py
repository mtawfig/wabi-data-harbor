#!/usr/bin/env python3
"""Regenerates the Wabi Bank sample scripts in this folder from the shared model (bank_people.py, bank_customers.py):

  01-create-wabibankpersonneldb.sql    WabiBankPersonnelDB     Personnel and Attendance (system of record for every identifier)
  02-create-wabibankcorebankingdb.sql  WabiBankCoreBankingDB   Core Banking
  03-create-wabibankfinancedb.sql      WabiBankFinanceDB       Finance
  04-create-wabibankdigitaldb.sql      WabiBankDigitalDB       Digital Banking
  05-create-wabibankitdb.sql           WabiBankITDB            IT Help Desk (Requests and Incidents)
  06-seed-wabiinsightdb.sql            WabiInsightDB           the Wabi Bank organization in Wabi Insight

07-check-consistency.sql is written by hand. Every generator validates the model and asserts its consistency rules
before writing; the output is deterministic, so regenerating an unchanged model gives the same files.
Run:  python3 scripts/sql/samples/wabi-bank/build_bank_sql.py
"""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.dirname(HERE))

import build_corebanking  # noqa: E402
import build_digital  # noqa: E402
import build_finance  # noqa: E402
import build_it  # noqa: E402
import build_personnel  # noqa: E402
import build_seed  # noqa: E402

FILES = [
    ("01-create-wabibankpersonneldb.sql", build_personnel),
    ("02-create-wabibankcorebankingdb.sql", build_corebanking),
    ("03-create-wabibankfinancedb.sql", build_finance),
    ("04-create-wabibankdigitaldb.sql", build_digital),
    ("05-create-wabibankitdb.sql", build_it),
    ("06-seed-wabiinsightdb.sql", build_seed),
]

if __name__ == "__main__":
    for name, module in FILES:
        started = time.time()
        text, *_ = module.build()
        with open(os.path.join(HERE, name), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        print(f"{name}: {len(text) // 1024} KB in {time.time() - started:.1f} s")
