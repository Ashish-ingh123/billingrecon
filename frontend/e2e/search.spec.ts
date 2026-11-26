import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { open, pageOf, patient, payer, type Answers } from './stubbed';

// Searching while typing (Prompt 10.2b; ADR 0039 and ADR 0040, amended 2026-11-25) in a real browser, with no API
// behind it: a list answers after a pause with no Search button, keeps its rows and moves nothing, and replaces the
// address so Back leaves the list; a record picker is an autocomplete in the combobox pattern, by the keyboard alone
// and by the pointer, passing axe with its results open, in both themes. It needs the built app (`npm run build`,
// `npm run preview`, E2E_BASE_URL=http://localhost:4173).

const ravi = { ...patient, id: 'a1000000-0000-4000-8000-000000000002', mrn: 'MRN-2002', fullName: 'Ravi Verma' };
const desktop = { width: 1280, height: 900 };
const phone = { width: 390, height: 844 };

function answersOf(seen: string[]): Answers {
  return {
    'GET /api/v1/patients': (url) => {
      const search = url.searchParams.get('Search');
      seen.push(search ?? '');
      return pageOf(search === null || 'ravi verma mrn-2002'.includes(search.toLowerCase()) ? [patient, ravi] : []);
    },
    'GET /api/v1/payers': () => pageOf([payer]),
    'GET /api/v1/service-items': () => pageOf([]),
  };
}

async function seriousFindings(page: Page): Promise<string[]> {
  const axe = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  return axe.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
}

for (const theme of ['light', 'dark'] as const) {
  test(`a list answers while its search is typed, and Back leaves it (${theme})`, async ({ page }) => {
    const seen: string[] = [];
    const view = await open(page, '/payers', theme, desktop, answersOf(seen));
    await expect(page.getByRole('heading', { name: 'Payers' })).toBeVisible();
    await page.getByRole('navigation').getByRole('link', { name: 'Patients' }).first().click();
    await expect(page.getByRole('link', { name: 'Asha Verma' })).toBeVisible();
    await view.settle();
    const before = (await view.probe()).shift;

    await expect(page.getByRole('button', { name: 'Search' })).toHaveCount(0);
    await page.getByLabel('MRN or name').pressSequentially('2002', { delay: 40 });
    // The keys were one search, sent after the pause; the rows stayed until its answer came.
    await expect(page).toHaveURL(/\/patients\?search=2002$/);
    await expect(page.getByRole('link', { name: 'Asha Verma' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Ravi Verma' })).toBeVisible();
    expect(seen).toEqual(['', '2002']);
    expect((await view.probe()).shift - before, 'layout shift while searching').toBeLessThan(0.01);

    await page.getByLabel('MRN or name').pressSequentially('x', { delay: 40 });
    await expect(page.getByText('No patient matches.')).toBeVisible();

    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Payers' })).toBeVisible();
    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });

  test(`a picker is an autocomplete by the keyboard alone (${theme})`, async ({ page }) => {
    const seen: string[] = [];
    const view = await open(page, '/bills/new', theme, desktop, answersOf(seen));
    const box = page.getByRole('combobox', { name: 'Patient' });
    await expect(box).toBeVisible();
    await expect(page.getByRole('button', { name: 'Find' })).toHaveCount(0);
    await view.settle();
    const before = (await view.probe()).shift;

    await box.focus();
    await page.keyboard.type('verma', { delay: 40 });
    const list = page.getByRole('listbox', { name: 'patient found' });
    await expect(list.getByRole('option')).toHaveCount(2);
    expect(seen).toEqual(['verma']);
    await expect(box).toHaveAttribute('aria-expanded', 'true');
    expect((await view.probe()).shift - before, 'layout shift when the results open').toBeLessThan(0.01);

    // Since 2026-11-26 (ADR 0040, amended) the first result is the marked one as soon as the results open.
    await expect(list.getByRole('option', { name: 'Asha Verma (MRN MRN-1001)' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    await page.keyboard.press('ArrowDown');
    await expect(list.getByRole('option', { name: 'Ravi Verma (MRN MRN-2002)' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    expect(await seriousFindings(page), 'axe with the results open').toEqual([]);

    await page.keyboard.press('Escape');
    await expect(list).toHaveCount(0);
    await expect(box).toHaveValue('verma');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    // The record itself, not the announcement that names it for a screen reader (since 2026-11-26).
    await expect(page.getByText('Asha Verma (MRN MRN-1001)', { exact: true })).toBeVisible();
    // Enter chose the patient and did not submit the form: the page is still the new bill.
    await expect(page).toHaveURL(/\/bills\/new$/);
    expect(await seriousFindings(page), 'axe after the choice').toEqual([]);
    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });
}

test('a picker is used by a tap on a phone, its results inside the screen', async ({ page }) => {
  const seen: string[] = [];
  const view = await open(page, '/bills/new', 'light', phone, answersOf(seen));
  const box = page.getByRole('combobox', { name: 'Patient' });
  await box.click();
  await page.keyboard.type('mrn-2', { delay: 40 });

  const option = page.getByRole('option', { name: 'Ravi Verma (MRN MRN-2002)' });
  await expect(option).toBeVisible();
  const frame = await option.boundingBox();
  expect(frame?.x ?? -1).toBeGreaterThanOrEqual(0);
  expect((frame?.x ?? 0) + (frame?.width ?? 0)).toBeLessThanOrEqual(phone.width);
  expect(frame?.height ?? 0, 'a target of at least 40 px').toBeGreaterThanOrEqual(40);

  await option.click();
  await expect(page.getByText('Ravi Verma (MRN MRN-2002)')).toBeVisible();
  await expect(page.getByRole('listbox')).toHaveCount(0);
  expect(view.errors).toEqual([]);
  expect(view.unexpected).toEqual([]);
});
