import { expect, test, type Page } from '@playwright/test';
import { open, pageOf, patient, payer, posted, screenshots, sessionOf, trail, type Answers } from './stubbed';

// Print, forced colours and the keyboard (Prompt 9.4b, ADR 0050 amended), with the API's answers given by the test under
// nginx's content security policy. On paper a bill, a payer's statement and a report have no shell, dark text on white
// in either theme, the hospital's name and the day at the top, and no row cut in two; each prints to an A4 PDF. Windows
// forced colours and prefers-contrast still show the page's heading, its controls and the focus. The keyboard reaches
// the content first. It needs the built app (`npm run build`, `npm run preview`, E2E_BASE_URL=http://localhost:4173).

const everyRole = sessionOf(['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier']);
const period = { from: '2026-11-01', to: '2026-11-30' };
const report = (items: object[], totals: object) => ({ ...pageOf(items), totals, period });
const billRows = Array.from({ length: 60 }, (_, index) => ({
  billId: `e5000000-0000-4000-8000-${String(index + 100).padStart(12, '0')}`,
  number: `BIL/26-27/${String(index + 100).padStart(6, '0')}`,
  billDate: '2026-11-20',
  dueDate: '2026-12-05',
  patientId: patient.id,
  patientName: patient.fullName,
  payerId: payer.id,
  payerName: payer.name,
  total: 1000,
  balanceDue: 500,
  status: 'PartiallyPaid',
  postedAtUtc: '2026-11-20T06:00:00+00:00',
}));

const answers: Answers = {
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  [`GET /api/v1/patients/${patient.id}`]: () => patient,
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  'GET /api/v1/credit-notes': () => pageOf([]),
  'GET /api/v1/adjustments': () => pageOf([]),
  'GET /api/v1/bills': () => pageOf([posted]),
  'GET /api/v1/audit-records': (url) => trail(url.searchParams.get('EntityId') ?? '', ['Drafted', 'Posted']),
  [`GET /api/v1/reports/payers/${payer.id}/balance`]: () => ({
    payerId: payer.id,
    payerName: payer.name,
    receivable: 949.48,
    unappliedCash: 0,
    creditAvailable: 0,
    netOutstanding: 949.48,
  }),
  'GET /api/v1/reports/unapplied-cash': () => report([], { payments: 0, unappliedAmount: 0 }),
  'GET /api/v1/reports/available-credit': () => report([], { creditNotes: 0, availableAmount: 0 }),
  'GET /api/v1/reports/bill-register': () => ({
    ...report(billRows, { bills: 60, total: 60000, balanceDue: 30000 }),
    pageSize: 100,
  }),
};

const printed = [
  { name: 'a bill', path: `/bills/${posted.id}`, heading: posted.number },
  { name: 'a statement', path: `/payers/${payer.id}/statement`, heading: 'Statement of Asha Verma' },
  { name: 'a report', path: '/reports/bills', heading: 'Bill register' },
] as const;

/** Whether an element is laid out on the page as it is now (print or screen). */
const shown = (page: Page, selector: string) =>
  page.evaluate((query) => {
    const element = document.querySelector(query);
    return element !== null && getComputedStyle(element).display !== 'none' && element.getClientRects().length > 0;
  }, selector);

for (const { name, path, heading } of printed) {
  for (const theme of ['light', 'dark'] as const) {
    test(`${name} prints on A4 in the ${theme} theme: no shell, dark on white, the hospital and the day`, async ({
      page,
    }, info) => {
      const { errors } = await open(page, path, theme, { width: 1440, height: 900 }, answers, everyRole);
      await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
      await page.waitForLoadState('networkidle');

      await page.emulateMedia({ media: 'print' });
      expect(await shown(page, '#sidebar'), 'the sidebar').toBe(false);
      expect(await shown(page, 'header[class*="topBar"]'), 'the top bar').toBe(false);
      expect(await shown(page, 'main button'), 'a button').toBe(false);
      await expect(page.getByText('city-hospital · BillingRecon · printed', { exact: false })).toBeVisible();
      const colours = await page.evaluate(() => ({
        ground: getComputedStyle(document.body).backgroundColor,
        text: getComputedStyle(document.querySelector('h1') ?? document.body).color,
      }));
      expect(colours.ground).toBe('rgb(255, 255, 255)');
      expect(colours.text).toBe('rgb(0, 0, 0)');
      const rows = await page.evaluate(() =>
        Array.from(document.querySelectorAll('main tr')).every((row) => getComputedStyle(row).breakInside === 'avoid'),
      );
      expect(rows, 'no row is cut in two').toBe(true);

      if (screenshots && name === 'a bill') {
        await page.screenshot({
          path: `../docs/ui/bill-printed${theme === 'dark' ? '-dark' : ''}.png`,
          fullPage: true,
        });
      }

      const pdf = await page.pdf({ format: 'A4', printBackground: true });
      expect(pdf.byteLength).toBeGreaterThan(1000);
      await info.attach(`${name}-${theme}.pdf`, { body: pdf, contentType: 'application/pdf' });
      expect(errors).toEqual([]);
    });
  }
}

for (const media of [{ forcedColors: 'active' }, { contrast: 'more' }] as const) {
  test(`with ${Object.keys(media)[0] ?? ''} the bill still shows its heading, its controls and the focus`, async ({
    page,
  }) => {
    await page.emulateMedia(media);
    const { errors } = await open(
      page,
      `/bills/${posted.id}`,
      'light',
      { width: 1440, height: 900 },
      answers,
      everyRole,
    );

    await expect(page.getByRole('heading', { level: 1, name: posted.number })).toBeVisible();
    const link = page.getByRole('link', { name: 'Draft a credit note' });
    await expect(link).toBeVisible();
    await link.focus();
    const outline = await link.evaluate((element) => getComputedStyle(element).outlineStyle);
    expect(outline, 'the focus is drawn').not.toBe('none');
    expect(errors).toEqual([]);
  });
}

test('by the keyboard the first stop is the way to the content, and it leads there', async ({ page }) => {
  await open(page, '/reports', 'light', { width: 1440, height: 900 }, answers, everyRole);
  await expect(page.getByRole('heading', { level: 1, name: 'Reports' })).toBeVisible();

  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
  await page.keyboard.press('Enter');
  await page.keyboard.press('Tab');
  expect(await page.evaluate(() => document.activeElement?.closest('main') !== null)).toBe(true);
});
