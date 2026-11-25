import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { letterhead, open, patient, payer, posted, sessionOf, type Answers } from './stubbed';

// The printed bill, receipt and refund voucher in a real browser (Prompt 10.4b, ADR 0054), with no API behind them:
// Print records the print and only then opens the dialog, and the sheet says Original; on paper there is no application
// around the sheet and no bar above it, the text is dark on white in either theme, and the sheet prints to an A4 PDF;
// on a phone nothing scrolls sideways; and each page passes axe. It needs the built app (`npm run build`,
// `npm run preview`, E2E_BASE_URL=http://localhost:4173).

const cashier = sessionOf(['Cashier', 'BillingClerk']);
const desktop = { width: 1440, height: 900 };
const phone = { width: 390, height: 844 };
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
  origin: 'Standard',
  legacyNumber: null,
  notes: null,
  reversedAtUtc: null,
  reversalReason: null,
  createdAtUtc: '2026-11-21T05:00:00+00:00',
  updatedAtUtc: '2026-11-21T05:00:00+00:00',
  version: 'AAAAAAAACQA=',
};
const allocation = {
  id: 'f7000000-0000-4000-8000-000000000001',
  billId: posted.id,
  amount: 500,
  status: 'Active',
  allocatedAtUtc: '2026-11-21T06:00:00+00:00',
  allocatedByUserId: null,
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
  status: 'Posted',
  number: 'REF/26-27/000004',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-11-22T06:00:00+00:00',
  voidedAtUtc: null,
  voidReason: null,
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T06:00:00+00:00',
  version: 'AAAAAAAACgA=',
};
const original = { copyNumber: 1, original: true, printedAtUtc: '2026-11-25T06:30:00+00:00' };

const answers: Answers = {
  'GET /api/v1/settings/letterhead': () => letterhead,
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  [`GET /api/v1/patients/${patient.id}`]: () => patient,
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  [`GET /api/v1/payments/${payment.id}/allocations`]: () => ({ payment, allocations: [allocation] }),
  [`GET /api/v1/refunds/${refund.id}`]: () => refund,
  [`POST /api/v1/bills/${posted.id}/prints`]: () => original,
  [`POST /api/v1/payments/${payment.id}/prints`]: () => original,
  [`POST /api/v1/refunds/${refund.id}/prints`]: () => original,
};

const documents = [
  { name: 'the bill', path: `/bills/${posted.id}/print`, title: 'Tax Invoice', number: posted.number },
  { name: 'the receipt', path: `/payments/${payment.id}/print`, title: 'Receipt', number: payment.number },
  { name: 'the refund voucher', path: `/refunds/${refund.id}/print`, title: 'Refund Voucher', number: refund.number },
] as const;

/** Whether an element is laid out on the page as it is now (print or screen). */
const shown = (page: Page, selector: string) =>
  page.evaluate((query) => {
    const element = document.querySelector(query);
    return element !== null && getComputedStyle(element).display !== 'none' && element.getClientRects().length > 0;
  }, selector);

/** The browser's print dialog cannot be driven by a test: it is counted instead of opened. */
async function countPrints(page: Page) {
  await page.addInitScript(() => {
    const counted = window as unknown as { prints: number };
    counted.prints = 0;
    window.print = () => {
      counted.prints += 1;
    };
  });
}

for (const { name, path, title, number } of documents) {
  for (const theme of ['light', 'dark'] as const) {
    test(`${name} is recorded, marked Original and printed on A4 in the ${theme} theme`, async ({ page }, info) => {
      await countPrints(page);
      const view = await open(page, path, theme, desktop, answers, cashier);
      const sheet = page.getByRole('article', { name: title });
      await expect(sheet).toBeVisible();
      await expect(sheet.getByText(number, { exact: true })).toBeVisible();
      await expect(sheet.getByText('City Hospital Private Limited').first()).toBeVisible();
      await expect(sheet.getByText('Unrecorded copy')).toBeVisible();
      await page.waitForLoadState('networkidle');

      const axe = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
        .analyze();
      const serious = axe.violations
        .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
        .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
      expect(serious, 'axe').toEqual([]);

      await page.getByRole('button', { name: 'Print', exact: true }).click();
      await expect(sheet.getByText('Original', { exact: false })).toBeVisible();
      await expect.poll(() => page.evaluate(() => (window as unknown as { prints: number }).prints)).toBe(1);

      // On paper: the sheet alone, dark on white whatever the theme.
      await page.emulateMedia({ media: 'print' });
      expect(await shown(page, '#sidebar'), 'the sidebar').toBe(false);
      expect(await shown(page, 'header[class*="topBar"]'), 'the top bar').toBe(false);
      expect(await shown(page, 'main button'), 'a button').toBe(false);
      expect(await shown(page, 'main article'), 'the sheet').toBe(true);
      const colours = await page.evaluate(() => {
        const article = document.querySelector('main article');
        return {
          ground: getComputedStyle(document.body).backgroundColor,
          sheet: article === null ? '' : getComputedStyle(article).backgroundColor,
          text: article === null ? '' : getComputedStyle(article).color,
        };
      });
      expect(colours).toEqual({ ground: 'rgb(255, 255, 255)', sheet: 'rgb(255, 255, 255)', text: 'rgb(0, 0, 0)' });

      const pdf = await page.pdf({ format: 'A4', printBackground: true });
      expect(pdf.byteLength).toBeGreaterThan(1000);
      await info.attach(`${name}-${theme}.pdf`, { body: pdf, contentType: 'application/pdf' });

      expect(view.errors).toEqual([]);
      expect(view.unexpected).toEqual([]);
      expect((await view.probe()).violations).toEqual([]);
    });
  }

  test(`${name} fits a phone without a sideways scroll of the page`, async ({ page }) => {
    const view = await open(page, path, 'light', phone, answers, cashier);
    await expect(page.getByRole('article', { name: title })).toBeVisible();
    await page.waitForLoadState('networkidle');

    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(sideways, 'sideways scroll of the page').toBeLessThanOrEqual(0);
    expect(view.errors).toEqual([]);
  });
}
