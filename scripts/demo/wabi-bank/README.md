# Wabi Bank demo

A recording of the second sample organization, **Wabi Bank** (`scripts/sql/samples/wabi-bank`), in the running app. Nothing is mocked: the questions go to the organization's real AI provider, and the row scopes, the scheduler and the alert centre are the product's own.

Result: [`docs/demo/p13-6-wabi-bank.gif`](../../../docs/demo/p13-6-wabi-bank.gif)

## The story

1. **Daniel Hartwell** (Branch Manager, Deira) asks for cash deposits and withdrawals per teller in December 2025. The Branch Manager role reads Core Banking with the `DEPARTMENT` row scope on the posting unit, so only Deira's tellers come back. The answer shows *Rows limited to what you may see: your department or branch (BR-DXB01)*, and the narrative says the figures cover the branch, not the bank.
2. **Emily Carver** (Teller, Deira) asks the same question. The Teller role reads only the staff view of postings with the `SELF` row scope: the answer holds only Emily's own postings and says it is not the whole branch.
3. **Laura Bexley** (Compliance and AML Officer) opens *Alerts*. The rule *Cash deposits of AED 55,000 or more in a day (individuals)* found customer C100503, who deposited cash just under AED 50,000 on several days and then twice a day on the last two days. Laura acknowledges it with a note; alerts never block anyone automatically.

## Run it

Prerequisites:
- The API on `:5080` and the web app on `:4200`.
- Wabi Bank built and seeded (`scripts/sql/samples/wabi-bank/01`–`06`) and its five applications connected and approved.
- The accounts' passwords set once through the app (the seed's initial password must be changed at first sign-in).
- An AI provider for the organization (the platform default is enough).

```bash
WI_SAMPLE_PASSWORD='<the accounts' password>' \
WI_SQLCMD="sqlcmd -S localhost,1433 -U sa -P '<sa password>' -C -d WabiInsightDB" \
  node scripts/demo/wabi-bank/record.js --gif docs/demo/p13-6-wabi-bank.gif
```

- `WI_SQLCMD` is optional. Without an open alert, it makes the cash-deposit rule's job due so the next scheduler tick (at most a minute) raises it; without it, the rule's daily run must have happened.
- A second recording finds the alert already acknowledged and shows it as it is.
- `--videos <dir>` keeps the three scenes (WebM) and `--screens <dir>` saves a PNG of each answer and of the alert.
- `--gif` needs `ffmpeg`: 960 px wide, 8 fps, the blank first second of each scene trimmed.
