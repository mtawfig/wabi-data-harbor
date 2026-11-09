#!/usr/bin/env node
/**
 * Prepares a freshly seeded Wabi Bank organization for the walkthrough recording (record.js). Run it once after
 * scripts/sql/samples/wabi-bank/01-06 on a migrated, bootstrapped Wabi Insight with the API on WI_API_URL:
 *
 *   1. signs every sample user in with the seed's initial password and sets WI_DEMO_PASSWORD (first sign-in change);
 *   2. as the Organization Administrator (W1001): stores each application's read-only connection, tests it and
 *      approves the application (the step the README describes in the UI; connection strings are encrypted with
 *      the deployment's Data Protection key ring, so SQL alone cannot store them);
 *   3. with WI_SQLCMD: makes every Monitoring Rule's job due, so the next scheduler tick (at most a minute) raises the
 *      planted alerts that the video opens.
 *
 * Nothing here touches the source databases; it only uses the product's own API, as an administrator would.
 *
 *   WI_API_URL=http://localhost:5080 WI_DEMO_PASSWORD='<new password>' WI_DEMO_LANGUAGE=en|ar \
 *   WI_SQL_SERVER='localhost,1433' WI_SQLCMD="sqlcmd -S localhost,1433 -U sa -P '<sa password>' -C -d WabiInsightDB" \
 *     node scripts/demo/walkthrough/prepare.mjs
 */
import { execSync } from 'node:child_process';

const API = process.env.WI_API_URL || 'http://localhost:5080';
const INITIAL = process.env.WI_SAMPLE_PASSWORD || 'Wabi#Sample#2026';
const DEMO = process.env.WI_DEMO_PASSWORD;
const SQL_SERVER = process.env.WI_SQL_SERVER || 'localhost,1433';
const LANGUAGE = process.env.WI_DEMO_LANGUAGE || 'en'; // the language of the video being recorded
if (!DEMO) throw new Error('Set WI_DEMO_PASSWORD: the password the demo accounts get at their first sign-in.');

const USERS = ['w1001', 'w1002', 'w1003', 'w1004', 'w1005', 'w1006', 'w1007', 'w1008'].map((u) => `${u}@wabibank.example`);
// Read-only logins created by the sample scripts (scripts/sql/samples/wabi-bank/README.md, "SQL logins").
const READERS = {
  PERSONNEL: ['WabiBankPersonnelDB', 'wbpersonnel_reader', 'WbPersonnel#Reader#2026'],
  'CORE-BANKING': ['WabiBankCoreBankingDB', 'wbcore_reader', 'WbCore#Reader#2026'],
  FINANCE: ['WabiBankFinanceDB', 'wbfinance_reader', 'WbFinance#Reader#2026'],
  'DIGITAL-BANKING': ['WabiBankDigitalDB', 'wbdigital_reader', 'WbDigital#Reader#2026'],
  'IT-HELPDESK': ['WabiBankITDB', 'wbit_reader', 'WbIt#Reader#2026'],
};

class Session {
  constructor() { this.cookies = new Map(); this.token = ''; }
  store(response) {
    for (const cookie of response.headers.getSetCookie()) {
      const [pair] = cookie.split(';');
      const i = pair.indexOf('=');
      this.cookies.set(pair.slice(0, i), pair.slice(i + 1));
    }
  }
  header() { return [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; '); }
  async csrf() {
    const response = await fetch(`${API}/api/auth/csrf`, { headers: { cookie: this.header() } });
    this.store(response);
    this.token = (await response.json()).token;
  }
  async call(method, url, body) {
    const response = await fetch(`${API}${url}`, {
      method,
      headers: { cookie: this.header(), 'content-type': 'application/json', 'X-XSRF-TOKEN': this.token },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    this.store(response);
    const text = await response.text();
    let json = null;
    try { json = text ? JSON.parse(text) : null; } catch { json = text; }
    return { status: response.status, json };
  }
}

const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Signs in with the demo password, or with the initial one and changes it. Retries once after the sign-in rate limit. */
async function signIn(email) {
  for (let attempt = 0; attempt < 2; attempt++) {
    for (const password of [DEMO, INITIAL]) {
      const session = new Session();
      await session.csrf();
      const login = await session.call('POST', '/api/auth/login', { email, password });
      if (login.status === 429) { await pause(65000); break; }
      if (login.status !== 200) continue;
      await session.csrf();
      if (password === INITIAL) {
        const changed = await session.call('POST', '/api/auth/change-password', { currentPassword: INITIAL, newPassword: DEMO });
        if (changed.status >= 300) throw new Error(`${email}: password change failed (${changed.status}) ${JSON.stringify(changed.json)}`);
        await session.csrf();
      }
      return session;
    }
  }
  throw new Error(`${email}: sign-in failed with both passwords`);
}

for (const email of USERS) {
  await signIn(email);
  console.log(`signed in: ${email}`);
}

const admin = await signIn(USERS[0]);
const listed = (await admin.call('GET', '/api/applications?pageSize=50')).json;
for (const app of listed.items ?? listed) {
  const reader = READERS[app.applicationCode];
  if (!reader) continue;
  const [database, login, password] = reader;
  const connection = `Server=${SQL_SERVER};Database=${database};User Id=${login};Password=${password};Encrypt=True;TrustServerCertificate=True;`;
  const stored = await admin.call('PUT', `/api/applications/${app.applicationId}/connection`, { connectionString: connection, connectionHint: `${login} (read-only)` });
  const tested = await admin.call('POST', `/api/applications/${app.applicationId}/test-connection`, {});
  const approved = await admin.call('POST', `/api/applications/${app.applicationId}/approval`, { isApproved: true });
  console.log(`${app.applicationCode}: connection ${stored.status}, test ${tested.status} ${JSON.stringify(tested.json)}, approval ${approved.status}`);
}

// Every user's saved language becomes the video's language (WI_DEMO_LANGUAGE, en or ar), because the app applies the
// saved language on every page load. The "other language" scene switches language on screen.
const users = (await admin.call('GET', '/api/users?pageSize=50')).json;
for (const u of (users.items ?? users).filter((x) => x.preferredLanguage !== LANGUAGE)) {
  const updated = await admin.call('PUT', `/api/users/${u.userId}`, {
    fullName: u.fullName, employeeNumber: u.employeeNumber, phoneNumber: u.phoneNumber, departmentId: u.departmentId ?? null,
    sectionId: u.sectionId ?? null, preferredLanguage: LANGUAGE, roleCodes: u.roles,
  });
  console.log(`${u.email}: preferred language ${LANGUAGE} (${updated.status})`);
}

// The access rule "KYC documents looked up outside Compliance" needs a look-up to report: the CEO asks one question
// that reads KycDocuments (his role may; the rule tells Compliance that it happened).
const ceo = await signIn(USERS[1]);
const apps = (await ceo.call('GET', '/api/ai/applications')).json;
const core = apps.find((a) => a.applicationCode === 'CORE-BANKING');
const kyc = await ceo.call('POST', '/api/ai/ask', { applicationId: core.applicationId, question: 'How many KYC documents expire in 2026, by document type?', language: 'en', interpret: false });
console.log(`KYC look-up by the CEO: ${kyc.status}`);

if (process.env.WI_SQLCMD) {
  execSync(`${process.env.WI_SQLCMD} -Q "SET NOCOUNT ON; UPDATE j SET NextRunAt = DATEADD(second, -5, SYSUTCDATETIME()) FROM dbo.ScheduledJobs j JOIN dbo.MonitoringRules m ON m.RuleId = j.TargetId JOIN dbo.Organizations o ON o.OrganizationId = m.OrganizationId WHERE o.OrganizationCode = N'WBK'"`, { stdio: 'inherit' });
  console.log('Monitoring Rules are due: the scheduler runs them within a minute.');
}
