import { expect, test } from '@playwright/test';
import { open, pageOf, patient, session, type Answers } from './stubbed';

// Signing out in a real browser (Prompt 10.2c, ADR 0057), with no API behind it: the Sign out button tells the API with
// the token of the session that ends, and the other tab of the same user in the same browser goes to the sign-in at once,
// through a storage event a component test can only imitate. It needs the built app (`npm run build`, `npm run preview`,
// E2E_BASE_URL=http://localhost:4173).

const desktop = { width: 1280, height: 900 };

test('a sign-out tells the API and signs the same user out of the other tab', async ({ page, context }) => {
  const told: (string | null)[] = [];
  const answers: Answers = {
    'GET /api/v1/patients': () => pageOf([patient]),
    'POST /api/v1/auth/logout': () => ({}),
  };
  page.on('request', (request) => {
    if (request.url().endsWith('/api/v1/auth/logout')) {
      told.push(request.headers().authorization ?? null);
    }
  });
  const other = await context.newPage();
  const first = await open(page, '/patients', 'light', desktop, answers);
  const second = await open(other, '/patients', 'light', desktop, answers);
  await expect(page.getByRole('link', { name: 'Asha Verma' })).toBeVisible();
  await expect(other.getByRole('link', { name: 'Asha Verma' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign out' }).click();

  await expect(page).toHaveURL(/\/sign-in$/);
  await expect(page.getByText('You have signed out.')).toBeVisible();
  // The other tab was not touched: it heard of the sign-out and left by itself.
  await expect(other).toHaveURL(/\/sign-in$/);
  await expect(other.getByText('You have signed out.')).toBeVisible();

  expect(told).toEqual([`Bearer ${session.accessToken}`]);
  expect(await page.evaluate(() => window.localStorage.getItem('billingrecon.signed-out'))).toBeNull();
  expect(await other.evaluate(() => window.sessionStorage.getItem('billingrecon.session'))).toBeNull();
  for (const view of [first, second]) {
    expect(view.errors).toEqual([]);
    expect(view.unexpected).toEqual([]);
    expect((await view.probe()).violations).toEqual([]);
  }
});
