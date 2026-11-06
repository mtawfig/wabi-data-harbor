#!/usr/bin/env node
/**
 * Wabi Insight — load check of the hot read endpoints (P12.2). Signs in once per virtual user, then fires
 * requests concurrently for a fixed duration and reports throughput and latency percentiles per endpoint.
 * It needs nothing beyond Node 22 (built-in fetch). It never calls the AI endpoints (they cost money).
 *
 *   node scripts/perf/load-check.js --base http://localhost:5080 --users 20 --logins 5 --seconds 20 \
 *     --email omar.hassan@acme.example --password 'Omar#Strong#Pass#2026'
 */
const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const BASE = opt('--base', 'http://localhost:5080');
const USERS = Number(opt('--users', 20));
// Sign-ins are rate limited per client address (Security:LoginRateLimitPerMinute, default 10), so virtual users share a few sessions.
const LOGINS = Math.min(USERS, Number(opt('--logins', 5)));
const SECONDS = Number(opt('--seconds', 20));
const EMAIL = opt('--email', process.env.WI_USER_EMAIL || 'omar.hassan@acme.example');
const PASSWORD = opt('--password', process.env.WI_USER_PASSWORD || 'Omar#Strong#Pass#2026');
const ENDPOINTS = (opt('--endpoints', '/api/auth/me,/api/notifications/unread-count,/api/history?pageSize=25,/api/history/reports?pageSize=25,/api/shares/with-me,/api/ai/applications,/api/permissions/me')).split(',');

class Session {
  constructor() { this.cookies = new Map(); this.csrf = null; this.csrfHeader = 'X-XSRF-TOKEN'; }
  cookieHeader() { return [...this.cookies.entries()].map(([k, v]) => `${k}=${v}`).join('; '); }
  absorb(response) {
    for (const line of response.headers.getSetCookie?.() ?? []) { const [pair] = line.split(';'); const i = pair.indexOf('='); this.cookies.set(pair.slice(0, i).trim(), pair.slice(i + 1).trim()); }
  }
  async fetch(path, init = {}) {
    const headers = { ...(init.headers || {}), cookie: this.cookieHeader() };
    if (this.csrf) headers[this.csrfHeader] = this.csrf;
    const response = await fetch(BASE + path, { ...init, headers, redirect: 'manual' });
    this.absorb(response);
    return response;
  }
  async login() {
    let r = await this.fetch('/api/auth/csrf'); let j = await r.json(); this.csrf = j.token; this.csrfHeader = j.headerName || this.csrfHeader;
    r = await this.fetch('/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: EMAIL, password: PASSWORD }) });
    if (!r.ok) throw new Error(`login failed: ${r.status}`);
    r = await this.fetch('/api/auth/csrf'); j = await r.json(); this.csrf = j.token;
  }
}

const percentile = (sorted, p) => sorted.length ? sorted[Math.min(sorted.length - 1, Math.floor(p * sorted.length))] : 0;

(async () => {
  const logins = [];
  for (let i = 0; i < LOGINS; i++) { const s = new Session(); await s.login(); logins.push(s); }
  const sessions = Array.from({ length: USERS }, (_, i) => logins[i % LOGINS]);
  const stats = Object.fromEntries(ENDPOINTS.map((e) => [e, { latencies: [], errors: 0, statuses: {} }]));
  const deadline = Date.now() + SECONDS * 1000;
  let total = 0;
  await Promise.all(sessions.map(async (session, index) => {
    let k = index;
    while (Date.now() < deadline) {
      const endpoint = ENDPOINTS[k++ % ENDPOINTS.length];
      const started = performance.now();
      try {
        const r = await session.fetch(endpoint);
        await r.arrayBuffer();
        const ms = performance.now() - started;
        stats[endpoint].latencies.push(ms);
        stats[endpoint].statuses[r.status] = (stats[endpoint].statuses[r.status] || 0) + 1;
        if (!r.ok) stats[endpoint].errors++;
      } catch { stats[endpoint].errors++; }
      total++;
    }
  }));
  const rows = ENDPOINTS.map((e) => {
    const l = stats[e].latencies.sort((a, b) => a - b);
    return { endpoint: e, requests: l.length, errors: stats[e].errors, rps: (l.length / SECONDS).toFixed(1), p50: percentile(l, 0.5).toFixed(1), p95: percentile(l, 0.95).toFixed(1), p99: percentile(l, 0.99).toFixed(1), max: (l[l.length - 1] || 0).toFixed(1), statuses: JSON.stringify(stats[e].statuses) };
  });
  console.log(`Base ${BASE} · ${USERS} virtual users · ${SECONDS} s · ${total} requests (${(total / SECONDS).toFixed(1)} req/s overall)`);
  console.log('| Endpoint | Requests | Errors | req/s | p50 ms | p95 ms | p99 ms | max ms |');
  console.log('|---|---|---|---|---|---|---|---|');
  for (const r of rows) console.log(`| \`${r.endpoint}\` | ${r.requests} | ${r.errors} | ${r.rps} | ${r.p50} | ${r.p95} | ${r.p99} | ${r.max} |`);
  const bad = rows.filter((r) => r.errors > 0);
  if (bad.length) { console.log('Non-2xx responses:', bad.map((r) => `${r.endpoint} ${r.statuses}`).join('; ')); process.exit(1); }
})().catch((e) => { console.error(e); process.exit(1); });
