import { readFileSync } from 'node:fs';
import { expect, test, type Locator, type Page } from '@playwright/test';

// The sign-in page in a real browser (Prompt 9.1, ADR 0047), with no API behind it: its layout held to what was
// measured on the reference, docs/ui/sign-in.png (1672 x 941); the content security policy of nginx, read from
// frontend/deploy/nginx.conf and put on the page's response here, because the office machine has no nginx; no movement
// with reduced motion; no shift of the layout; the order of the keyboard; narrow windows.
//
// It needs the built app (`npm run build`, then `npm run preview`, E2E_BASE_URL=http://localhost:4173) or the
// one-command run: the Vite dev server puts inline scripts of its own on the page, which the policy rightly refuses.
// E2E_SCREENSHOTS=1 also writes the page's screenshots into docs/ui/.

const policy = /set \$csp "([^"]+)";/.exec(readFileSync(new URL('../deploy/nginx.conf', import.meta.url), 'utf8'))?.[1];

// Measured on the reference: [left, top, right, bottom] in its own pixels.
const reference = {
  card: [864, 84, 1552, 875],
  hospitalCode: [923, 392, 1498, 448],
  email: [923, 513, 1498, 569],
  password: [923, 631, 1498, 687],
  signIn: [923, 708, 1498, 765],
  mark: [91, 152, 214, 276],
} as const;
const tolerance = 4;

type Probe = { violations: string[]; shift: number };

async function open(page: Page, theme: 'light' | 'dark', size = { width: 1672, height: 941 }) {
  expect(policy, 'the policy of frontend/deploy/nginx.conf').toBeTruthy();
  await page.setViewportSize(size);
  await page.route('**/*', async (route) => {
    if (route.request().resourceType() !== 'document') {
      await route.continue();
      return;
    }

    const response = await route.fetch();
    await route.fulfill({ response, headers: { ...response.headers(), 'content-security-policy': policy ?? '' } });
  });
  await page.addInitScript((chosen) => {
    window.localStorage.setItem('billingrecon.theme', chosen);
    window.localStorage.setItem('billingrecon.hospital', 'demo');
    const probe: Probe = { violations: [], shift: 0 };
    (window as unknown as { probe: Probe }).probe = probe;
    document.addEventListener('securitypolicyviolation', (event) => {
      probe.violations.push(`${event.violatedDirective}: ${event.blockedURI}`);
    });
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) probe.shift += (entry as unknown as { value: number }).value;
    }).observe({ type: 'layout-shift', buffered: true });
  }, theme);

  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));

  await page.goto('/sign-in');
  await expect(page.getByRole('heading', { name: 'Sign in to BillingRecon' })).toBeVisible();
  // The entrances are over: what is measured and photographed is the page at rest.
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (animation) => animation.playState === 'finished' || animation.effect?.getTiming().iterations === Infinity,
      ),
  );

  return { errors, probe: () => page.evaluate(() => (window as unknown as { probe: Probe }).probe) };
}

async function box(locator: Locator) {
  const found = await locator.boundingBox();
  expect(found).not.toBeNull();
  const { x = 0, y = 0, width = 0, height = 0 } = found ?? {};
  return [x, y, x + width, y + height];
}

function expectNear(actual: number[], expected: readonly number[], what: string, sides = [0, 1, 2, 3]) {
  for (const side of sides) {
    expect(
      Math.abs((actual[side] ?? 0) - (expected[side] ?? 0)),
      `${what}, side ${side}: ${actual.join(', ')}`,
    ).toBeLessThanOrEqual(tolerance);
  }
}

for (const theme of ['light', 'dark'] as const) {
  test(`the ${theme} page is laid out as the reference, under nginx's policy, with nothing refused`, async ({
    page,
  }) => {
    const { errors, probe } = await open(page, theme);

    expect(await page.evaluate(() => document.documentElement.getAttribute('data-theme'))).toBe(theme);
    // The card's bottom edge depends on the support link, which a build may be given or not.
    const withSupport = (await page.getByRole('link', { name: 'Contact support' }).count()) > 0;
    expectNear(await box(page.locator('form')), reference.card, 'the card', withSupport ? [0, 1, 2, 3] : [0, 1, 2]);
    expectNear(await box(page.getByLabel('Hospital code')), reference.hospitalCode, 'Hospital code');
    expectNear(await box(page.getByLabel('E-mail address')), reference.email, 'E-mail address');
    expectNear(await box(page.getByLabel('Password', { exact: true })), reference.password, 'Password');
    expectNear(await box(page.getByRole('button', { name: 'Sign in' })), reference.signIn, 'Sign in');
    expectNear(
      await box(page.getByRole('region', { name: 'BillingRecon' }).locator('svg').first()),
      reference.mark,
      'the mark',
      [0, 2],
    );

    await expect(page).toHaveTitle('Sign in · BillingRecon');
    await expect(page.locator('link[rel="icon"]')).toHaveAttribute('href', '/favicon.svg');
    await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute('content', /^#[0-9a-f]{6}$/);
    await expect(page.getByRole('img', { name: 'Ashvanta Technologies' })).toBeVisible();

    const { violations, shift } = await probe();
    expect(violations, 'refused by the content security policy').toEqual([]);
    expect(errors, 'errors in the console').toEqual([]);
    expect(shift, 'layout shift after the first paint').toBe(0);

    if (process.env.E2E_SCREENSHOTS === '1') {
      await page.screenshot({ path: `../docs/ui/sign-in-built${theme === 'dark' ? '-dark' : ''}.png` });
    }
  });
}

test('the waves drift, and with reduced motion nothing moves at all', async ({ page }) => {
  await open(page, 'light');
  expect(await page.evaluate(() => document.getAnimations().length)).toBeGreaterThan(0);

  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Sign in to BillingRecon' })).toBeVisible();
  expect(await page.evaluate(() => document.getAnimations().length)).toBe(0);
});

test('the keyboard walks the page in the order it reads', async ({ page }) => {
  await open(page, 'light');
  // The theme pill is one stop, on the setting chosen: the test chose Light.
  const stops = ['Light', 'Hospital code', 'E-mail address', 'Password', 'Show password', 'Sign in'];

  for (const stop of stops) {
    await page.keyboard.press('Tab');
    const focused = page.locator(':focus');
    const name = await focused.evaluate((element) => {
      const labelled = element.getAttribute('aria-label') ?? (element as HTMLInputElement).labels?.[0]?.textContent;
      return (labelled ?? element.textContent ?? '').trim();
    });
    expect(name).toBe(stop);
  }
});

for (const width of [390, 360]) {
  test(`at ${width} px the three lines fold away, the card stays and nothing scrolls sideways`, async ({ page }) => {
    const { errors, probe } = await open(page, 'light', { width, height: 800 });

    await expect(page.getByText('Trusted financial insights')).toBeHidden();
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('img', { name: 'Ashvanta Technologies' })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    expect((await probe()).violations).toEqual([]);
    expect(errors).toEqual([]);

    if (process.env.E2E_SCREENSHOTS === '1' && width === 390) {
      await page.screenshot({ path: '../docs/ui/sign-in-built-phone.png', fullPage: true });
    }
  });
}
