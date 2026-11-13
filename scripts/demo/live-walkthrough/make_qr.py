#!/usr/bin/env python3
"""QR code of the closing frame: a vCard whose name is the company, with its mobile and e-mail (script.<lang>.json,
"contact"). Scanning it opens "Add contact" on a phone.   python3 make_qr.py --script script.ar.json --out <dir>/qr.svg"""
import argparse, json, segno

parser = argparse.ArgumentParser()
parser.add_argument("--script", required=True)
parser.add_argument("--out", required=True)
args = parser.parse_args()
c = json.load(open(args.script, encoding="utf-8"))["contact"]
mobile = "".join(ch for ch in c["mobile"] if ch.isdigit() or ch == "+")
vcard = "\r\n".join(["BEGIN:VCARD", "VERSION:3.0", f"N:{c['company']};;;;", f"FN:{c['company']}", f"ORG:{c['company']}",
                     f"TEL;TYPE=CELL:{mobile}", f"EMAIL;TYPE=INTERNET:{c['email']}", "END:VCARD"])
segno.make(vcard, error="m").save(args.out, scale=10, border=3, dark="#0b1f3a", light="#ffffff")
print(vcard.replace("\r\n", " | "))
