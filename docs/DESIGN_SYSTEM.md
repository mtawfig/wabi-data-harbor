# Wabi Insight — Design System

Status: P1.4 foundation. P11 completes the logo set, marketing assets and cross-page refinement.

## Identity
| Token | Value | Use |
|---|---|---|
| `--wi-navy` | `#0B1F3A` | Primary text, platform header, hero gradient start |
| `--wi-ocean` | `#087DF2` | Primary actions, links, active navigation |
| `--wi-aqua` | `#22B8CF` | Accents, success-like highlights, gradient end |
| `--wi-cloud` | `#F5F8FC` | Page background |
| `--wi-white` | `#FFFFFF` | Cards, header (organization shell) |
| `--wi-slate` | `#334155` | Secondary text |
| `--wi-border` | `#E2E8F0` | Borders, dividers |

Dimensions: header 76 px (`--wi-header-height`), sidebar 248 px (`--wi-sidebar-width`), card radius 14 px, hero heading 36–56 px (clamped), page heading 28 px, navigation 15 px, body 15 px.

Interim logo: `BrandLogoComponent` (lighthouse over two data waves, dark and light variants). The final SVG set arrives in P11.1.

## Typography and direction
- English: Inter (self-hosted via `@fontsource/inter`, weights 400–700), LTR.
- Arabic: IBM Plex Sans Arabic (self-hosted via `@fontsource/ibm-plex-sans-arabic`), RTL. `html[lang='ar']` swaps `--wi-font`.
- `LanguageService` sets `<html lang dir>` and persists the choice (`wi.lang`); after sign-in the account's preferred language applies. The root component wraps the router outlet in the CDK `dir` directive so Material layouts re-flow on runtime switches.
- Layout uses logical properties (`margin-inline-start`, `inset-inline-start`) so no per-direction overrides are needed. Icons that imply direction get `.wi-flip-rtl`.

## Components
- Angular Material 22 (M3) with `mat.theme` (azure/cyan palettes) and brand overrides of the key `--mat-sys-*` tokens in `src/styles.scss`. Icons: Material Icons font (self-hosted `material-icons` package).
- `AppShellComponent`: shared authenticated shell (header, collapsible sidebar, user menu, language switcher). Organization shell uses the white header; platform shell uses the navy header with light button tokens.
- `LanguageSwitcherComponent`, `BrandLogoComponent`, `PagePlaceholderComponent` (honest "coming in phase N" page driven by route data).
- Auth layouts (login, bootstrap, change password) share `login.component.scss`: gradient aside + card.
- Utility classes: `.wi-card`, `.wi-page`, `.wi-page__header`, `.wi-eyebrow`, `.wi-muted`, `.wi-gradient`, `.wi-ms-auto`.

## Charts (P3.2)
- ECharts 6 through `ngx-echarts` (`provideEchartsCore` with a lazy `import('echarts')` so the chart runtime loads only with the dashboard chunk).
- `shared/ui/chart-theme.ts` holds the palette (`CHART_COLORS`: ocean, aqua, navy, amber, red, grey) and `baseChartOption(rtl)`: brand fonts, navy tooltips, light grid lines, animation off (deterministic screenshots), and in RTL the category axis is inverted and the value axis moves to the right so time still reads in the writing direction.
- KPI cards (`.kpi`) show counts in ocean; failure counters (failed sign-ins, email failures, API errors) in amber. Metrics the backend reports as `collected = false` are never drawn as zeros; they appear as chips under “Metrics arriving with later phases”.

## Support desk (P4.2)
- `features/support/` is shared by both areas: every page takes an `area` input (`app` | `platform`) from route data and calls the matching API prefix. Chips (`app-ticket-status`, `app-ticket-impact`, `app-ticket-type`) carry stable colours per code; the platform reads "Waiting for user" where the requester reads "Waiting for you".
- Ticket details use `mat-tab-group` inside a `.wi-card` with zero padding; the Activity Timeline is a connected vertical list (`.timeline` with an inline-start rule and circular icon nodes) that mirrors correctly in RTL through logical properties.
- The shell header shows a notification bell with a `matBadge` unread count (red), refreshed on load and every minute; clicking opens the area's notification centre.

## Translations
Transloco with `public/i18n/en.json` and `public/i18n/ar.json`. Keys are grouped by area (`common`, `landing`, `login`, `changePassword`, `bootstrap`, `nav`, `ask`, `platform`, `platformDashboard`, `health`, `audit`, `platformAdmins`, `organizations`, `admin`, `support`, `notifications`). Status codes and error codes from the API are mapped to keys (`login.errors.<code>`, `changePassword.errors.<code>`); codes themselves are never shown.

## Screenshots (P1.4)
`docs/screenshots/p1-4-*.png`: landing EN/AR (full page) and mobile, login EN/AR and mobile, bootstrap, change password, Ask workspace EN/AR, platform overview, platform placeholder. Demo recording: `docs/demo/p1-4-landing-login-ask.gif`.

## Screenshots (P3.2)
`docs/screenshots/p3-2-*.png`: platform dashboard EN/AR (full page), System Health, Audit Logs EN/AR, Platform Administrators list and create form.

## Support access (P5.2)
- `app-support-access-tab` is ticket tab 5, shown only when the ticket is flagged as needing support access. The same component serves both areas: the platform side gets the proposal form and the green "Run a permitted operation" panel while its session is active; the organization side gets approve / decline / revoke. Grant statuses use `app-grant-status` chips (amber awaiting approval, blue approved, green in support mode, red declined/revoked).
- `app-support-mode-banner` sits under the platform header (amber band) while a session is active, with the organization, ticket reference, remaining minutes and an explicit exit button. It is driven by the `session` signal of `SupportAccessService`, refreshed on load, every minute and after every grant action.

## Screenshots (P4.2)
`docs/screenshots/p4-2-*.png`: new ticket, My Tickets, platform queue, ticket overview (requester and platform), conversation, timeline EN/AR, attachments AR, notification centre (platform EN, organization AR), user dashboard widget AR, platform dashboard with ticket KPIs, chart and open-tickets widget.

## Screenshots (P5.2)
`docs/screenshots/p5-2-*.png`: proposal form (platform), proposed grant, organization approval list, pending grant on the organization side, approved grant AR, platform Support Access page, support mode with the operation panel before and after a run, support-mode banner EN/AR, timeline with grant events.

## Screenshots (P6.1)
`docs/screenshots/p6-1-*.png`: new application form with a readable connection error, applications list EN/AR (configured connection with hint, last test, metadata counts, approval), metadata after refresh, object detail with columns, classification chips and approval EN/AR.

## Permissions (P6.2)
- `app-permissions` under Organization Administration: a role and application selector above a plain table (object, access chip, columns summary, row-scope chip with an amber warning when the scope has no mapping, mapping availability). Clicking a row opens the sticky detail card: grant checkbox, row scope with inline hints, *all approved columns* or an explicit column picker (classification chips on sensitive columns), and the shared row-scope mapping editor. `Management` and `OrganizationAdmin` show an informational band instead of editable grants.
- The *Preview as user* tab lists what one user can query after combining all roles: roles and row-scope context, then one block per object with scope chips (`All rows`, `Department`, …), unmapped warnings, a *no rows* chip and the column list.
- Scope chips use purple (All), blue (Department), teal (Section) and slate (Self) so the same scope is recognizable in the matrix, the detail card, the preview and the support-mode run form.

## Screenshots (P6.2)
`docs/screenshots/p6-2-*.png`: matrix for a custom role, grant with selected columns and an unmapped Department scope warning, the same grant after mapping, matrix with two grants, Management (automatic), preview as user EN/AR, grant editor AR, and the platform-side support-mode run form for `UpdateTablePermissions` before and after execution.

## Platform Insights Configuration (P7.1)
- `app-insights-config` under the platform shell: a four-cell source status card (views reachable, dedicated login, confinement to the insights schema, last check) with green/amber/red icons, then two tabs. *Views and columns* lists the `insights` views with EN/AR names and approved column counts; the sticky detail card edits names, description, order and approval, and lists columns with approval checkboxes and inline label editing. *Metrics* lists definitions as code, names, an aggregate chip plus column/view/date column and filter summary, with approval checkboxes and edit/delete; the metric form adds fixed filters (column, operator, comma-separated values) and a dark *Preview SQL* block that shows the compiled statement with `@tenant`.

## Screenshots (P7.1)
`docs/screenshots/p7-1-*.png`: views list EN, view detail with columns EN/AR, metrics list EN/AR, metric editor with SQL preview.

## Organization dashboard and Organization Insights (P7.2)
- `app-organization-dashboard` reuses the platform dashboard layout (presets, granularity, KPI grid, four charts, pending-indicator chips) with an *Organization Insights* shortcut; tables for usage by department, tickets by status and applications, and the open-tickets widget.
- `app-organization-insights`: metric list on the left (name plus aggregate · column · view), controls card (period presets including *All time*, granularity with *Total*, group by approved columns, *Run*), result card with the total in brand blue, a line (bucketed), bar (grouped) or stacked bar (both) chart and the rows table.
- Permissions page tab *Platform Insights metrics*: checklist of approved metrics per role; the Organization Administrator role shows an informational band instead.

## Screenshots (P7.2)
`docs/screenshots/p7-2-*.png`: organization dashboard EN (viewport and full page) and AR, Organization Insights with grouping EN and AR, metric grants tab.

## AI providers (P8.1)
- Platform *AI Providers* page: provider cards (name with *Platform default* / *Not approved* chips, type · model · endpoint, key status with hint, prices, effort and token cap, last test with green/red icon), actions *Test*, *Make default*, edit, delete; a shared create/edit form (`app-ai-provider-form`, write-only key field with a keep-hint on edit); the allow-list checkboxes below.
- Organization Settings gains an *AI provider* card: a select (platform default, approved platform providers, own providers) and the organization's own providers with test/edit/delete using the same form restricted to allow-listed types.

## Screenshots (P8.1)
`docs/screenshots/p8-1-*.png`: platform providers list after a test EN/AR, provider form, organization AI settings with the own-provider form EN and the saved state EN/AR.

## Ask Wabi Insight (P8.2)
- `app-ask`: composer card (application selector showing the permitted-object count, answer language, question with Ctrl+Enter, example chips) with a progress bar and a status line while planning, running and interpreting.
- Answer card: objective as the heading, narrative, highlight list with green check icons, an amber truncation band, a large KPI value for single-number answers, an ECharts bar/line/pie from the plan's visualization hint, a sticky-header rows table, and the *How this was computed* expansion panel (assumptions, dark redacted SQL block, model · tokens · cost · time · rows).
- Rejections and execution failures are red bands with the stage and localized reason codes.

## Screenshots (P8.2)
`docs/screenshots/p8-2-*.png`: empty Ask page, a live answer from Claude Opus 5.5 over the demo ERP (chart, rows, narrative), the *How this was computed* panel, an Arabic answer, and the outcome of an injection-style question.

## Dashboard answers (P8.3)
- An answer is one or more panels. The answer card keeps the narrative and highlight list on top, then a note with the number of read-only queries that ran, then: a row of KPI cards (`auto-fit`, minimum 180 px, 34 px ocean value, muted label = panel title), a two-column chart grid (one column under 900 px; a single chart takes the full width; a second category column becomes grouped bars or several lines with a bottom legend; month-truncated dates are shown as `YYYY-MM`), and the detail tables with the panel title and row count (column aliases rendered as words). A single-panel answer keeps the P8.2 layout (chart plus rows).
- *How this was computed* lists the assumptions once and one redacted SQL block per query, titled with the panel name.

## Report and email cards (P8.3)
- When the answer includes a study, a *Report (PDF)* card precedes the answer: eyebrow, report title and subtitle, a filled *Download PDF* button, file name · size · section count, and a two-column table of contents (summary, sections, recommendations). An *Email draft* card follows: subject as heading, a *Copy email* outline button (turns into *Copied* for a moment), the body in a soft grey block with preserved paragraphs, and an attachment pill with the PDF file name. Both cards carry a 4 px ocean start border.
- The PDF itself (QuestPDF, A4): logo top-left of every page (or the organization name when none is uploaded), report title and "Confidential" on the right, title block, light-blue executive summary box, key findings, numbered sections with KPI boxes, SVG charts (brand palette) and zebra tables, numbered recommendations, methodology appendix, and a footer with generation time and "Page x / y". Arabic reports flip to right-to-left with IBM Plex Sans Arabic.
- Organization Settings gains a *Report logo* card: preview, *Upload logo* (PNG, JPEG, SVG ≤ 1 MB) and *Remove*.

## My History and Favorites (P9.1)
- `app-history-list`: filters row (search, application, type, from/to dates, *Show archived* toggle), a Material table (star toggle + title and prompt excerpt, application, type chip with a PDF marker and a *Last run failed* chip, version and cost, last run, a row menu with Open / Add to collection / Archive / Delete) with the paginator, and a *Collections* sidebar (list with counts, inline create, delete). The same component serves *Favorites* with the favorite filter pinned.
- `app-history-detail`: back link, editable title, type chip and metadata line, action bar (star, Collections menu with check boxes, Archive, Delete, filled *Re-run*), the original question card with the "stored result" note, the stored answer rendered by `app-analysis-answer` (identical to the Ask page), and a *Versions* sidebar (active version highlighted, failed versions in red, PDF marker).
- `app-analysis-answer` is the shared renderer of an answer (report and email cards, narrative, KPI cards, charts, tables, *How this was computed*) used by the Ask page and by history; the Ask page adds a "Saved to My History · Open" line under a fresh answer.

## Screenshots (P9.1)
`docs/screenshots/p9-1-*.png`: My History list, an analysis reopened from history with its versions, Favorites, and the Arabic history list.

## Screenshots (P8.3)
`docs/screenshots/p8-3-*.png`: the dashboard prompt typed on the Ask page, the live dashboard answer from Claude Opus 5.5 over the Sharjah Airport 2025 demo source (KPI cards, four charts, two tables, narrative), the full page, and the *How this was computed* panel with the ten queries. `docs/screenshots/p8-4-*.png`: the study prompt, the report and email draft cards, and the full page; `docs/demo/p8-4-ask-study-report-email.gif` records the prompt being typed, the wait, and the rendered email draft and report card.
