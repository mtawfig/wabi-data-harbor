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
