#!/usr/bin/env node
/* eslint-disable */
/**
 * Wabi Insight — the 42 required screens (specification §32), captured from the running application in
 * English and Arabic at desktop (1440×960) and mobile (390×844) widths. Nothing is mocked: the script signs in
 * with real demo accounts against the running API and Angular dev server.
 *
 * Usage (API on :5080, web on :4200, demo data seeded — see docs/SCREENSHOTS.md):
 *   node scripts/screenshots/capture.js [--only 31,32] [--lang en] [--device desktop] [--out docs/screenshots/screens]
 *
 * Environment: WI_BASE_URL, WI_PLATFORM_EMAIL/PASSWORD, WI_ORGADMIN_EMAIL/PASSWORD, WI_USER_EMAIL/PASSWORD,
 * WI_RECIPIENT_EMAIL/PASSWORD (a colleague who received a share), WI_HISTORY_ID (a saved study with a PDF),
 * WI_ORGANIZATION_ID, WI_TICKET_ID.
 */
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const BASE = process.env.WI_BASE_URL || 'http://localhost:4200';
const OUT = path.resolve(opt('--out', 'docs/screenshots/screens'));
const ONLY = opt('--only', '') ? opt('--only', '').split(',').map(Number) : null;
const LANGS = opt('--lang', 'en,ar').split(',');
const DEVICES = opt('--device', 'desktop,mobile').split(',');
const env = (k, d) => process.env[k] || d;
const ACCOUNTS = {
  platform: { email: env('WI_PLATFORM_EMAIL', 'support@wabitex.com'), password: env('WI_PLATFORM_PASSWORD', 'Operator#Strong#Secret#2026') },
  orgAdmin: { email: env('WI_ORGADMIN_EMAIL', 'maryam.admin@acme.example'), password: env('WI_ORGADMIN_PASSWORD', 'Maryam#Strong#Pass#2026') },
  user: { email: env('WI_USER_EMAIL', 'omar.hassan@acme.example'), password: env('WI_USER_PASSWORD', 'Omar#Strong#Pass#2026') },
  recipient: { email: env('WI_RECIPIENT_EMAIL', 'huda.salem@acme.example'), password: env('WI_RECIPIENT_PASSWORD', 'Huda#Strong#Pass#2026') },
};
const IDS = { history: env('WI_HISTORY_ID', ''), organization: env('WI_ORGANIZATION_ID', ''), ticket: env('WI_TICKET_ID', '') };
const VIEWPORTS = { desktop: { viewport: { width: 1440, height: 960 }, deviceScaleFactor: 2 }, mobile: { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true } };

/** The 42 screens: number, slug, who signs in, the URL (or a function), optional extra steps before the shot. */
const SCREENS = [
  // Public (1–4)
  { n: 1, slug: 'landing', role: null, url: '/', wait: '[data-testid="landing-hero"]', fullPage: true },
  { n: 2, slug: 'login', role: null, url: '/login', wait: '[data-testid="login-email"]' },
  { n: 3, slug: 'bootstrap', role: null, url: '/bootstrap', wait: 'form, .wi-card' },
  { n: 4, slug: 'change-password', role: 'user', url: '/change-password', wait: 'form' },
  // Platform administrator (5–15)
  { n: 5, slug: 'platform-overview', role: 'platform', url: '/platform/overview', wait: '.wi-page' },
  { n: 6, slug: 'platform-organizations', role: 'platform', url: '/platform/organizations', wait: 'table, .wi-card' },
  { n: 7, slug: 'platform-organization-create', role: 'platform', url: '/platform/organizations/new', wait: 'form' },
  { n: 8, slug: 'platform-organization-detail', role: 'platform', url: () => `/platform/organizations/${IDS.organization}`, wait: '.wi-page h1' },
  { n: 9, slug: 'platform-administrators', role: 'platform', url: '/platform/administrators', wait: 'table, .wi-card' },
  { n: 10, slug: 'platform-role-templates', role: 'platform', url: '/platform/role-templates', wait: '.wi-card' },
  { n: 11, slug: 'platform-ai-providers', role: 'platform', url: '/platform/ai-providers', wait: '.wi-card' },
  { n: 12, slug: 'platform-insights-config', role: 'platform', url: '/platform/insights', wait: '.wi-card' },
  { n: 13, slug: 'platform-support-desk', role: 'platform', url: '/platform/support', wait: 'table, .wi-card' },
  { n: 14, slug: 'platform-health', role: 'platform', url: '/platform/health', wait: '.wi-card' },
  { n: 15, slug: 'platform-audit', role: 'platform', url: '/platform/audit', wait: 'table, .wi-card' },
  // Organization administrator (16–29)
  { n: 16, slug: 'org-admin-dashboard', role: 'orgAdmin', url: '/app/admin/dashboard', wait: '.wi-card' },
  { n: 17, slug: 'org-users', role: 'orgAdmin', url: '/app/admin/users', wait: 'table' },
  { n: 18, slug: 'org-user-form', role: 'orgAdmin', url: '/app/admin/users/new', wait: '[data-testid="user-form"]' },
  { n: 19, slug: 'org-departments', role: 'orgAdmin', url: '/app/admin/departments', wait: '.wi-card' },
  { n: 20, slug: 'org-sections', role: 'orgAdmin', url: '/app/admin/sections', wait: '.wi-card' },
  { n: 21, slug: 'org-roles', role: 'orgAdmin', url: '/app/admin/roles', wait: '.wi-card' },
  { n: 22, slug: 'org-applications', role: 'orgAdmin', url: '/app/admin/applications', wait: '.wi-card' },
  { n: 23, slug: 'org-metadata', role: 'orgAdmin', url: '/app/admin/metadata', wait: '.wi-card' },
  { n: 24, slug: 'org-permissions', role: 'orgAdmin', url: '/app/admin/permissions', wait: '.wi-card' },
  { n: 25, slug: 'org-insights', role: 'orgAdmin', url: '/app/admin/insights', wait: '.wi-card' },
  { n: 26, slug: 'org-support-access', role: 'orgAdmin', url: '/app/admin/support-access', wait: '.wi-card' },
  { n: 27, slug: 'org-integrations', role: 'orgAdmin', url: '/app/admin/integrations', wait: '[data-testid="integration-drive"]' },
  { n: 28, slug: 'org-settings', role: 'orgAdmin', url: '/app/admin/settings', wait: 'form, .wi-card' },
  { n: 29, slug: 'org-notifications', role: 'orgAdmin', url: '/app/notifications', wait: '.wi-card' },
  // Organization user (30–42)
  { n: 30, slug: 'user-dashboard', role: 'user', url: '/app/dashboard', wait: '[data-testid="dashboard-recent"]' },
  { n: 31, slug: 'ask-prompt', role: 'user', url: '/app/ask', wait: '[data-testid="ask-prompt"]', steps: async (page, lang) => {
      await page.getByTestId('ask-prompt').fill(lang === 'ar' ? 'اعرض لوحة معلومات للرحلات المغادرة من الشارقة في 2025: الإجمالي، الاتجاه الشهري، وتوزيع حسب شركة الطيران' : 'Show a dashboard of departures from Sharjah in 2025: total, monthly trend and breakdown by airline');
    } },
  { n: 32, slug: 'ask-answer-dashboard', role: 'user', url: '/app/history', wait: '[data-testid="history-table"] tr[mat-row]', steps: async (page) => {
      await page.locator('[data-testid="history-table"] tr[mat-row]').filter({ hasText: /dashboard|لوحة/i }).first().click();
      await page.waitForSelector('[data-testid="ask-answer"]', { timeout: 60000 });
      await page.waitForTimeout(1500);
    }, fullPage: true },
  { n: 33, slug: 'my-history', role: 'user', url: '/app/history', wait: '[data-testid="history-table"] tr[mat-row]' },
  { n: 34, slug: 'analysis-detail-study', role: 'user', url: () => `/app/history/${IDS.history}`, wait: '[data-testid="ask-answer"]' },
  { n: 35, slug: 'my-reports', role: 'user', url: '/app/reports', wait: '[data-testid="reports-table"]' },
  { n: 36, slug: 'favorites', role: 'user', url: '/app/favorites', wait: '[data-testid="history-table"]' },
  { n: 37, slug: 'shared-with-me', role: 'recipient', url: '/app/shared-with-me', wait: '[data-testid="shared-with-me-table"] tr[mat-row]' },
  { n: 38, slug: 'shared-analysis', role: 'recipient', url: () => `/app/shared-with-me/${IDS.history}`, wait: '[data-testid="ask-answer"]' },
  { n: 39, slug: 'shared-by-me', role: 'user', url: '/app/shared-by-me', wait: '[data-testid="shared-by-me-table"] tr[mat-row]' },
  { n: 40, slug: 'support-new-ticket', role: 'user', url: '/app/support/new', wait: '[data-testid="ticket-form"]' },
  { n: 41, slug: 'support-ticket-detail', role: 'user', url: () => `/app/support/${IDS.ticket}`, wait: '.wi-page h1' },
  { n: 42, slug: 'profile', role: 'user', url: '/app/profile', wait: '[data-testid="profile-card"]' },
];

async function login(page, account) {
  await page.goto(`${BASE}/login`);
  await page.getByTestId('login-email').fill(account.email);
  await page.getByTestId('login-password').fill(account.password);
  await page.getByTestId('login-submit').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 });
}

/** The signed-in account's preferred language wins on every full load; switch through the visible control when needed. */
async function ensureLanguage(page, lang) {
  const current = await page.evaluate(() => document.documentElement.getAttribute('lang'));
  if (current === lang) return;
  const sw = page.locator('[data-testid="language-switcher"]:visible').first();
  if (await sw.count()) {
    await sw.click();
    await page.waitForTimeout(900);
  }
}

async function overflow(page) {
  return page.evaluate(() => {
    const w = document.documentElement.clientWidth;
    const bad = [];
    document.querySelectorAll('body *').forEach((el) => {
      const b = el.getBoundingClientRect();
      if (b.width > 0 && (b.right > w + 2 || b.left < -2) && getComputedStyle(el).position !== 'fixed') bad.push(el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.split(' ').slice(0, 2).join('.') : ''));
    });
    return { scrollWidth: document.documentElement.scrollWidth, width: w, bad: [...new Set(bad)].slice(0, 6) };
  });
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch();
  const report = [];
  for (const device of DEVICES) {
    for (const lang of LANGS) {
      const context = await browser.newContext({ ...VIEWPORTS[device], locale: lang === 'ar' ? 'ar' : 'en-US' });
      await context.addInitScript((l) => { try { localStorage.setItem('wi.lang', l); } catch {} }, lang);
      const page = await context.newPage();
      let signedInAs = null;
      for (const screen of SCREENS) {
        if (ONLY && !ONLY.includes(screen.n)) continue;
        const file = `${String(screen.n).padStart(2, '0')}-${screen.slug}-${lang}-${device}.png`;
        try {
          if (screen.role !== signedInAs) {
            if (signedInAs) { await context.clearCookies(); }
            if (screen.role) await login(page, ACCOUNTS[screen.role]);
            signedInAs = screen.role;
          }
          const url = typeof screen.url === 'function' ? screen.url() : screen.url;
          await page.goto(`${BASE}${url}`);
          await page.waitForSelector(screen.wait, { timeout: 45000 });
          await ensureLanguage(page, lang);
          if (!page.url().endsWith(url)) await page.waitForSelector(screen.wait, { timeout: 45000 });
          if (screen.steps) await screen.steps(page, lang);
          await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => undefined);
          await page.waitForTimeout(700);
          await page.screenshot({ path: path.join(OUT, file), fullPage: !!screen.fullPage && device === 'desktop' });
          const o = await overflow(page);
          report.push({ n: screen.n, slug: screen.slug, lang, device, file, ok: true, overflow: o.scrollWidth > o.width + 2 ? o : null });
          console.log(`✔ ${file}${o.scrollWidth > o.width + 2 ? `  ⚠ overflow ${o.scrollWidth}>${o.width} ${o.bad.join(', ')}` : ''}`);
        } catch (e) {
          report.push({ n: screen.n, slug: screen.slug, lang, device, file, ok: false, error: String(e.message || e).split('\n')[0] });
          console.log(`✘ ${file}: ${String(e.message || e).split('\n')[0]}`);
        }
      }
      await context.close();
    }
  }
  await browser.close();
  // Merge with earlier runs so a partial run (--only, --lang, --device) keeps the other shots' entries.
  const reportPath = path.join(OUT, 'capture-report.json');
  let previous = [];
  try { previous = JSON.parse(fs.readFileSync(reportPath, 'utf8')).screens || []; } catch {}
  const done = new Set(report.map((r) => r.file));
  const merged = [...previous.filter((r) => !done.has(r.file)), ...report.map((r) => ({ ...r, capturedAt: new Date().toISOString() }))].sort((a, b) => a.file.localeCompare(b.file));
  fs.writeFileSync(reportPath, JSON.stringify({ updatedAt: new Date().toISOString(), base: BASE, total: merged.length, overflowing: merged.filter((r) => r.overflow).length, failed: merged.filter((r) => !r.ok).length, screens: merged }, null, 2));
  const failed = report.filter((r) => !r.ok).length;
  console.log(`${report.length - failed}/${report.length} captured${failed ? `, ${failed} failed` : ''}`);
  process.exit(failed ? 1 : 0);
})();
