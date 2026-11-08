#!/usr/bin/env node
/* eslint-disable */
/**
 * Wabi Insight — Wabi Bank demo, recorded from the running application with the second sample organization
 * (scripts/sql/samples/wabi-bank). Nothing is mocked: the questions go to the organization's real AI provider, and
 * row scopes, the scheduler and the alert centre are the product's own.
 *
 *   1. Omar Haddad (Branch Manager, Deira) asks for cash deposits and withdrawals per teller in December 2025. The
 *      role reads Core Banking with the DEPARTMENT row scope on the posting unit, so only Deira's tellers come back.
 *   2. Priya Menon (Teller, Deira) asks the same question. The Teller role reads only the staff view of postings with the
 *      SELF row scope: the answer holds only Priya's own postings.
 *   3. Layla Darwish (Compliance and AML Officer) opens Alerts: the rule "Cash deposits of AED 55,000 or more in a
 *      day (individuals)" found customer C100503, who split cash deposits to stay under the reporting threshold.
 *      The alert is acknowledged with a note. Alerts never block anyone automatically.
 *
 * Usage (API on :5080, web on :4200, Wabi Bank seeded and its five applications connected and approved):
 *   WI_SAMPLE_PASSWORD='<the accounts' password>' \
 *   WI_SQLCMD='sqlcmd -S localhost,1433 -U sa -P <password> -C -d WabiInsightDB' \
 *     node scripts/demo/wabi-bank/record.js [--gif docs/demo/p13-5-wabi-bank.gif] [--videos <dir>] [--screens <dir>]
 *
 * WI_SQLCMD is optional: with it the script makes the cash-deposit rule's job due (one scheduler tick, at most a
 * minute) when no alert is open yet; without it an open alert must already exist (the rule runs daily at 06:30).
 * --screens saves a PNG of each answer and of the alert (1440 x 900).
 */
const { chromium } = require('playwright');
const { execSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const BASE = process.env.WI_BASE_URL || 'http://localhost:4200';
const PASSWORD = process.env.WI_SAMPLE_PASSWORD || 'Wabi#Sample#2026';
const MANAGER = 'omar.haddad@wabibank.example';
const TELLER = 'priya.menon@wabibank.example';
const AML = 'layla.darwish@wabibank.example';
const RULE_NAME = 'Cash deposits of AED 55,000 or more in a day (individuals)';
const QUESTION = 'Cash deposits and withdrawals per teller in December 2025';
const GIF = opt('--gif', null);
const SCREENS = opt('--screens', null);
const VIDEOS = path.resolve(opt('--videos', fs.mkdtempSync(path.join(os.tmpdir(), 'wi-wbk-demo-'))));
const SIZE = { width: 1440, height: 900 };

const pause = (page, ms) => page.waitForTimeout(ms);

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

async function api(page, method, url, body) {
  return page.evaluate(async ({ method, url, body }) => {
    const csrf = await (await fetch('/api/auth/csrf', { credentials: 'include' })).json();
    const response = await fetch(url, { method, credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': csrf.token }, body: body ? JSON.stringify(body) : undefined });
    const text = await response.text();
    let json = null; try { json = text ? JSON.parse(text) : null; } catch { json = text; }
    return { status: response.status, json };
  }, { method, url, body });
}

async function screen(page, name) {
  if (!SCREENS) return;
  fs.mkdirSync(SCREENS, { recursive: true });
  await page.evaluate(() => document.getElementById('wi-demo-caption')?.remove());
  await page.screenshot({ path: path.join(SCREENS, `${name}.png`) });
}

async function ask(page, who, question, note) {
  await page.goto(`${BASE}/app/ask`);
  await caption(page, who);
  await page.getByTestId('ask-application').click();
  await page.locator('mat-option', { hasText: 'Core Banking' }).click();
  await pause(page, 600);
  await page.getByTestId('ask-prompt').click();
  await page.keyboard.type(question, { delay: 26 });
  await pause(page, 400);
  await page.getByTestId('ask-submit').click();
  await caption(page, 'The plan is validated and authorized under this user’s permissions, then run read-only…');
  await page.waitForSelector('[data-testid="ask-answer"], [data-testid="ask-rejected"], [data-testid="ask-error"]', { timeout: 180000 });
  await pause(page, 800);
  await caption(page, note);
  await page.mouse.wheel(0, 420);
  await pause(page, 5500);
}

/** The open alert of the cash-deposit rule, making the rule's job due first when none is open (needs WI_SQLCMD). */
async function cashAlert(page) {
  const find = async () => ((await api(page, 'GET', '/api/monitoring/alerts?status=Open&pageSize=50')).json?.items ?? []).find((a) => a.ruleName === RULE_NAME);
  let alert = await find();
  if (!alert && process.env.WI_SQLCMD) {
    execSync(`${process.env.WI_SQLCMD} -Q "SET NOCOUNT ON; UPDATE j SET NextRunAt = DATEADD(second, -5, SYSUTCDATETIME()) FROM dbo.ScheduledJobs j JOIN dbo.MonitoringRules m ON m.RuleId = j.TargetId WHERE m.Name = N'${RULE_NAME.replace(/'/g, "''")}'"`, { stdio: 'ignore' });
    const deadline = Date.now() + 3 * 60 * 1000;
    while (!alert && Date.now() < deadline) { await pause(page, 5000); alert = await find(); }
  }
  if (!alert) throw new Error('No open alert for the cash-deposit rule.');
  return alert;
}

(async () => {
  const browser = await chromium.launch();
  const scene = async (email) => {
    const context = await browser.newContext({ viewport: SIZE, recordVideo: { dir: VIDEOS, size: SIZE }, locale: 'en-US' });
    await context.addInitScript(() => { try { localStorage.setItem('wi.lang', 'en'); } catch {} });
    const page = await context.newPage();
    await signIn(page, email);
    return { context, page };
  };

  // Scene 1: the branch manager sees only the Deira branch.
  const one = await scene(MANAGER);
  await ask(one.page, 'Omar (Branch Manager, Deira) asks for the branch’s cash postings per teller.', QUESTION,
    'Row scope DEPARTMENT: only postings made at Deira (BR-DXB01) come back — the other five branches are filtered out by the platform, not the prompt.');
  await screen(one.page, 'wbk-01-branch-manager-tellers');
  await one.context.close();

  // Scene 2: the teller sees only their own postings.
  const two = await scene(TELLER);
  await ask(two.page, 'Priya (Teller, Deira) asks the same question.', QUESTION,
    'Row scope SELF through the staff view: only Priya’s own postings come back — not the colleagues’.');
  await screen(two.page, 'wbk-02-teller-own-postings');
  await two.context.close();

  // Scene 3: the AML officer's alert.
  const three = await scene(AML);
  const alert = await cashAlert(three.page);
  await three.page.goto(`${BASE}/app/alerts`);
  await caption(three.page, 'Layla (AML) has a High alert: an individual’s cash deposits reached AED 55,000 within 24 hours.');
  await three.page.waitForSelector('[data-testid="alerts-list"]');
  await pause(three.page, 2500);
  await three.page.getByTestId(`alert-${alert.alertId}`).click();
  await three.page.waitForSelector('[data-testid="alert-detail"]');
  await caption(three.page, 'Customer C100503 split cash into deposits just under AED 50,000, then twice a day — a structuring pattern.');
  await pause(three.page, 4500);
  await screen(three.page, 'wbk-03-aml-alert');
  await three.page.getByTestId('alert-note').click();
  await three.page.keyboard.type('Structuring pattern on C100503: escalating for a suspicious transaction report review.', { delay: 18 });
  await three.page.getByTestId('alert-ack').click();
  await pause(three.page, 1200);
  await caption(three.page, 'A person decides. Alerts never block or suspend anyone automatically; every action is recorded.');
  await pause(three.page, 4500);
  await three.context.close();
  await browser.close();

  const videos = fs.readdirSync(VIDEOS).filter((f) => f.endsWith('.webm')).map((f) => path.join(VIDEOS, f)).sort((a, b) => fs.statSync(a).mtimeMs - fs.statSync(b).mtimeMs);
  console.log('videos', videos.join(' '));
  if (GIF) {
    // The scenes in order to one GIF (960 px wide, 8 fps, shared palette); the blank first second of each is trimmed.
    const inputs = videos.map((v) => `-i "${v}"`).join(' ');
    const trims = videos.map((_, i) => `[${i}:v]trim=start=1,setpts=PTS-STARTPTS[s${i}]`).join(';');
    const concat = `${trims};` + videos.map((_, i) => `[s${i}]`).join('') + `concat=n=${videos.length}:v=1:a=0,fps=8,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=bayer:bayer_scale=4`;
    execSync(`ffmpeg -y ${inputs} -filter_complex "${concat}" "${path.resolve(GIF)}"`, { stdio: 'ignore' });
    console.log('gif', path.resolve(GIF), `${Math.round(fs.statSync(GIF).size / 1024)} KB`);
  }
})().catch((e) => { console.error(e); process.exit(1); });
