// The money screens of Prompt 9.3 in a real browser (ADR 0049), with no API behind them: a session is put in the tab and
// the API's answers are given by the test; a request the test did not expect fails it. The page's response carries
// nginx's content security policy, read from frontend/deploy/nginx.conf. It needs the built app (`npm run build`,
// `npm run preview`, E2E_BASE_URL=http://localhost:4173). E2E_SCREENSHOTS=1 also writes the screenshots into docs/ui/.

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
  trail,
  type Answers,
} from './stubbed';

const answers: Answers = {
  [`GET /api/v1/bills/${draft.id}`]: () => draft,
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  [`GET /api/v1/patients/${patient.id}`]: () => patient,
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  [`GET /api/v1/credit-notes/${creditNote.id}`]: () => creditNote,
  [`GET /api/v1/adjustments/${adjustment.id}`]: () => adjustment,
  'GET /api/v1/credit-notes': () => pageOf([creditNote]),
  'GET /api/v1/adjustments': () => pageOf([adjustment]),
  'GET /api/v1/audit-records': (url) =>
    trail(url.searchParams.get('EntityId') ?? '', ['Drafted', 'LinesReplaced', 'Posted', 'CreditApplied']),
};

const screens = [
  {
    name: 'bill-editor',
    path: `/bills/${draft.id}/edit`,
    ready: async (page: Page) => {
      await expect(page.getByLabel('Estimated totals')).toContainText('Total₹2,249.48');
      await expect(page.getByLabel('Line 2: description')).toHaveValue('Consultation, general medicine');
    },
  },
  {
    name: 'bill-posted',
    path: `/bills/${posted.id}`,
    ready: async (page: Page) => {
      await expect(page.getByRole('heading', { level: 1, name: 'BIL/26-27/000123' })).toBeVisible();
      await expect(page.getByLabel('Totals of the bill')).toContainText('Balance due₹949.48');
      await expect(page.getByRole('list', { name: 'Changes, oldest first' })).toBeVisible();
    },
  },
  {
    name: 'credit-note',
    path: `/credit-notes/${creditNote.id}`,
    ready: async (page: Page) => {
      await expect(page.getByRole('heading', { level: 1, name: 'CRN/26-27/000004' })).toBeVisible();
      await expect(page.getByLabel('Figures of the credit note')).toContainText('Total₹300.00');
    },
  },
  {
    name: 'adjustment',
    path: `/adjustments/${adjustment.id}`,
    ready: async (page: Page) => {
      await expect(page.getByText('Waiting for approval').first()).toBeVisible();
      await expect(page.getByLabel('Figures of the adjustment')).toContainText('Amount₹49.48');
    },
  },
] as const;

for (const screen of screens) {
  for (const theme of ['light', 'dark'] as const) {
    for (const [label, size] of [
      ['desktop', { width: 1440, height: 900 }],
      ['phone', { width: 390, height: 844 }],
    ] as const) {
      test(`${screen.name} in the ${theme} theme on a ${label}: nothing refused, no sideways scroll, no shift`, async ({
        page,
      }) => {
        const { errors, unexpected, settle, probe } = await open(page, screen.path, theme, size, answers);

        await screen.ready(page);
        await settle();
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(size.width);
        const { violations, shift, moved } = await probe();
        expect(violations, 'refused by the content security policy').toEqual([]);
        expect(errors, 'errors in the console').toEqual([]);
        expect(unexpected, 'requests the test did not expect').toEqual([]);
        expect(shift, `layout shift after the first paint: ${moved.join(', ')}`).toBeLessThan(0.01);

        if (screenshots && (label === 'desktop' || screen.name === 'bill-editor')) {
          await page.screenshot({
            path: `../docs/ui/${screen.name}${label === 'phone' ? '-phone' : ''}${theme === 'dark' ? '-dark' : ''}.png`,
            fullPage: label === 'desktop',
          });
        }
      });
    }
  }
}

test('the totals card stays in view while the lines of the editor scroll', async ({ page }) => {
  const { settle } = await open(page, `/bills/${draft.id}/edit`, 'light', { width: 390, height: 640 }, answers);
  await screens[0].ready(page);
  await settle();

  await page.getByLabel('Line 1: description').scrollIntoViewIfNeeded();
  const card = page.getByLabel('Estimated totals');
  const box = await card.boundingBox();
  expect(box, 'the totals card').not.toBeNull();
  expect((box?.y ?? 0) + (box?.height ?? 0)).toBeLessThanOrEqual(640);
  expect(box?.y ?? -1).toBeGreaterThanOrEqual(0);
});

test('with reduced motion nothing on the money screens animates', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  for (const screen of screens) {
    await open(page, screen.path, 'light', { width: 1440, height: 900 }, answers);
    await screen.ready(page);
    expect(await page.evaluate(() => document.getAnimations().length), screen.name).toBe(0);
    await page.unrouteAll({ behavior: 'ignoreErrors' });
  }
});
