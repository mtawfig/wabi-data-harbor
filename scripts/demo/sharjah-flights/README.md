# Demo source: Sharjah International Airport, 2025 flight movements

> Superseded for new setups by the three sample source databases and the WabiInsightDB seed in [`scripts/sql/samples`](../../sql/samples/README.md) (flight operations with schedules and aircraft, HR/payroll/attendance, procurement/stores). This folder is kept because the P8.3–P11.2 screenshots were taken with it.

A realistic **synthetic** dataset used to demonstrate dashboard answers in *Ask Wabi Insight*
(`docs/screenshots/p8-3-*.png`). Carriers, the route network, fleet types, terminals, seasonality
(summer and year-end peaks, Eid weeks), weekday patterns and delay behaviour mirror the real airport;
the individual movements are simulated by `generate.py` (131,516 rows: 65,758 departures and
65,758 arrivals). No real passenger or flight records are involved.

```bash
python3 generate.py                      # writes airlines.csv, airports.csv, flights.csv next to the script
sqlcmd -S localhost,1433 -U sa -P '<sa password>' -C -i create-database.sql
for t in Airlines Airports Flights; do
  bcp dbo.$t in $(echo $t | tr 'A-Z' 'a-z').csv -S localhost,1433 -U sa -P '<sa password>' -d SharjahAirport2025 -c -t ',' -r '\n' -u -b 20000
done
```

Then, as an Organization Administrator: register the application (SQL Server,
`Server=localhost,1433;Database=SharjahAirport2025;User Id=shj_reader;Password=...;Encrypt=True;TrustServerCertificate=True;`),
test the connection, approve it, refresh metadata, approve the three objects with their columns, and grant a
role table access (all rows). Column descriptions that help the planner (`Direction` values, `Status` values,
`DayOfWeek` names, local time) are worth adding in the Metadata page.
