import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { open, pageOf, payer, type Answers } from './stubbed';

// The one search box of the five lists of documents (Prompt 11.1b; ADR 0036, amended 2026-11-26) in a real browser,
// with no API behind it: each list sends what is typed after a pause, with no Search button, as the `Search` of its
// request; the address holds it and Back leaves the list; leaving the box sends at once; Clear shows everything again;
// one character is not sent. It needs the built app (`npm run build`, `npm run preview`,
// E2E_BASE_URL=http://localhost:4173).

const label = 'Number, patient, payer or amount';
const desktop = { width: 1280, height: 900 };
const phone = { width: 390, height: 844 };

const lists = [
  { name: 'Bills', path: '/bills', api: '/api/v1/bills' },
  { name: 'Payments', path: '/payments', api: '/api/v1/payments' },
  { name: 'Credit notes', path: '/credit-notes', api: '/api/v1/credit-notes' },
  { name: 'Adjustments', path: '/adjustments', api: '/api/v1/adjustments' },
  { name: 'Refunds', path: '/refunds', api: '/api/v1/refunds' },
] as const;

/** Every list answers an empty page and notes the search it was asked for (null when it was asked for none). */
function answersOf(seen: (string | null)[]): Answers {
  const answers: Answers = { 'GET /api/v1/payers': () => pageOf([payer]) };
  for (const list of lists) {
    answers[`GET ${list.api}`] = (url) => {
      seen.push(url.searchParams.get('Search'));
      return pageOf([]);
    };
  }

  return answers;
}

async function seriousFindings(page: Page): Promise<string[]> {
  const axe = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  return axe.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
}

for (const list of lists) {
  test(`the list of ${list.name.toLowerCase()} answers while its search is typed, and Back leaves it`, async ({
    page,
  }) => {
    const seen: (string | null)[] = [];
    const view = await open(page, '/payers', 'light', desktop, answersOf(seen));
    await expect(page.getByRole('heading', { name: 'Payers' })).toBeVisible();
    await page.getByRole('navigation').getByRole('link', { name: list.name, exact: true }).first().click();
    await expect(page.getByRole('heading', { name: list.name, exact: true })).toBeVisible();
    await view.settle();
    const before = (await view.probe()).shift;

    await expect(page.getByRole('button', { name: 'Search' })).toHaveCount(0);
    const box = page.getByLabel(label);

    // One character is not a search: nothing is sent, and the address stays.
    await box.pressSequentially('d', { delay: 40 });
    await page.waitForTimeout(500);
    expect(seen).toEqual([null]);

    // The keys are one search, sent after the pause, and the address holds it.
    await box.pressSequentially('eshmukh 590', { delay: 40 });
    await expect(page).toHaveURL(new RegExp(`${list.path}\\?search=deshmukh\\+590$`));
    expect(seen).toEqual([null, 'deshmukh 590']);
    expect((await view.probe()).shift - before, 'layout shift while searching').toBeLessThan(0.01);
    expect(await seriousFindings(page)).toEqual([]);

    // Clear shows everything again.
    await page.getByRole('button', { name: 'Clear search' }).click();
    await expect(page).toHaveURL(new RegExp(`${list.path}$`));
    await expect(box).toHaveValue('');
    expect(seen).toEqual([null, 'deshmukh 590', null]);

    // A search replaced the entry of the address: Back leaves the list.
    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Payers' })).toBeVisible();
    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });
}

for (const theme of ['light', 'dark'] as const) {
  test(`leaving the box sends the search at once, and the address opens the list with it (${theme})`, async ({
    page,
  }) => {
    const seen: (string | null)[] = [];
    const view = await open(page, '/payments?search=000042&status=Posted', theme, desktop, answersOf(seen));
    const box = page.getByLabel(label);

    // The address is the search: the box shows it and the list was asked with it, beside the other filter.
    await expect(box).toHaveValue('000042');
    await expect(page.getByLabel('Status')).toHaveValue('Posted');
    expect(seen).toEqual(['000042']);

    // Tab leaves the box before the pause has passed, and the search goes at once. The other filter stays.
    await box.fill('');
    await box.pressSequentially('kavita');
    await page.keyboard.press('Tab');
    await expect(page).toHaveURL(/\/payments\?search=kavita&status=Posted$/);
    // The address changes a moment before the request is seen: wait for it.
    await expect.poll(() => seen.at(-1)).toBe('kavita');

    // Another filter keeps the search, and starts at the first page.
    await page.getByLabel('Method').selectOption('Cash');
    await expect(page).toHaveURL(/\/payments\?search=kavita&status=Posted&method=Cash$/);
    expect(await seriousFindings(page)).toEqual([]);
    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
  });
}

test('on a phone the search box of a list fits and nothing scrolls sideways', async ({ page }) => {
  const seen: (string | null)[] = [];
  const view = await open(page, '/bills', 'light', phone, answersOf(seen));
  const box = page.getByLabel(label);
  await expect(box).toBeVisible();
  await box.pressSequentially('BIL/26-27/000001', { delay: 20 });
  await expect(page).toHaveURL(/\/bills\?search=BIL%2F26-27%2F000001$/);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(
    true,
  );
  expect(view.errors).toEqual([]);
});
