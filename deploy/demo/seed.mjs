// The demo of the one-command local run (ADR 0045 point 1). When DEMO_ENABLED is true, signs in as the demo
// administrator (made by the API at startup) and enters, through the API as its users would: four more users (one per
// role), a catalogue, six patients with their payers, and a week of bills and payments, with a refund waiting for a
// checker. Numbering, the ledger and the audit trail are the API's own. Does nothing when the demo hospital already has
// patients. Node 24, no packages: `node seed.mjs`, with BASE_URL and DEMO_PASSWORD.

const base = (process.env.BASE_URL ?? 'https://localhost').replace(/\/$/, '');
const password = process.env.DEMO_PASSWORD ?? '';
const tenant = 'demo';

if (process.env.DEMO_ENABLED !== 'true') {
  console.log('The demo is off (DEMO_ENABLED is not true): nothing to seed.');
  process.exit(0);
}

if (password.length < 12) {
  console.error('DEMO_PASSWORD must be 12 to 128 characters.');
  process.exit(1);
}

/** A request to the API; a refusal stops the seed with the API's own message. */
async function call(method, path, { token, body, ifMatch, key } = {}) {
  const headers = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (token !== undefined) headers.Authorization = `Bearer ${token}`;
  if (ifMatch !== undefined) headers['If-Match'] = `"${ifMatch}"`;
  if (key) headers['Idempotency-Key'] = crypto.randomUUID();
  const response = await fetch(`${base}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`${method} ${path} answered ${response.status}: ${text}`);
  }

  return text === '' ? null : JSON.parse(text);
}

async function waitForApi() {
  for (let attempt = 1; attempt <= 60; attempt += 1) {
    try {
      const response = await fetch(`${base}/health/ready`);
      if (response.ok) return;
    } catch {
      // nginx or the API is not up yet.
    }

    await new Promise((resolve) => setTimeout(resolve, 2000));
  }

  throw new Error(`The API did not become ready at ${base}.`);
}

async function signIn(email, secret = password) {
  const answer = await call('POST', '/api/v1/auth/login', { body: { tenant, email, password: secret } });
  return answer.accessToken;
}

/** A one-time password for a new user: 24 characters, letters and digits. */
function oneTimePassword() {
  const bytes = crypto.getRandomValues(new Uint8Array(18));
  return Buffer.from(bytes).toString('base64url').replace(/[-_]/g, 'x');
}

/**
 * Creates a user as the administrator does, with a one-time password, then sets DEMO_PASSWORD as that user must before
 * anything else (ADR 0051): the token of the answer is the user's, with no change due.
 */
async function createUser(adminToken, email, displayName, role) {
  const oneTime = oneTimePassword();
  await call('POST', '/api/v1/users', { token: adminToken, body: { email, displayName, password: oneTime, roles: [role] } });
  const changing = await signIn(email, oneTime);
  const answer = await call('POST', '/api/v1/auth/password', {
    token: changing,
    body: { currentPassword: oneTime, newPassword: password },
  });
  return answer.accessToken;
}

/** A business date in India, `days` before today. */
function daysAgo(days) {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(new Date());
  const moment = new Date(`${today}T00:00:00Z`);
  moment.setUTCDate(moment.getUTCDate() - days);
  return moment.toISOString().slice(0, 10);
}

await waitForApi();
const admin = await signIn('admin@demo.local');

const existing = await call('GET', '/api/v1/patients?Page=1&PageSize=1', { token: admin });
if (Number(existing.totalCount) > 0) {
  console.log('The demo hospital already has its data: nothing to seed.');
  process.exit(0);
}

const tokens = {};
for (const [email, displayName, role] of [
  ['supervisor@demo.local', 'Sunita Supervisor', 'Supervisor'],
  ['clerk@demo.local', 'Kavya Clerk', 'BillingClerk'],
  ['cashier@demo.local', 'Chetan Cashier', 'Cashier'],
  ['viewer@demo.local', 'Vivek Viewer', 'Viewer'],
]) {
  tokens[role] = await createUser(admin, email, displayName, role);
}

const clerk = tokens.BillingClerk;
const cashier = tokens.Cashier;

const items = [];
for (const [code, name, category, price, tax] of [
  ['OPD-CONS', 'OPD consultation', 'Consultation', 500, 0],
  ['LAB-CBC', 'Complete blood count', 'Laboratory', 350, 5],
  ['RAD-XRAY', 'X-ray, chest', 'Radiology', 900, 5],
  ['PRC-DRESS', 'Dressing', 'Procedure', 60, 5],
  ['WARD-BED', 'Ward bed, per day', 'Ward', 1800, 12],
]) {
  items.push(
    await call('POST', '/api/v1/service-items', {
      token: clerk,
      body: { code, name, category, defaultUnitPrice: price, taxRatePercent: tax },
    }),
  );
}

const people = [
  ['DEMO-1001', 'Asha Verma', '1984-03-12', 'Female'],
  ['DEMO-1002', 'Ravi Kumar', '1979-01-02', 'Male'],
  ['DEMO-1003', 'Meera Iyer', '1991-07-23', 'Female'],
  ['DEMO-1004', 'Imran Shaikh', '1968-11-05', 'Male'],
  ['DEMO-1005', 'Lakshmi Babu', '1955-05-30', 'Female'],
  ['DEMO-1006', 'Gurpreet Pillai', '2001-09-14', 'Male'],
];
const parties = [];
for (const [mrn, fullName, dateOfBirth, sex] of people) {
  const patient = await call('POST', '/api/v1/patients', {
    token: clerk,
    body: { mrn, fullName, dateOfBirth, sex, phone: null, email: null, addressLine1: null, addressLine2: null, city: 'Pune', state: 'Maharashtra', pinCode: null },
  });
  const payer = await call('POST', '/api/v1/payers', {
    token: clerk,
    body: { kind: 'SelfPay', patientId: patient.id, name: fullName, phone: null, email: null },
  });
  parties.push({ patient, payer });
}

const line = (item, quantity) => ({
  serviceItemId: item.id,
  description: item.name,
  quantity,
  unitPrice: Number(item.defaultUnitPrice),
  discountAmount: 0,
  taxRatePercent: Number(item.taxRatePercent),
});

// A week: each day two patients are billed, and most pay; some pay in part, one pays more than they owe.
let bills = 0;
let payments = 0;
let lastPayment = null;
for (let day = 6; day >= 0; day -= 1) {
  const date = daysAgo(day);
  for (const offset of [0, 3]) {
    const { patient, payer } = parties[(day + offset) % parties.length];
    const lines = [line(items[0], 1), line(items[1 + ((day + offset) % 4)], 1 + (day % 2))];
    const draft = await call('POST', '/api/v1/bills', {
      token: clerk,
      key: true,
      body: { patientId: patient.id, payerId: payer.id, billDate: date, dueDate: daysAgo(day - 15), encounterReference: `OPD-${date}-${offset}`, notes: null, lines },
    });
    const bill = await call('POST', `/api/v1/bills/${draft.id}/post`, { token: clerk, ifMatch: draft.version, key: true });
    bills += 1;

    const share = (day + offset) % 3;
    if (share === 2) continue;
    const total = Number(bill.total);
    const amount = share === 0 ? total : Math.round(total * 0.5 * 100) / 100;
    const extra = day === 1 && offset === 0 ? 200 : 0;
    const method = ['Cash', 'UPI', 'Card'][day % 3];
    const payment = await call('POST', '/api/v1/payments', {
      token: cashier,
      key: true,
      body: { payerId: payer.id, paymentDate: date, method, reference: method === 'Cash' ? null : `REF-${date}-${offset}`, amount: amount + extra, notes: null },
    });
    await call('POST', `/api/v1/payments/${payment.id}/allocations`, {
      token: cashier,
      ifMatch: payment.version,
      key: true,
      body: { allocations: [{ billId: bill.id, amount }] },
    });
    payments += 1;
    if (extra > 0) lastPayment = payment.id;
  }
}

// The overpayment is refunded, and the refund waits for a checker in the inbox.
if (lastPayment !== null) {
  const refund = await call('POST', '/api/v1/refunds', {
    token: cashier,
    key: true,
    body: { paymentId: lastPayment, refundDate: daysAgo(0), amount: 200, method: 'Cash', reference: null, reason: 'Paid more than the bill' },
  });
  await call('POST', `/api/v1/refunds/${refund.id}/post`, { token: cashier, ifMatch: refund.version, key: true });
}

console.log(`The demo is ready: ${bills} bills, ${payments} payments, one refund waiting for a checker.`);
console.log('Sign in at https://localhost with the hospital code "demo" and DEMO_PASSWORD as one of:');
console.log('  admin@demo.local, supervisor@demo.local, clerk@demo.local, cashier@demo.local, viewer@demo.local');
