// The load test of bill posting and payment recording (Prompt 8.2, ADR 0046; docs/requirements.md section 5:
// p95 under 500 ms at 50 concurrent users with 10,000 bills in the month, the number series held under 20 ms).
//
// One hospital, LOAD_USERS users each with a patient and a payer of their own, each in a loop: draft a bill, post it,
// record a payment of its total, allocate the payment to the bill. First the hospital is filled to LOAD_FILL_BILLS posted
// bills in the month, with no pause (timed, not judged); then LOAD_SECONDS of the loop with LOAD_THINK_MS between one
// user's requests (1 s by default, still far quicker than a clerk) are measured and judged; then LOAD_STRESS_SECONDS with
// no pause at all, reported as a stress figure and not judged: every user then queues on the one series of bills (ADR
// 0046); then the reconciliation must show no drift. The API's Server-Timing (Diagnostics:ServerTiming=true) says
// how long each posting waited for its number series and held it. Exit code 0 when every target is met, 1 when one is not, 2 when the run could not be made.
//
// Node 24, no packages. Everything is entered through the API as its users would, signed in as the demo administrator
// first (the demo hospital of ADR 0045). docs/runbook.md section 14 has the commands.

import { format, judge, parseServerTiming, summarise, table } from './stats.mjs';

const settings = {
  base: (process.env.BASE_URL ?? 'https://localhost').replace(/\/$/, ''),
  tenant: process.env.LOAD_TENANT ?? 'demo',
  adminEmail: process.env.LOAD_ADMIN_EMAIL ?? 'admin@demo.local',
  adminPassword: process.env.DEMO_PASSWORD ?? '',
  users: integer('LOAD_USERS', 50),
  fillBills: integer('LOAD_FILL_BILLS', 10_000),
  seconds: integer('LOAD_SECONDS', 120),
  thinkMs: integer('LOAD_THINK_MS', 1000),
  stressSeconds: integer('LOAD_STRESS_SECONDS', 60),
  // Only for an API reached directly on this machine, without the proxy: the API believes the scheme a loopback
  // address says it used (ADR 0035), so a plain-HTTP request on loopback can stand for one that came over HTTPS.
  forwardedHttps: process.env.LOAD_FORWARDED_HTTPS === 'true',
};

const targets = { p95Ms: 500, holdMs: 20 };
const requestTimeoutMs = 60_000;

// The two operations the requirement is about, and the two the loop also needs.
const judged = ['post bill', 'record payment'];
const operations = ['draft bill', 'post bill', 'record payment', 'allocate'];

function integer(name, fallback) {
  const text = process.env[name];
  if (text === undefined || text === '') return fallback;
  const value = Number(text);
  if (!Number.isInteger(value) || value < 0) stop(`${name} must be a whole number of zero or more, not "${text}".`);
  return value;
}

function stop(message) {
  console.error(message);
  process.exit(2);
}

const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

/** Today in India, as the API dates a document (BR-003). */
function todayInIndia() {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(new Date());
}

/** A request to the API, timed from sending to the last byte of the answer. Never throws for an HTTP status. */
async function call(method, path, { token, body, ifMatch, key } = {}) {
  const headers = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (token !== undefined) headers.Authorization = `Bearer ${token}`;
  if (ifMatch !== undefined) headers['If-Match'] = `"${ifMatch}"`;
  if (key) headers['Idempotency-Key'] = crypto.randomUUID();
  if (settings.forwardedHttps) headers['X-Forwarded-Proto'] = 'https';

  const started = performance.now();
  try {
    const response = await fetch(`${settings.base}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(requestTimeoutMs),
    });
    const text = await response.text();
    const milliseconds = performance.now() - started;
    let json = null;
    try {
      json = text === '' ? null : JSON.parse(text);
    } catch {
      // Not JSON: kept as text for the message.
    }

    return {
      status: response.status,
      ok: response.ok,
      json,
      text,
      milliseconds,
      timing: parseServerTiming(response.headers.get('server-timing')),
      retryAfter: Number(response.headers.get('retry-after') ?? 0),
    };
  } catch (error) {
    return { status: 0, ok: false, json: null, text: String(error), milliseconds: performance.now() - started, timing: {}, retryAfter: 0 };
  }
}

/** A setup step that must succeed: its refusal stops the run with the API's own answer. */
async function must(method, path, options) {
  const answer = await call(method, path, options);
  if (!answer.ok) stop(`${method} ${path} answered ${answer.status}: ${answer.text}`);
  return answer.json;
}

async function waitForApi() {
  for (let attempt = 1; attempt <= 60; attempt += 1) {
    const answer = await call('GET', '/health/ready');
    if (answer.ok) return;
    await sleep(2000);
  }

  stop(`The API did not become ready at ${settings.base}.`);
}

/** Signs in, waiting out the limit of sign-ins per address (429 with Retry-After, ADR 0035). */
async function signIn(email, password) {
  for (;;) {
    const answer = await call('POST', '/api/v1/auth/login', { body: { tenant: settings.tenant, email, password } });
    if (answer.ok) return answer.json.accessToken;
    if (answer.status !== 429) stop(`Signing in as ${email} answered ${answer.status}: ${answer.text}`);
    const seconds = Math.max(answer.retryAfter, 1);
    console.log(`  the sign-in limit of the address was reached; waiting ${seconds} s`);
    await sleep(seconds * 1000);
  }
}

/** A password for a load user: 24 characters, letters and digits. */
function newPassword() {
  const bytes = crypto.getRandomValues(new Uint8Array(18));
  return Buffer.from(bytes).toString('base64url').replace(/[-_]/g, 'x');
}

// What every phase measures: the duration of each answer by operation, the Server-Timing of each series, the refusals.
function newRecord() {
  return {
    durations: Object.fromEntries(operations.map((name) => [name, []])),
    series: { Bill: { wait: [], hold: [] }, Payment: { wait: [], hold: [] } },
    errors: [],
    started: performance.now(),
    ended: null,
  };
}

function note(record, operation, answer, series) {
  if (!answer.ok) {
    record.errors.push({ operation, status: answer.status, code: answer.json?.errorCode ?? answer.text.slice(0, 120) });
    return false;
  }

  record.durations[operation].push(answer.milliseconds);
  if (series !== undefined) {
    if (answer.timing['number-wait'] !== undefined) record.series[series].wait.push(answer.timing['number-wait']);
    if (answer.timing['number-hold'] !== undefined) record.series[series].hold.push(answer.timing['number-hold']);
  }

  return true;
}

// Two lines that stay below any approval threshold above 700: an OPD consultation and one to three dressings.
function lines(iteration) {
  return [
    { description: 'OPD consultation', quantity: 1, unitPrice: 500, discountAmount: 0, taxRatePercent: 0 },
    { description: 'Dressing', quantity: 1 + (iteration % 3), unitPrice: 60, discountAmount: 0, taxRatePercent: 5 },
  ];
}

/** One turn of the loop for one user. Answers whether a bill was posted. */
async function iteration(user, record, turn, thinkMs) {
  const think = () => (thinkMs > 0 ? sleep(thinkMs) : Promise.resolve());
  const today = todayInIndia();
  const draft = await call('POST', '/api/v1/bills', {
    token: user.token,
    key: true,
    body: { patientId: user.patientId, payerId: user.payerId, billDate: today, dueDate: today, encounterReference: `LOAD-${turn}`, notes: null, lines: lines(turn) },
  });
  if (!note(record, 'draft bill', draft)) return false;
  await think();

  const posted = await call('POST', `/api/v1/bills/${draft.json.id}/post`, { token: user.token, ifMatch: draft.json.version, key: true });
  if (posted.ok && posted.status !== 200) {
    // 202: the bill waits for a checker, so it was not posted (BR-207).
    record.errors.push({ operation: 'post bill', status: posted.status, code: 'sent for approval instead of posted' });
    return false;
  }
  if (!note(record, 'post bill', posted, 'Bill')) return false;
  await think();

  const amount = Number(posted.json.total);
  const payment = await call('POST', '/api/v1/payments', {
    token: user.token,
    key: true,
    body: { payerId: user.payerId, paymentDate: today, method: 'Cash', reference: null, amount, notes: null },
  });
  if (!note(record, 'record payment', payment, 'Payment')) return true;
  await think();

  const allocation = await call('POST', `/api/v1/payments/${payment.json.id}/allocations`, {
    token: user.token,
    ifMatch: payment.json.version,
    key: true,
    body: { allocations: [{ billId: posted.json.id, amount }] },
  });
  note(record, 'allocate', allocation);
  await think();

  return true;
}

/** Every user runs the loop, with `thinkMs` between their requests, until `done()` says so. */
async function phase(users, done, thinkMs) {
  const record = newRecord();
  let turn = 0;
  let posted = 0;
  await Promise.all(
    users.map(async (user) => {
      while (!done(posted)) {
        turn += 1;
        if (await iteration(user, record, turn, thinkMs)) posted += 1;
      }
    }),
  );
  record.ended = performance.now();
  record.posted = posted;
  return record;
}

function report(title, record) {
  const seconds = (record.ended - record.started) / 1000;
  console.log(`\n${title}: ${seconds.toFixed(0)} s, ${record.posted} bills posted, ${record.errors.length} errors`);
  console.log(
    table(
      ['operation', 'count', 'per s', 'p50 ms', 'p95 ms', 'p99 ms', 'max ms'],
      operations.map((name) => {
        const summary = summarise(record.durations[name]);
        return [name, summary.count, (summary.count / seconds).toFixed(1), format(summary.p50), format(summary.p95), format(summary.p99), format(summary.max)];
      }),
    ),
  );
  console.log(
    table(
      ['number series', 'count', 'wait p50', 'wait p95', 'wait max', 'hold p50', 'hold p95', 'hold max'],
      Object.entries(record.series).map(([name, timing]) => {
        const wait = summarise(timing.wait);
        const hold = summarise(timing.hold);
        return [name, hold.count, format(wait.p50), format(wait.p95), format(wait.max), format(hold.p50), format(hold.p95), format(hold.max)];
      }),
    ),
  );

  if (record.errors.length > 0) {
    const byKind = new Map();
    for (const error of record.errors) {
      const kind = `${error.operation}: ${error.status} ${error.code}`;
      byKind.set(kind, (byKind.get(kind) ?? 0) + 1);
    }
    for (const [kind, count] of byKind) console.log(`  ${count} x ${kind}`);
  }
}

// --- The run ---

if (settings.adminPassword.length < 12) stop('DEMO_PASSWORD (the demo administrator\'s password) must be set: 12 to 128 characters.');
if (settings.users < 1) stop('LOAD_USERS must be at least 1.');

console.log(`Load test against ${settings.base}, hospital "${settings.tenant}": ${settings.users} users, fill to ${settings.fillBills} bills, measure ${settings.seconds} s with ${settings.thinkMs} ms between a user's requests, then ${settings.stressSeconds} s with none.`);
await waitForApi();
const admin = await signIn(settings.adminEmail, settings.adminPassword);

const tenantSettings = await must('GET', '/api/v1/settings', { token: admin });
const threshold = tenantSettings.postingApprovalThreshold === null ? null : Number(tenantSettings.postingApprovalThreshold);
if (threshold !== null && threshold <= 700) {
  stop(`The hospital's approval threshold is ${threshold}: every bill of the load (up to 689.00) would wait for a checker. Raise it above 700 or remove it.`);
}

// The users, their patients and their payers: new ones every run, named after it.
const run = new Date().toISOString().replace(/\D/g, '').slice(2, 12);
console.log(`\nSetup (run ${run}): ${settings.users} users with BillingClerk and Cashier, a patient and a payer each`);
const password = newPassword();
const users = [];
for (let index = 1; index <= settings.users; index += 1) {
  const number = String(index).padStart(3, '0');
  const email = `load-${run}-${number}@${settings.tenant}.local`;
  await must('POST', '/api/v1/users', { token: admin, body: { email, displayName: `Load ${run} ${number}`, password, roles: ['BillingClerk', 'Cashier'] } });
  const patient = await must('POST', '/api/v1/patients', {
    token: admin,
    body: { mrn: `LOAD-${run}-${number}`, fullName: `Load Patient ${number}`, dateOfBirth: '1980-01-01', sex: 'Female', phone: null, email: null, addressLine1: null, addressLine2: null, city: 'Pune', state: 'Maharashtra', pinCode: null },
  });
  const payer = await must('POST', '/api/v1/payers', { token: admin, body: { kind: 'SelfPay', patientId: patient.id, name: patient.fullName, phone: null, email: null } });
  users.push({ email, patientId: patient.id, payerId: payer.id });
}

for (const user of users) user.token = await signIn(user.email, password);

const register = await must('GET', '/api/v1/reports/bill-register?page=1&pageSize=1', { token: admin });
const already = Number(register.totalCount);
const missing = Math.max(settings.fillBills - already, 0);
console.log(`The hospital has ${already} posted bills this month; ${missing} more to reach ${settings.fillBills}.`);

if (missing > 0) {
  const fill = await phase(users, (posted) => posted >= missing, 0);
  report('Fill (not judged)', fill);
}

const until = performance.now() + settings.seconds * 1000;
const measured = await phase(users, () => performance.now() >= until, settings.thinkMs);
report(`Measured, ${settings.users} users, ${settings.thinkMs} ms between a user's requests`, measured);

if (settings.stressSeconds > 0) {
  // No pause at all: the users queue on the series of bills, which serves one posting at a time (ADR 0046). Reported,
  // not judged.
  const stressUntil = performance.now() + settings.stressSeconds * 1000;
  const stress = await phase(users, () => performance.now() >= stressUntil, 0);
  report(`Stress, ${settings.users} users with no pause (not judged)`, stress);
}

const reconciliation = await must('GET', '/api/v1/reports/reconciliation?page=1&pageSize=100', { token: admin });
const driftRows = Number(reconciliation.totalCount);
console.log(`\nReconciliation at ${reconciliation.totals?.checkedAtUtc ?? 'now'}: ${driftRows} drift rows`);
for (const row of reconciliation.items ?? []) console.log(`  ${row.identity} ${row.reference}: cached ${row.cached}, ledger ${row.ledger}`);

const verdicts = judge(
  {
    operations: Object.fromEntries(judged.map((name) => [name, summarise(measured.durations[name])])),
    series: Object.fromEntries(Object.entries(measured.series).map(([name, timing]) => [name, { hold: summarise(timing.hold) }])),
    errors: measured.errors.length,
    driftRows,
  },
  targets,
);

console.log('\nTargets');
console.log(table(['target', 'measured', 'result'], verdicts.map((verdict) => [verdict.target, verdict.measured, verdict.pass ? 'PASS' : 'FAIL'])));
const passed = verdicts.every((verdict) => verdict.pass);
console.log(passed ? '\nEvery target is met.' : '\nA target is missed.');
process.exit(passed ? 0 : 1);
