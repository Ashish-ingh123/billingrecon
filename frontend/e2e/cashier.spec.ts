import { expect, test, type Page } from '@playwright/test';
import { open, pageOf, payer, patient, screenshots, trail, type Answers } from './stubbed';

// The cashier's screens and the checker's inbox of Prompt 9.3b in a real browser (ADR 0049), with no API behind them:
// the helper of e2e/stubbed.ts puts a session in the tab, gives the API's answers and fails on a request it did not
// expect, under nginx's content security policy. It needs the built app (`npm run build`, `npm run preview`,
// E2E_BASE_URL=http://localhost:4173). E2E_SCREENSHOTS=1 also writes the screenshots into docs/ui/.

const older = {
  id: 'e5000000-0000-4000-8000-000000000011',
  patientId: patient.id,
  payerId: payer.id,
  billDate: '2026-11-10',
  dueDate: '2026-11-25',
  encounterReference: null,
  status: 'Posted',
  number: 'BIL/26-27/000011',
  total: 1049.48,
  balanceDue: 1049.48,
  createdAtUtc: '2026-11-10T05:00:00+00:00',
  updatedAtUtc: '2026-11-10T05:00:00+00:00',
  version: 'AAAAAAAACBA=',
};
const newer = {
  ...older,
  id: 'e5000000-0000-4000-8000-000000000012',
  number: 'BIL/26-27/000012',
  billDate: '2026-11-15',
  dueDate: '2026-11-30',
  status: 'PartiallyPaid',
  total: 2511.07,
  balanceDue: 2011.07,
};
const payment = {
  id: 'f6000000-0000-4000-8000-000000000001',
  number: 'PAY/26-27/000001',
  fiscalYear: '2026-27',
  payerId: payer.id,
  paymentDate: '2026-11-21',
  method: 'Cash',
  reference: null,
  amount: 2000,
  unappliedAmount: 1500,
  status: 'Posted',
  notes: null,
  reversedAtUtc: null,
  reversalReason: null,
  createdAtUtc: '2026-11-21T05:00:00+00:00',
  updatedAtUtc: '2026-11-21T06:00:00+00:00',
  version: 'AAAAAAAACQA=',
};
const allocation = {
  id: 'f7000000-0000-4000-8000-000000000001',
  billId: newer.id,
  amount: 500,
  status: 'Active',
  allocatedAtUtc: '2026-11-21T06:00:00+00:00',
  allocatedByUserId: 'd0000000-0000-4000-8000-000000000002',
  reversedAtUtc: null,
  reversalReason: null,
};
const refund = {
  id: 'f8000000-0000-4000-8000-000000000001',
  payerId: payer.id,
  sourceKind: 'Payment',
  paymentId: payment.id,
  creditNoteId: null,
  refundDate: '2026-11-22',
  amount: 200,
  method: 'UPI',
  reference: 'asha@upi',
  reason: 'Paid twice at the counter',
  status: 'PendingApproval',
  number: null,
  fiscalYear: null,
  postedAtUtc: null,
  voidedAtUtc: null,
  voidReason: null,
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T05:00:00+00:00',
  version: 'AAAAAAAACgA=',
};
const request = (id: string, documentType: string, documentId: string, extra: object) => ({
  approvalRequestId: id,
  documentType,
  action: 'Post',
  documentId,
  parentDocumentId: null,
  makerUserId: 'e1000000-0000-4000-8000-000000000009',
  makerComment: null,
  requestedAtUtc: '2026-11-22T05:00:00+00:00',
  waitingDays: 1,
  payerId: payer.id,
  payerName: payer.name,
  amount: 1049.48,
  ...extra,
});
const reportPage = (items: object[], totals: object = {}) => ({ ...pageOf(items), totals, period: null });

const answers: Answers = {
  [`GET /api/v1/payments/${payment.id}/allocations`]: () => ({ payment, allocations: [allocation] }),
  [`GET /api/v1/payments/${payment.id}`]: () => payment,
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  [`GET /api/v1/bills/${newer.id}`]: () => ({ ...newer, lines: [] }),
  [`GET /api/v1/refunds/${refund.id}`]: () => refund,
  'GET /api/v1/bills': (url) => pageOf(url.searchParams.get('Status') === 'Posted' ? [older] : [newer]),
  'GET /api/v1/audit-records': (url) => trail(url.searchParams.get('EntityId') ?? '', ['Recorded', 'Allocated']),
  [`GET /api/v1/reports/payers/${payer.id}/balance`]: () => ({
    payerId: payer.id,
    payerName: payer.name,
    receivable: 3060.55,
    unappliedCash: 1500,
    creditAvailable: 120,
    netOutstanding: 1440.55,
  }),
  'GET /api/v1/reports/unapplied-cash': () =>
    reportPage([
      {
        paymentId: payment.id,
        number: payment.number,
        paymentDate: payment.paymentDate,
        payerId: payer.id,
        payerName: payer.name,
        method: 'Cash',
        amount: 2000,
        unappliedAmount: 1500,
        status: 'Posted',
      },
    ]),
  'GET /api/v1/reports/available-credit': () =>
    reportPage([
      {
        creditNoteId: 'f9000000-0000-4000-8000-000000000001',
        number: 'CRN/26-27/000004',
        creditNoteDate: '2026-11-18',
        billId: older.id,
        billNumber: older.number,
        payerId: payer.id,
        payerName: payer.name,
        total: 200,
        availableAmount: 120,
      },
    ]),
  'GET /api/v1/reports/pending-approvals': () =>
    reportPage(
      [
        request('fb000000-0000-4000-8000-000000000001', 'Bill', older.id, { makerComment: 'Above the threshold' }),
        request('fb000000-0000-4000-8000-000000000002', 'Refund', refund.id, {
          amount: 200,
          waitingDays: 3,
          makerComment: 'Paid twice at the counter',
        }),
        request('fb000000-0000-4000-8000-000000000003', 'Payment', payment.id, {
          action: 'Reverse',
          amount: 2000,
          waitingDays: 0,
          makerUserId: 'd0000000-0000-4000-8000-000000000002',
          makerComment: 'Cheque dishonoured',
        }),
      ],
      { oldestRequestedAtUtc: '2026-11-19T05:00:00+00:00' },
    ),
};

const screens = [
  {
    name: 'payment',
    path: `/payments/${payment.id}`,
    ready: async (page: Page) => {
      await expect(page.getByLabel('Figures of the payment')).toContainText('Unapplied cash₹1,500.00');
      await expect(page.getByRole('link', { name: 'BIL/26-27/000012' })).toBeVisible();
    },
  },
  {
    name: 'allocating',
    path: `/payments/${payment.id}/allocate`,
    ready: async (page: Page) => {
      await page.getByRole('button', { name: 'Apply oldest first' }).click();
      await expect(page.getByLabel('Amount to apply to BIL/26-27/000011')).toHaveValue('1049.48');
      await expect(page.getByRole('meter')).toBeVisible();
    },
  },
  {
    name: 'refund',
    path: `/refunds/${refund.id}`,
    ready: async (page: Page) => {
      await expect(page.getByLabel('Figures of the refund')).toContainText('Amount₹200.00');
      await expect(page.getByText('This refund waits for approval', { exact: false })).toBeVisible();
    },
  },
  {
    name: 'statement',
    path: `/payers/${payer.id}/statement`,
    ready: async (page: Page) => {
      await expect(page.getByLabel("The payer's figures")).toContainText('Net outstanding₹1,440.55');
      await expect(page.getByRole('cell', { name: 'CRN/26-27/000004' })).toBeVisible();
    },
  },
  {
    name: 'inbox',
    path: '/approvals',
    ready: async (page: Page) => {
      const cards = page.getByRole('list', { name: 'Requests that wait, oldest first' }).getByRole('listitem');
      await expect(cards).toHaveCount(3);
      await expect(cards.nth(2)).toContainText('Yours: waits for another checker');
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
        const { errors, unexpected, settle, probe } = await open(page, screen.path, theme, size, answers);

        await screen.ready(page);
        await settle();
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(size.width);
        const { violations, shift, moved } = await probe();
        expect(violations, 'refused by the content security policy').toEqual([]);
        expect(errors, 'errors in the console').toEqual([]);
        expect(unexpected, 'requests the test did not expect').toEqual([]);
        expect(shift, `layout shift after the first paint: ${moved.join(', ')}`).toBeLessThan(0.01);

        if (screenshots && ['allocating', 'statement', 'inbox'].includes(screen.name)) {
          await page.screenshot({
            path: `../docs/ui/${screen.name}${label === 'phone' ? '-phone' : ''}${theme === 'dark' ? '-dark' : ''}.png`,
            fullPage: label === 'desktop',
          });
        }
      });
    }
  }
}

test('the meter stays in view under the bills, and warns in words before the cap is passed', async ({ page }) => {
  const { settle } = await open(
    page,
    `/payments/${payment.id}/allocate`,
    'light',
    { width: 390, height: 640 },
    answers,
  );
  await screens[1].ready(page);
  await settle();

  await page.getByLabel('Amount to apply to BIL/26-27/000012').fill('451');
  await expect(page.getByText('More than the unapplied cash by ₹0.48', { exact: false })).toBeVisible();
  await page.getByLabel('Amount to apply to BIL/26-27/000011').scrollIntoViewIfNeeded();
  const box = await page.getByRole('meter').boundingBox();
  expect(box, 'the meter').not.toBeNull();
  expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(640);
});

test('with reduced motion nothing on the cashier screens animates', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  for (const screen of screens) {
    await open(page, screen.path, 'light', { width: 1440, height: 900 }, answers);
    await screen.ready(page);
    expect(await page.evaluate(() => document.getAnimations().length), screen.name).toBe(0);
    await page.unrouteAll({ behavior: 'ignoreErrors' });
  }
});
