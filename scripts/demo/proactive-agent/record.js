#!/usr/bin/env node
/* eslint-disable */
/**
 * Wabi Insight — proactive agent demo (docs/PROACTIVE_AGENT.md §16), recorded from the running application with
 * the sample organization Gulf Aviation Services (scripts/sql/samples). Nothing is mocked: the question goes to the
 * organization's real AI provider, the access ledger and the scheduler are the product's own.
 *
 *   1. Arjun Nair (Operations Analyst) asks for six named colleagues' net pay. His role reads Payslips with the
 *      SELF row scope, so the answer holds none of their figures — but the look-up is written to the access ledger.
 *   2. The access rule "Look-ups of named colleagues' pay" (identifier filter values on Payslips, more than five in
 *      a day) runs on its schedule and raises a High alert; the administrator gets an in-app notification.
 *   3. Faisal Al Shamsi (Organization Administrator, cyber security) opens the alert and acknowledges it with a note.
 *      Nothing happens to Arjun's account: alerts never suspend anyone automatically.
 *
 * Usage (API on :5080, web on :4200, sample organization seeded, accounts' passwords set):
 *   WI_SQLCMD='sqlcmd -S localhost,1433 -U sa -P <password> -C -d WabiInsightDB' \
 *     node scripts/demo/proactive-agent/record.js [--gif docs/demo/p14-5-proactive-agent-alert.gif] [--videos <dir>]
 *
 * WI_SQLCMD is optional: with it the script makes the rule's job due now (one scheduler tick, at most a minute);
 * without it the script waits for the rule's own 15-minute schedule. Accounts: WI_ANALYST_EMAIL / WI_ADMIN_EMAIL and
 * WI_SAMPLE_PASSWORD (the sample seed's passwords are emailed; set them once through the app).
 * Run it on a freshly seeded sample organization: an alert that is already open for the rule only counts a repeat.
 */
const { chromium } = require('playwright');
const { execSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const BASE = process.env.WI_BASE_URL || 'http://localhost:4200';
const ANALYST = process.env.WI_ANALYST_EMAIL || 'arjun.nair@gulfaviation.example';
const ADMIN = process.env.WI_ADMIN_EMAIL || 'faisal.alshamsi@gulfaviation.example';
const PASSWORD = process.env.WI_SAMPLE_PASSWORD || 'Wabi#Sample#2026';
const GIF = opt('--gif', null);
const VIDEOS = path.resolve(opt('--videos', fs.mkdtempSync(path.join(os.tmpdir(), 'wi-demo-'))));
const RULE_NAME = "Look-ups of named colleagues' pay";
const QUESTION = 'Show the net pay of employees E1001, E1002, E1004, E1005, E1006 and E1007 for March 2025';
const SIZE = { width: 1440, height: 900 };

const pause = (page, ms) => page.waitForTimeout(ms);

/** A caption bar at the bottom of the page, re-applied after each navigation. */
async function caption(page, text) {
  await page.evaluate((t) => {
    let bar = document.getElementById('wi-demo-caption');
    if (!bar) {
      bar = document.createElement('div');
      bar.id = 'wi-demo-caption';
      bar.style.cssText = 'position:fixed;inset-inline:0;bottom:0;z-index:100000;padding:14px 24px;background:rgba(10,37,64,.92);color:#fff;font:600 18px/1.4 Inter,system-ui,sans-serif;text-align:center;direction:ltr';
      document.body.appendChild(bar);
    }
    bar.textContent = t;
  }, text);
}

async function signIn(page, email) {
  await page.goto(`${BASE}/login`);
  await page.getByTestId('login-email').fill(email);
  await page.getByTestId('login-password').fill(PASSWORD);
  await page.getByTestId('login-submit').click();
  await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 });
}

/** Same-origin API call from a signed-in page (cookie session plus the antiforgery header). */
async function api(page, method, url, body) {
  return page.evaluate(async ({ method, url, body }) => {
    const csrf = await (await fetch('/api/auth/csrf', { credentials: 'include' })).json();
    const response = await fetch(url, { method, credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': csrf.token }, body: body ? JSON.stringify(body) : undefined });
    const text = await response.text();
    return { status: response.status, json: text ? JSON.parse(text) : null };
  }, { method, url, body });
}

/** The access rule of the demo, created by the administrator if it does not exist yet. */
async function ensureRule(page) {
  const rules = (await api(page, 'GET', '/api/monitoring/rules')).json;
  const existing = rules.find((r) => r.name === RULE_NAME);
  if (existing) {
    return existing.ruleId;
  }
  const applications = (await api(page, 'GET', '/api/applications')).json;
  const hr = applications.find((a) => a.applicationCode === 'HR-PAYROLL');
  const objects = (await api(page, 'GET', `/api/metadata/applications/${hr.applicationId}/objects?search=Payslips&pageSize=10`)).json;
  const payslips = objects.items.find((o) => o.schemaName === 'dbo' && o.objectName === 'Payslips');
  const created = await api(page, 'POST', '/api/monitoring/rules', {
    name: RULE_NAME,
    description: 'Someone filters payslips by specific employee numbers: more than five in a day.',
    kind: 'Access', templateCode: null,
    definition: { access: { signal: 'identifier_filter_values', windowHours: 24, objectIds: [payslips.objectId], minClassification: 'Confidential', roleCodes: [], outsideWorkingHours: false, newUsersWithinDays: null, threshold: { mode: 'absolute', value: 5 } } },
    severity: 'High', recurrenceKind: 'Interval', intervalMinutes: 15, timeOfDay: '00:00:00', dayOfWeek: null, cooldownHours: 24, isActive: true, ownerUserId: null, recipients: [],
  });
  if (created.status !== 201) {
    throw new Error(`Rule not created: ${created.status} ${JSON.stringify(created.json)}`);
  }
  return created.json.ruleId;
}

async function waitForAlert(page, ruleId) {
  if (process.env.WI_SQLCMD) {
    execSync(`${process.env.WI_SQLCMD} -Q "SET NOCOUNT ON; UPDATE dbo.ScheduledJobs SET NextRunAt = DATEADD(second, -5, SYSUTCDATETIME()) WHERE TargetId = '${ruleId}'"`, { stdio: 'ignore' });
  }
  const deadline = Date.now() + (process.env.WI_SQLCMD ? 3 : 20) * 60 * 1000;
  while (Date.now() < deadline) {
    const page1 = (await api(page, 'GET', `/api/monitoring/alerts?ruleId=${ruleId}&status=Open`)).json;
    if (page1.items.length > 0) {
      return page1.items[0].alertId;
    }
    await pause(page, 5000);
  }
  throw new Error('No alert was raised in time.');
}

(async () => {
  const browser = await chromium.launch();

  // Setup (not recorded): the administrator's access rule.
  const setup = await browser.newContext({ viewport: SIZE });
  const admin = await setup.newPage();
  await signIn(admin, ADMIN);
  const ruleId = await ensureRule(admin);
  console.log('rule', ruleId);

  // Scene 1: the analyst looks up colleagues' pay.
  const one = await browser.newContext({ viewport: SIZE, recordVideo: { dir: VIDEOS, size: SIZE }, locale: 'en-US' });
  await one.addInitScript(() => { try { localStorage.setItem('wi.lang', 'en'); } catch {} });
  const analyst = await one.newPage();
  await signIn(analyst, ANALYST);
  await analyst.goto(`${BASE}/app/ask`);
  await caption(analyst, 'Arjun (Operations Analyst) asks for six colleagues’ net pay.');
  await analyst.getByTestId('ask-application').click();
  await analyst.locator('mat-option', { hasText: 'HR-PAYROLL' }).click();
  await pause(analyst, 600);
  await analyst.getByTestId('ask-prompt').click();
  await analyst.keyboard.type(QUESTION, { delay: 28 });
  await pause(analyst, 500);
  await analyst.getByTestId('ask-submit').click();
  await caption(analyst, 'The plan is validated and authorized under Arjun’s permissions, then run read-only…');
  await analyst.waitForSelector('[data-testid="ask-answer"], [data-testid="ask-rejected"], [data-testid="ask-error"]', { timeout: 120000 });
  await pause(analyst, 800);
  await caption(analyst, 'Row scope SELF: Arjun sees only his own payslips — none of his colleagues’ figures. The look-up is still written to the access ledger.');
  await analyst.mouse.wheel(0, 380);
  await pause(analyst, 5500);
  await one.close();

  // The access rule runs on its schedule over the ledger.
  const alertId = await waitForAlert(admin, ruleId);
  console.log('alert', alertId);
  await setup.close();

  // Scene 2: the administrator receives the alert and decides.
  const two = await browser.newContext({ viewport: SIZE, recordVideo: { dir: VIDEOS, size: SIZE }, locale: 'en-US' });
  await two.addInitScript(() => { try { localStorage.setItem('wi.lang', 'en'); } catch {} });
  const officer = await two.newPage();
  await signIn(officer, ADMIN);
  await caption(officer, 'Every 15 minutes the access rule checks the ledger. Faisal (cyber security) gets a High alert in the app.');
  await pause(officer, 2500);
  await officer.getByTestId('notifications-bell').click();
  await officer.waitForSelector('[data-testid="notification-list"]');
  await pause(officer, 2500);
  // The notification opens the alert in the app.
  await officer.locator('[data-testid="notification-list"] li', { hasText: RULE_NAME }).first().click();
  await officer.waitForSelector('[data-testid="alert-detail"]');
  await caption(officer, 'The alert names the user and how many specific employees were looked up \u2014 no salary figures.');
  await pause(officer, 4500);
  await officer.getByTestId('alert-note').click();
  await officer.keyboard.type('Spoke to Arjun: he was checking the overtime policy. Reminded him of the payroll access policy.', { delay: 18 });
  await officer.getByTestId('alert-ack').click();
  await pause(officer, 1200);
  await caption(officer, 'A person decides. Alerts never suspend anyone automatically; every action is recorded.');
  await pause(officer, 5000);
  await two.close();
  await browser.close();

  const videos = fs.readdirSync(VIDEOS).filter((f) => f.endsWith('.webm')).map((f) => path.join(VIDEOS, f)).sort((a, b) => fs.statSync(a).mtimeMs - fs.statSync(b).mtimeMs);
  console.log('videos', videos.join(' '));
  if (GIF) {
    // Two scenes, in order, to one GIF (960 px wide, 8 fps, shared palette); the first second of each scene is the
    // blank page before sign-in, so it is trimmed.
    const inputs = videos.map((v) => `-i "${v}"`).join(' ');
    const trims = videos.map((_, i) => `[${i}:v]trim=start=1,setpts=PTS-STARTPTS[s${i}]`).join(';');
    const concat = `${trims};` + videos.map((_, i) => `[s${i}]`).join('') + `concat=n=${videos.length}:v=1:a=0,fps=8,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=bayer:bayer_scale=4`;
    execSync(`ffmpeg -y ${inputs} -filter_complex "${concat}" "${path.resolve(GIF)}"`, { stdio: 'ignore' });
    console.log('gif', path.resolve(GIF), `${Math.round(fs.statSync(GIF).size / 1024)} KB`);
  }
})().catch((e) => { console.error(e); process.exit(1); });
