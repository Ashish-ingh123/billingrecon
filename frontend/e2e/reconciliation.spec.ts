import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { open, pageOf, screenshots, sessionOf, type Answers } from './stubbed';

// The import of a bank's statement in a real browser (Prompt 13.1c, US-61; ADR 0065), with no API behind it: an
// accountant chooses the account and a file from the disk, checks it, sees the refused rows and the first lines, and
// imports exactly the file the check read; the result arrives without moving the page; it is done with the keyboard
// alone; and it passes axe in both themes and fits a phone without a sideways scroll. It needs the built app
// (`npm run build`, `npm run preview`, E2E_BASE_URL=http://localhost:4173).

const accountant = sessionOf(['Accountant']);
const desktop = { width: 1440, height: 900 };
const phone = { width: 390, height: 844 };
const file = (name: string, text: string) => ({ name, mimeType: 'text/csv', buffer: Buffer.from(text, 'utf8') });

const bank = {
  id: 'ec000000-0000-4000-8000-000000000001',
  kind: 'Bank',
  name: 'HDFC current',
  accountLabel: '4521',
  gateway: null,
  bankChannelId: null,
  dateWindowDays: 3,
  mapping: null,
  status: 'Active',
  createdAtUtc: '2026-10-16T05:00:00+00:00',
  updatedAtUtc: '2026-10-16T05:00:00+00:00',
  version: 'AAAAAAAAB9E=',
};

const refusals = Array.from({ length: 6 }, (_, index) => ({
  rowNumber: 40 + index,
  reason: `the date "${String(index + 31)}/10/2026" is not in the form dd/MM/yyyy.`,
}));
const checked = {
  fileHash: 'A'.repeat(64),
  alreadyImported: false,
  rowsRead: 46,
  linesToAdd: 38,
  linesAlreadyHeld: 2,
  rowsRefused: 6,
  moneyIn: 482300.5,
  moneyOut: 12059,
  firstDate: '2026-10-01',
  lastDate: '2026-10-16',
  refusals,
  lines: Array.from({ length: 40 }, (_, index) => ({
    rowNumber: index + 2,
    transactionDate: '2026-10-16',
    valueDate: null,
    direction: index % 5 === 0 ? 'Out' : 'In',
    amount: 500 + index * 25,
    reference: `UPI${String(88_120 + index)}`,
    description: `UPI/CR/PATIENT ${String(index + 1)}/OPD CONSULTATION AND PHARMACY`,
    balance: 150_000 + index * 500,
    alreadyHeld: index < 2,
  })),
};
// Since 13.2: the matching run that follows an import.
const matched = { linesRead: 38, matched: 30, proposed: 6, left: 2, skipped: 0 };
const imported = {
  id: 'ed000000-0000-4000-8000-000000000001',
  channelId: bank.id,
  fileName: 'hdfc-oct.csv',
  fileHash: checked.fileHash,
  rowsRead: 46,
  linesAdded: 38,
  linesAlreadyHeld: 2,
  rowsRefused: 6,
  firstDate: '2026-10-01',
  lastDate: '2026-10-16',
  createdAtUtc: '2026-10-17T05:00:00+00:00',
  createdByUserId: 'd0000000-0000-4000-8000-000000000002',
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
  test(`a statement is checked, its refused rows named, and then imported (${theme})`, async ({ page }) => {
    const imports: string[] = [];
    const answers: Answers = {
      'GET /api/v1/settlement-channels': () => pageOf([bank]),
      'POST /api/v1/statement-imports/dry-run': () => checked,
      'POST /api/v1/statement-imports': () => {
        imports.push('import');
        return imported;
      },
      [`POST /api/v1/settlement-channels/${bank.id}/matching-runs`]: () => matched,
    };
    const view = await open(page, '/reconciliation/import', theme, desktop, answers, accountant);
    await expect(page.getByRole('heading', { level: 1, name: 'Import a bank statement' })).toBeVisible();
    const check = page.getByRole('button', { name: 'Check the file' });
    await expect(check).toBeDisabled();

    await page.getByLabel(/^Bank account/).selectOption(bank.id);
    await page.getByLabel(/^Statement/).setInputFiles(file('hdfc-oct.csv', 'Txn Date,Narration,Debit,Credit\n'));
    await expect(check).toBeEnabled();
    await view.settle();
    const before = (await view.probe()).shift;
    await check.click();

    await expect(page.getByText('The file can be imported')).toBeVisible();
    await expect(page.getByRole('heading', { name: '6 rows cannot be read' })).toBeVisible();
    await expect(page.getByRole('heading', { name: /^The first 40 lines read/ })).toBeVisible();
    await view.settle();
    expect((await view.probe()).shift - before, 'layout shift when the result arrives').toBeLessThan(0.01);
    expect(await seriousFindings(page), 'axe with the result open').toEqual([]);
    // The training notes' screenshot (docs/go-live.md, Accountant), light only, with the result of a check open.
    if (screenshots && theme === 'light') {
      await page.screenshot({ path: '../docs/ui/statement-import.png', fullPage: false });
    }

    await page.getByRole('button', { name: 'Import', exact: true }).click();
    await expect(page.getByText('Imported', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'See the lines of this import' })).toBeVisible();
    await expect(page.getByText(/Matching: 30 matched, 6 proposed, 2 left for you\./)).toBeVisible();
    expect(imports).toHaveLength(1);
    await expect(page.getByRole('button', { name: 'Import', exact: true })).toBeDisabled();

    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
  });
}

test('the import is done with the keyboard alone, and the result fits a phone', async ({ page }) => {
  const answers: Answers = {
    'GET /api/v1/settlement-channels': () => pageOf([bank]),
    'POST /api/v1/statement-imports/dry-run': () => checked,
    'POST /api/v1/statement-imports': () => imported,
    [`POST /api/v1/settlement-channels/${bank.id}/matching-runs`]: () => matched,
  };
  const view = await open(page, '/reconciliation/import', 'light', phone, answers, accountant);
  await expect(page.getByRole('heading', { level: 1, name: 'Import a bank statement' })).toBeVisible();

  // The file is the one thing a keyboard cannot choose from the disk here: it is set, and everything else is keys.
  await page.getByLabel(/^Statement/).setInputFiles(file('hdfc-oct.csv', 'Txn Date\n'));
  await page.getByLabel(/^Bank account/).focus();
  await page.keyboard.press('ArrowDown');
  await expect(page.getByLabel(/^Bank account/)).toHaveValue(bank.id);
  await page.getByRole('button', { name: 'Check the file' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('The file can be imported')).toBeVisible();

  const sideways = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(sideways, 'sideways scroll of the page on a phone').toBeLessThanOrEqual(0);

  await page.getByRole('button', { name: 'Import', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Imported', { exact: true })).toBeVisible();
  expect(view.errors).toEqual([]);
  expect(view.unexpected).toEqual([]);
});
