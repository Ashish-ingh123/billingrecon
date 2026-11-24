import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

// The money screens of Prompt 9.3 in a real browser (ADR 0049), with no API behind them: a session is put in the tab and
// the API's answers are given by the test; a request the test did not expect fails it. The page's response carries
// nginx's content security policy, read from frontend/deploy/nginx.conf. It needs the built app (`npm run build`,
// `npm run preview`, E2E_BASE_URL=http://localhost:4173). E2E_SCREENSHOTS=1 also writes the screenshots into docs/ui/.

const policy = /set \$csp "([^"]+)";/.exec(readFileSync(new URL('../deploy/nginx.conf', import.meta.url), 'utf8'))?.[1];
const screenshots = process.env.E2E_SCREENSHOTS === '1';

const base64 = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
const roles = ['BillingClerk', 'Supervisor'];
const userId = 'd0000000-0000-4000-8000-000000000002';
const token = `${base64({ alg: 'HS256', typ: 'JWT' })}.${base64({ sub: userId, tenant_id: 't1', role: roles, exp: Math.floor(Date.now() / 1000) + 3600 })}.signature`;
const session = {
  accessToken: token,
  expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
  userId,
  tenantId: 'd0000000-0000-4000-8000-000000000001',
  roles,
  email: 'asha@cityhospital.org',
  hospital: 'city-hospital',
};

const patient = {
  id: 'a1000000-0000-4000-8000-000000000001',
  mrn: 'MRN-1001',
  fullName: 'Asha Verma',
  dateOfBirth: '1984-03-12',
  sex: 'Female',
  phone: null,
  email: null,
  addressLine1: null,
  addressLine2: null,
  city: 'Bengaluru',
  state: 'Karnataka',
  pinCode: null,
  status: 'Active',
  createdAtUtc: '2026-10-01T04:30:00+00:00',
  updatedAtUtc: '2026-10-01T04:30:00+00:00',
  version: 'AAAAAAAAB9E=',
};
const payer = {
  id: 'b2000000-0000-4000-8000-000000000002',
  kind: 'SelfPay',
  name: 'Asha Verma',
  patientId: patient.id,
  phone: null,
  email: null,
  status: 'Active',
  createdAtUtc: '2026-10-01T04:31:00+00:00',
  updatedAtUtc: '2026-10-01T04:31:00+00:00',
  version: 'AAAAAAAAB9I=',
};
const lines = [
  {
    id: 'd4000000-0000-4000-8000-000000000001',
    lineNo: 1,
    serviceItemId: 'c3000000-0000-4000-8000-000000000003',
    description: 'Complete blood count',
    quantity: 3,
    unitPrice: 333.3333,
    lineAmount: 1000,
    discountAmount: 0.5,
    taxRatePercent: 5,
    taxAmount: 49.98,
    lineTotal: 1049.48,
  },
  {
    id: 'd4000000-0000-4000-8000-000000000002',
    lineNo: 2,
    serviceItemId: null,
    description: 'Consultation, general medicine',
    quantity: 1,
    unitPrice: 1200,
    lineAmount: 1200,
    discountAmount: 0,
    taxRatePercent: 0,
    taxAmount: 0,
    lineTotal: 1200,
  },
];
const draft = {
  id: 'e5000000-0000-4000-8000-000000000001',
  patientId: patient.id,
  payerId: payer.id,
  billDate: '2026-11-20',
  dueDate: '2026-12-05',
  encounterReference: 'OPD-7781',
  notes: null,
  status: 'Draft',
  number: null,
  fiscalYear: null,
  postedAtUtc: null,
  subTotal: 2200,
  discountTotal: 0.5,
  taxTotal: 49.98,
  total: 2249.48,
  balanceDue: 2249.48,
  voidedAtUtc: null,
  voidReason: null,
  lines,
  createdAtUtc: '2026-11-20T05:00:00+00:00',
  updatedAtUtc: '2026-11-20T05:00:00+00:00',
  version: 'AAAAAAAACAA=',
};
const posted = {
  ...draft,
  id: 'e5000000-0000-4000-8000-000000000002',
  status: 'PartiallyPaid',
  number: 'BIL/26-27/000123',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-11-20T06:00:00+00:00',
  balanceDue: 949.48,
};
const creditNote = {
  id: 'f9000000-0000-4000-8000-000000000001',
  billId: posted.id,
  payerId: payer.id,
  patientId: patient.id,
  creditNoteDate: '2026-11-22',
  reason: 'Test not done',
  status: 'Posted',
  number: 'CRN/26-27/000004',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-11-22T06:00:00+00:00',
  subTotal: 300,
  discountTotal: 0,
  taxTotal: 0,
  total: 300,
  availableAmount: 0,
  voidedAtUtc: null,
  voidReason: null,
  lines: [{ ...lines[0], billLineId: lines[0]?.id, quantity: 1, lineAmount: 300, lineTotal: 300, taxAmount: 0 }],
  applications: [
    {
      id: 'f9100000-0000-4000-8000-000000000001',
      billId: posted.id,
      amount: 300,
      status: 'Active',
      allocatedAtUtc: '2026-11-22T06:00:00+00:00',
      allocatedByUserId: null,
      appliedAtPosting: true,
      reversedAtUtc: null,
      reversalReason: null,
    },
  ],
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T06:00:00+00:00',
  version: 'AAAAAAAACwA=',
};
const adjustment = {
  id: 'fa000000-0000-4000-8000-000000000001',
  billId: posted.id,
  payerId: payer.id,
  adjustmentDate: '2026-11-22',
  kind: 'Discount',
  amount: 49.48,
  reason: 'Staff discount',
  status: 'PendingApproval',
  number: null,
  fiscalYear: null,
  postedAtUtc: null,
  voidedAtUtc: null,
  voidReason: null,
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T05:00:00+00:00',
  version: 'AAAAAAAADAA=',
};
const trail = (entityId: string, actions: string[]) => ({
  items: actions.map((action, index) => ({
    id: index + 1,
    occurredAtUtc: `2026-11-2${String(index)}T05:00:00+00:00`,
    userId: index % 2 === 0 ? userId : 'e1000000-0000-4000-8000-000000000009',
    correlationId: `c0000000-0000-4000-8000-00000000000${String(index)}`,
    entityType: 'Bill',
    entityId,
    documentNumber: action === 'Posted' ? 'BIL/26-27/000123' : null,
    action,
    before: null,
    after: { Status: action },
    reason: action === 'Voided' ? 'Entered twice' : null,
  })),
  page: 1,
  pageSize: 20,
  totalCount: actions.length,
});
const pageOf = (items: object[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });

/** The API's answers, by method and path; anything else is a request the screens did not send before. */
const answers: Record<string, (url: URL) => object> = {
  [`GET /api/v1/bills/${draft.id}`]: () => draft,
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  [`GET /api/v1/patients/${patient.id}`]: () => patient,
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  [`GET /api/v1/credit-notes/${creditNote.id}`]: () => creditNote,
  [`GET /api/v1/adjustments/${adjustment.id}`]: () => adjustment,
  'GET /api/v1/credit-notes': () => pageOf([creditNote]),
  'GET /api/v1/adjustments': () => pageOf([adjustment]),
  'GET /api/v1/audit-records': (url) =>
    trail(url.searchParams.get('EntityId') ?? '', ['Drafted', 'LinesReplaced', 'Posted', 'CreditApplied']),
};

type Probe = { violations: string[]; shift: number; moved: string[] };

async function open(page: Page, path: string, theme: 'light' | 'dark', size: { width: number; height: number }) {
  expect(policy, 'the policy of frontend/deploy/nginx.conf').toBeTruthy();
  const unexpected: string[] = [];
  await page.setViewportSize(size);
  await page.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url());
    const answer = answers[`${route.request().method()} ${url.pathname}`];
    if (answer === undefined) {
      unexpected.push(`${route.request().method()} ${url.pathname}`);
      await route.fulfill({ status: 503, json: { title: 'Not in the test', status: 503 } });
      return;
    }

    await route.fulfill({ json: answer(url), headers: { etag: '"AAAAAAAACAA="' } });
  });
  await page.route('**/*', async (route) => {
    if (route.request().resourceType() !== 'document') {
      await route.fallback();
      return;
    }

    const response = await route.fetch();
    await route.fulfill({ response, headers: { ...response.headers(), 'content-security-policy': policy ?? '' } });
  });
  await page.addInitScript(
    ([kept, chosen]) => {
      window.sessionStorage.setItem('billingrecon.session', kept);
      window.localStorage.setItem('billingrecon.theme', chosen);
      const probe: Probe = { violations: [], shift: 0, moved: [] };
      (window as unknown as { probe: Probe }).probe = probe;
      document.addEventListener('securitypolicyviolation', (event) => {
        probe.violations.push(`${event.violatedDirective}: ${event.blockedURI}`);
      });
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          const shift = entry as unknown as { value: number; sources?: { node?: Node }[] };
          probe.shift += shift.value;
          for (const source of shift.sources ?? []) {
            const node = source.node as Element | undefined;
            probe.moved.push(`${node?.nodeName ?? '?'}.${String(node?.className ?? '')} ${shift.value.toFixed(4)}`);
          }
        }
      }).observe({ type: 'layout-shift', buffered: true });
    },
    [JSON.stringify(session), theme] as const,
  );

  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto(path);

  return {
    errors,
    unexpected,
    settle: () =>
      page.waitForFunction(() =>
        document
          .getAnimations()
          .every(
            (animation) => animation.playState === 'finished' || animation.effect?.getTiming().iterations === Infinity,
          ),
      ),
    probe: () => page.evaluate(() => (window as unknown as { probe: Probe }).probe),
  };
}

const screens = [
  {
    name: 'bill-editor',
    path: `/bills/${draft.id}/edit`,
    ready: async (page: Page) => {
      await expect(page.getByLabel('Estimated totals')).toContainText('Total₹2,249.48');
      await expect(page.getByLabel('Line 2: description')).toHaveValue('Consultation, general medicine');
    },
  },
  {
    name: 'bill-posted',
    path: `/bills/${posted.id}`,
    ready: async (page: Page) => {
      await expect(page.getByRole('heading', { level: 1, name: 'BIL/26-27/000123' })).toBeVisible();
      await expect(page.getByLabel('Totals of the bill')).toContainText('Balance due₹949.48');
      await expect(page.getByRole('list', { name: 'Changes, oldest first' })).toBeVisible();
    },
  },
  {
    name: 'credit-note',
    path: `/credit-notes/${creditNote.id}`,
    ready: async (page: Page) => {
      await expect(page.getByRole('heading', { level: 1, name: 'CRN/26-27/000004' })).toBeVisible();
      await expect(page.getByLabel('Figures of the credit note')).toContainText('Total₹300.00');
    },
  },
  {
    name: 'adjustment',
    path: `/adjustments/${adjustment.id}`,
    ready: async (page: Page) => {
      await expect(page.getByText('Waiting for approval').first()).toBeVisible();
      await expect(page.getByLabel('Figures of the adjustment')).toContainText('Amount₹49.48');
    },
  },
] as const;

for (const screen of screens) {
  for (const theme of ['light', 'dark'] as const) {
    for (const [label, size] of [
      ['desktop', { width: 1440, height: 900 }],
      ['phone', { width: 390, height: 844 }],
    ] as const) {
      test(`${screen.name} in the ${theme} theme on a ${label}: nothing refused, no sideways scroll, no shift`, async ({
        page,
      }) => {
        const { errors, unexpected, settle, probe } = await open(page, screen.path, theme, size);

        await screen.ready(page);
        await settle();
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(size.width);
        const { violations, shift, moved } = await probe();
        expect(violations, 'refused by the content security policy').toEqual([]);
        expect(errors, 'errors in the console').toEqual([]);
        expect(unexpected, 'requests the test did not expect').toEqual([]);
        expect(shift, `layout shift after the first paint: ${moved.join(', ')}`).toBeLessThan(0.01);

        if (screenshots && (label === 'desktop' || screen.name === 'bill-editor')) {
          await page.screenshot({
            path: `../docs/ui/${screen.name}${label === 'phone' ? '-phone' : ''}${theme === 'dark' ? '-dark' : ''}.png`,
            fullPage: label === 'desktop',
          });
        }
      });
    }
  }
}

test('the totals card stays in view while the lines of the editor scroll', async ({ page }) => {
  const { settle } = await open(page, `/bills/${draft.id}/edit`, 'light', { width: 390, height: 640 });
  await screens[0].ready(page);
  await settle();

  await page.getByLabel('Line 1: description').scrollIntoViewIfNeeded();
  const card = page.getByLabel('Estimated totals');
  const box = await card.boundingBox();
  expect(box, 'the totals card').not.toBeNull();
  expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(640);
  expect(box?.y ?? -1).toBeGreaterThanOrEqual(0);
});

test('with reduced motion nothing on the money screens animates', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  for (const screen of screens) {
    await open(page, screen.path, 'light', { width: 1440, height: 900 });
    await screen.ready(page);
    expect(await page.evaluate(() => document.getAnimations().length), screen.name).toBe(0);
    await page.unrouteAll({ behavior: 'ignoreErrors' });
  }
});
