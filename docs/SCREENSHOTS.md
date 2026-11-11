# Wabi Insight — Screens

The 42 screens required by specification §32, captured from the running application (never mocked) by `scripts/screenshots/capture.js` in English and Arabic at desktop (1440 × 960, device scale 2) and mobile (390 × 844, device scale 2) widths. Files live in `docs/screenshots/screens/` as `NN-slug-{en|ar}-{desktop|mobile}.png`; `capture-report.json` next to them records when each shot was taken and any horizontal overflow the script detected (none at the time of writing).

## Reproducing

```bash
# 1. API and web running with the demo data (docs/PROGRESS.md P8.3 / scripts/demo/sharjah-flights)
dotnet run --project src/WabiInsight.Api            # :5080
cd src/web && npm start                              # :4200
# 2. Capture (ids of a saved study with a PDF, the organization and a ticket come from the demo data)
WI_HISTORY_ID=<history guid> WI_ORGANIZATION_ID=<organization guid> WI_TICKET_ID=<ticket guid> \
  node scripts/screenshots/capture.js [--lang en,ar] [--device desktop,mobile] [--only 31,32] [--out docs/screenshots/screens]
```

Accounts default to the demo users (`WI_PLATFORM_*`, `WI_ORGADMIN_*`, `WI_USER_*`, `WI_RECIPIENT_*` override them). The script toggles the visible language switcher when the signed-in account's preferred language differs from the requested one, so Arabic shots show the real RTL layout of the same session.

## Public (1–4)

| # | Screen | Signed in as | EN desktop | AR desktop | EN mobile | AR mobile |
|---|---|---|---|---|---|---|
| 1 | Landing page | public | [en-desktop](screenshots/screens/01-landing-en-desktop.png) | [ar-desktop](screenshots/screens/01-landing-ar-desktop.png) | [en-mobile](screenshots/screens/01-landing-en-mobile.png) | [ar-mobile](screenshots/screens/01-landing-ar-mobile.png) |
| 2 | Sign in | public | [en-desktop](screenshots/screens/02-login-en-desktop.png) | [ar-desktop](screenshots/screens/02-login-ar-desktop.png) | [en-mobile](screenshots/screens/02-login-en-mobile.png) | [ar-mobile](screenshots/screens/02-login-ar-mobile.png) |
| 3 | Platform bootstrap (first administrator) | public | [en-desktop](screenshots/screens/03-bootstrap-en-desktop.png) | [ar-desktop](screenshots/screens/03-bootstrap-ar-desktop.png) | [en-mobile](screenshots/screens/03-bootstrap-en-mobile.png) | [ar-mobile](screenshots/screens/03-bootstrap-ar-mobile.png) |
| 4 | Change password | Organization user | [en-desktop](screenshots/screens/04-change-password-en-desktop.png) | [ar-desktop](screenshots/screens/04-change-password-ar-desktop.png) | [en-mobile](screenshots/screens/04-change-password-en-mobile.png) | [ar-mobile](screenshots/screens/04-change-password-ar-mobile.png) |

## Platform Administrator (5–15)

| # | Screen | Signed in as | EN desktop | AR desktop | EN mobile | AR mobile |
|---|---|---|---|---|---|---|
| 5 | Platform Overview | Platform Administrator | [en-desktop](screenshots/screens/05-platform-overview-en-desktop.png) | [ar-desktop](screenshots/screens/05-platform-overview-ar-desktop.png) | [en-mobile](screenshots/screens/05-platform-overview-en-mobile.png) | [ar-mobile](screenshots/screens/05-platform-overview-ar-mobile.png) |
| 6 | Organizations | Platform Administrator | [en-desktop](screenshots/screens/06-platform-organizations-en-desktop.png) | [ar-desktop](screenshots/screens/06-platform-organizations-ar-desktop.png) | [en-mobile](screenshots/screens/06-platform-organizations-en-mobile.png) | [ar-mobile](screenshots/screens/06-platform-organizations-ar-mobile.png) |
| 7 | Create organization | Platform Administrator | [en-desktop](screenshots/screens/07-platform-organization-create-en-desktop.png) | [ar-desktop](screenshots/screens/07-platform-organization-create-ar-desktop.png) | [en-mobile](screenshots/screens/07-platform-organization-create-en-mobile.png) | [ar-mobile](screenshots/screens/07-platform-organization-create-ar-mobile.png) |
| 8 | Organization detail (status, administrators, applications) | Platform Administrator | [en-desktop](screenshots/screens/08-platform-organization-detail-en-desktop.png) | [ar-desktop](screenshots/screens/08-platform-organization-detail-ar-desktop.png) | [en-mobile](screenshots/screens/08-platform-organization-detail-en-mobile.png) | [ar-mobile](screenshots/screens/08-platform-organization-detail-ar-mobile.png) |
| 9 | Platform Administrators | Platform Administrator | [en-desktop](screenshots/screens/09-platform-administrators-en-desktop.png) | [ar-desktop](screenshots/screens/09-platform-administrators-ar-desktop.png) | [en-mobile](screenshots/screens/09-platform-administrators-en-mobile.png) | [ar-mobile](screenshots/screens/09-platform-administrators-ar-mobile.png) |
| 10 | Global role templates | Platform Administrator | [en-desktop](screenshots/screens/10-platform-role-templates-en-desktop.png) | [ar-desktop](screenshots/screens/10-platform-role-templates-ar-desktop.png) | [en-mobile](screenshots/screens/10-platform-role-templates-en-mobile.png) | [ar-mobile](screenshots/screens/10-platform-role-templates-ar-mobile.png) |
| 11 | AI providers | Platform Administrator | [en-desktop](screenshots/screens/11-platform-ai-providers-en-desktop.png) | [ar-desktop](screenshots/screens/11-platform-ai-providers-ar-desktop.png) | [en-mobile](screenshots/screens/11-platform-ai-providers-en-mobile.png) | [ar-mobile](screenshots/screens/11-platform-ai-providers-ar-mobile.png) |
| 12 | Platform Insights configuration | Platform Administrator | [en-desktop](screenshots/screens/12-platform-insights-config-en-desktop.png) | [ar-desktop](screenshots/screens/12-platform-insights-config-ar-desktop.png) | [en-mobile](screenshots/screens/12-platform-insights-config-en-mobile.png) | [ar-mobile](screenshots/screens/12-platform-insights-config-ar-mobile.png) |
| 13 | Support desk (platform queue) | Platform Administrator | [en-desktop](screenshots/screens/13-platform-support-desk-en-desktop.png) | [ar-desktop](screenshots/screens/13-platform-support-desk-ar-desktop.png) | [en-mobile](screenshots/screens/13-platform-support-desk-en-mobile.png) | [ar-mobile](screenshots/screens/13-platform-support-desk-ar-mobile.png) |
| 14 | System health | Platform Administrator | [en-desktop](screenshots/screens/14-platform-health-en-desktop.png) | [ar-desktop](screenshots/screens/14-platform-health-ar-desktop.png) | [en-mobile](screenshots/screens/14-platform-health-en-mobile.png) | [ar-mobile](screenshots/screens/14-platform-health-ar-mobile.png) |
| 15 | Audit logs | Platform Administrator | [en-desktop](screenshots/screens/15-platform-audit-en-desktop.png) | [ar-desktop](screenshots/screens/15-platform-audit-ar-desktop.png) | [en-mobile](screenshots/screens/15-platform-audit-en-mobile.png) | [ar-mobile](screenshots/screens/15-platform-audit-ar-mobile.png) |

## Organization Administrator (16–29)

| # | Screen | Signed in as | EN desktop | AR desktop | EN mobile | AR mobile |
|---|---|---|---|---|---|---|
| 16 | Organization dashboard | Organization Administrator | [en-desktop](screenshots/screens/16-org-admin-dashboard-en-desktop.png) | [ar-desktop](screenshots/screens/16-org-admin-dashboard-ar-desktop.png) | [en-mobile](screenshots/screens/16-org-admin-dashboard-en-mobile.png) | [ar-mobile](screenshots/screens/16-org-admin-dashboard-ar-mobile.png) |
| 17 | Organization users | Organization Administrator | [en-desktop](screenshots/screens/17-org-users-en-desktop.png) | [ar-desktop](screenshots/screens/17-org-users-ar-desktop.png) | [en-mobile](screenshots/screens/17-org-users-en-mobile.png) | [ar-mobile](screenshots/screens/17-org-users-ar-mobile.png) |
| 18 | Create user | Organization Administrator | [en-desktop](screenshots/screens/18-org-user-form-en-desktop.png) | [ar-desktop](screenshots/screens/18-org-user-form-ar-desktop.png) | [en-mobile](screenshots/screens/18-org-user-form-en-mobile.png) | [ar-mobile](screenshots/screens/18-org-user-form-ar-mobile.png) |
| 19 | Departments | Organization Administrator | [en-desktop](screenshots/screens/19-org-departments-en-desktop.png) | [ar-desktop](screenshots/screens/19-org-departments-ar-desktop.png) | [en-mobile](screenshots/screens/19-org-departments-en-mobile.png) | [ar-mobile](screenshots/screens/19-org-departments-ar-mobile.png) |
| 20 | Sections | Organization Administrator | [en-desktop](screenshots/screens/20-org-sections-en-desktop.png) | [ar-desktop](screenshots/screens/20-org-sections-ar-desktop.png) | [en-mobile](screenshots/screens/20-org-sections-en-mobile.png) | [ar-mobile](screenshots/screens/20-org-sections-ar-mobile.png) |
| 21 | Roles | Organization Administrator | [en-desktop](screenshots/screens/21-org-roles-en-desktop.png) | [ar-desktop](screenshots/screens/21-org-roles-ar-desktop.png) | [en-mobile](screenshots/screens/21-org-roles-en-mobile.png) | [ar-mobile](screenshots/screens/21-org-roles-ar-mobile.png) |
| 22 | Applications (data sources) | Organization Administrator | [en-desktop](screenshots/screens/22-org-applications-en-desktop.png) | [ar-desktop](screenshots/screens/22-org-applications-ar-desktop.png) | [en-mobile](screenshots/screens/22-org-applications-en-mobile.png) | [ar-mobile](screenshots/screens/22-org-applications-ar-mobile.png) |
| 23 | Metadata approval | Organization Administrator | [en-desktop](screenshots/screens/23-org-metadata-en-desktop.png) | [ar-desktop](screenshots/screens/23-org-metadata-ar-desktop.png) | [en-mobile](screenshots/screens/23-org-metadata-en-mobile.png) | [ar-mobile](screenshots/screens/23-org-metadata-ar-mobile.png) |
| 24 | Permissions | Organization Administrator | [en-desktop](screenshots/screens/24-org-permissions-en-desktop.png) | [ar-desktop](screenshots/screens/24-org-permissions-ar-desktop.png) | [en-mobile](screenshots/screens/24-org-permissions-en-mobile.png) | [ar-mobile](screenshots/screens/24-org-permissions-ar-mobile.png) |
| 25 | Organization Insights | Organization Administrator | [en-desktop](screenshots/screens/25-org-insights-en-desktop.png) | [ar-desktop](screenshots/screens/25-org-insights-ar-desktop.png) | [en-mobile](screenshots/screens/25-org-insights-en-mobile.png) | [ar-mobile](screenshots/screens/25-org-insights-ar-mobile.png) |
| 26 | Support access grants | Organization Administrator | [en-desktop](screenshots/screens/26-org-support-access-en-desktop.png) | [ar-desktop](screenshots/screens/26-org-support-access-ar-desktop.png) | [en-mobile](screenshots/screens/26-org-support-access-en-mobile.png) | [ar-mobile](screenshots/screens/26-org-support-access-ar-mobile.png) |
| 27 | Integrations (Drive / OneDrive deferred) | Organization Administrator | [en-desktop](screenshots/screens/27-org-integrations-en-desktop.png) | [ar-desktop](screenshots/screens/27-org-integrations-ar-desktop.png) | [en-mobile](screenshots/screens/27-org-integrations-en-mobile.png) | [ar-mobile](screenshots/screens/27-org-integrations-ar-mobile.png) |
| 28 | Organization settings and report logo | Organization Administrator | [en-desktop](screenshots/screens/28-org-settings-en-desktop.png) | [ar-desktop](screenshots/screens/28-org-settings-ar-desktop.png) | [en-mobile](screenshots/screens/28-org-settings-en-mobile.png) | [ar-mobile](screenshots/screens/28-org-settings-ar-mobile.png) |
| 29 | Notifications | Organization Administrator | [en-desktop](screenshots/screens/29-org-notifications-en-desktop.png) | [ar-desktop](screenshots/screens/29-org-notifications-ar-desktop.png) | [en-mobile](screenshots/screens/29-org-notifications-en-mobile.png) | [ar-mobile](screenshots/screens/29-org-notifications-ar-mobile.png) |

## Organization user (30–42)

| # | Screen | Signed in as | EN desktop | AR desktop | EN mobile | AR mobile |
|---|---|---|---|---|---|---|
| 30 | User dashboard | Organization user | [en-desktop](screenshots/screens/30-user-dashboard-en-desktop.png) | [ar-desktop](screenshots/screens/30-user-dashboard-ar-desktop.png) | [en-mobile](screenshots/screens/30-user-dashboard-en-mobile.png) | [ar-mobile](screenshots/screens/30-user-dashboard-ar-mobile.png) |
| 31 | Ask Wabi Insight — prompt | Organization user | [en-desktop](screenshots/screens/31-ask-prompt-en-desktop.png) | [ar-desktop](screenshots/screens/31-ask-prompt-ar-desktop.png) | [en-mobile](screenshots/screens/31-ask-prompt-en-mobile.png) | [ar-mobile](screenshots/screens/31-ask-prompt-ar-mobile.png) |
| 32 | Dashboard answer (KPI cards, charts, tables, narrative) | Organization user | [en-desktop](screenshots/screens/32-ask-answer-dashboard-en-desktop.png) | [ar-desktop](screenshots/screens/32-ask-answer-dashboard-ar-desktop.png) | [en-mobile](screenshots/screens/32-ask-answer-dashboard-en-mobile.png) | [ar-mobile](screenshots/screens/32-ask-answer-dashboard-ar-mobile.png) |
| 33 | My History | Organization user | [en-desktop](screenshots/screens/33-my-history-en-desktop.png) | [ar-desktop](screenshots/screens/33-my-history-ar-desktop.png) | [en-mobile](screenshots/screens/33-my-history-en-mobile.png) | [ar-mobile](screenshots/screens/33-my-history-ar-mobile.png) |
| 34 | Analysis detail — study with PDF report and email draft | Organization user | [en-desktop](screenshots/screens/34-analysis-detail-study-en-desktop.png) | [ar-desktop](screenshots/screens/34-analysis-detail-study-ar-desktop.png) | [en-mobile](screenshots/screens/34-analysis-detail-study-en-mobile.png) | [ar-mobile](screenshots/screens/34-analysis-detail-study-ar-mobile.png) |
| 35 | My Reports | Organization user | [en-desktop](screenshots/screens/35-my-reports-en-desktop.png) | [ar-desktop](screenshots/screens/35-my-reports-ar-desktop.png) | [en-mobile](screenshots/screens/35-my-reports-en-mobile.png) | [ar-mobile](screenshots/screens/35-my-reports-ar-mobile.png) |
| 36 | Favorites | Organization user | [en-desktop](screenshots/screens/36-favorites-en-desktop.png) | [ar-desktop](screenshots/screens/36-favorites-ar-desktop.png) | [en-mobile](screenshots/screens/36-favorites-en-mobile.png) | [ar-mobile](screenshots/screens/36-favorites-ar-mobile.png) |
| 37 | Shared With Me | Organization user (share recipient) | [en-desktop](screenshots/screens/37-shared-with-me-en-desktop.png) | [ar-desktop](screenshots/screens/37-shared-with-me-ar-desktop.png) | [en-mobile](screenshots/screens/37-shared-with-me-en-mobile.png) | [ar-mobile](screenshots/screens/37-shared-with-me-ar-mobile.png) |
| 38 | Shared analysis (recipient view) | Organization user (share recipient) | [en-desktop](screenshots/screens/38-shared-analysis-en-desktop.png) | [ar-desktop](screenshots/screens/38-shared-analysis-ar-desktop.png) | [en-mobile](screenshots/screens/38-shared-analysis-en-mobile.png) | [ar-mobile](screenshots/screens/38-shared-analysis-ar-mobile.png) |
| 39 | Shared By Me | Organization user | [en-desktop](screenshots/screens/39-shared-by-me-en-desktop.png) | [ar-desktop](screenshots/screens/39-shared-by-me-ar-desktop.png) | [en-mobile](screenshots/screens/39-shared-by-me-en-mobile.png) | [ar-mobile](screenshots/screens/39-shared-by-me-ar-mobile.png) |
| 40 | New support ticket | Organization user | [en-desktop](screenshots/screens/40-support-new-ticket-en-desktop.png) | [ar-desktop](screenshots/screens/40-support-new-ticket-ar-desktop.png) | [en-mobile](screenshots/screens/40-support-new-ticket-en-mobile.png) | [ar-mobile](screenshots/screens/40-support-new-ticket-ar-mobile.png) |
| 41 | Support ticket detail | Organization user | [en-desktop](screenshots/screens/41-support-ticket-detail-en-desktop.png) | [ar-desktop](screenshots/screens/41-support-ticket-detail-ar-desktop.png) | [en-mobile](screenshots/screens/41-support-ticket-detail-en-mobile.png) | [ar-mobile](screenshots/screens/41-support-ticket-detail-ar-mobile.png) |
| 42 | Profile | Organization user | [en-desktop](screenshots/screens/42-profile-en-desktop.png) | [ar-desktop](screenshots/screens/42-profile-ar-desktop.png) | [en-mobile](screenshots/screens/42-profile-en-mobile.png) | [ar-mobile](screenshots/screens/42-profile-ar-mobile.png) |

## Product walkthrough videos (P13.12, P13.13)

[`docs/demo/wabi-insight-walkthrough-en.mp4`](demo/wabi-insight-walkthrough-en.mp4) — 8 min 37 s, English, recorded from the running app with the Wabi Bank sample and the real model, with subtitles ([`.srt`](demo/wabi-insight-walkthrough-en.srt)) and chapters; the narration script with timecodes is [`wabi-insight-walkthrough-en.md`](demo/wabi-insight-walkthrough-en.md). Generated by `scripts/demo/walkthrough` (see its README).

Arabic version (P13.13): [`docs/demo/wabi-insight-walkthrough-ar.mp4`](demo/wabi-insight-walkthrough-ar.mp4) — 8 min 56 s, Arabic interface and narration, with Arabic subtitles ([`.srt`](demo/wabi-insight-walkthrough-ar.srt)), chapters and the timecoded script [`wabi-insight-walkthrough-ar.md`](demo/wabi-insight-walkthrough-ar.md).

## Wabi Bank (P13.6)

Recorded demo from the running app with the second sample organization (`scripts/sql/samples/wabi-bank`) and the real model: [`docs/demo/p13-6-wabi-bank.gif`](demo/p13-6-wabi-bank.gif) — the Deira branch manager sees only Deira, a Deira teller only their own postings (both answers say so), and the AML officer opens the structuring alert (script and story: `scripts/demo/wabi-bank/README.md`).

## Phase 14 — Proactive agent

Captured from the running app with the sample organization Gulf Aviation Services (`scripts/sql/samples`); drafts in P14.4 come from the real model. Recorded demo: [`docs/demo/p14-5-proactive-agent-alert.gif`](demo/p14-5-proactive-agent-alert.gif) — an analyst looks up six colleagues' pay, row scope shows him none of it, the access rule raises a High alert and the administrator acknowledges it (script and story: `scripts/demo/proactive-agent/README.md`).

### P14.2 — Monitoring Rules and Alerts

| Screen | Signed in as | Files |
|---|---|---|
| Monitoring Rules list | Organization Administrator | [p14-2-rules-en](screenshots/p14-2-rules-en.png) · [p14-2-rules-ar](screenshots/p14-2-rules-ar.png) |
| Template picker (field roles, unavailable templates greyed) | Organization Administrator | [p14-2-templates-en](screenshots/p14-2-templates-en.png) · [p14-2-templates-ar](screenshots/p14-2-templates-ar.png) |
| Rule editor and test of an unsaved draft | Organization Administrator | [p14-2-rule-editor-en](screenshots/p14-2-rule-editor-en.png) · [p14-2-rule-draft-test-en](screenshots/p14-2-rule-draft-test-en.png) |
| Test run of the seeded "same account, several devices" rule | Organization Administrator | [p14-2-rule-test-en](screenshots/p14-2-rule-test-en.png) |
| Monitoring settings (digest time, working hours, retention) | Organization Administrator | [p14-2-monitoring-settings-en](screenshots/p14-2-monitoring-settings-en.png) |
| Alert centre with a data alert | Rule owner | [p14-2-alerts-en](screenshots/p14-2-alerts-en.png) · [p14-2-alerts-ar](screenshots/p14-2-alerts-ar.png) |
| Redacted alert (recipient without permission on the data) | Recipient | [p14-2-alerts-redacted-en](screenshots/p14-2-alerts-redacted-en.png) |
| Suspension confirmation (human decision) | Organization Administrator | [p14-2-alert-suspend-confirm-en](screenshots/p14-2-alert-suspend-confirm-en.png) |
| Daily digest preview and emails as received | Recipients | [p14-2-digest-preview-en](screenshots/p14-2-digest-preview-en.png) · [p14-2-digest-email-en](screenshots/p14-2-digest-email-en.png) · [p14-2-digest-email-ar](screenshots/p14-2-digest-email-ar.png) |
| Field roles in Metadata | Organization Administrator | [p14-2-metadata-field-roles-en](screenshots/p14-2-metadata-field-roles-en.png) |
| Alert centre and detail on a phone | Rule owner | [p14-2-alerts-mobile-ar](screenshots/p14-2-alerts-mobile-ar.png) · [p14-2-alert-detail-mobile-ar](screenshots/p14-2-alert-detail-mobile-ar.png) |

### P14.3 — Periodic Reports

| Screen | Signed in as | Files |
|---|---|---|
| Periodic report editor (monthly, four recipients) | Organization Administrator | [p14-3-periodic-editor-en](screenshots/p14-3-periodic-editor-en.png) |
| "Send now" with per-recipient results | Organization Administrator | [p14-3-periodic-run-en](screenshots/p14-3-periodic-run-en.png) |
| Delivery log | Organization Administrator | [p14-3-periodic-log-en](screenshots/p14-3-periodic-log-en.png) |
| Periodic reports list | Organization Administrator | [p14-3-periodic-reports-ar](screenshots/p14-3-periodic-reports-ar.png) · [p14-3-periodic-mobile-en](screenshots/p14-3-periodic-mobile-en.png) |
| Management's self-only editor | Management | [p14-3-periodic-management-en](screenshots/p14-3-periodic-management-en.png) |
| "Sent to me" in My Reports | Recipient | [p14-3-sent-to-me-en](screenshots/p14-3-sent-to-me-en.png) · [p14-3-sent-to-me-ar](screenshots/p14-3-sent-to-me-ar.png) |
| Delivered and "no data for your permissions" emails | Recipients | [p14-3-email-delivered-en](screenshots/p14-3-email-delivered-en.png) · [p14-3-email-no-data-en](screenshots/p14-3-email-no-data-en.png) |
| First PDF page per recipient (HR only vs all departments) | Recipients | [p14-3-pdf-sara-hr-only-en](screenshots/p14-3-pdf-sara-hr-only-en.png) · [p14-3-pdf-khalid-all-departments-ar](screenshots/p14-3-pdf-khalid-all-departments-ar.png) |

### P14.4 — Natural-language and system rules

| Screen | Signed in as | Files |
|---|---|---|
| Describe a rule in Arabic: proposed rule, nothing saved yet | Management | [p14-4-draft-ar](screenshots/p14-4-draft-ar.png) · [p14-4-draft-ar-mobile](screenshots/p14-4-draft-ar-mobile.png) |
| Describe a rule in English (with a filter) | Organization Administrator | [p14-4-draft-en](screenshots/p14-4-draft-en.png) |
| Review and save the draft (columns fixed) | Organization Administrator | [p14-4-draft-review-en](screenshots/p14-4-draft-review-en.png) |
| System templates | Organization Administrator | [p14-4-templates-en](screenshots/p14-4-templates-en.png) |
| Monitoring settings with the monthly AI budget | Organization Administrator | [p14-4-settings-budget-en](screenshots/p14-4-settings-budget-en.png) |
| Rules list with system rules and a paused rule | Organization Administrator | [p14-4-rules-en](screenshots/p14-4-rules-en.png) |
| AI budget alert | Organization Administrator | [p14-4-ai-budget-alert-en](screenshots/p14-4-ai-budget-alert-en.png) |
| Rule paused after a column was un-approved | Organization Administrator | [p14-4-rule-paused-alert-en](screenshots/p14-4-rule-paused-alert-en.png) |

## P13.7 — Answers that state their access

Captured from the running app with the Wabi Bank sample organization (`scripts/sql/samples/wabi-bank`) and the real model.

| Screen | Signed in as | Files |
|---|---|---|
| Teller's answer limited to their own postings (`SELF`) | Teller, Deira | [p13-7-answer-access-self-en](screenshots/p13-7-answer-access-self-en.png) |
| Branch manager's answer limited to the branch (`DEPARTMENT`), Arabic | Branch Manager, Deira | [p13-7-answer-access-department-ar](screenshots/p13-7-answer-access-department-ar.png) · [p13-7-answer-access-department-ar-mobile](screenshots/p13-7-answer-access-department-ar-mobile.png) |

## P13.8 — Totals, column comparisons and "my department"

Captured from the running app with the Wabi Bank sample organization and the real model.

| Screen | Signed in as | Files |
|---|---|---|
| Condition on a total: accounts with more than two returned cheques ("Only groups where … is more than 2") | Compliance and AML Officer | [p13-8-totals-filter-en](screenshots/p13-8-totals-filter-en.png) |
| "My department" in Arabic: the asker's department code is bound by the server ("… يساوي إدارتك") | IT Service Desk Manager | [p13-8-my-department-ar](screenshots/p13-8-my-department-ar.png) |
| Access note with the neutral wording "your department" (mobile) | Branch Manager, Deira | [p13-8-access-note-department-en-mobile](screenshots/p13-8-access-note-department-en-mobile.png) |

## P13.9 — Example questions and readable numbers

Captured from the running app with the Wabi Bank sample organization.

| Screen | Signed in as | Files |
|---|---|---|
| Example questions editor (English and Arabic per row, at most six) | Organization Administrator | [p13-9-admin-examples-en](screenshots/p13-9-admin-examples-en.png) |
| Ask page: the application's own examples; a click fills the question | Compliance and AML Officer | [p13-9-ask-examples-en](screenshots/p13-9-ask-examples-en.png) |
| Ask page in Arabic on a phone: long examples wrap | HR officer | [p13-9-ask-examples-ar-mobile](screenshots/p13-9-ask-examples-ar-mobile.png) |
| Alert detail with formatted value and threshold (58,000 / 55,000), English and Arabic digits | Compliance and AML Officer | [p13-9-alert-numbers-en](screenshots/p13-9-alert-numbers-en.png) · [p13-9-alert-numbers-ar](screenshots/p13-9-alert-numbers-ar.png) |

## P13.10 — Complete explanations, Arabic dates and numbers

Captured from the running app with the Wabi Bank sample organization and the real model.

| Screen | Signed in as | Files |
|---|---|---|
| The two-panel budget question whose explanation used to come back blank, now explained | Budget controller | [p13-10-explanation-en](screenshots/p13-10-explanation-en.png) |
| Alerts in Arabic: counts, occurrences, dates and the badge in Arabic digits and month names | Compliance and AML Officer | [p13-10-alerts-ar](screenshots/p13-10-alerts-ar.png) |
| My History in Arabic: run dates, versions and costs | HR officer | [p13-10-history-ar](screenshots/p13-10-history-ar.png) |

## P13.11 — Plain-language explanation, compact language switch, generic sample names

Captured from the running app with the Wabi Bank sample organization (now with generic names) and the real model. The Wabi Bank screenshots of P13.7–P13.10 and the P13.6 demo were re-captured with the new names.

| Screen | Signed in as | Files |
|---|---|---|
| *How this was computed* in words: sources, totals, grouping, the row and group conditions, sort and row cap (no SQL) | Compliance and AML Officer | [p13-11-how-en](screenshots/p13-11-how-en.png) |
| The same in Arabic, with Arabic dates and numbers | IT Service Desk Manager | [p13-11-how-ar](screenshots/p13-11-how-ar.png) |
| Arabic on a phone: the list stacks | Branch Manager, Deira | [p13-11-how-ar-mobile](screenshots/p13-11-how-ar-mobile.png) |
| Globe-only language switch on a phone, aligned with the bell and account icons, English and Arabic | Teller, Deira | [p13-11-switcher-en-mobile](screenshots/p13-11-switcher-en-mobile.png) · [p13-11-switcher-ar-mobile](screenshots/p13-11-switcher-ar-mobile.png) |

## P13.32 — Self-service password reset

Captured from the running application with the demo organization (e-mails written by the File provider and rendered in the browser).

| What it shows | English | Arabic |
|---|---|---|
| "Forgot password?" on the sign-in page | [p13-32-login-forgot-en](screenshots/p13-32-login-forgot-en.png) | |
| Asking for a link | [p13-32-forgot-password-en](screenshots/p13-32-forgot-password-en.png) | [p13-32-forgot-password-ar](screenshots/p13-32-forgot-password-ar.png) |
| The same confirmation for every address | [p13-32-forgot-password-sent-en](screenshots/p13-32-forgot-password-sent-en.png) | [p13-32-forgot-password-sent-ar](screenshots/p13-32-forgot-password-sent-ar.png) |
| The e-mail: a link, never a password | [p13-32-reset-email-en](screenshots/p13-32-reset-email-en.png) | [p13-32-reset-email-ar](screenshots/p13-32-reset-email-ar.png) |
| The page the link opens: the new password, twice | [p13-32-reset-password-en](screenshots/p13-32-reset-password-en.png) · [phone](screenshots/p13-32-reset-password-mobile-en.png) | [p13-32-reset-password-ar](screenshots/p13-32-reset-password-ar.png) |
| Password changed | [p13-32-reset-password-done-en](screenshots/p13-32-reset-password-done-en.png) | |
| Opened without a link, or with a used or expired one: no form | [p13-32-reset-password-invalid-en](screenshots/p13-32-reset-password-invalid-en.png) | [p13-32-reset-password-invalid-ar](screenshots/p13-32-reset-password-invalid-ar.png) |

## Feature screenshots by PR

Earlier PRs keep their own captures under `docs/screenshots/p<phase>-<n>-*.png` (listed per PR in `docs/DESIGN_SYSTEM.md`), the recorded demo under `docs/demo/`, and the brand sheet at `docs/screenshots/p11-1-brand-sheet.png`.
