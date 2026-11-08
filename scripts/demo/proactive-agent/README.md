# Proactive agent demo

A 40-second recording of the proactive agent working on the sample organization, Gulf Aviation Services (`scripts/sql/samples`). The script drives the running app and records it; nothing is mocked. The question goes to the organization's real AI provider, and the access ledger, the scheduler and the alert centre are the product's own.

Result: [`docs/demo/p14-5-proactive-agent-alert.gif`](../../../docs/demo/p14-5-proactive-agent-alert.gif)

## The story

1. **Arjun Nair** (Operations Analyst) asks for the net pay of six named colleagues. His role reads `Payslips` with the `SELF` row scope, so the authorized query returns none of their figures. The look-up is still written to the append-only access ledger: object, classification (Confidential), and the six identifier values he filtered on.
2. The access rule **Look-ups of named colleagues' pay** runs every 15 minutes over the ledger (signal: identifier filter values on `Payslips`, more than five in a day). It raises a **High** alert, and the administrator gets an in-app notification at once.
3. **Faisal Al Shamsi** (Organization Administrator, cyber security) opens the alert from the notification. It shows the user and how many specific employees were looked up, but no salary figures. He acknowledges the alert with a note. Nothing happens to Arjun's account: alerts never suspend anyone automatically, and every action is recorded in the alert's timeline and the audit log.

## Run it

Prerequisites:
- The API on `:5080` and the web app on `:4200`.
- The sample databases and the organization seeded (`scripts/sql/samples/01`–`04`). Start from a fresh seed, because an alert that is already open for the rule only counts a repeat.
- The sample accounts' passwords set once through the app; the seed emails temporary passwords.
- An AI provider configured for the organization.

```bash
WI_SAMPLE_PASSWORD='<the sample accounts' password>' \
WI_SQLCMD="sqlcmd -S localhost,1433 -U sa -P '<sa password>' -C -d WabiInsightDB" \
  node scripts/demo/proactive-agent/record.js --gif docs/demo/p14-5-proactive-agent-alert.gif
```

- On the first run the script creates the access rule as the administrator.
- `WI_SQLCMD` is optional. With it, the script makes the rule's job due so that the next scheduler tick (at most a minute) evaluates it. Without it, the script waits for the rule's own 15-minute schedule.
- `--videos <dir>` keeps the two recorded scenes (WebM).
- `--gif` needs `ffmpeg`. The script trims the blank first second of each scene and writes a 960 px wide, 8 fps GIF.
- Accounts can be changed with `WI_ANALYST_EMAIL` and `WI_ADMIN_EMAIL`, and the web address with `WI_BASE_URL`.

In a live presentation, run the same steps by hand: ask the question as Arjun, then either wait for the rule's schedule or make its job due, then open the notification as Faisal.
