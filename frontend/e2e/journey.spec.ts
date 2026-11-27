import { expect, test as base, type Page } from '@playwright/test';

// The main journey of R1 in a real browser against the whole running stack (Prompt 8.2, ADR 0046), in the demo hospital
// of the one-command local run (ADR 0045), by three of its users in turn: a clerk registers a patient and their payer,
// then drafts and posts a bill; a cashier records the payer's payment and applies it oldest first, and the bill is
// settled; a supervisor sees the dashboard and a reconciliation without drift. Each step is a test; they run in order
// and share what the earlier ones made. No page of the journey may log an error to the console: a script the content
// security policy refuses, or a page that throws, fails the step it happened in. (Until Prompt 9.1 gave the app its
// favicon, the browser's own request for /favicon.ico was the one exception.)
//
// E2E_PASSWORD is the demo users' password (DEMO_PASSWORD of .env); E2E_HOSPITAL the hospital's code, "demo" by default.

const password = process.env.E2E_PASSWORD ?? '';
const hospital = process.env.E2E_HOSPITAL ?? 'demo';
const run = Date.now().toString(36).toUpperCase();
const mrn = `E2E-${run}`;
const fullName = `Journey Patient ${run}`;

type Journey = { payerPath: string; billPath: string; billNumber: string; billTotal: string };
const journey: Journey = { payerPath: '', billPath: '', billNumber: '', billTotal: '' };

// Every step fails when a page of it wrote an error to the console or threw.
const test = base.extend<{ consoleErrors: string[] }>({
  consoleErrors: [
    async ({ page }, provide) => {
      const errors: string[] = [];
      page.on('console', (message) => {
        if (message.type() === 'error') errors.push(message.text());
      });
      page.on('pageerror', (error) => errors.push(error.message));
      await provide(errors);
      expect(errors, 'no page of the step logs an error').toEqual([]);
    },
    { auto: true },
  ],
});

test.describe.configure({ mode: 'serial' });

test.beforeAll(() => {
  expect(password, 'E2E_PASSWORD is the demo users’ password (DEMO_PASSWORD of .env)').not.toBe('');
});

async function signIn(page: Page, email: string) {
  await page.goto('/sign-in');
  await page.getByLabel('Hospital code').fill(hospital);
  await page.getByLabel('E-mail address').fill(email);
  // Exact since Prompt 9.1: the show/hide button beside the field is named "Show password".
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).not.toHaveURL(/\/sign-in/);
}

async function signOut(page: Page) {
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Sign in to BillingRecon' })).toBeVisible();
}

/**
 * The signed-in cashier's shift of today (Prompt 11.2, ADR 0060): cash is taken in an open shift, on the business date
 * of that shift. Asked of the API with the page's own token (the journey is about the payment; the shift's screens have tests of their own): an open
 * shift of an earlier day is counted to exactly its expected cash and closed, and today's is opened.
 */
async function shiftOfToday(page: Page) {
  const token = await page.evaluate(
    () =>
      (JSON.parse(window.sessionStorage.getItem('billingrecon.session') ?? '{}') as { accessToken?: string })
        .accessToken ?? '',
  );
  expect(token, 'the cashier is signed in').not.toBe('');
  const headers = { Authorization: `Bearer ${token}` };
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' }).format(new Date());

  const mine = await page.request.get('/api/v1/cashier-shifts?mine=true&status=Open', { headers });
  expect(mine.status()).toBe(200);
  const { items } = (await mine.json()) as {
    items: { id: string; businessDate: string; expectedCash: number; version: string }[];
  };
  const open = items[0];
  if (open?.businessDate === today) return;

  if (open !== undefined) {
    // The fewest notes and coins that come to the expected cash, and its paise as loose change.
    let rupees = Math.trunc(open.expectedCash);
    const count: { denomination: number; pieces: number }[] = [];
    for (const denomination of [2000, 500, 200, 100, 50, 20, 10, 5, 2, 1]) {
      const pieces = Math.floor(rupees / denomination);
      if (pieces > 0) count.push({ denomination, pieces });
      rupees -= pieces * denomination;
    }
    const looseChange = Math.round((open.expectedCash - Math.trunc(open.expectedCash)) * 100) / 100;
    const closed = await page.request.post(`/api/v1/cashier-shifts/${open.id}/close`, {
      headers: { ...headers, 'If-Match': `"${open.version}"` },
      data: { count, looseChange, reason: null },
    });
    expect(closed.status(), 'the shift of an earlier day closes on its expected cash').toBe(200);
  }

  const opened = await page.request.post('/api/v1/cashier-shifts', { headers, data: { openingFloat: 0 } });
  expect(opened.status()).toBe(201);
}

/** Chooses the first record a picker finds for `text`. */
async function pick(page: Page, label: string, kind: string, text: string) {
  // The results open while the text is typed (Prompt 10.2b): nothing is pressed to find them.
  await page.getByLabel(label).fill(text);
  await page
    .getByRole('listbox', { name: `${kind} found` })
    .getByRole('option')
    .first()
    .click();
}

test('a clerk registers a patient and their payer, then drafts a bill and posts it', async ({ page }) => {
  await signIn(page, 'clerk@demo.local');

  await page.goto('/patients/new');
  await page.getByLabel('MRN').fill(mrn);
  await page.getByLabel('Full name').fill(fullName);
  await page.getByLabel('Date of birth').fill('1985-06-15');
  await page.getByLabel('Sex').selectOption('Female');
  await page.getByRole('button', { name: 'Register patient' }).click();
  await expect(page.getByRole('heading', { name: fullName })).toBeVisible();

  await page.getByRole('link', { name: "Create the patient's payer" }).click();
  await expect(page.getByRole('heading', { name: `Payer for ${fullName}` })).toBeVisible();
  await page.getByLabel('Name').fill(fullName);
  await page.getByRole('button', { name: 'Create payer' }).click();
  await expect(page.getByRole('link', { name: 'Record a payment from them' })).toBeVisible();
  journey.payerPath = new URL(page.url()).pathname;

  // A line from the catalogue, the demo's OPD consultation, and a free-text line; the patient's payer comes with them.
  await page.goto('/bills/new');
  await pick(page, 'Patient', 'patient', mrn);
  await pick(page, 'Add a line from the catalogue', 'service item', 'OPD');
  await page.getByRole('button', { name: 'Add a free-text line' }).click();
  const second = page.getByRole('list', { name: 'Lines of the bill' }).getByRole('listitem').nth(1);
  await second.getByLabel('Line 2: description').fill('Dressing');
  await second.getByLabel('Quantity').fill('2');
  await second.getByLabel('Unit price (₹)').fill('60');
  await second.getByLabel('Tax rate (%)').fill('5');
  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect(page.getByRole('heading', { name: 'Draft bill' })).toBeVisible();

  await page.getByRole('button', { name: 'Post bill' }).click();
  const answer = page.waitForResponse(
    (response) => response.request().method() === 'POST' && /\/api\/v1\/bills\/[^/]+\/post$/.test(response.url()),
  );
  await page.getByRole('button', { name: 'Confirm: post the bill' }).click();
  const posted = (await (await answer).json()) as { number: string; total: number };
  expect(posted.number).toMatch(/^BIL\/\d{2}-\d{2}\/\d{6}$/);
  await expect(page.getByRole('heading', { name: posted.number })).toBeVisible();
  journey.billPath = new URL(page.url()).pathname;
  journey.billNumber = posted.number;
  journey.billTotal = posted.total.toFixed(2);

  await signOut(page);
});

test('a cashier records the payer’s payment and applies it oldest first, which settles the bill', async ({ page }) => {
  expect(journey.billPath, 'the clerk’s step posted the bill').not.toBe('');
  await signIn(page, 'cashier@demo.local');
  await shiftOfToday(page);

  await page.goto(journey.payerPath);
  await page.getByRole('link', { name: 'Record a payment from them' }).click();
  await page.getByLabel('Method').selectOption('Cash');
  await page.getByLabel('Amount (₹)').fill(journey.billTotal);
  await page.getByRole('button', { name: 'Record payment' }).click();
  await expect(page.getByRole('heading', { name: /^PAY\/\d{2}-\d{2}\/\d{6}$/ })).toBeVisible();

  await page.getByRole('link', { name: 'Allocate to bills' }).click();
  await page.getByRole('button', { name: 'Apply oldest first' }).click();
  await expect(page.getByLabel(`Amount to apply to ${journey.billNumber}`)).not.toHaveValue('');
  await page.getByRole('button', { name: 'Allocate', exact: true }).click();
  // Back on the payment's page once the allocation is answered (Prompt 9.3: on the allocate page itself the link is
  // absent too, so without this the step went on while the allocation was still on its way, and cut it off).
  await expect(page).toHaveURL(/\/payments\/[0-9a-f-]+$/);
  await expect(page.getByRole('link', { name: 'Allocate to bills' })).toBeHidden();

  await page.goto(`${journey.payerPath}/statement`);
  await expect(page.getByText('Nothing is owed on a posted bill.')).toBeVisible();
  await page.goto(journey.billPath);
  await expect(page.getByText('Settled', { exact: true })).toBeVisible();

  await signOut(page);
});

test('a supervisor sees the dashboard, and the reconciliation finds no drift', async ({ page }) => {
  await signIn(page, 'supervisor@demo.local');

  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Billing against collections' })).toBeVisible();

  await page.goto('/reports/reconciliation');
  await expect(page.getByText(/No drift: every cached balance equals the ledger/)).toBeVisible();

  await signOut(page);
});
