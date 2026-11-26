import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import {
  ImageBody,
  letterhead,
  logoImage,
  open,
  pageOf,
  patient,
  payer,
  posted,
  screenshots,
  sessionOf,
  signatureImage,
  type Answers,
} from './stubbed';

// The hospital's logo and its signatory's signature in a real browser (Prompt 10.4c, ADR 0059), with no API behind
// them and the page under the content security policy of frontend/deploy/nginx.conf: the images are read with the
// session's token and shown from their own bytes, which the policy admits and never refuses; each stands in a box of a
// fixed size, so nothing on the sheet moves when it arrives; Print waits for them; on paper the sheet is dark on white
// in either theme and prints to an A4 PDF that holds the images; and the administrator uploads, sees, replaces and
// removes them on the Settings screen. It needs the built app (`npm run build`, `npm run preview`,
// E2E_BASE_URL=http://localhost:4173). E2E_SCREENSHOTS=1 also writes the screenshots into docs/ui/.

const cashier = sessionOf(['Cashier', 'BillingClerk']);
const administrator = sessionOf(['TenantAdmin']);
const desktop = { width: 1440, height: 900 };
const phone = { width: 390, height: 844 };
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
const original = { copyNumber: 1, original: true, printedAtUtc: '2026-11-26T06:30:00+00:00' };

const answers: Answers = {
  'GET /api/v1/settings/letterhead': () => signed,
  'GET /api/v1/settings/letterhead/logo': () => logoImage,
  'GET /api/v1/settings/letterhead/signature': () => signatureImage,
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  [`GET /api/v1/patients/${patient.id}`]: () => patient,
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  [`GET /api/v1/payments/${payment.id}/allocations`]: () => ({ payment, allocations: [] }),
  [`GET /api/v1/refunds/${refund.id}`]: () => refund,
  [`POST /api/v1/bills/${posted.id}/prints`]: () => original,
  [`POST /api/v1/payments/${payment.id}/prints`]: () => original,
  [`POST /api/v1/refunds/${refund.id}/prints`]: () => original,
};

const documents = [
  { name: 'the bill', path: `/bills/${posted.id}/print`, title: 'Tax Invoice' },
  { name: 'the receipt', path: `/payments/${payment.id}/print`, title: 'Receipt' },
  { name: 'the refund voucher', path: `/refunds/${refund.id}/print`, title: 'Refund Voucher' },
] as const;

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

/** Where the two images and the parts of the sheet around them are, to the pixel. */
const layout = (page: Page) =>
  page.evaluate(() => {
    const box = (selector: string) => {
      const rect = document.querySelector(`main article ${selector}`)?.getBoundingClientRect();
      return rect === undefined ? null : [rect.x, rect.y, rect.width, rect.height].map((value) => Math.round(value));
    };
    return {
      logoBox: box('header > div:first-child'),
      hospital: box('header p'),
      title: box('#sheet-title'),
      signatureBox: box('footer > div:first-of-type'),
      foot: box('footer > p:last-child'),
    };
  });

const decodedImages = (page: Page) =>
  page.evaluate(() =>
    Array.from(document.querySelectorAll('main article img')).map((image) => {
      const img = image as HTMLImageElement;
      return { complete: img.complete, width: img.naturalWidth, height: img.naturalHeight, data: img.src.slice(0, 22) };
    }),
  );

for (const { name, path, title } of documents) {
  for (const theme of ['light', 'dark'] as const) {
    test(`${name} carries the logo and the signature and prints on A4 in the ${theme} theme`, async ({
      page,
    }, info) => {
      await countPrints(page);
      const view = await open(page, path, theme, desktop, answers, cashier);
      const sheet = page.getByRole('article', { name: title });
      await expect(sheet).toBeVisible();
      await expect(sheet.getByRole('img', { name: 'Signature of Dr A. Example' })).toBeVisible();
      await expect(sheet.getByText('Medical Superintendent')).toBeVisible();
      await expect(sheet.getByText('Authorised signatory')).toBeVisible();
      await page.waitForLoadState('networkidle');

      // Both images are the stub's own bytes, decoded by the browser from a data URL the policy admits.
      expect(await decodedImages(page)).toEqual([
        { complete: true, width: 360, height: 180, data: 'data:image/png;base64,' },
        { complete: true, width: 320, height: 96, data: 'data:image/png;base64,' },
      ]);

      // Each in its box of a fixed size: 9 rem by 4.5 rem at the head, 3.5 rem high at the foot.
      const placed = await layout(page);
      expect(placed.logoBox?.slice(2)).toEqual([144, 72]);
      expect(placed.signatureBox?.[3]).toBe(56);

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

      if (screenshots && name === 'the bill') {
        await page.screenshot({
          path: `../docs/ui/bill-printed-letterhead${theme === 'dark' ? '-dark' : ''}.png`,
          fullPage: true,
        });
      }

      // On paper: the sheet alone, dark on white whatever the theme, with its two images.
      await page.emulateMedia({ media: 'print' });
      const colours = await page.evaluate(() => {
        const article = document.querySelector('main article');
        const label = document.querySelector('main article footer p');
        return {
          ground: getComputedStyle(document.body).backgroundColor,
          sheet: article === null ? '' : getComputedStyle(article).backgroundColor,
          text: article === null ? '' : getComputedStyle(article).color,
          images: document.querySelectorAll('main article img').length,
          labelIsDark: label !== null && /^rgb\((\d+), (\d+), (\d+)\)$/.test(getComputedStyle(label).color),
        };
      });
      expect(colours).toEqual({
        ground: 'rgb(255, 255, 255)',
        sheet: 'rgb(255, 255, 255)',
        text: 'rgb(0, 0, 0)',
        images: 2,
        labelIsDark: true,
      });

      const pdf = await page.pdf({ format: 'A4', printBackground: true });
      // A sheet with two images is far heavier than the type alone.
      expect(pdf.byteLength).toBeGreaterThan(5000);
      expect(pdf.subarray(0, 2048).toString('latin1')).toContain('%PDF-');
      await info.attach(`${name}-letterhead-${theme}.pdf`, { body: pdf, contentType: 'application/pdf' });

      expect(view.errors).toEqual([]);
      expect(view.unexpected).toEqual([]);
      expect((await view.probe()).violations).toEqual([]);
    });
  }

  test(`${name} with its images fits a phone without a sideways scroll of the page`, async ({ page }) => {
    const view = await open(page, path, 'light', phone, answers, cashier);
    await expect(page.getByRole('article', { name: title }).getByRole('img', { name: /^Signature of/ })).toBeVisible();
    await page.waitForLoadState('networkidle');

    const sideways = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(sideways, 'sideways scroll of the page').toBeLessThanOrEqual(0);
    expect(view.errors).toEqual([]);
  });
}

test('nothing on the sheet moves when the images arrive, and Print waits for them', async ({ page }) => {
  let release: () => void = () => undefined;
  const held = new Promise<void>((resolve) => {
    release = resolve;
  });
  await countPrints(page);
  // The two images are held back until the test lets them go; everything else answers at once.
  const late: Answers = {
    ...answers,
    'GET /api/v1/settings/letterhead/logo': () => held.then(() => logoImage),
    'GET /api/v1/settings/letterhead/signature': () => held.then(() => signatureImage),
  };
  const view = await open(page, `/bills/${posted.id}/print`, 'light', desktop, late, cashier);
  const sheet = page.getByRole('article', { name: 'Tax Invoice' });
  await expect(sheet.getByText('Asha Verma (MRN MRN-1001)')).toBeVisible();
  await expect(sheet.getByText('Medical Superintendent')).toBeVisible();

  const before = await layout(page);
  expect(await decodedImages(page)).toEqual([]);
  const button = page.getByRole('button', { name: 'Preparing…' });
  await expect(button).toBeDisabled();
  expect(await page.evaluate(() => (window as unknown as { prints: number }).prints)).toBe(0);

  release();
  await expect(sheet.getByRole('img', { name: 'Signature of Dr A. Example' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Print', exact: true })).toBeEnabled();
  expect(await layout(page)).toEqual(before);

  expect(view.errors).toEqual([]);
  expect((await view.probe()).violations).toEqual([]);
});

test('a sheet whose images cannot be read prints without them', async ({ page }) => {
  await countPrints(page);
  const broken: Answers = { ...answers };
  delete broken['GET /api/v1/settings/letterhead/logo'];
  // Bytes that are called a PNG and are none: the browser shows a broken image, and the print is not held back.
  broken['GET /api/v1/settings/letterhead/signature'] = () => new ImageBody('image/png', Buffer.from('not an image'));
  await open(page, `/bills/${posted.id}/print`, 'light', desktop, broken, cashier);
  const sheet = page.getByRole('article', { name: 'Tax Invoice' });
  await expect(sheet.getByText('Dr A. Example')).toBeVisible();

  await page.getByRole('button', { name: 'Print', exact: true }).click();

  await expect(sheet.getByText('Original', { exact: false })).toBeVisible();
  await expect.poll(() => page.evaluate(() => (window as unknown as { prints: number }).prints)).toBe(1);
});

for (const theme of ['light', 'dark'] as const) {
  test(`the administrator uploads, sees and removes the logo and the signature in the ${theme} theme`, async ({
    page,
  }) => {
    const settings = {
      postingApprovalThreshold: 100000,
      maxBackdateDays: 7,
      defaultDueDays: 15,
      prefixes: { bill: 'BIL', creditNote: 'CRN', payment: 'PAY', adjustment: 'ADJ', refund: 'REF' },
      letterhead: { ...letterhead } as Record<string, string | null>,
      version: 'AAAAAAAAC1E=',
    };
    let now = settings;
    const change = (part: Record<string, string | null>, version: string) => () => {
      now = { ...now, letterhead: { ...now.letterhead, ...part }, version };
      return now;
    };
    const view = await open(
      page,
      '/administration/settings',
      theme,
      desktop,
      {
        'GET /api/v1/settings': () => now,
        'GET /api/v1/settings/prefix-changes': () => pageOf([]),
        'GET /api/v1/settings/letterhead/logo': () => logoImage,
        'GET /api/v1/settings/letterhead/signature': () => signatureImage,
        'PUT /api/v1/settings/letterhead/logo': change({ logoSha256: 'a'.repeat(64) }, 'AAAAAAAAC1F='),
        'PUT /api/v1/settings/letterhead/signature': change({ signatureSha256: 'b'.repeat(64) }, 'AAAAAAAAC1G='),
        'POST /api/v1/settings/letterhead/logo/remove': change({ logoSha256: null }, 'AAAAAAAAC1H='),
      },
      administrator,
    );
    await expect(page.getByRole('heading', { level: 2, name: 'Logo and signature' })).toBeVisible();
    await expect(page.getByText('No logo yet')).toBeVisible();

    // An SVG is refused at the field, and nothing is sent.
    await page.getByLabel('Choose a logo').setInputFiles({
      name: 'logo.svg',
      mimeType: 'image/svg+xml',
      buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg"/>'),
    });
    await expect(page.getByText('An SVG is not accepted. Choose a PNG, JPEG or WebP image.')).toBeVisible();

    // The logo goes as the file itself, with its type and the version of the settings.
    const upload = page.waitForRequest(
      (request) => request.method() === 'PUT' && request.url().endsWith('/letterhead/logo'),
    );
    await page
      .getByLabel('Choose a logo')
      .setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: logoImage.bytes });
    const sent = await upload;
    expect(sent.headers()['content-type']).toBe('image/png');
    expect(sent.headers()['if-match']).toBe('"AAAAAAAAC1E="');
    expect(sent.postDataBuffer()?.equals(logoImage.bytes)).toBe(true);
    await expect(page.getByText('Documents printed from now on carry this logo.')).toBeVisible();
    const preview = page.getByRole('img', { name: 'The logo as it is printed' });
    await expect(preview).toBeVisible();
    expect(await preview.evaluate((image) => (image as HTMLImageElement).naturalWidth)).toBe(360);

    // The signature after it carries the version the logo's save answered.
    const second = page.waitForRequest(
      (request) => request.method() === 'PUT' && request.url().endsWith('/letterhead/signature'),
    );
    await page
      .getByLabel('Choose a signature')
      .setInputFiles({ name: 'signature.png', mimeType: 'image/png', buffer: signatureImage.bytes });
    expect((await second).headers()['if-match']).toBe('"AAAAAAAAC1F="');
    await expect(page.getByRole('img', { name: 'The signature as it is printed' })).toBeVisible();
    await page.waitForLoadState('networkidle');

    const axe = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    const serious = axe.violations
      .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
      .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
    expect(serious, 'axe').toEqual([]);

    if (screenshots) {
      await page.getByRole('heading', { level: 2, name: 'Logo and signature' }).scrollIntoViewIfNeeded();
      await page.screenshot({ path: `../docs/ui/settings-letterhead-images${theme === 'dark' ? '-dark' : ''}.png` });
    }

    await page.getByRole('button', { name: 'Remove the logo' }).click();
    await expect(page.getByText('Documents printed from now on carry no logo.')).toBeVisible();
    await expect(page.getByText('No logo yet')).toBeVisible();
    await expect(page.getByRole('img', { name: 'The signature as it is printed' })).toBeVisible();

    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });
}
