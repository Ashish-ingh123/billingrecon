import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { open, sessionOf, type Answers } from './stubbed';

// Bringing in a hospital's records and opening balances in a real browser (Prompt 10.3b, ADR 0053), with no API behind
// it: files are chosen from the disk, checked, and their findings shown; the result arrives without moving the page;
// the findings fit a phone without a sideways scroll of the page; everything is reached by the keyboard; and the
// pages pass axe in both themes with their results open. It needs the built app (`npm run build`, `npm run preview`,
// E2E_BASE_URL=http://localhost:4173).

const admin = sessionOf(['TenantAdmin']);
const desktop = { width: 1440, height: 900 };
const phone = { width: 390, height: 844 };
const file = (name: string, text: string) => ({ name, mimeType: 'text/csv', buffer: Buffer.from(text, 'utf8') });

const rejected = Array.from({ length: 12 }, (_, index) => ({
  file: index < 9 ? 'patients.csv' : 'guarantors.csv',
  line: index + 2,
  reason: `MRN NF-${String(index + 1)} is a patient here already, under another name. Nothing is merged: correct the file or the patient.`,
}));
const partyAnswer = (found: object[]) => ({
  dryRun: true,
  written: false,
  patientsCreated: 1998,
  patientsMatched: 2,
  payersCreated: 1998,
  guarantorsCreated: 12,
  guarantorsMatched: 0,
  rejected: found,
});
const accepted = {
  cutOverDate: '2027-04-01',
  accepted: true,
  receivables: 300,
  receivableTotal: 6584094.91,
  advances: 55,
  advanceTotal: 540270.61,
  creditBalanceTotal: 174519,
  rejected: [],
};

async function seriousFindings(page: Page): Promise<string[]> {
  const axe = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  return axe.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
}

for (const theme of ['light', 'dark'] as const) {
  test(`patients are checked before they are imported, and the findings are named (${theme})`, async ({ page }) => {
    const answers: Answers = { 'POST /api/v1/imports/parties': () => partyAnswer(rejected) };
    const view = await open(page, '/administration/import/patients', theme, desktop, answers, admin);
    await expect(page.getByRole('heading', { level: 1, name: 'Import patients and guarantors' })).toBeVisible();
    const check = page.getByRole('button', { name: 'Check the files' });
    await expect(check).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Import', exact: true })).toBeDisabled();

    await page.getByLabel(/^Patients/).setInputFiles(file('patients.csv', 'mrn,full_name\nNF-1,Asha Rao\n'));
    await expect(check).toBeEnabled();
    await view.settle();
    const before = (await view.probe()).shift;
    await check.click();

    await expect(page.getByRole('heading', { name: '12 rows cannot be brought in' })).toBeVisible();
    await expect(page.getByRole('row').nth(1)).toContainText('patients.csv');
    await expect(page.getByRole('row').nth(1)).toContainText('Line 2');
    await expect(page.getByRole('button', { name: 'Import', exact: true })).toBeDisabled();
    await view.settle();
    expect((await view.probe()).shift - before, 'layout shift when the result arrives').toBeLessThan(0.01);
    expect(await seriousFindings(page), 'axe with the findings open').toEqual([]);

    // The findings as a file, in the browser: nothing is asked of the API for it.
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download the findings' }).click();
    expect((await download).suggestedFilename()).toBe('patients-import-findings.csv');

    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });

  test(`opening balances are checked against the trial balance and then offered for approval (${theme})`, async ({
    page,
  }) => {
    const answers: Answers = { 'POST /api/v1/opening-batches/dry-run': () => accepted };
    const view = await open(page, '/administration/import/opening-balances', theme, desktop, answers, admin);
    await expect(page.getByRole('heading', { level: 1, name: 'Import opening balances' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Submit for approval' })).toHaveCount(0);

    await page.getByLabel(/^Cut-over date/).fill('2027-04-01');
    await page
      .getByLabel(/^Trial balance/)
      .setInputFiles(file('trial_balance.csv', 'account,amount\nReceivable,10.00\n'));
    await page.getByLabel(/^Unpaid bills/).setInputFiles(file('bills.csv', 'legacy_number,mrn\nOLD-1,NF-1\n'));
    await page.getByRole('button', { name: 'Check the files' }).click();

    await expect(page.getByText('The files agree with the trial balance')).toBeVisible();
    await expect(page.getByText('Owed in all')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Submit for approval' })).toBeEnabled();
    expect(await seriousFindings(page), 'axe with the result open').toEqual([]);
    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });
}

test('on a phone the findings scroll in their own frame, not the page', async ({ page }) => {
  const answers: Answers = { 'POST /api/v1/imports/parties': () => partyAnswer(rejected) };
  const view = await open(page, '/administration/import/patients', 'light', phone, answers, admin);
  await page.getByLabel(/^Patients/).setInputFiles(file('patients.csv', 'mrn,full_name\nNF-1,Asha Rao\n'));
  await page.getByRole('button', { name: 'Check the files' }).click();
  await expect(page.getByRole('heading', { name: '12 rows cannot be brought in' })).toBeVisible();

  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(sideways, 'sideways scroll of the page').toBeLessThanOrEqual(0);
  expect(view.errors).toEqual([]);
});

test('the import of patients is reached and used by the keyboard alone', async ({ page }) => {
  const answers: Answers = {
    'GET /api/v1/opening-batches': () => ({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
    'POST /api/v1/imports/parties': () => partyAnswer([]),
  };
  const view = await open(page, '/administration/import', 'light', desktop, answers, admin);
  await expect(page.getByText('No opening batch was submitted yet.')).toBeVisible();

  const link = page.getByRole('link', { name: 'Import patients and guarantors' });
  await link.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { level: 1, name: 'Import patients and guarantors' })).toBeFocused();

  // A file is chosen through the system's own dialog, which a test cannot type into; everything after it is keys.
  await page.getByLabel(/^Patients/).setInputFiles(file('patients.csv', 'mrn,full_name\nNF-1,Asha Rao\n'));
  const check = page.getByRole('button', { name: 'Check the files' });
  await check.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('The files can be imported')).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Import', exact: true })).toBeFocused();
  expect(view.errors).toEqual([]);
  expect(view.unexpected).toEqual([]);
});
