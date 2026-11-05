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

## Translations
Transloco with `public/i18n/en.json` and `public/i18n/ar.json`. Keys are grouped by area (`common`, `landing`, `login`, `changePassword`, `bootstrap`, `nav`, `ask`, `platform`, `platformDashboard`, `health`, `audit`, `platformAdmins`, `organizations`, `admin`). Status codes and error codes from the API are mapped to keys (`login.errors.<code>`, `changePassword.errors.<code>`); codes themselves are never shown.

## Screenshots (P1.4)
`docs/screenshots/p1-4-*.png`: landing EN/AR (full page) and mobile, login EN/AR and mobile, bootstrap, change password, Ask workspace EN/AR, platform overview, platform placeholder. Demo recording: `docs/demo/p1-4-landing-login-ask.gif`.

## Screenshots (P3.2)
`docs/screenshots/p3-2-*.png`: platform dashboard EN/AR (full page), System Health, Audit Logs EN/AR, Platform Administrators list and create form.
