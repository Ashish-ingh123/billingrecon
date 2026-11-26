import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { open, pageOf, patient, payer, screenshots, sessionOf, type Answers } from './stubbed';

// The lens of a search box and the Tab key in a search (Prompt 10.4d; ADR 0039 and ADR 0040, amended 2026-11-26) in a
// real browser, with no API behind it, under the policy of nginx.conf: every search box shows the lens, which keeps its
// place and size when the spinner takes its turn; Clear empties the box and the address; a list's box sends its search
// when it is left; and a new bill and a payment are filled by typing a few letters and pressing Tab, with the focus in
// the next field each time. In both themes, passing axe. It needs the built app (`npm run build`, `npm run preview`,
// E2E_BASE_URL=http://localhost:4173). E2E_SCREENSHOTS=1 also writes the screenshots into docs/ui/.

const ravi = { ...patient, id: 'a1000000-0000-4000-8000-000000000002', mrn: 'MRN-2002', fullName: 'Ravi Verma' };
const ravisPayer = { ...payer, id: 'b2000000-0000-4000-8000-000000000003', name: 'Ravi Verma', patientId: ravi.id };
const item = {
  id: 'c3000000-0000-4000-8000-000000000003',
  code: 'LAB-CBC',
  name: 'Complete blood count',
  defaultUnitPrice: 333.3333,
  taxRatePercent: 5,
  sacCode: '999311',
  status: 'Active',
  createdAtUtc: '2026-10-01T04:30:00+00:00',
  updatedAtUtc: '2026-10-01T04:30:00+00:00',
  version: 'AAAAAAAAB9M=',
};
const desktop = { width: 1280, height: 900 };

function answersOf(seen: string[], wait?: Promise<void>): Answers {
  const matching = <T extends { fullName?: string; name?: string }>(url: URL, all: T[]) => {
    const search = (url.searchParams.get('Search') ?? '').toLowerCase();
    return all.filter((one) => (one.fullName ?? one.name ?? '').toLowerCase().includes(search));
  };
  return {
    'GET /api/v1/patients': async (url) => {
      seen.push(url.searchParams.get('Search') ?? '');
      if (url.searchParams.get('Search') !== null) {
        await wait;
      }
      return pageOf(matching(url, [patient, ravi]));
    },
    'GET /api/v1/payers': (url) =>
      pageOf(url.searchParams.get('PatientId') === null ? matching(url, [payer, ravisPayer]) : [payer]),
    'GET /api/v1/service-items': (url) => pageOf(matching(url, [item])),
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

/** Where the lens or the spinner of a box stands, and how large, to the pixel. */
const leadOf = (page: Page, label: string) =>
  page.evaluate((name) => {
    const box = Array.from(document.querySelectorAll('input')).find((input) => input.labels?.[0]?.textContent === name);
    const lead = box?.parentElement?.querySelector('[data-testid^="search-"]');
    const rect = lead?.getBoundingClientRect();
    return {
      what: lead?.getAttribute('data-testid') ?? null,
      hidden: lead?.getAttribute('aria-hidden') ?? null,
      box: rect === undefined ? null : [rect.x, rect.y, rect.width, rect.height].map((value) => Math.round(value)),
      // The lens is inside the field, before its text.
      inside: rect !== undefined && box !== undefined && rect.left > box.getBoundingClientRect().left,
      colour: lead === null || lead === undefined ? '' : getComputedStyle(lead).color,
    };
  }, label);

for (const theme of ['light', 'dark'] as const) {
  test(`the lens stands in a list's search, gives its place to the spinner, and Clear empties it (${theme})`, async ({
    page,
  }) => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const seen: string[] = [];
    const view = await open(page, '/patients', theme, desktop, answersOf(seen, held));
    await expect(page.getByRole('link', { name: 'Asha Verma' })).toBeVisible();
    await page.waitForLoadState('networkidle');

    const lens = await leadOf(page, 'MRN or name');
    expect(lens).toMatchObject({ what: 'search-lens', hidden: 'true', inside: true });
    expect(lens.box?.slice(2)).toEqual([18, 18]);
    await expect(page.getByRole('button', { name: /^(Search|Find)$/ })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Clear search' })).toHaveCount(0);
    expect(await seriousFindings(page)).toEqual([]);

    // Left by Tab inside the pause: the search goes at once, and the spinner stands exactly where the lens stood.
    const box = page.getByLabel('MRN or name');
    await box.pressSequentially('ravi');
    await box.press('Tab');
    await expect.poll(() => seen).toEqual(['', 'ravi']);
    await expect.poll(async () => (await leadOf(page, 'MRN or name')).what).toBe('search-spinner');
    expect((await leadOf(page, 'MRN or name')).box).toEqual(lens.box);
    await expect(
      page.getByRole('link', { name: 'Asha Verma' }),
      'the rows stay until the new ones arrive',
    ).toBeVisible();

    release();
    await expect(page.getByRole('link', { name: 'Asha Verma' })).toHaveCount(0);
    await expect.poll(async () => (await leadOf(page, 'MRN or name')).what).toBe('search-lens');
    expect((await leadOf(page, 'MRN or name')).box).toEqual(lens.box);
    expect(new URL(page.url()).search).toBe('?search=ravi');

    if (screenshots && theme === 'light') {
      await page.screenshot({ path: '../docs/ui/search-lens.png', clip: { x: 240, y: 0, width: 1040, height: 420 } });
    }

    // Clear: the box and the address are empty, the whole list is back, and the box has the focus.
    await page.getByRole('button', { name: 'Clear search' }).click();
    await expect(box).toHaveValue('');
    await expect(box).toBeFocused();
    await expect(page.getByRole('link', { name: 'Asha Verma' })).toBeVisible();
    expect(new URL(page.url()).search).toBe('');
    await expect(page.getByRole('button', { name: 'Clear search' })).toHaveCount(0);

    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });

  test(`a new bill is filled by a few letters and Tab, the focus in the next field each time (${theme})`, async ({
    page,
  }) => {
    const seen: string[] = [];
    const view = await open(page, '/bills/new', theme, desktop, answersOf(seen), sessionOf(['BillingClerk']));
    const patientBox = page.getByRole('combobox', { name: 'Patient' });
    await expect(patientBox).toBeVisible();
    for (const label of ['Patient', 'Payer', 'Add a line from the catalogue']) {
      expect(await leadOf(page, label), label).toMatchObject({ what: 'search-lens', hidden: 'true', inside: true });
    }

    // The patient: three letters, the first result marked, Tab.
    await patientBox.pressSequentially('rav');
    const option = page.getByRole('option', { name: 'Ravi Verma (MRN MRN-2002)' });
    await expect(option).toBeVisible();
    await expect(option).toHaveAttribute('aria-selected', 'true');
    expect(await seriousFindings(page)).toEqual([]);
    await patientBox.press('Tab');

    await expect(page.getByText('Ravi Verma (MRN MRN-2002)', { exact: true }).first()).toBeVisible();
    await expect(page.getByRole('listbox')).toHaveCount(0);
    await expect(page.getByRole('status').filter({ hasText: 'Chosen: Ravi Verma (MRN MRN-2002)' })).toHaveCount(1);
    // The focus went on, and is inside the form: never on the page's body.
    expect(await page.evaluate(() => document.activeElement?.tagName)).not.toBe('BODY');
    expect(await page.evaluate(() => document.activeElement?.closest('main') !== null)).toBe(true);

    // The catalogue line the same way: letters, Tab, and the line is on the bill.
    const itemBox = page.getByRole('combobox', { name: 'Add a line from the catalogue' });
    await itemBox.click();
    await itemBox.pressSequentially('blood');
    await expect(page.getByRole('option', { name: /^LAB-CBC: Complete blood count/ })).toBeVisible();
    await itemBox.press('Tab');
    await expect
      .poll(() =>
        page.evaluate(() =>
          Array.from(document.querySelectorAll('main input')).some(
            (input) => (input as HTMLInputElement).value === 'Complete blood count',
          ),
        ),
      )
      .toBe(true);
    expect(await page.evaluate(() => document.activeElement?.tagName)).not.toBe('BODY');

    if (screenshots) {
      await page.screenshot({ path: `../docs/ui/bill-editor-tab${theme === 'dark' ? '-dark' : ''}.png` });
    }

    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  });
}

test('the payer of a payment is taken by Tab only when one alone matches', async ({ page }) => {
  const view = await open(page, '/payments/new', 'light', desktop, answersOf([]), sessionOf(['Cashier']));
  const box = page.getByRole('combobox', { name: 'Payer' });
  await expect(box).toBeVisible();

  // Two payers are called Verma: Tab chooses neither, says so at the field, and still moves on.
  await box.pressSequentially('verma');
  const found = page.getByRole('listbox', { name: 'payer found' }).getByRole('option');
  await expect(found).toHaveCount(2);
  await box.press('Tab');
  await expect(page.getByText('No record chosen: 2 match. Choose one from the list.')).toBeVisible();
  await expect(box).toHaveAttribute('aria-invalid', 'true');
  await expect(box).toHaveValue('verma');
  await expect(page.getByLabel(/^Payment date/)).toBeFocused();
  expect(await seriousFindings(page)).toEqual([]);

  // One alone is called Ravi: Tab takes it.
  await box.click();
  await box.fill('ravi');
  await expect(found).toHaveCount(1);
  await box.press('Tab');
  await expect(page.getByText("Ravi Verma (Self-pay, a patient's own payer)", { exact: true }).first()).toBeVisible();
  await expect(page.getByLabel(/^Payment date/)).toBeFocused();

  expect(view.errors).toEqual([]);
  expect(view.unexpected).toEqual([]);
});
