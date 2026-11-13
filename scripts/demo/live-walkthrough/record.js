#!/usr/bin/env node
/* eslint-disable */
/**
 * Wabi Insight — walkthrough of the DEPLOYED site (https://insight.wabitex.com), recorded on a desktop screen with a
 * phone shown in a few scenes. Follows script.<lang>.json scene by scene with the sample organization (GAS): the
 * organization administrator for the short set-up scene, the management user for everything else. Same method as
 * ../walkthrough (real pages, real answers; screen frames + a timeline per scene), and the same composer:
 *
 *   python3 ../walkthrough/tts.py --script script.ar.json --out <dir>            # narration (Azure Speech)
 *   WI_DEMO_PASSWORD='<password of the two sample accounts>' node record.js --out <dir> --lang ar [--only ask,how]
 *   python3 ../walkthrough/compose.py --script script.ar.json --work <dir> --video <file>.mp4
 *
 * The recording creates a few demo records in that organization: new answers, one more version, a favourite, a
 * collection and a share between two sample users. Nothing is e-mailed, saved as a rule or scheduled.
 * Behind an HTTPS proxy (HTTPS_PROXY) the browser uses it.
 */
const { chromium } = require('playwright');
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const LANG = opt('--lang', 'ar');
const HERE = __dirname;
const SHARED = path.resolve(HERE, '../walkthrough/graphics');
const OUT = path.resolve(opt('--out', path.join(HERE, 'out', LANG)));
const ONLY = opt('--only', null);
const BASE = process.env.WI_BASE_URL || 'https://insight.wabitex.com';
const PASSWORD = process.env.WI_DEMO_PASSWORD;
const WEB = path.resolve(HERE, '../../../src/web');
const SCRIPT = JSON.parse(fs.readFileSync(path.join(HERE, `script.${LANG}.json`), 'utf-8'));
const DURATIONS = JSON.parse(fs.readFileSync(path.join(OUT, 'durations.json'), 'utf-8'));
const SIZE = { width: 1920, height: 1080 };
const PHONE = { width: 390, height: 844 };
const GAP = 0.55;      // silence after each narration line, in seconds
const LEAD = 0.25;     // the voice starts this long after a line's mark
if (!PASSWORD) throw new Error('Set WI_DEMO_PASSWORD (the password of the two sample accounts).');
const USERS = { admin: 'faisal.alshamsi@gulfaviation.example', manager: 'khalid.alsuwaidi@gulfaviation.example' };
/** Everything the scenes type, choose or show in callouts, per language of the video. */
const TEXT = {
  ar: {
    qFlights: 'اعرض عدد المسافرين الشهري ومتوسط نسبة الإشغال لكل شركة طيران خلال عام 2025',
    qPayroll: 'اعرض إجمالي تكلفة الرواتب لكل إدارة خلال عام 2025 مع رسم بياني',
    payrollRow: /الرواتب/,
    colleague: 'Sara',
    colleagueEmail: 'sara.khoury@gulfaviation.example',
    shareMessage: 'للاطلاع قبل اجتماع الإدارة يوم الأحد.',
    collection: 'اجتماع الإدارة',
    ticketSubject: 'ربط نظام جديد',
    c: {
      readOnly: 'للقراءة فقط · كلٌّ يرى ما هو مصرَّح له به', encrypted: 'اتصال مشفّر ومُختبر وللقراءة فقط', yours: 'الإعداد ملك لمؤسستكم وحدها',
      support: 'الدعم يدخل بموافقتكم فقط، ولا يرى بياناتكم', signin: 'اتصال مشفّر · جلسة آمنة · حماية من تكرار المحاولات', reset: 'رابط إعادة التعيين صالح لثلاثين دقيقة',
      own: 'كل ما هنا يخصّ هذا المستخدم وحده', plan: 'الذكاء الاصطناعي يقترح خطة فقط، ولا يكتب أي استعلام',
      checked: 'خطة مُتحقَّق منها · صلاحيات السائل · استعلام للقراءة فقط', scopes: 'المدير: المؤسسة كلها · رئيس القسم: قسمه · الموظف: سجلاته',
      always: 'تُطبَّق تلقائيًا، في كل مرة', anyScreen: 'الإجابة بلغة السؤال، وعلى أي شاشة', version: 'إصدار جديد مع الاحتفاظ بالسابق',
      recipient: 'يُتحقق مما يحق للمستلم أن يراه قبل الإرسال', noBypass: 'المشاركة لا تمنح أحدًا بيانات ليست من حقّه',
      owner: 'القاعدة تعمل بصلاحيات صاحبها ولا تتجاوزها', human: 'التنبيه يُعلِم فقط · القرار دائمًا بيد الإنسان', each: 'كل مستلم يتلقى نسخته وفق صلاحياته',
    },
  },
};
const T = TEXT[LANG];
if (!T) throw new Error(`No recorder texts for --lang ${LANG}`);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const now = () => Date.now() / 1000;

// ------------------------------------------------------------------------------------------------ on-screen layer
/** Injected into every app page: fake cursor, click ripple, spotlight, chapter label, callout card and a file gallery. */
const OVERLAY = `(() => {
  if (window.__wi) return;
  const css = \`
  #wi-ov{position:fixed;inset:0;pointer-events:none;z-index:2147483000;font-family:Inter,'IBM Plex Sans Arabic',system-ui,sans-serif}
  #wi-ov .cursor{position:absolute;left:0;top:0;width:30px;height:38px;transform:translate(1560px,760px);transition:transform .65s cubic-bezier(.3,.7,.2,1);filter:drop-shadow(0 4px 6px rgba(0,0,0,.35));z-index:5}
  #wi-ov .ripple{position:absolute;width:60px;height:60px;margin:-30px 0 0 -30px;border-radius:50%;border:3px solid #22b8cf;animation:wiRip .65s ease-out forwards;z-index:4}
  @keyframes wiRip{from{transform:scale(.2);opacity:1}to{transform:scale(1.5);opacity:0}}
  #wi-ov .spot{position:absolute;border-radius:14px;box-shadow:0 0 0 4px rgba(34,184,207,.95),0 0 0 9999px rgba(6,20,42,.34),0 0 40px 10px rgba(34,184,207,.45);transition:all .55s cubic-bezier(.2,.8,.2,1);opacity:0}
  #wi-ov .spot.on{opacity:1}
  #wi-ov .chapter{position:absolute;left:26px;bottom:24px;display:flex;align-items:center;gap:12px;padding:11px 22px 11px 12px;border-radius:999px;background:linear-gradient(90deg,#0b1f3a,#14427a);color:#fff;font:600 21px/1.2 Inter,'IBM Plex Sans Arabic',sans-serif;box-shadow:0 10px 30px rgba(11,31,58,.35);transform:translateY(130px);transition:transform .8s cubic-bezier(.2,.8,.2,1);z-index:3}
  #wi-ov .chapter.on{transform:none}
  #wi-ov .chapter b{display:grid;place-items:center;min-width:32px;height:32px;padding:0 8px;border-radius:999px;background:linear-gradient(135deg,#087df2,#22b8cf);font:800 16px/1 Inter,sans-serif}
  #wi-ov .callout{position:absolute;right:30px;bottom:28px;max-width:560px;display:flex;gap:14px;align-items:center;padding:18px 24px;border-radius:18px;background:rgba(11,31,58,.95);color:#fff;font:600 22px/1.4 Inter,'IBM Plex Sans Arabic',sans-serif;box-shadow:0 16px 40px rgba(0,0,0,.3);border:1px solid rgba(34,184,207,.6);opacity:0;transform:translateY(24px);transition:all .6s cubic-bezier(.2,.8,.2,1);z-index:3}
  #wi-ov .callout.on{opacity:1;transform:none}
  #wi-ov .callout i{font-family:'Material Icons';font-style:normal;font-size:30px;color:#22b8cf;line-height:1}
  #wi-ov .gallery{position:absolute;inset:0;background:rgba(6,20,42,.86);display:flex;align-items:center;justify-content:center;gap:36px;opacity:0;transition:opacity .6s;z-index:2}
  #wi-ov .gallery.on{opacity:1}
  #wi-ov .gallery img{height:840px;border-radius:8px;box-shadow:0 30px 70px rgba(0,0,0,.55);transform:translateY(50px) scale(.95);opacity:0;transition:all .9s cubic-bezier(.2,.8,.2,1)}
  #wi-ov .gallery.on img{transform:none;opacity:1}
  #wi-ov .tick{position:absolute;left:0;top:0;width:2px;height:2px;opacity:.01}
  #wi-ov.rtl .chapter{left:auto;right:26px;padding:11px 12px 11px 22px}
  #wi-ov.rtl .callout{right:auto;left:30px}\`;
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
    chapter(num, text) { ensure(); root.classList.toggle('rtl', document.documentElement.dir === 'rtl'); chapter.querySelector('b').textContent = num; chapter.querySelector('b').style.display = num ? '' : 'none'; chapter.querySelector('span').textContent = text; chapter.classList.add('on'); },
    callout(text, icon) { ensure(); root.classList.toggle('rtl', document.documentElement.dir === 'rtl'); if (!text) { callout.classList.remove('on'); return; } callout.querySelector('i').textContent = icon || 'info'; callout.querySelector('span').textContent = text; callout.querySelector('span').dir = 'auto'; callout.classList.add('on'); },
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
    await l.waitFor({ state: 'visible', timeout: this.patience ?? 30000 });
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
  /** Waits until `seconds` of the current narration line have been seen (to place an action inside a line). */
  async at(seconds) { await this.cap.until(this.cap.beats[this.current], seconds); }
  /** Optional step: the scene goes on when the element is not there. */
  async maybe(step) { this.patience = 5000; try { await step(); } catch (e) { console.warn(`  ${this.scene.id}: skipped a step (${String(e.message).split('\n')[0]})`); } finally { this.patience = undefined; } }
  async phone(file, ms = 4200) {
    const src = 'data:image/png;base64,' + fs.readFileSync(file).toString('base64');
    await this.page.evaluate((s) => window.__wiPhone.show(s), src);
    await this.page.waitForTimeout(ms);
    await this.page.evaluate(() => window.__wiPhone.hide());
    await this.page.waitForTimeout(700);
  }
}

/** A phone frame that slides in over the desktop page with a real phone-sized screenshot of the same site. */
const PHONE_OVERLAY = `(() => {
  if (window.__wiPhone) return;
  let el = null;
  function ensure() {
    if (el && document.body.contains(el)) return;
    const style = document.createElement('style');
    style.textContent = '#wi-phone{position:fixed;inset:0;z-index:2147483600;pointer-events:none;background:rgba(6,20,42,0);transition:background .6s}#wi-phone.on{background:rgba(6,20,42,.55)}' +
      '#wi-phone .f{position:absolute;left:50%;top:50%;width:400px;margin-left:-200px;padding:14px;border-radius:54px;background:linear-gradient(#1b2433,#0c121c);box-shadow:0 50px 120px rgba(0,0,0,.6),inset 0 0 0 2px rgba(255,255,255,.12);transform:translateY(80vh);transition:transform .8s cubic-bezier(.2,.8,.2,1)}' +
      '#wi-phone.on .f{transform:translateY(-50%)}#wi-phone .f:before{content:"";position:absolute;left:50%;top:26px;width:110px;height:28px;margin-left:-55px;border-radius:16px;background:#05090f;z-index:2}' +
      '#wi-phone img{display:block;width:100%;border-radius:42px}';
    document.head.appendChild(style);
    el = document.createElement('div'); el.id = 'wi-phone'; el.innerHTML = '<div class="f"><img></div>';
    document.body.appendChild(el);
  }
  window.__wiPhone = {
    show(src) { ensure(); el.querySelector('img').src = src; requestAnimationFrame(() => requestAnimationFrame(() => el.classList.add('on'))); },
    hide() { ensure(); el.classList.remove('on'); },
  };
})();`;

// ------------------------------------------------------------------------------------------------ browser, users
let browser;
const sessions = {};
const contextOptions = { viewport: SIZE, deviceScaleFactor: 1, locale: 'en-US', timezoneId: 'Asia/Dubai', acceptDownloads: true };

async function newContext(options = {}) {
  const context = await browser.newContext({ ...contextOptions, ...options });
  await context.addInitScript(OVERLAY);
  await context.addInitScript(PHONE_OVERLAY);
  await context.addInitScript((lang) => { try { if (!sessionStorage.getItem('wi.walkthrough')) { localStorage.setItem('wi.lang', lang); sessionStorage.setItem('wi.walkthrough', '1'); } } catch {} }, LANG);
  return context;
}

async function signIn(page, email) {
  for (let attempt = 0; attempt < 3; attempt++) {
    await page.goto(`${BASE}/login`);
    await page.getByTestId('login-email').fill(email);
    await page.getByTestId('login-password').fill(PASSWORD);
    await page.getByTestId('login-submit').click();
    const ok = await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30000 }).then(() => true).catch(() => false);
    if (ok) return;
    if (attempt === 2) throw new Error(`${email}: sign-in failed`);
    await sleep(65000); // sign-in rate limit
  }
}

async function userPage(user) {
  if (sessions[user]) return sessions[user];
  const page = await (await newContext()).newPage();
  await signIn(page, USERS[user]);
  await language(page);
  sessions[user] = page;
  return page;
}

/** Keeps the interface in the video's language (the app applies each user's saved language on every page load). */
async function language(page, lang = LANG) {
  await page.waitForTimeout(1500);
  for (let i = 0; i < 2 && (await page.locator('html').getAttribute('lang')) !== lang; i++) {
    await page.getByTestId('language-switcher').first().click();
    await page.waitForTimeout(1500);
  }
}

async function open(page, url) {
  await page.goto(`${BASE}${url}`);
  await page.waitForLoadState('networkidle').catch(() => {});
  if (url.startsWith('/app')) await language(page);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(800);
}

/** A phone-sized screenshot of a page of the live site; with a user, in that user's session. */
async function phoneShot(name, url, user, prepare) {
  const dir = path.join(OUT, 'graphics', 'shots');
  fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, `${name}.png`);
  const storageState = user ? await (await userPage(user)).context().storageState() : undefined;
  const context = await browser.newContext({ viewport: PHONE, deviceScaleFactor: 2, isMobile: true, hasTouch: true, locale: 'en-US', timezoneId: 'Asia/Dubai', storageState });
  await context.addInitScript((lang) => { try { localStorage.setItem('wi.lang', lang); } catch {} }, LANG);
  const page = await context.newPage();
  await page.goto(`${BASE}${url}`);
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(2500);
  if (prepare) await prepare(page);
  await page.screenshot({ path: file });
  await context.close();
  return file;
}

/** PNG pages of a PDF, Word or Excel file (LibreOffice converts Word and Excel to PDF first). */
function renderFile(file, prefix) {
  const dir = path.join(OUT, 'graphics', 'files');
  fs.mkdirSync(dir, { recursive: true });
  let pdf = file;
  if (!file.toLowerCase().endsWith('.pdf')) {
    execFileSync('soffice', ['--headless', '--convert-to', 'pdf', '--outdir', path.dirname(file), file], { stdio: 'ignore', timeout: 180000 });
    pdf = file.replace(/\.[^.]+$/, '.pdf');
  }
  execFileSync('pdftoppm', ['-png', '-r', '80', '-f', '1', '-l', '1', '-singlefile', pdf, path.join(dir, prefix)], { stdio: 'ignore' });
  return path.join(dir, `${prefix}.png`);
}

async function download(page, click, name) {
  const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 120000 }), click()]);
  const dir = path.join(OUT, 'downloads');
  fs.mkdirSync(dir, { recursive: true });
  const file = path.join(dir, name);
  await dl.saveAs(file);
  return file;
}

/** Opens the newest answer in My History whose title matches (the scenes are recorded independently). */
async function openAnswer(page, match) {
  await open(page, '/app/history');
  await page.locator('[data-testid^="history-row-"]', match ? { hasText: match } : {}).first().click();
  await page.getByTestId('history-share').waitFor({ timeout: 30000 });
  await page.waitForTimeout(1500);
}

// ------------------------------------------------------------------------------------------------ graphic scenes
async function prepareGraphics() {
  const dir = path.join(OUT, 'graphics');
  fs.mkdirSync(path.join(dir, 'shots'), { recursive: true });
  const c = SCRIPT.contact;
  const fill = (text) => text.replaceAll('__WEB__', WEB).replaceAll('__WEBSITE__', c.website).replaceAll('__EMAIL__', c.email)
    .replaceAll('__COMPANY_AR__', c.companyAr).replaceAll('__COMPANY__', c.company).replaceAll('__MOBILE__', c.mobile)
    .replaceAll('__PHONE__', c.phone).replaceAll('__LOCATION__', c.location);
  for (const f of [path.join(SHARED, 'base.css'), path.join(SHARED, 'stage.js'), path.join(SHARED, 'ar', 'base-ar.css'),
    ...fs.readdirSync(path.join(HERE, 'graphics')).map((f) => path.join(HERE, 'graphics', f))]) {
    fs.writeFileSync(path.join(dir, path.basename(f)), fill(fs.readFileSync(f, 'utf-8')));
  }
  execFileSync('python3', [path.join(HERE, 'make_qr.py'), '--script', path.join(HERE, `script.${LANG}.json`), '--out', path.join(dir, 'qr.svg')], { stdio: 'ignore' });
  // The intro shows the live landing page on a laptop and on a phone.
  const shots = path.join(dir, 'shots');
  if (!fs.existsSync(path.join(shots, 'landing-desktop.png'))) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
    await context.addInitScript((lang) => { try { localStorage.setItem('wi.lang', lang); } catch {} }, LANG);
    const page = await context.newPage();
    await page.goto(`${BASE}/`);
    await page.waitForLoadState('networkidle').catch(() => {});
    await page.waitForTimeout(2500);
    await page.screenshot({ path: path.join(shots, 'landing-desktop.png') });
    await context.close();
  }
  if (!fs.existsSync(path.join(shots, 'landing-mobile.png'))) await phoneShot('landing-mobile', '/');
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
  const timeline = await d.end(scene.tail ?? 1.4);   // "tail": seconds the last frame is held after the voice
  await context.close();
  return timeline;
}

// ------------------------------------------------------------------------------------------------ app scenes
const SCENES = {
  async landing(d) {
    const p = await (await newContext()).newPage();
    await open(p, '/');
    const phone = await phoneShot('landing-mobile', '/');
    await d.attach(p);
    await d.beat(0);
    await d.maybe(() => d.moveTo(p.getByTestId('hero-sign-in')));
    await d.at(6);
    await d.maybe(() => d.scrollTo('#capabilities'));
    await d.at(12);
    await d.phone(phone, 4500);
    await d.beat(1);
    await d.maybe(() => d.scrollTo('#how'));
    await d.beat(2);
    await d.maybe(() => d.scrollTo('#security'));
    await d.callout(T.c.readOnly, 'verified_user');
    await d.at(8);
    await d.callout(null);
    const t = await d.end();
    await p.context().close();
    return t;
  },

  async admin(d) {
    const p = await userPage('admin');
    await open(p, '/app/admin/applications');
    await d.attach(p);
    await d.beat(0);
    await d.spot(p.getByTestId('app-table'));
    await d.at(4);
    await d.unspot();
    await d.maybe(async () => {
      await d.click(p.getByTestId('app-test-stored-FLIGHT-OPS'), 300);
      await p.locator('mat-snack-bar-container').first().waitFor({ timeout: 30000 });
      await d.spot(p.getByTestId('app-row-FLIGHT-OPS'), 6);
    });
    await d.callout(T.c.encrypted, 'lock');
    await d.at(10);
    await d.callout(null); await d.unspot();
    await d.nav('users');
    await d.spot(p.getByTestId('user-table'));
    await d.at(14.5);
    await d.unspot();
    await d.nav('permissions');
    await d.spot(p.getByTestId('perm-matrix'));

    await d.beat(1);
    await d.callout(T.c.yours, 'apartment');
    await d.at(5);
    await d.unspot();
    await d.nav('support-access');
    await d.callout(T.c.support, 'admin_panel_settings');
    await d.at(13.5);
    await d.callout(null);
    return d.end();
  },

  async signin(d) {
    const p = await (await newContext()).newPage();
    await open(p, '/login');
    await d.attach(p);
    await d.beat(0);
    await d.type(p.getByTestId('login-email'), USERS.manager, 30);
    await d.type(p.getByTestId('login-password'), PASSWORD, 60);
    await d.callout(T.c.signin, 'lock');
    await d.beat(1);
    await d.callout(null);
    await d.maybe(() => d.spot(p.locator('a[href*="forgot-password"]').first(), 12));
    await d.callout(T.c.reset, 'mail');
    await d.at(8.5);
    await d.callout(null); await d.unspot();
    await d.click(p.getByTestId('login-submit'), 300);
    d.cut(true);
    await p.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 60000 });
    await p.waitForLoadState('networkidle').catch(() => {});
    await language(p);
    d.cut(false);
    await d.chapter();
    await d.wait(1500);
    const t = await d.end();
    await p.context().close();
    return t;
  },

  async dashboard(d) {
    const p = await userPage('manager');
    await open(p, '/app/dashboard');
    await d.attach(p);
    await d.beat(0);
    await d.maybe(() => d.spot(p.getByTestId('dashboard-recent')));
    await d.at(6);
    await d.unspot();
    await d.maybe(() => d.spot(p.getByTestId('notifications-bell'), 8));
    await d.callout(T.c.own, 'person');
    await d.at(11.5);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async ask(d) {
    const p = await userPage('manager');
    await open(p, '/app/ask');
    await d.attach(p);
    await d.beat(0);
    await d.spot(p.getByTestId('ask-application'), 8);
    await d.at(4.5);
    await d.unspot();
    await d.maybe(() => d.spot(p.getByTestId('ask-examples')));
    await d.at(9);
    await d.unspot();
    await d.ask('FLIGHT-OPS', T.qFlights);

    await d.beat(1);
    await d.scrollTo(p.getByTestId('ask-answer'));
    await d.at(3);
    await d.at(6);
    await d.maybe(() => d.scrollTo(p.getByTestId('ask-table'), 'center'));

    await d.beat(2);
    await d.callout(T.c.plan, 'psychology');
    await d.at(5);
    await d.scrollTo(p.getByTestId('ask-answer'));

    await d.beat(3);
    await d.callout(T.c.checked, 'verified_user');
    await d.at(8);
    await d.maybe(() => d.scrollTo(p.getByTestId('ask-table'), 'center'));
    await d.maybe(() => d.spot(p.getByTestId('ask-table')));
    await d.at(17);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async how(d) {
    const p = await userPage('manager');
    await openAnswer(p, null);
    await d.attach(p);
    await d.beat(0);
    const how = p.getByTestId('ask-how').first();
    await d.scrollTo(how, 'center');
    await d.click(how, 1200);
    await d.scrollTo(how, 'start');
    await d.maybe(() => d.spot(how.locator('xpath=ancestor::mat-expansion-panel[1]')));
    await d.at(14.5);
    await d.unspot();
    return d.end();
  },

  async payroll(d) {
    const p = await userPage('manager');
    await open(p, '/app/ask');
    await d.attach(p);
    await d.beat(0);
    await d.ask('HR-PAYROLL', T.qPayroll);
    await d.maybe(async () => { await d.scrollTo(p.getByTestId('ask-charts'), 'center'); await d.spot(p.getByTestId('ask-charts')); });

    await d.beat(1);
    await d.unspot();
    await d.callout(T.c.scopes, 'groups');
    await d.at(7);
    await d.maybe(() => d.scrollTo(p.getByTestId('ask-table'), 'center'));

    await d.beat(2);
    await d.callout(T.c.always, 'shield');
    await d.at(5.5);
    await d.callout(null);
    return d.end();
  },

  async bilingual(d) {
    const p = await userPage('manager');
    const phone = await phoneShot('answer-mobile', '/app/history', 'manager', async (m) => {
      await m.locator('[data-testid^="history-row-"]', { hasText: T.payrollRow }).first().click();
      await m.getByTestId('ask-answer').waitFor({ timeout: 30000 });
      await m.waitForTimeout(2500);
      await m.getByTestId('ask-answer').evaluate((e) => e.scrollIntoView({ block: 'start' }));
      await m.waitForTimeout(800);
    });
    await open(p, '/app/history');
    await d.attach(p);
    try {
      await d.beat(0);
      await d.at(3.5);
      await d.click(p.getByTestId('language-switcher').first(), 1800);
      await d.spot(p.getByTestId('history-table'));
      await d.at(11);
      await d.unspot();

      await d.beat(1);
      await d.callout(T.c.anyScreen, 'translate');
      await d.at(3);
      await d.phone(phone, 5000);
      await d.click(p.getByTestId('language-switcher').first(), 1500);
      await d.callout(null);
      return await d.end();
    } finally { await language(p); }
  },

  async export(d) {
    const p = await userPage('manager');
    await openAnswer(p, T.payrollRow);
    await d.attach(p);
    await d.beat(0);
    const files = [];
    for (const fmt of ['pdf', 'docx', 'xlsx']) {
      await d.click(p.getByTestId('history-export'), 450);
      const file = await download(p, () => d.click(p.getByTestId(`history-export-${fmt}`), 200), `export.${fmt}`);
      d.cut(true);
      files.push(renderFile(file, fmt));
      d.cut(false);
    }
    // The three files side by side, on screen while the line is still being read.
    await p.evaluate((srcs) => {
      window.__wi.gallery(srcs);
      document.querySelectorAll('#wi-ov .gallery img').forEach((i) => Object.assign(i.style, { height: 'auto', maxHeight: '820px', maxWidth: '570px' }));
    }, files.map((f) => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64')));
    await d.wait(4000);
    await d.at(13.6);
    await p.evaluate(() => window.__wi.ungallery());
    await d.wait(600);
    return d.end();
  },

  async history(d) {
    const p = await userPage('manager');
    await open(p, '/app/history');
    await d.attach(p);
    await d.beat(0);
    await d.spot(p.getByTestId('history-table'));
    await d.at(3);
    await d.unspot();
    await d.click(p.locator('[data-testid^="history-row-"]', { hasText: T.payrollRow }).first(), 1800);
    await p.getByTestId('history-rerun').waitFor({ timeout: 30000 });
    await d.maybe(() => d.spot(p.getByTestId('history-versions')));

    await d.beat(1);
    await d.unspot();
    const versions = await p.locator('[data-testid^="history-version-"]').count();
    await d.click(p.getByTestId('history-rerun'), 1500);
    d.cut(true);
    await p.getByTestId(`history-version-${versions + 1}`).waitFor({ timeout: 300000 });
    await p.waitForTimeout(1500);
    d.cut(false);
    await d.maybe(() => d.spot(p.getByTestId('history-versions')));
    await d.callout(T.c.version, 'history');

    await d.beat(2);
    await d.callout(null); await d.unspot();
    await d.maybe(() => d.click(p.getByTestId('history-favorite'), 900));
    await d.nav('history');
    await d.maybe(async () => {
      if (await p.getByTestId('history-collections').getByText(T.collection).count() === 0) {
        await d.type(p.getByTestId('collection-name'), T.collection);
        await d.click(p.getByTestId('collection-create'), 1200);
      }
      await d.spot(p.getByTestId('history-collections'));
    });
    await d.at(7);
    await d.unspot();
    return d.end();
  },

  async share(d) {
    const p = await userPage('manager');
    await openAnswer(p, T.payrollRow);
    await d.attach(p);
    await d.beat(0);
    await d.click(p.getByTestId('history-share'), 1200);
    await d.type(p.getByTestId('share-search'), T.colleague);
    await d.wait(1200);
    await d.click(p.locator('[data-testid^="share-recipient-"]').first(), 600);
    await d.type(p.getByTestId('share-message'), T.shareMessage, 18);

    await d.beat(1);
    await d.maybe(() => d.spot(p.locator('mat-dialog-container').first(), 4));
    await d.callout(T.c.recipient, 'policy');
    await d.at(5);
    await d.callout(null); await d.unspot();
    await d.click(p.getByTestId('share-submit'), 1800);
    await p.keyboard.press('Escape').catch(() => {});

    await d.beat(2);
    await d.maybe(async () => {
      await d.click(p.getByTestId('history-email'), 1400);
      await d.spot(p.getByTestId('email-classification'));
    });
    await d.callout(T.c.noBypass, 'gpp_good');
    await d.at(5);
    await d.unspot();
    await p.keyboard.press('Escape').catch(() => {});
    await d.wait(500);
    await d.nav('shared-by-me');
    await d.maybe(() => d.spot(p.getByTestId('shared-by-me-table')));
    await d.at(9.5);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async reports(d) {
    const p = await userPage('manager');
    await open(p, '/app/history');
    await d.attach(p);
    await d.beat(0);
    await d.nav('reports');
    await d.spot(p.getByTestId('reports-table'));
    await d.at(5);
    await d.unspot();
    await d.maybe(() => d.moveTo(p.locator('[data-testid^="report-download-"]').first()));
    return d.end();
  },

  async rules(d) {
    const p = await userPage('manager');
    await open(p, '/app/monitoring/rules');
    await d.attach(p);
    await d.beat(0);
    await d.spot(p.getByTestId('rules-table'));
    await d.at(6);
    await d.unspot();
    const row = p.locator('[data-testid^="rule-row-"]').first();
    await d.spot(row, 6);
    await d.callout(T.c.owner, 'shield');
    await d.at(13.5);
    await d.callout(null); await d.unspot();

    await d.beat(1);
    await d.click(p.locator('[data-testid^="rule-test-"]:not([data-testid="rule-test-result"])').first(), 1000);
    d.cut(true);
    await p.getByTestId('rule-test-result').waitFor({ timeout: 180000 });
    await p.waitForTimeout(1000);
    d.cut(false);
    await d.scrollTo(p.getByTestId('rule-test-result'), 'center');
    await d.spot(p.getByTestId('rule-test-result'));
    const phone = await phoneShot('alerts-mobile', '/app/alerts', 'manager');

    await d.beat(2);
    await d.unspot();
    await d.nav('alerts');
    await d.maybe(async () => {
      const first = p.locator('[data-testid="alerts-list"] [data-testid^="alert-"]').first();
      if (await first.count()) { await d.click(first, 1500); await d.spot(p.getByTestId('alert-detail')); }
      else await d.spot(p.getByTestId('alerts-kpis'));
    });
    await d.callout(T.c.human, 'front_hand');
    await d.at(7);
    await d.unspot();
    await d.phone(phone, 4000);
    await d.callout(null);
    return d.end();
  },

  async periodic(d) {
    const p = await userPage('manager');
    await open(p, '/app/periodic-reports');
    await d.attach(p);
    await d.beat(0);
    await d.click(p.getByTestId('periodic-new'), 1200);
    await d.maybe(() => d.select(p.getByTestId('periodic-analysis'), T.payrollRow));
    await d.maybe(() => d.click(p.locator('[data-testid="periodic-form"] mat-select[name="recurrence"]'), 1800));
    await p.keyboard.press('Escape').catch(() => {});
    await d.maybe(() => d.spot(p.getByTestId('periodic-form')));
    await d.callout(T.c.each, 'groups');
    await d.at(14.5);
    await d.callout(null); await d.unspot();
    return d.end();
  },

  async more(d) {
    const p = await userPage('manager');
    await open(p, '/app/periodic-reports');
    await d.attach(p);
    await d.beat(0);
    await d.nav('notifications');
    await d.maybe(() => d.spot(p.getByTestId('notification-list')));
    await d.at(3.6);
    await d.unspot();
    await d.nav('support');
    await d.maybe(async () => {
      await d.click(p.getByTestId('ticket-new'), 1200);
      await d.type(p.getByTestId('ticket-subject'), T.ticketSubject);
      await d.spot(p.getByTestId('ticket-form'));
    });
    await d.at(8.2);
    await d.unspot();
    await d.nav('profile');
    await d.maybe(() => d.spot(p.getByTestId('profile-card')));
    await d.at(11.6);
    await d.unspot();
    return d.end();
  },
};

// ------------------------------------------------------------------------------------------------ main
(async () => {
  // A UTF-8 locale lets the browser keep non-Latin download names (Arabic analysis titles) instead of "download".
  browser = await chromium.launch({
    env: { ...process.env, LANG: 'C.UTF-8', LC_ALL: 'C.UTF-8' },
    proxy: process.env.HTTPS_PROXY ? { server: process.env.HTTPS_PROXY } : undefined,
  });
  await prepareGraphics();
  const only = ONLY ? ONLY.split(',') : null;
  for (const scene of SCRIPT.scenes) {
    if (only && !only.includes(scene.id)) continue;
    const started = Date.now();
    const timeline = scene.kind === 'graphic' ? await graphicScene(scene) : await SCENES[scene.id](new Director(scene));
    const cut = timeline.cuts.reduce((s, [a, b]) => s + (b - a), 0);
    console.log(`${scene.id}: ${(timeline.end - timeline.start - cut).toFixed(1)} s visible, ${timeline.frames.length} frames, ${cut.toFixed(0)} s cut (${((Date.now() - started) / 1000).toFixed(0)} s)`);
  }
  await browser.close();
})().catch(async (e) => { console.error(e); if (browser) await browser.close(); process.exit(1); });
