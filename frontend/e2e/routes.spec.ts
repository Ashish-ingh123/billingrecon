import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import {
  adjustment,
  creditNote,
  draft,
  open,
  pageOf,
  patient,
  payer,
  posted,
  screenshots,
  sessionOf,
  trail,
  type Answers,
} from './stubbed';

// The finishing pass of Phase 9 in a real browser (Prompt 9.4b, ADR 0050 amended): every route of the shell, with the
// API's answers given by the test, under nginx's content security policy. For each: both themes at 1440 and 390 px with
// nothing refused, no console error, no request the test did not expect, no layout shift; no sideways scroll from 360 to
// 1,920 px and at 200% zoom (a 720 px window); and no serious or critical finding of axe's WCAG 2.2 A and AA rules. Every
// route's screenshot in both themes at both widths is kept as an artifact of the run (test-results), not in git. It needs
// the built app (`npm run build`, `npm run preview`, E2E_BASE_URL=http://localhost:4173).

const everyRole = sessionOf(['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier']);

const serviceItem = {
  id: 'c3000000-0000-4000-8000-000000000003',
  code: 'LAB/CBC',
  name: 'Complete blood count',
  category: 'Laboratory',
  defaultUnitPrice: 450,
  taxRatePercent: 0,
  status: 'Active',
  createdAtUtc: '2026-10-01T04:32:00+00:00',
  updatedAtUtc: '2026-10-01T04:32:00+00:00',
  version: 'AAAAAAAAB9M=',
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
  billId: posted.id,
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
  status: 'Draft',
  number: null,
  fiscalYear: null,
  postedAtUtc: null,
  voidedAtUtc: null,
  voidReason: null,
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T05:00:00+00:00',
  version: 'AAAAAAAACgA=',
};
const draftCreditNote = { ...creditNote, id: 'f9000000-0000-4000-8000-000000000002', status: 'Draft', number: null };
const draftAdjustment = { ...adjustment, id: 'fa000000-0000-4000-8000-000000000002', status: 'Draft' };
const period = { from: '2026-11-01', to: '2026-11-30' };
const report = (items: object[], totals: object) => ({ ...pageOf(items), totals, period });
const billRow = {
  billId: posted.id,
  number: posted.number,
  billDate: posted.billDate,
  dueDate: posted.dueDate,
  patientId: patient.id,
  patientName: patient.fullName,
  payerId: payer.id,
  payerName: payer.name,
  total: posted.total,
  balanceDue: posted.balanceDue,
  status: 'PartiallyPaid',
  postedAtUtc: posted.postedAtUtc,
};
const paymentRow = {
  paymentId: payment.id,
  number: payment.number,
  paymentDate: payment.paymentDate,
  payerId: payer.id,
  payerName: payer.name,
  method: 'Cash',
  reference: null,
  amount: 2000,
  unappliedAmount: 1500,
  status: 'Posted',
  reversedAtUtc: null,
};
const request = {
  approvalRequestId: 'fb000000-0000-4000-8000-000000000001',
  documentType: 'Adjustment',
  action: 'Post',
  documentId: adjustment.id,
  parentDocumentId: null,
  makerUserId: 'e1000000-0000-4000-8000-000000000009',
  makerComment: 'Staff discount',
  requestedAtUtc: '2026-11-22T05:00:00+00:00',
  waitingDays: 2,
  payerId: payer.id,
  payerName: payer.name,
  amount: 49.48,
};
const balance = {
  payerId: payer.id,
  payerName: payer.name,
  receivable: 949.48,
  unappliedCash: 1500,
  creditAvailable: 0,
  netOutstanding: -550.52,
};
const figure = (value: number, previous: number | null, changePercent: number | null) => ({
  value,
  previous,
  changePercent,
});

const answers: Answers = {
  'GET /api/v1/patients': () => pageOf([patient]),
  [`GET /api/v1/patients/${patient.id}`]: () => patient,
  'GET /api/v1/payers': () => pageOf([payer]),
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  'GET /api/v1/service-items': () => pageOf([serviceItem]),
  [`GET /api/v1/service-items/${serviceItem.id}`]: () => serviceItem,
  // A filter by status (the payer's open bills) answers the posted bill alone, as the API does.
  'GET /api/v1/bills': (url) => pageOf(url.searchParams.has('Status') ? [posted] : [draft, posted]),
  [`GET /api/v1/bills/${draft.id}`]: () => draft,
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  'GET /api/v1/payments': () => pageOf([payment]),
  [`GET /api/v1/payments/${payment.id}`]: () => payment,
  [`GET /api/v1/payments/${payment.id}/allocations`]: () => ({ payment, allocations: [allocation] }),
  'GET /api/v1/refunds': () => pageOf([refund]),
  [`GET /api/v1/refunds/${refund.id}`]: () => refund,
  'GET /api/v1/credit-notes': () => pageOf([creditNote, draftCreditNote]),
  [`GET /api/v1/credit-notes/${creditNote.id}`]: () => creditNote,
  [`GET /api/v1/credit-notes/${draftCreditNote.id}`]: () => draftCreditNote,
  'GET /api/v1/adjustments': () => pageOf([adjustment, draftAdjustment]),
  [`GET /api/v1/adjustments/${adjustment.id}`]: () => adjustment,
  [`GET /api/v1/adjustments/${draftAdjustment.id}`]: () => draftAdjustment,
  'GET /api/v1/audit-records': (url) =>
    trail(url.searchParams.get('EntityId') ?? '', ['Drafted', 'LinesReplaced', 'Posted']),
  'GET /api/v1/reports/bill-register': () => report([billRow], { bills: 1, total: 2249.48, balanceDue: 949.48 }),
  'GET /api/v1/reports/payment-register': () =>
    report([paymentRow], { payments: 1, amount: 2000, unappliedAmount: 1500 }),
  'GET /api/v1/reports/daily-collection': () => ({
    date: '2026-11-23',
    methods: [{ method: 'Cash', payments: 1, received: 2000, reversed: 0, refunded: 0, net: 2000 }],
    totals: { payments: 1, received: 2000, reversed: 0, refunded: 0, net: 2000 },
  }),
  'GET /api/v1/reports/outstanding-by-payer': () =>
    report([balance], {
      payers: 1,
      receivable: 949.48,
      unappliedCash: 1500,
      creditAvailable: 0,
      netOutstanding: -550.52,
    }),
  'GET /api/v1/reports/unapplied-cash': () => report([paymentRow], { payments: 1, unappliedAmount: 1500 }),
  'GET /api/v1/reports/available-credit': () => report([], { creditNotes: 0, availableAmount: 0 }),
  'GET /api/v1/reports/pending-approvals': () =>
    report([request], { requests: 1, oldestRequestedAtUtc: request.requestedAtUtc }),
  'GET /api/v1/reports/reconciliation': () =>
    report([], { rows: 0, byIdentity: [], checkedAtUtc: '2026-11-23T06:00:00+00:00' }),
  [`GET /api/v1/reports/payers/${payer.id}/balance`]: () => balance,
  'GET /api/v1/dashboard/summary': () => ({
    period,
    previousPeriod: { from: '2026-10-01', to: '2026-10-31' },
    totalBilled: figure(2249.48, 1000, 124.95),
    amountCollected: figure(2000, 1500, 33.33),
    outstandingDues: figure(949.48, 500, 89.9),
    unappliedCash: figure(1500, 0, null),
    pendingApprovals: figure(1, 0, null),
    reconciliationDrift: figure(0, null, null),
  }),
  'GET /api/v1/dashboard/trend': () => ({ period, interval: 'Day', points: [], daily: [] }),
  'GET /api/v1/dashboard/ageing': () => ({
    asOf: '2026-11-23',
    bills: 1,
    total: 949.48,
    buckets: [
      { bucket: '0-30', bills: 1, amount: 949.48, share: 100 },
      { bucket: '31-60', bills: 0, amount: 0, share: 0 },
      { bucket: '61-90', bills: 0, amount: 0, share: 0 },
      { bucket: '90+', bills: 0, amount: 0, share: 0 },
    ],
  }),
  'GET /api/v1/dashboard/top-payers': () =>
    report([balance], { payers: 1, receivable: 0, unappliedCash: 0, creditAvailable: 0, netOutstanding: 0 }),
  'GET /api/v1/dashboard/recent-payments': () =>
    report([paymentRow], { payments: 1, amount: 2000, unappliedAmount: 1500 }),
  'GET /api/v1/dashboard/pending-approvals': () =>
    report([request], { requests: 1, oldestRequestedAtUtc: request.requestedAtUtc }),
  'GET /api/v1/dashboard/drift': () =>
    report([], { rows: 0, byIdentity: [], checkedAtUtc: '2026-11-23T06:00:00+00:00' }),
};

/** Every route of the shell and the heading it shows once it has drawn. */
const routes: readonly (readonly [string, string | RegExp])[] = [
  ['/dashboard', 'Dashboard'],
  ['/patients', 'Patients'],
  ['/patients/new', 'Register patient'],
  [`/patients/${patient.id}`, patient.fullName],
  [`/patients/${patient.id}/edit`, /^Edit/],
  ['/payers', 'Payers'],
  [`/payers/new?patientId=${patient.id}`, 'New payer'],
  [`/payers/${payer.id}`, /Asha Verma/],
  [`/payers/${payer.id}/edit`, /^Edit/],
  [`/payers/${payer.id}/statement`, 'Statement of Asha Verma'],
  ['/service-items', 'Service items'],
  ['/service-items/new', /^New/],
  [`/service-items/${serviceItem.id}`, /Complete blood count|LAB\/CBC/],
  [`/service-items/${serviceItem.id}/edit`, /^Edit/],
  ['/bills', 'Bills'],
  ['/bills/new', 'New bill'],
  [`/bills/${draft.id}`, 'Draft bill'],
  [`/bills/${posted.id}`, posted.number],
  [`/bills/${draft.id}/edit`, 'Edit draft bill'],
  ['/payments', 'Payments'],
  [`/payments/new?payerId=${payer.id}`, /^Record/],
  [`/payments/${payment.id}`, payment.number],
  [`/payments/${payment.id}/allocate`, /^Allocate/],
  ['/refunds', 'Refunds'],
  [`/refunds/new?paymentId=${payment.id}`, 'Draft a refund'],
  [`/refunds/${refund.id}`, /refund/i],
  [`/refunds/${refund.id}/edit`, /refund/i],
  ['/credit-notes', 'Credit notes'],
  [`/credit-notes/new?billId=${posted.id}`, /credit note/i],
  [`/credit-notes/${creditNote.id}`, creditNote.number ?? 'Credit note'],
  [`/credit-notes/${draftCreditNote.id}/edit`, /credit note/i],
  [`/credit-notes/${creditNote.id}/apply`, /^Apply/],
  ['/adjustments', 'Adjustments'],
  [`/adjustments/new?billId=${posted.id}`, /adjustment/i],
  [`/adjustments/${adjustment.id}`, /adjustment/i],
  [`/adjustments/${draftAdjustment.id}/edit`, /adjustment/i],
  ['/approvals', 'Approvals'],
  ['/reports', 'Reports'],
  ['/reports/bills', 'Bill register'],
  ['/reports/payments', 'Payment register'],
  ['/reports/collection', 'Daily collection'],
  ['/reports/outstanding', 'Outstanding by payer'],
  ['/reports/unapplied-cash', 'Unapplied cash'],
  ['/reports/available-credit', 'Available credit'],
  ['/reports/pending-approvals', 'Pending approvals'],
  ['/reports/reconciliation', 'Reconciliation'],
  ['/no-access', 'No access'],
  ['/no-such-page', 'Page not found'],
];

const sizes = { desktop: { width: 1440, height: 900 }, phone: { width: 390, height: 844 } } as const;

async function sidewaysAt(page: Page, width: number) {
  await page.setViewportSize({ width, height: 900 });
  return page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
}

for (const [path, heading] of routes) {
  for (const theme of ['light', 'dark'] as const) {
    for (const [label, size] of Object.entries(sizes)) {
      test(`${path} in the ${theme} theme on a ${label}`, async ({ page }, info) => {
        const { errors, unexpected, settle, probe } = await open(page, path, theme, size, answers, everyRole);

        await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
        await page.waitForLoadState('networkidle');
        await settle();
        const { violations, shift, moved } = await probe();
        expect(violations, 'refused by the content security policy').toEqual([]);
        expect(errors, 'errors in the console').toEqual([]);
        expect(unexpected, 'requests the test did not expect').toEqual([]);
        expect(shift, `layout shift after the first paint: ${moved.join(', ')}`).toBeLessThan(0.01);
        expect(await page.locator('h1').count(), 'one h1').toBe(1);
        await page.screenshot({ path: info.outputPath(`${label}-${theme}.png`), fullPage: true });
        // The chosen screenshots of 9.4b for docs/ui (E2E_SCREENSHOTS=1): the reports' index and one report.
        const chosen = { '/reports': 'reports', '/reports/bills': 'report-bills' }[path];
        if (screenshots && chosen !== undefined) {
          await page.screenshot({
            path: `../docs/ui/${chosen}${label === 'phone' ? '-phone' : ''}${theme === 'dark' ? '-dark' : ''}.png`,
          });
        }

        if (label === 'desktop') {
          const axe = await new AxeBuilder({ page })
            .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
            .analyze();
          const serious = axe.violations
            .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
            .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
          expect(serious, 'axe: serious and critical findings').toEqual([]);

          // From 360 to 1,920 px, and at 200% zoom (a 1440 px screen's page at 720 px): nothing wider than the window.
          for (const width of [1920, 720, 360]) {
            expect(await sidewaysAt(page, width), `sideways scroll at ${String(width)} px`).toBeLessThanOrEqual(0);
          }
        }
      });
    }
  }
}
