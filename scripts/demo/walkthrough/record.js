#!/usr/bin/env node
/* eslint-disable */
/**
 * Wabi Insight — product walkthrough recorder. Drives the running application (real pages, real AI answers, real
 * files) with the Wabi Bank sample organization, scene by scene, following script.<lang>.json, and records every
 * scene as screen frames (Chrome DevTools screencast) with a timeline: when each narration line starts, and which
 * stretches to cut (sign-ins, waiting for the AI). compose.py turns the frames, the narration (tts.py) and the
 * timelines into the final video. Graphic scenes (title cards) are HTML pages in graphics/, animated line by line.
 *
 * Before recording: Wabi Bank seeded (scripts/sql/samples/wabi-bank/01-06), prepare.mjs run once, narration
 * generated (tts.py), API on :5080 and the web app on :4200.
 *
 *   WI_DEMO_PASSWORD='<demo accounts password>' node scripts/demo/walkthrough/record.js --out <dir> [--only admin,ask] [--lang en]
 *
 * Every scene is recorded independently, so one scene can be re-recorded with --only. Scenes that need files from
 * earlier ones (the files graphic uses the exports of "history") read them from <dir>/graphics/files.
 */
const { chromium } = require('playwright');
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const LANG = opt('--lang', 'en');
const HERE = __dirname;
const OUT = path.resolve(opt('--out', path.join(HERE, 'out', LANG)));
const ONLY = opt('--only', null);
const BASE = process.env.WI_BASE_URL || 'http://localhost:4200';
const PASSWORD = process.env.WI_DEMO_PASSWORD;
const WEB = path.resolve(HERE, '../../../src/web');
const SCRIPT = JSON.parse(fs.readFileSync(path.join(HERE, `script.${LANG}.json`), 'utf-8'));
const DURATIONS = JSON.parse(fs.readFileSync(path.join(OUT, 'durations.json'), 'utf-8'));
const SIZE = { width: 1920, height: 1080 };
const GAP = 0.55;      // silence after each narration line, in seconds
const LEAD = 0.25;     // the voice starts this long after a line's mark
if (!PASSWORD) throw new Error('Set WI_DEMO_PASSWORD (the password prepare.mjs gave the demo accounts).');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const now = () => Date.now() / 1000;

// ------------------------------------------------------------------------------------------------ on-screen layer
/** Injected into every app page: fake cursor, click ripple, spotlight, chapter label, callout card and a file gallery. */
const OVERLAY = `(() => {
  if (window.__wi) return;
  const css = \`
  #wi-ov{position:fixed;inset:0;pointer-events:none;z-index:2147483000;font-family:Inter,system-ui,sans-serif}
  #wi-ov .cursor{position:absolute;left:0;top:0;width:30px;height:38px;transform:translate(1560px,760px);transition:transform .65s cubic-bezier(.3,.7,.2,1);filter:drop-shadow(0 4px 6px rgba(0,0,0,.35));z-index:5}
  #wi-ov .ripple{position:absolute;width:60px;height:60px;margin:-30px 0 0 -30px;border-radius:50%;border:3px solid #22b8cf;animation:wiRip .65s ease-out forwards;z-index:4}
  @keyframes wiRip{from{transform:scale(.2);opacity:1}to{transform:scale(1.5);opacity:0}}
  #wi-ov .spot{position:absolute;border-radius:14px;box-shadow:0 0 0 4px rgba(34,184,207,.95),0 0 0 9999px rgba(6,20,42,.34),0 0 40px 10px rgba(34,184,207,.45);transition:all .55s cubic-bezier(.2,.8,.2,1);opacity:0}
  #wi-ov .spot.on{opacity:1}
  #wi-ov .chapter{position:absolute;left:26px;bottom:24px;display:flex;align-items:center;gap:12px;padding:11px 22px 11px 12px;border-radius:999px;background:linear-gradient(90deg,#0b1f3a,#14427a);color:#fff;font:600 21px/1 Inter,sans-serif;box-shadow:0 10px 30px rgba(11,31,58,.35);transform:translateY(130px);transition:transform .8s cubic-bezier(.2,.8,.2,1);z-index:3}
  #wi-ov .chapter.on{transform:none}
  #wi-ov .chapter b{display:grid;place-items:center;min-width:32px;height:32px;padding:0 8px;border-radius:999px;background:linear-gradient(135deg,#087df2,#22b8cf);font:800 16px/1 Inter,sans-serif}
  #wi-ov .callout{position:absolute;right:30px;bottom:28px;max-width:560px;display:flex;gap:14px;align-items:center;padding:18px 24px;border-radius:18px;background:rgba(11,31,58,.95);color:#fff;font:600 22px/1.35 Inter,sans-serif;box-shadow:0 16px 40px rgba(0,0,0,.3);border:1px solid rgba(34,184,207,.6);opacity:0;transform:translateY(24px);transition:all .6s cubic-bezier(.2,.8,.2,1);z-index:3}
  #wi-ov .callout.on{opacity:1;transform:none}
  #wi-ov .callout i{font-family:'Material Icons';font-style:normal;font-size:30px;color:#22b8cf;line-height:1}
  #wi-ov .gallery{position:absolute;inset:0;background:rgba(6,20,42,.86);display:flex;align-items:center;justify-content:center;gap:36px;opacity:0;transition:opacity .6s;z-index:2}
  #wi-ov .gallery.on{opacity:1}
  #wi-ov .gallery img{height:840px;border-radius:8px;box-shadow:0 30px 70px rgba(0,0,0,.55);transform:translateY(50px) scale(.95);opacity:0;transition:all .9s cubic-bezier(.2,.8,.2,1)}
  #wi-ov .gallery.on img{transform:none;opacity:1}
  #wi-ov .tick{position:absolute;left:0;top:0;width:2px;height:2px;opacity:.01}\`;
  const arrow = '<svg viewBox="0 0 30 38" width="30" height="38"><path d="M3 2 L3 30 L10 23 L15 35 L20 33 L15 21 L25 21 Z" fill="#fff" stroke="#0b1f3a" stroke-width="2.2" stroke-linejoin="round"/></svg>';
  let root = null, cursor = null, spot = null, chapter = null, callout = null, gallery = null, tick = null;
  function ensure() {
    if (root && document.body.contains(root)) return;
    const style = document.createElement('style'); style.textContent = css; document.head.appendChild(style);
    root = document.createElement('div'); root.id = 'wi-ov';
    root.innerHTML = '<div class="spot"></div><div class="gallery"></div><div class="chapter"><b></b><span></span></div><div class="callout"><i></i><span></span></div><div class="cursor">' + arrow + '</div><div class="tick"></div>';
    document.body.appendChild(root);
    cursor = root.querySelector('.cursor'); spot = root.querySelector('.spot'); chapter = root.querySelector('.chapter');
    callout = root.querySelector('.callout'); gallery = root.querySelector('.gallery'); tick = root.querySelector('.tick');
  }
  let pos = [1560, 760];
  window.__wi = {
    cursor(x, y, ms) { ensure(); cursor.style.transitionDuration = (ms || 650) + 'ms'; cursor.style.transform = 'translate(' + (x - 3) + 'px,' + (y - 2) + 'px)'; pos = [x, y]; },
    ripple(x, y) { ensure(); const r = document.createElement('div'); r.className = 'ripple'; r.style.left = x + 'px'; r.style.top = y + 'px'; root.appendChild(r); setTimeout(() => r.remove(), 800); },
    spot(r) { ensure(); Object.assign(spot.style, { left: r.x + 'px', top: r.y + 'px', width: r.w + 'px', height: r.h + 'px' }); spot.classList.add('on'); },
    unspot() { ensure(); spot.classList.remove('on'); },
    chapter(num, text) { ensure(); chapter.querySelector('b').textContent = num; chapter.querySelector('b').style.display = num ? '' : 'none'; chapter.querySelector('span').textContent = text; chapter.classList.add('on'); },
    callout(text, icon) { ensure(); if (!text) { callout.classList.remove('on'); return; } callout.querySelector('i').textContent = icon || 'info'; callout.querySelector('span').textContent = text; callout.classList.add('on'); },
    gallery(srcs) { ensure(); gallery.innerHTML = srcs.map((s) => '<img src="' + s + '">').join(''); requestAnimationFrame(() => requestAnimationFrame(() => gallery.classList.add('on'))); },
    ungallery() { ensure(); gallery.classList.remove('on'); },
    tick() { ensure(); tick.style.opacity = tick.style.opacity === '0.02' ? '0.01' : '0.02'; },
    pos() { return pos; },
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ensure); else ensure();
})();`;

// ------------------------------------------------------------------------------------------------ capture
/** Screen frames of one scene plus its timeline (narration marks and cuts), written to <out>/scenes/<id>/. */
class Capture {
  constructor(id) {
    this.id = id;
    this.dir = path.join(OUT, 'scenes', id);
    fs.rmSync(this.dir, { recursive: true, force: true });
    fs.mkdirSync(path.join(this.dir, 'frames'), { recursive: true });
    this.frames = []; this.beats = {}; this.cuts = []; this.clicks = []; this.cutFrom = null; this.n = 0; this.session = null; this.start = null;
  }
  async attach(page) {
    if (this.session) { await this.session.send('Page.stopScreencast').catch(() => {}); await this.session.detach().catch(() => {}); }
    const session = await page.context().newCDPSession(page);
    session.on('Page.screencastFrame', (f) => {
      const file = `${String(this.n++).padStart(6, '0')}.jpg`;
      fs.writeFileSync(path.join(this.dir, 'frames', file), Buffer.from(f.data, 'base64'));
      this.frames.push({ t: f.metadata.timestamp, file });
      session.send('Page.screencastFrameAck', { sessionId: f.sessionId }).catch(() => {});
    });
    await session.send('Page.startScreencast', { format: 'jpeg', quality: 90, maxWidth: SIZE.width, maxHeight: SIZE.height, everyNthFrame: 1 });
    this.session = session; this.page = page;
    if (this.start === null) this.start = now();
    // A static page sends no frames: nudge one so the scene starts on this page.
    await page.evaluate(() => window.__wi && window.__wi.tick()).catch(() => {});
    await sleep(120);
  }
  cut(on) {
    if (on && this.cutFrom === null) this.cutFrom = now();
    if (!on && this.cutFrom !== null) { this.cuts.push([this.cutFrom, now()]); this.cutFrom = null; }
  }
  /** Seconds of the scene that will be seen since t (cut stretches do not count). */
  visibleSince(t) {
    const end = now();
    let cut = 0;
    for (const [a, b] of this.cuts) cut += Math.max(0, Math.min(b, end) - Math.max(a, t));
    if (this.cutFrom !== null) cut += end - Math.max(this.cutFrom, t);
    return end - t - cut;
  }
  async until(t, seconds) { while (this.visibleSince(t) < seconds) await sleep(40); }
  async finish() {
    if (this.session) { await this.session.send('Page.stopScreencast').catch(() => {}); await this.session.detach().catch(() => {}); }
    this.cut(false);
    const timeline = { id: this.id, start: this.start, end: now(), beats: this.beats, cuts: this.cuts, clicks: this.clicks, frames: this.frames };
    fs.writeFileSync(path.join(this.dir, 'timeline.json'), JSON.stringify(timeline));
    return timeline;
  }
}

/** Scene helper: narration pacing, cursor actions and the on-screen layer, bound to the page being captured. */
class Director {
  constructor(scene) {
    this.scene = scene; this.cap = new Capture(scene.id); this.page = null; this.current = -1;
  }
  dur(n) { return DURATIONS[`${this.scene.id}-${n}`] || 0; }
  async attach(page) {
    this.page = page;
    await page.evaluate(() => window.__wi && window.__wi.unspot()).catch(() => {});
    await this.chapter();
    await this.cap.attach(page);
  }
  async chapter() {
    if (this.scene.kind !== 'app') return;
    const m = /^(\d+)\s*·\s*(.*)$/.exec(this.scene.chapter);
    await this.page.evaluate(([n, t]) => window.__wi.chapter(n, t), m ? [m[1], m[2]] : ['', this.scene.chapter]);
  }
  /** Starts narration line n once line n-1 has been heard in full. */
  async beat(n) {
    if (this.current >= 0) await this.cap.until(this.cap.beats[this.current], this.dur(this.current) + LEAD + GAP);
    this.cap.beats[n] = now(); this.current = n;
  }
  async end(tail = 0.8) {
    if (this.current >= 0) await this.cap.until(this.cap.beats[this.current], this.dur(this.current) + LEAD + tail);
    return this.cap.finish();
  }
  cut(on) { this.cap.cut(on); }
  async wait(ms) { await this.page.waitForTimeout(ms); }
  loc(target) { return typeof target === 'string' ? this.page.locator(target).first() : target; }
  async box(target) {
    const l = this.loc(target);
    await l.waitFor({ state: 'visible', timeout: 30000 });
    return l.boundingBox();
  }
  /** Smoothly scrolls the page (the window scrolls in this layout) until the element is comfortably in view. */
  async reveal(target) {
    const l = this.loc(target);
    const b = await this.box(l);
    if (b.y < 70 || b.y + Math.min(b.height, 200) > SIZE.height - 40) {
      await l.evaluate((e) => e.scrollIntoView({ behavior: 'smooth', block: 'center' }));
      await this.page.waitForTimeout(900);
    }
  }
  async moveTo(target, ms = 650) {
    const l = this.loc(target);
    await this.reveal(l);
    const b = await this.box(l);
    const x = b.x + b.width / 2, y = b.y + b.height / 2;
    await this.page.evaluate(([x, y, ms]) => window.__wi.cursor(x, y, ms), [x, y, ms]);
    await this.page.waitForTimeout(ms + 60);
    return [x, y];
  }
  async click(target, after = 450) {
    const [x, y] = await this.moveTo(target);
    await this.page.evaluate(([x, y]) => window.__wi.ripple(x, y), [x, y]);
    this.cap.clicks.push(now());
    await this.page.mouse.click(x, y);
    await this.page.waitForTimeout(after);
  }
  async type(target, text, delay = 26) {
    await this.click(target, 200);
    await this.page.keyboard.type(text, { delay });
    await this.page.waitForTimeout(300);
  }
  async select(selectTarget, optionText) {
    await this.click(selectTarget, 500);
    await this.click(this.page.locator('mat-option', { hasText: optionText }).first(), 500);
  }
  async spot(target, pad = 10) {
    const b = await this.box(target);
    const top = Math.max(b.y - pad, 6), bottom = Math.min(b.y + b.height + pad, SIZE.height - 8);
    await this.page.evaluate((r) => window.__wi.spot(r), { x: Math.max(b.x - pad, 6), y: top, w: Math.min(b.width + 2 * pad, SIZE.width - 12), h: Math.max(bottom - top, 30) });
    await this.page.waitForTimeout(600);
  }
  async unspot() { await this.page.evaluate(() => window.__wi.unspot()); await this.page.waitForTimeout(300); }
  async callout(text, icon) { await this.page.evaluate(([t, i]) => window.__wi.callout(t, i), [text, icon]); await this.page.waitForTimeout(text ? 500 : 250); }
  async scrollTo(target, block = 'start') {
    await this.loc(target).evaluate((e, b) => e.scrollIntoView({ behavior: 'smooth', block: b }), block);
    await this.page.waitForTimeout(1100);
  }
  async scrollBy(dy) {
    await this.page.evaluate((dy) => window.scrollBy({ top: dy, behavior: 'smooth' }), dy);
    await this.page.waitForTimeout(1100);
  }
  async scrollTop() {
    if (await this.page.evaluate(() => window.scrollY) === 0) return;
    await this.page.evaluate(() => window.scrollTo({ top: 0, behavior: 'smooth' }));
    await this.page.waitForTimeout(900);
  }
  /** Clicks a side-navigation item by the last segment of its link (nav-<segment> test id). */
  async nav(segment) {
    await this.click(this.page.getByTestId(`nav-${segment}`).first(), 900);
    await this.page.waitForLoadState('networkidle').catch(() => {});
    await this.scrollTop();
    await this.page.waitForTimeout(300);
  }
  /** Asks a question on the Ask page: picks the application, types, submits; the wait for the AI is cut after a moment. */
  async ask(appCode, question, { visibleWaitMs = 1700 } = {}) {
    const p = this.page;
    const current = await p.getByTestId('ask-application').innerText().catch(() => '');
    if (!current.includes(appCode)) await this.select(p.getByTestId('ask-application'), appCode);
    const prompt = p.getByTestId('ask-prompt');
    await prompt.fill('');
    await this.type(prompt, question);
    await this.click(p.getByTestId('ask-submit'), 200);
    await p.locator('.ask__working').waitFor({ state: 'visible', timeout: 8000 }).catch(() => {});
    await p.waitForTimeout(visibleWaitMs);
    this.cut(true);
    await p.locator('.ask__working').waitFor({ state: 'hidden', timeout: 300000 });
    await p.locator('[data-testid="ask-answer"], [data-testid="ask-rejected"], [data-testid="ask-error"]').first().waitFor({ timeout: 30000 });
    await p.waitForTimeout(1500);
    this.cut(false);
    if (await p.getByTestId('ask-answer').count() === 0) throw new Error(`${this.scene.id}: no answer for "${question}"`);
  }
}

// ------------------------------------------------------------------------------------------------ browser, users
let browser;
const sessions = {};

async function userPage(user) {
  if (sessions[user]) return sessions[user];
  const context = await browser.newContext({ viewport: SIZE, deviceScaleFactor: 1, locale: 'en-US', timezoneId: 'Asia/Dubai', acceptDownloads: true });
  await context.addInitScript(OVERLAY);
  await context.addInitScript(() => { try { if (!sessionStorage.getItem('wi.walkthrough')) { localStorage.setItem('wi.lang', 'en'); sessionStorage.setItem('wi.walkthrough', '1'); } } catch {} });
  const page = await context.newPage();
  for (let attempt = 0; attempt < 3; attempt++) {
    await page.goto(`${BASE}/login`);
    await page.getByTestId('login-email').fill(`${user}@wabibank.example`);
    await page.getByTestId('login-password').fill(PASSWORD);
    await page.getByTestId('login-submit').click();
    const ok = await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 20000 }).then(() => true).catch(() => false);
    if (ok) break;
    if (attempt === 2) throw new Error(`${user}: sign-in failed`);
    await sleep(65000); // sign-in rate limit
  }
  await english(page);
  sessions[user] = page;
  return page;
}

async function english(page) {
  await page.waitForTimeout(1500);
  for (let i = 0; i < 2 && (await page.locator('html').getAttribute('lang')) !== 'en'; i++) {
    await page.getByTestId('language-switcher').first().click();
    await page.waitForTimeout(1500);
  }
}

async function open(page, url) {
  await page.goto(`${BASE}${url}`);
  await page.waitForLoadState('networkidle').catch(() => {});
  await english(page);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(800);
}

/** PNG pages of a PDF, Word or Excel file (LibreOffice converts Word and Excel to PDF first). */
function renderFile(file, prefix, pages = 1) {
  const dir = path.join(OUT, 'graphics', 'files');
  fs.mkdirSync(dir, { recursive: true });
  let pdf = file;
  if (!file.toLowerCase().endsWith('.pdf')) {
    execFileSync('soffice', ['--headless', '--convert-to', 'pdf', '--outdir', path.dirname(file), file], { stdio: 'ignore', timeout: 180000 });
    pdf = file.replace(/\.[^.]+$/, '.pdf');
  }
  execFileSync('pdftoppm', ['-png', '-r', '80', '-f', '1', '-l', String(pages), pdf, path.join(dir, prefix)], { stdio: 'ignore' });
  // pdftoppm names pages prefix-1.png or prefix-01.png depending on the page count.
  return fs.readdirSync(dir).filter((f) => f.startsWith(prefix + '-')).sort().map((f) => {
    const n = +/-(\d+)\.png$/.exec(f)[1];
    const target = path.join(dir, `${prefix}-${n}.png`);
    if (path.join(dir, f) !== target) fs.renameSync(path.join(dir, f), target);
    return target;
  });
}

async function download(page, click) {
  const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 120000 }), click()]);
  const dir = path.join(OUT, 'downloads');
  fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, dl.suggestedFilename());
  await dl.saveAs(file);
  return file;
}

// ------------------------------------------------------------------------------------------------ graphic scenes
function prepareGraphics() {
  const dir = path.join(OUT, 'graphics');
  fs.mkdirSync(path.join(dir, 'files'), { recursive: true });
  for (const f of fs.readdirSync(path.join(HERE, 'graphics'))) {
    const text = fs.readFileSync(path.join(HERE, 'graphics', f), 'utf-8')
      .replaceAll('__WEB__', WEB).replaceAll('__WEBSITE__', SCRIPT.contact.website).replaceAll('__EMAIL__', SCRIPT.contact.email);
    fs.writeFileSync(path.join(dir, f), text);
  }
}

async function graphicScene(scene) {
  const context = await browser.newContext({ viewport: SIZE, deviceScaleFactor: 1 });
  const page = await context.newPage();
  await page.goto('file://' + path.join(OUT, 'graphics', scene.graphic));
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(1200);
  const d = new Director(scene);
  d.page = page;
  await d.cap.attach(page);
  await page.waitForTimeout(400);
  for (let n = 0; n < scene.beats.length; n++) {
    await d.beat(n);
    await page.evaluate((n) => window.showBeat(n), n);
  }
  const timeline = await d.end(1.4);
  await context.close();
  return timeline;
}

// ------------------------------------------------------------------------------------------------ app scenes
const SCENES = {
  async admin(d) {
    const p = await userPage('w1001');
    await open(p, '/app/admin/dashboard');
    await d.attach(p);
    await d.beat(0);
    await d.spot(p.getByTestId('odash-kpis'));
    await d.wait(2500);
    await d.unspot();

    await d.beat(1);
    await d.nav('applications');
    await d.spot(p.getByTestId('app-table'));
    await d.wait(1200);
    await d.unspot();
    await d.click(p.getByTestId('app-test-stored-CORE-BANKING'), 300);
    await p.locator('mat-snack-bar-container').first().waitFor({ timeout: 30000 });
    await d.spot(p.getByTestId('app-table').locator('tr', { hasText: 'CORE-BANKING' }).first(), 6);
    await d.callout('Encrypted, tested, read-only', 'lock');
    await d.wait(2600);
    await d.callout(null); await d.unspot();

    await d.beat(2);
    await d.nav('metadata');
    await d.click(p.getByTestId('meta-row-Customers'), 1500);
    await d.spot(p.getByTestId('meta-detail'));
    await d.wait(1500);
    await d.scrollTo(p.getByTestId('meta-columns'), 'center');
    await d.spot(p.getByTestId('meta-columns'));
    await d.wait(2000);
    await d.unspot();

    await d.beat(3);
    await d.nav('roles');
    await d.spot(p.getByTestId('role-table'));
    await d.wait(900);
    await d.unspot();
    await d.nav('permissions');
    await d.select(p.getByTestId('perm-role'), 'Branch Manager');
    const app = await p.getByTestId('perm-app').innerText().catch(() => '');
    if (!app.includes('CORE-BANKING')) await d.select(p.getByTestId('perm-app'), 'CORE-BANKING');
    await d.wait(400);
    await d.spot(p.getByTestId('perm-matrix'));
    await d.callout('Branch managers: their own branch only', 'store');
    await d.wait(1600);
    await d.callout(null); await d.unspot();

    await d.beat(4);
    await d.nav('users');
    await d.spot(p.getByTestId('user-table'));
    await d.wait(900);
    await d.unspot();
    await d.click(p.getByTestId('user-new'), 1000);
    await d.spot(p.getByTestId('user-form'));
    await d.callout('Temporary password sent by email', 'mail');
    await d.wait(1500);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async ask(d) {
    const p = await userPage('w1002');
    await open(p, '/app/ask');
    await d.attach(p);
    await d.beat(0);
    await d.select(p.getByTestId('ask-application'), 'CORE-BANKING');
    await d.spot(p.getByTestId('ask-examples'));
    await d.wait(1500);
    await d.unspot();
    await d.ask('CORE-BANKING', 'Total deposit balances by branch at the end of December 2025');

    await d.beat(1);
    await d.scrollTo(p.getByTestId('ask-answer'));
    await d.spot(p.getByTestId('ask-answer'), 6);
    await d.wait(3000);
    await d.unspot();

    await d.beat(2);
    await d.scrollTo(p.getByTestId('ask-how'), 'start');
    await d.click(p.getByTestId('ask-how').locator('mat-expansion-panel-header'), 1000);
    await d.scrollTo(p.getByTestId('ask-how'), 'start');
    await d.spot(p.getByTestId('ask-how'));
    await d.callout('Every query is logged for accuracy reviews', 'fact_check');
    await d.wait(3000);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async dashboard(d) {
    const p = await userPage('w1002');
    await open(p, '/app/ask');
    await d.attach(p);
    await d.beat(0);
    await d.ask('IT-HELPDESK', 'Give me a dashboard of IT incidents in 2025: totals, by priority, by month and by assignment group');
    await d.scrollTo(p.getByTestId('ask-answer'));
    await d.spot(p.getByTestId('ask-kpis'));
    await d.wait(2000);
    await d.unspot();

    await d.beat(1);
    await d.scrollTo(p.getByTestId('ask-charts'));
    await d.wait(1600);
    await d.scrollBy(560);
    await d.wait(1600);
    await d.scrollBy(560);
    await d.wait(1200);
    return d.end();
  },

  async study(d) {
    const p = await userPage('w1002');
    await open(p, '/app/ask');
    await d.attach(p);
    await d.beat(0);
    await d.ask('CORE-BANKING', 'Study cheque returns in 2025: by month, by branch and by return reason, and explain what stands out');
    await d.scrollTo(p.getByTestId('ask-answer'));
    await d.wait(1500);

    await d.beat(1);
    await d.scrollTo(p.getByTestId('ask-report'), 'center');
    await d.spot(p.getByTestId('ask-report'));
    // The download button opens the PDF in a new tab: the recording fetches the same file with the user's session.
    const link = p.getByTestId('ask-report-download');
    const [x, y] = await d.moveTo(link);
    await p.evaluate(([x, y]) => window.__wi.ripple(x, y), [x, y]);
    const response = await p.context().request.get(new URL(await link.getAttribute('href'), BASE).toString());
    if (!response.ok()) throw new Error(`study report: ${response.status()}`);
    fs.mkdirSync(path.join(OUT, 'downloads'), { recursive: true });
    const pdf = path.join(OUT, 'downloads', 'study.pdf');
    fs.writeFileSync(pdf, await response.body());
    const pages = renderFile(pdf, 'pdf', 4);
    await d.unspot();
    const images = pages.slice(0, 3).map((f) => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64'));
    await p.evaluate((srcs) => window.__wi.gallery(srcs), images);
    await d.wait(5200);

    await d.beat(2);
    await p.evaluate(() => window.__wi.ungallery());
    await d.wait(800);
    await d.scrollTo(p.getByTestId('ask-kpis'));
    await d.wait(1200);
    await d.scrollBy(620);
    await d.wait(1400);
    return d.end();
  },

  async email(d) {
    const p = await userPage('w1002');
    await open(p, '/app/ask');
    await d.attach(p);
    await d.beat(0);
    await d.ask('CORE-BANKING', 'Draft an email to the branch managers summarizing returned cheques by branch in December 2025');
    await d.scrollTo(p.getByTestId('ask-email'), 'start');
    await d.spot(p.getByTestId('ask-email'));
    await d.wait(2600);
    await d.unspot();

    await d.beat(1);
    await d.scrollTop();
    await d.ask('FINANCE', 'Prepare a report on Q3 2025 expenses versus the revised budget by department, and draft an email to the finance committee with the report attached');
    await d.scrollTo(p.getByTestId('ask-report'), 'start');
    await d.spot(p.getByTestId('ask-report'));
    await d.wait(2200);
    await d.unspot();

    await d.beat(2);
    await d.scrollTo(p.getByTestId('ask-email'), 'start');
    await d.spot(p.getByTestId('ask-email'));
    await d.wait(1000);
    await d.click(p.getByTestId('ask-email-copy'), 900);
    await d.callout('Report attached, email ready to send', 'attach_email');
    await d.wait(2200);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async arabic(d) {
    const p = await userPage('w1002');
    await open(p, '/app/ask');
    await d.attach(p);
    await d.beat(0);
    await d.click(p.getByTestId('language-switcher').first(), 1800);
    await d.ask('PERSONNEL', 'ما عدد الموظفين النشطين في كل قطاع؟', { visibleWaitMs: 1200 });
    await d.scrollTo(p.getByTestId('ask-answer'));
    await d.wait(3200);
    const timeline = await d.end();
    await p.getByTestId('language-switcher').first().click();
    await p.waitForTimeout(1500);
    return timeline;
  },

  async history(d) {
    const p = await userPage('w1002');
    await open(p, '/app/dashboard');
    await d.attach(p);
    await d.beat(0);
    await d.nav('history');
    await d.spot(p.getByTestId('history-table'));
    await d.wait(2600);
    await d.unspot();

    await d.beat(1);
    await d.click(p.locator('[data-testid^="history-row-"]', { hasText: 'Total deposit balances' }).first(), 1800);
    await d.spot(p.getByTestId('history-versions'));
    await d.wait(1200);
    await d.unspot();
    const versions = await p.locator('[data-testid^="history-version-"]').count();
    await d.click(p.getByTestId('history-rerun'), 1500);
    d.cut(true);
    await p.getByTestId(`history-version-${versions + 1}`).waitFor({ timeout: 300000 });
    await p.waitForTimeout(1500);
    d.cut(false);
    await d.spot(p.getByTestId('history-versions'));
    await d.callout('New version added, earlier versions kept', 'history');
    await d.wait(2400);
    await d.callout(null); await d.unspot();

    await d.beat(2);
    await d.nav('history');
    await d.click(p.locator('[data-testid^="history-row-"]', { hasText: 'heque' }).first(), 1800);
    await d.click(p.getByTestId('history-favorite'), 900);
    const exports = {};
    for (const [fmt, id] of [['docx', 'history-export-docx'], ['xlsx', 'history-export-xlsx']]) {
      await d.click(p.getByTestId('history-export'), 700);
      exports[fmt] = await download(p, () => d.click(p.getByTestId(id), 300));
      await d.wait(900);
    }
    renderFile(exports.docx, 'docx', 1);
    renderFile(exports.xlsx, 'xlsx', 1);
    await d.nav('history');
    await d.type(p.getByTestId('collection-name'), 'Board pack');
    await d.click(p.getByTestId('collection-create'), 1200);
    await d.spot(p.getByTestId('history-collections'));
    await d.wait(1500);
    await d.unspot();
    return d.end();
  },

  async share(d) {
    const p = await userPage('w1002');
    const martin = await userPage('w1006');
    await open(martin, '/app/shared-with-me');
    await open(p, '/app/history');
    await p.locator('[data-testid^="history-row-"]', { hasText: 'heque' }).first().click();
    await p.getByTestId('history-share').waitFor();
    await p.waitForTimeout(1200);
    await d.attach(p);
    await d.beat(0);
    await d.click(p.getByTestId('history-share'), 1200);
    await d.type(p.getByTestId('share-search'), 'Martin');
    await d.wait(1200);
    await d.click(p.locator('[data-testid^="share-recipient-"]').first(), 600);
    await d.type(p.getByTestId('share-message'), 'Please review before Sunday\'s board meeting.');
    await d.select(p.getByTestId('share-expiry'), '30');
    await d.click(p.getByTestId('share-submit'), 1500);
    await martin.reload(); await martin.waitForLoadState('networkidle').catch(() => {}); await martin.waitForTimeout(1500);
    await d.attach(martin);
    await d.spot(martin.getByTestId('shared-with-me-table'));
    await d.callout('Shared with Martin Calder, budget controller', 'share');
    await d.wait(2200);
    await d.callout(null); await d.unspot();

    await d.beat(1);
    await d.attach(p);
    await p.keyboard.press('Escape').catch(() => {});
    await d.click(p.getByTestId('history-email'), 1400);
    await d.spot(p.getByTestId('email-classification'));
    await d.wait(1600);
    await d.unspot();
    await d.type(p.getByTestId('email-recipients'), 'board.secretary@wabibank.example');
    await d.wait(600);
    await d.spot(p.locator('mat-dialog-container').first(), 4);
    await d.callout('Classification checked before anything is sent', 'policy');
    await d.wait(2600);
    await d.callout(null); await d.unspot();
    await p.keyboard.press('Escape');
    await d.wait(600);
    return d.end();
  },

  async security(d) {
    const bm = await userPage('w1003');
    const teller = await userPage('w1004');
    const hr = await userPage('w1005');
    const admin = await userPage('w1001');
    for (const page of [bm, teller, hr]) await open(page, '/app/ask');
    await open(admin, '/app/admin/support-access');

    await d.attach(bm);
    await d.beat(0);
    await d.ask('CORE-BANKING', 'Cash deposits and withdrawals per teller in December 2025');
    await d.scrollTo(bm.getByTestId('ask-answer'));
    await d.spot(bm.getByTestId('ask-access'));
    await d.callout('Daniel Hartwell · Deira branch only', 'store');
    await d.wait(2400);
    await d.callout(null); await d.unspot();

    await d.beat(1);
    await d.attach(teller);
    await d.ask('CORE-BANKING', 'My cash deposits and withdrawals per day in December 2025');
    await d.scrollTo(teller.getByTestId('ask-answer'));
    await d.spot(teller.getByTestId('ask-access'));
    await d.callout('Emily Carver · her own postings only', 'person');
    await d.wait(2200);
    await d.callout(null); await d.unspot();

    await d.beat(2);
    await d.attach(hr);
    await d.ask('PERSONNEL', 'List the employees of the HR department with their Emirates ID numbers');
    await d.scrollTo(hr.getByTestId('ask-answer'));
    await d.spot(hr.getByTestId('ask-narrative'));
    await d.callout('Restricted column: never shown to the AI', 'visibility_off');
    await d.wait(2600);
    await d.callout(null); await d.unspot();

    await d.beat(3);
    await d.attach(admin);
    await d.callout('Organizations separated · every action audited', 'verified_user');
    await d.wait(2200);
    await d.spot(admin.getByTestId('grant-table'));
    await d.callout('Platform support enters only with your approval', 'admin_panel_settings');
    await d.wait(2600);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async rules(d) {
    const p = await userPage('w1002');
    await open(p, '/app/monitoring/rules');
    await d.attach(p);
    await d.beat(0);
    await d.spot(p.getByTestId('rules-table'));
    await d.wait(2600);
    await d.unspot();

    await d.beat(1);
    await d.click(p.getByTestId('rules-new'), 1000);
    await d.scrollTo(p.getByTestId('rules-describe'), 'center');
    await d.type(p.getByTestId('rules-describe-text'), 'Alert me when a branch has more than 20 returned inward cheques in a single day');
    await d.click(p.getByTestId('rules-describe-go'), 1200);
    d.cut(true);
    await p.getByTestId('rules-draft').waitFor({ timeout: 180000 });
    await p.waitForTimeout(1000);
    d.cut(false);
    await d.scrollTo(p.getByTestId('rules-draft'), 'center');
    await d.spot(p.getByTestId('rules-draft'));
    await d.wait(2200);
    await d.unspot();
    await d.click(p.getByTestId('rules-draft-review'), 1200);
    await d.scrollTo(p.getByTestId('rule-form'), 'start');
    await d.spot(p.getByTestId('rule-form'));
    await d.wait(1600);
    await d.unspot();

    await d.beat(2);
    const bindings = p.getByTestId('rule-draft-bindings');
    const focus = (await bindings.count()) ? bindings : p.getByTestId('rule-form');
    await d.scrollTo(focus, 'center');
    await d.spot(focus, 10);
    await d.callout('Runs under its owner\'s permissions', 'shield');
    await d.wait(1800);
    await d.callout(null); await d.unspot();
    await d.click(p.getByTestId('rule-save'), 2000);
    await d.scrollTop();
    await d.spot(p.getByTestId('rules-table'));
    await d.wait(1800);
    await d.unspot();
    return d.end();
  },

  async alerts(d) {
    const p = await userPage('w1007');
    await open(p, '/app/alerts');
    await d.attach(p);
    await d.beat(0);
    await d.spot(p.getByTestId('alerts-list'));
    await d.wait(1500);
    await d.unspot();
    await d.spot(p.getByTestId('notifications-bell'), 8);
    await d.wait(1400);
    await d.unspot();

    await d.beat(1);
    await d.click(p.locator('[data-testid^="alert-"]', { hasText: 'Cash deposits' }).first(), 1500);
    await d.spot(p.getByTestId('alert-detail'));
    await d.wait(3000);
    await d.unspot();

    await d.beat(2);
    await d.type(p.getByTestId('alert-note'), 'Reviewed with the branch. Filing a suspicious transaction report.');
    await d.click(p.getByTestId('alert-ack'), 1500);
    await d.callout('Alerts never block anyone automatically', 'front_hand');
    await d.wait(2400);
    await d.callout(null);

    await d.beat(3);
    const kyc = p.locator('[data-testid^="alert-"]', { hasText: 'KYC' }).first();
    if (await kyc.count()) {
      await d.click(kyc, 1500);
      await d.spot(p.getByTestId('alert-detail'));
      await d.wait(2400);
      await d.unspot();
    }
    await d.click(p.getByTestId('alerts-digest'), 1500);
    await d.spot(p.getByTestId('alerts-digest-preview'));
    await d.wait(2600);
    await d.unspot();
    return d.end();
  },

  async periodic(d) {
    const p = await userPage('w1002');
    await open(p, '/app/periodic-reports');
    await d.attach(p);
    await d.beat(0);
    await d.click(p.getByTestId('periodic-new'), 1000);
    await d.select(p.getByTestId('periodic-analysis'), 'heque');
    const name = p.getByTestId('periodic-name');
    await name.fill('');
    await d.type(name, 'Weekly cheque returns for the board');
    await d.select(p.locator('[data-testid="periodic-form"] mat-select[name="recurrence"]'), 'Weekly');
    await d.select(p.locator('[data-testid="periodic-form"] mat-select[name="format"]'), 'PDF');
    await d.click(p.getByTestId('periodic-recipients'), 600);
    for (const who of ['Martin Calder', 'Clara Ashby']) await d.click(p.locator('mat-option', { hasText: who }).first(), 400);
    await p.keyboard.press('Escape');
    await d.spot(p.getByTestId('periodic-form'));
    await d.wait(1500);
    await d.unspot();
    await d.click(p.getByTestId('periodic-save'), 2000);

    await d.beat(1);
    await d.click(p.locator('[data-testid^="periodic-run-"]').first(), 1000);
    d.cut(true);
    await p.getByTestId('periodic-run-result').waitFor({ timeout: 300000 });
    await p.waitForTimeout(1000);
    d.cut(false);
    await d.scrollTo(p.getByTestId('periodic-run-result'), 'center');
    await d.spot(p.getByTestId('periodic-run-result'));
    await d.callout('Each recipient: their own permissions', 'groups');
    await d.wait(2600);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async insights(d) {
    const p = await userPage('w1001');
    await open(p, '/app/admin/insights');
    await d.attach(p);
    await d.beat(0);
    await d.click(p.getByTestId('oins-metrics').locator('button, li, a', { hasText: 'Users who signed in' }).first(), 800);
    await d.click(p.getByTestId('oins-run'), 1800);
    await d.spot(p.getByTestId('oins-result'));
    await d.wait(2400);
    await d.unspot();

    await d.beat(1);
    await d.nav('support');
    await d.click(p.getByTestId('ticket-new'), 1500);
    await d.type(p.getByTestId('ticket-subject'), 'Connect our new Treasury system');
    await d.type(p.getByTestId('ticket-description'), 'Please help us add the Treasury database as a new application.', 14);
    await d.spot(p.getByTestId('ticket-form'));
    await d.wait(1600);
    await d.unspot();
    return d.end();
  },
};

// ------------------------------------------------------------------------------------------------ main
(async () => {
  prepareGraphics();
  browser = await chromium.launch();
  const only = ONLY ? ONLY.split(',') : null;
  for (const scene of SCRIPT.scenes) {
    if (only && !only.includes(scene.id)) continue;
    const started = Date.now();
    let timeline;
    if (scene.kind === 'graphic') {
      timeline = await graphicScene(scene);
    } else {
      const d = new Director(scene);
      timeline = await SCENES[scene.id](d);
    }
    const cut = timeline.cuts.reduce((s, [a, b]) => s + (b - a), 0);
    console.log(`${scene.id}: ${(timeline.end - timeline.start - cut).toFixed(1)} s visible, ${timeline.frames.length} frames, ${cut.toFixed(0)} s cut (${((Date.now() - started) / 1000).toFixed(0)} s)`);
  }
  await browser.close();
})().catch(async (e) => { console.error(e); if (browser) await browser.close(); process.exit(1); });
