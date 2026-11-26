import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import {
  letterhead,
  logoImage,
  open,
  patient,
  payer,
  posted,
  printSettings,
  screenshots,
  sessionOf,
  signatureImage,
  type Answers,
} from './stubbed';

// The print settings in a real browser (Prompt 10.4c, ADR 0059), with no API behind them and the page under the content
// security policy of frontend/deploy/nginx.conf: the paper and the margins a hospital set are what the printer is
// given (read from the PDF's own page box and from where the sheet lies on it); the default is A4 as before; a roll of
// 80 mm for a receipt; the sheet carries no inline style and the policy refuses nothing; and on the Print tab the
// preview follows the form before it is saved, in both themes. It needs the built app (`npm run build`,
// `npm run preview`, E2E_BASE_URL=http://localhost:4173). E2E_SCREENSHOTS=1 also writes the screenshots into docs/ui/.

const cashier = sessionOf(['Cashier', 'BillingClerk']);
const administrator = sessionOf(['TenantAdmin']);
const desktop = { width: 1440, height: 900 };
const signed = {
  ...letterhead,
  signatory: 'Dr A. Example',
  signatoryDesignation: 'Medical Superintendent',
  logoSha256: 'a'.repeat(64),
  signatureSha256: 'b'.repeat(64),
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
  unappliedAmount: 2000,
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
const original = { copyNumber: 1, original: true, printedAtUtc: '2026-11-26T06:30:00+00:00' };

const answers = (settings: Parameters<typeof printSettings>[0] = {}): Answers => ({
  'GET /api/v1/settings/letterhead': () => signed,
  'GET /api/v1/settings/letterhead/logo': () => logoImage,
  'GET /api/v1/settings/letterhead/signature': () => signatureImage,
  'GET /api/v1/settings/print': () => printSettings(settings),
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  [`GET /api/v1/patients/${patient.id}`]: () => ({
    ...patient,
    phone: '+91 00000 00000',
    addressLine1: '4 Example Lane',
  }),
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  [`GET /api/v1/payments/${payment.id}/allocations`]: () => ({ payment, allocations: [] }),
  [`GET /api/v1/payments/${payment.id}/recorder`]: () => ({ name: 'Meera Nair' }),
  [`POST /api/v1/bills/${posted.id}/prints`]: () => original,
  [`POST /api/v1/payments/${payment.id}/prints`]: () => original,
});

const mm = (millimetres: number) => (millimetres * 72) / 25.4;

/** The size of the first page of a PDF in points, from its own page box. */
function pageBox(pdf: Buffer): { width: number; height: number } {
  const box = /\/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\]/.exec(pdf.toString('latin1'));
  expect(box, 'the page box of the PDF').not.toBeNull();
  return { width: Number(box?.[3]) - Number(box?.[1]), height: Number(box?.[4]) - Number(box?.[2]) };
}

/** Prints the page as the browser's own dialog would: on the paper and with the margins the page itself asks for. */
const printed = (page: Page) => page.pdf({ preferCSSPageSize: true, printBackground: true });

/** What the page says of its own layout: the sheets it adopted, and whether anything carries an inline style. */
const layoutOfPage = (page: Page) =>
  page.evaluate(() => ({
    rules: document.adoptedStyleSheets.flatMap((sheet) => Array.from(sheet.cssRules).map((rule) => rule.cssText)),
    inlineStyles: document.querySelectorAll('main [style]').length,
    styleElements: document.querySelectorAll('body style').length,
  }));

async function ready(page: Page, title: string) {
  const sheet = page.getByRole('article', { name: title });
  await expect(sheet).toBeVisible();
  await expect(page.getByRole('button', { name: 'Print', exact: true })).toBeEnabled();
  await page.waitForLoadState('networkidle');
  return sheet;
}

test('a bill with no settings saved is given to the printer on A4 with the margins it always had', async ({ page }) => {
  const view = await open(page, `/bills/${posted.id}/print`, 'light', desktop, answers(), cashier);
  await ready(page, 'Tax Invoice');

  const layout = await layoutOfPage(page);
  expect(layout.rules.join(' ')).toMatch(/size: a4/i);
  expect(layout.rules.join(' ')).toMatch(/margin: 14mm 12mm/);
  expect(layout).toMatchObject({ inlineStyles: 0, styleElements: 0 });

  const box = pageBox(await printed(page));
  expect(box.width).toBeCloseTo(mm(210), 0);
  expect(box.height).toBeCloseTo(mm(297), 0);

  expect(view.errors).toEqual([]);
  expect(view.unexpected).toEqual([]);
  expect((await view.probe()).violations).toEqual([]);
});

for (const theme of ['light', 'dark'] as const) {
  test(`a bill set to A5 with wide margins and the logo in the centre prints that way in the ${theme} theme`, async ({
    page,
  }, info) => {
    const settings = {
      Bill: {
        paper: 'A5',
        marginTopMm: 20,
        marginRightMm: 12,
        marginBottomMm: 20,
        marginLeftMm: 12,
        logoPosition: 'Centre',
        logoSize: 'Large',
        showPatientContact: true,
        footerLine: 'Open 24 hours',
        terms: 'Payment is due within 15 days.\nKeep this bill for your records.',
      },
    };
    // The dialog is not opened by a test: a print in progress would refuse the PDF below.
    await page.addInitScript(() => {
      window.print = () => undefined;
    });
    const view = await open(page, `/bills/${posted.id}/print`, theme, desktop, answers(settings), cashier);
    const sheet = await ready(page, 'Tax Invoice');
    await expect(sheet.getByText('Open 24 hours')).toBeVisible();
    await expect(sheet.getByText('4 Example Lane')).toBeVisible();
    // The logo stands over the name, in its large box of 12 rem by 6 rem.
    const logo = await sheet.locator('header > div').first().boundingBox();
    const name = await sheet.getByText('City Hospital Private Limited').first().boundingBox();
    expect([Math.round(logo?.width ?? 0), Math.round(logo?.height ?? 0)]).toEqual([192, 96]);
    expect((logo?.y ?? 0) + (logo?.height ?? 0)).toBeLessThanOrEqual(name?.y ?? 0);

    const layout = await layoutOfPage(page);
    expect(layout.rules.join(' ')).toMatch(/size: a5/i);
    expect(layout.rules.join(' ')).toMatch(/margin: 20mm 12mm/);
    expect(layout).toMatchObject({ inlineStyles: 0, styleElements: 0 });

    await page.getByRole('button', { name: 'Print', exact: true }).click();
    await expect(sheet.getByText('Original', { exact: false })).toBeVisible();

    // On paper: A5, dark on white whatever the theme, the sheet inside the margins that were set.
    await page.emulateMedia({ media: 'print' });
    const onPaper = await page.evaluate(() => {
      const article = document.querySelector('main article');
      const rect = article?.getBoundingClientRect();
      return {
        ground: getComputedStyle(document.body).backgroundColor,
        text: article === null ? '' : getComputedStyle(article).color,
        terms: getComputedStyle(document.querySelector('main article h3 + p') as Element).whiteSpace,
        width: rect?.width ?? 0,
      };
    });
    expect(onPaper.ground).toBe('rgb(255, 255, 255)');
    expect(onPaper.text).toBe('rgb(0, 0, 0)');
    expect(onPaper.terms).toBe('pre-line');
    // The nine columns of a tax invoice fit the A5 sheet between its margins (148 - 24 = 124 mm).
    await page.setViewportSize({ width: Math.round((124 * 96) / 25.4), height: 900 });
    const overflow = await page.evaluate(() => {
      const table = document.querySelector('main article table') as HTMLElement;
      const article = document.querySelector('main article') as HTMLElement;
      return Math.round(table.getBoundingClientRect().right - article.getBoundingClientRect().right);
    });
    expect(overflow, 'the table past the edge of the sheet').toBeLessThanOrEqual(0);
    // No figure is broken inside itself: every amount of the table stands on one line of its cell.
    const broken = await page.evaluate(() =>
      Array.from(document.querySelectorAll('main article tbody td'))
        .slice(2)
        // A cell that holds one figure and no space; an amount with its rate may stand on two lines, at the space.
        .filter((cell) => !/\s/.test(cell.textContent.trim()) && cell.getClientRects().length > 0)
        .filter((cell) => {
          const range = document.createRange();
          range.selectNodeContents(cell);
          return range.getClientRects().length > 1;
        })
        .map((cell) => cell.textContent),
    );
    expect(broken, 'figures broken across lines').toEqual([]);
    await page.setViewportSize(desktop);

    const pdf = await printed(page);
    const box = pageBox(pdf);
    expect(box.width).toBeCloseTo(mm(148), 0);
    expect(box.height).toBeCloseTo(mm(210), 0);
    await info.attach(`bill-a5-${theme}.pdf`, { body: pdf, contentType: 'application/pdf' });

    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });
}

test('a receipt set to an 80 mm roll is given to the printer 80 mm wide, with the cashier named', async ({
  page,
}, info) => {
  const settings = {
    Payment: {
      paper: 'Roll80',
      marginTopMm: 5,
      marginRightMm: 5,
      marginBottomMm: 5,
      marginLeftMm: 5,
      showCashierName: true,
    },
  };
  const view = await open(page, `/payments/${payment.id}/print`, 'light', desktop, answers(settings), cashier);
  const sheet = await ready(page, 'Receipt');
  await expect(sheet.getByText('Meera Nair')).toBeVisible();

  const layout = await layoutOfPage(page);
  expect(layout.rules.join(' ')).toMatch(/size: 80mm 297mm/);
  expect(layout).toMatchObject({ inlineStyles: 0, styleElements: 0 });

  const pdf = await printed(page);
  expect(pageBox(pdf).width).toBeCloseTo(mm(80), 0);
  await info.attach('receipt-roll.pdf', { body: pdf, contentType: 'application/pdf' });

  // On the roll nothing is wider than the paper between its margins: 70 mm.
  await page.emulateMedia({ media: 'print' });
  await page.setViewportSize({ width: Math.round((70 * 96) / 25.4), height: 900 });
  const overflow = await page.evaluate(() => {
    const article = document.querySelector('main article') as HTMLElement;
    return article.scrollWidth - article.clientWidth;
  });
  expect(overflow, 'sideways overflow of the sheet on the roll').toBeLessThanOrEqual(0);

  expect(view.errors).toEqual([]);
  expect(view.unexpected).toEqual([]);
  expect((await view.probe()).violations).toEqual([]);
});

test('a sheet on pre-printed stationery leaves the space that was set and prints no head of its own', async ({
  page,
}) => {
  const settings = { Bill: { prePrintedStationery: true, stationeryTopMm: 40 } };
  const view = await open(page, `/bills/${posted.id}/print`, 'light', desktop, answers(settings), cashier);
  const sheet = await ready(page, 'Tax Invoice');

  await expect(sheet.locator('header')).toHaveCount(0);
  const space = await sheet.getByTestId('stationery-space').boundingBox();
  // 40 mm at 96 pixels an inch.
  expect(Math.round(space?.height ?? 0)).toBe(Math.round((40 * 96) / 25.4));
  expect((await layoutOfPage(page)).inlineStyles).toBe(0);
  expect(view.unexpected).toEqual([]);
  expect((await view.probe()).violations).toEqual([]);
});

for (const theme of ['light', 'dark'] as const) {
  test(`on the Print tab the preview follows the form before it is saved, in the ${theme} theme`, async ({ page }) => {
    let saved: Record<string, unknown> | null = null;
    const view = await open(
      page,
      '/administration/print',
      theme,
      desktop,
      {
        ...answers(),
        'PUT /api/v1/settings/print/Bill': () => {
          saved = { ...printSettings()[0], paper: 'A5', footerLine: 'Open 24 hours', version: 'AAAAAAAAD0E=' };
          return saved;
        },
      },
      administrator,
    );
    const form = page.getByRole('form', { name: 'How a bill is printed' });
    const preview = page.getByRole('article', { name: 'Tax Invoice' });
    await expect(preview).toBeVisible();
    await expect(preview.getByRole('img', { name: 'Signature of Dr A. Example' })).toBeVisible();
    const widthOnA4 = (await preview.boundingBox())?.width ?? 0;

    // What a tax invoice must show is locked, and says why.
    const sac = form.getByRole('checkbox', { name: /^The SAC column/ });
    await expect(sac).toBeChecked();
    await expect(sac).toBeDisabled();
    await expect(
      form.getByText('A tax invoice must show this: it cannot be switched off for a bill.').first(),
    ).toBeVisible();

    await form.getByLabel('Paper size').selectOption('A5');
    await form.getByLabel('Footer line').fill('Open 24 hours');
    await form.getByRole('checkbox', { name: 'The signature block' }).uncheck();
    await expect(preview.getByText('Open 24 hours')).toBeVisible();
    await expect(preview.getByRole('img', { name: /^Signature of/ })).toHaveCount(0);
    expect((await preview.boundingBox())?.width ?? 0).toBeLessThan(widthOnA4);
    expect(saved, 'nothing was sent yet').toBeNull();

    // The preview is paper: white with dark text in either theme, with no inline style, under the page's policy.
    const paper = await preview.evaluate((article) => ({
      ground: getComputedStyle(article).backgroundColor,
      inline: article.querySelectorAll('[style]').length + (article.hasAttribute('style') ? 1 : 0),
    }));
    expect(paper).toEqual({ ground: 'rgb(255, 255, 255)', inline: 0 });
    const rules = (await layoutOfPage(page)).rules.join(' ');
    expect(rules).toContain('.print-sheet-preview');
    expect(rules, 'a preview gives the printer no paper').not.toContain('@page');

    await form.getByRole('checkbox', { name: 'The signature block' }).check();
    await page.waitForLoadState('networkidle');
    const axe = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    const serious = axe.violations
      .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
      .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
    expect(serious, 'axe').toEqual([]);

    if (screenshots) {
      await page.screenshot({
        path: `../docs/ui/settings-print${theme === 'dark' ? '-dark' : ''}.png`,
        fullPage: true,
      });
    }

    const request = page.waitForRequest((sent) => sent.method() === 'PUT');
    await form.getByRole('button', { name: 'Save how a bill is printed' }).click();
    const sent = await request;
    expect(sent.headers()['if-match']).toBe('"AAAAAAAAAAA="');
    expect(sent.postDataJSON()).toMatchObject({
      paper: 'A5',
      footerLine: 'Open 24 hours',
      showSacColumn: true,
      showTaxSplit: true,
    });
    await expect(page.getByText('Every bill printed from now on is laid out this way.')).toBeVisible();

    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });
}

test('the Print tab fits a phone: the sheet scrolls inside its own frame, never the page', async ({ page }) => {
  const view = await open(
    page,
    '/administration/print',
    'light',
    { width: 390, height: 844 },
    answers(),
    administrator,
  );
  await expect(page.getByRole('article', { name: 'Tax Invoice' })).toBeVisible();
  await page.waitForLoadState('networkidle');

  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(sideways, 'sideways scroll of the page').toBeLessThanOrEqual(0);
  expect(view.errors).toEqual([]);
});
