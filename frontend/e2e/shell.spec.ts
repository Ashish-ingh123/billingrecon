import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

// The shell and the screens of 7.2 in a real browser (Prompt 9.2, ADR 0048), with no API behind them: a session is put
// in the tab and the API's answers are given by the test. The page's response carries nginx's content security policy,
// read from frontend/deploy/nginx.conf. It needs the built app (`npm run build`, `npm run preview`,
// E2E_BASE_URL=http://localhost:4173). E2E_SCREENSHOTS=1 also writes the screenshots into docs/ui/.

const policy = /set \$csp "([^"]+)";/.exec(readFileSync(new URL('../deploy/nginx.conf', import.meta.url), 'utf8'))?.[1];
const screenshots = process.env.E2E_SCREENSHOTS === '1';

const base64 = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
const roles = ['TenantAdmin'];
const token = `${base64({ alg: 'HS256', typ: 'JWT' })}.${base64({ sub: 'u1', tenant_id: 't1', role: roles, exp: Math.floor(Date.now() / 1000) + 3600 })}.signature`;
const session = {
  accessToken: token,
  expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
  userId: 'd0000000-0000-4000-8000-000000000002',
  tenantId: 'd0000000-0000-4000-8000-000000000001',
  roles,
  email: 'asha@cityhospital.org',
  hospital: 'city-hospital',
};
const names = ['Asha Verma', 'Ravi Kumar', 'Meera Iyer', 'Imran Shaikh', 'Lakshmi Babu', 'Gurpreet Pillai'];
const patients = names.map((fullName, index) => ({
  id: `00000000-0000-4000-8000-00000000000${index}`,
  mrn: `MRN-00${index + 1}`,
  fullName,
  dateOfBirth: '1984-03-12',
  sex: 'Female',
  phone: index % 2 === 0 ? '98765 43210' : null,
  email: null,
  addressLine1: null,
  addressLine2: null,
  city: 'Pune',
  state: 'Maharashtra',
  pinCode: null,
  status: index === 3 ? 'Inactive' : 'Active',
  version: 'AAAAAAAAB9E=',
}));

type Probe = { violations: string[]; shift: number };

async function open(page: Page, path: string, theme: 'light' | 'dark', size: { width: number; height: number }) {
  expect(policy, 'the policy of frontend/deploy/nginx.conf').toBeTruthy();
  await page.setViewportSize(size);
  await page.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname === '/api/v1/patients') {
      await route.fulfill({ json: { items: patients, page: 1, pageSize: 20, totalCount: patients.length } });
      return;
    }

    await route.fulfill({ status: 404, json: { title: 'Not found', status: 404 } });
  });
  await page.route('**/*', async (route) => {
    if (route.request().resourceType() !== 'document') {
      await route.fallback();
      return;
    }

    const response = await route.fetch();
    await route.fulfill({ response, headers: { ...response.headers(), 'content-security-policy': policy ?? '' } });
  });
  await page.addInitScript(
    ([kept, chosen]) => {
      window.sessionStorage.setItem('billingrecon.session', kept);
      window.localStorage.setItem('billingrecon.theme', chosen);
      const probe: Probe = { violations: [], shift: 0 };
      (window as unknown as { probe: Probe }).probe = probe;
      document.addEventListener('securitypolicyviolation', (event) => {
        probe.violations.push(`${event.violatedDirective}: ${event.blockedURI}`);
      });
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) probe.shift += (entry as unknown as { value: number }).value;
      }).observe({ type: 'layout-shift', buffered: true });
    },
    [JSON.stringify(session), theme] as const,
  );

  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto(path);
  await page.waitForFunction(() =>
    document
      .getAnimations()
      .every(
        (animation) => animation.playState === 'finished' || animation.effect?.getTiming().iterations === Infinity,
      ),
  );

  return { errors, probe: () => page.evaluate(() => (window as unknown as { probe: Probe }).probe) };
}

for (const theme of ['light', 'dark'] as const) {
  for (const [label, size] of [
    ['desktop', { width: 1440, height: 900 }],
    ['phone', { width: 390, height: 844 }],
  ] as const) {
    test(`the patients list in the ${theme} theme on a ${label}: under nginx's policy, nothing refused, no sideways scroll`, async ({
      page,
    }) => {
      const { errors, probe } = await open(page, '/patients', theme, size);

      await expect(page.getByRole('heading', { level: 1, name: 'Patients' })).toBeVisible();
      await expect(page.getByRole('link', { name: 'Asha Verma' })).toBeVisible();
      await expect(page).toHaveTitle('Patients · BillingRecon');
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(size.width);
      const { violations, shift } = await probe();
      expect(violations, 'refused by the content security policy').toEqual([]);
      expect(errors, 'errors in the console').toEqual([]);
      expect(shift, 'layout shift after the first paint').toBeLessThan(0.01);

      if (screenshots) {
        await page.screenshot({ path: `../docs/ui/shell-${label}${theme === 'dark' ? '-dark' : ''}.png` });
      }
    });
  }
}

test('on a phone the navigation is a drawer: the keyboard opens it, stays in it, and Escape gives the focus back', async ({
  page,
}) => {
  const { errors } = await open(page, '/patients', 'light', { width: 390, height: 844 });
  const menu = page.getByRole('button', { name: 'Menu' });

  await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();
  await menu.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
  await expect(page.locator('#sidebar :focus')).toHaveCount(1);

  // Tab goes round inside the drawer.
  for (let step = 0; step < 20; step += 1) {
    await page.keyboard.press('Tab');
    expect(await page.evaluate(() => document.activeElement?.closest('#sidebar') !== null)).toBe(true);
  }

  await page.keyboard.press('Escape');
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();
  await expect(menu).toBeFocused();
  expect(errors).toEqual([]);
});

test('with reduced motion nothing in the shell animates', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, '/patients', 'light', { width: 1440, height: 900 });

  await expect(page.getByRole('heading', { level: 1, name: 'Patients' })).toBeVisible();
  expect(await page.evaluate(() => document.getAnimations().length)).toBe(0);
});

test('the specimen page is not in a production build', async ({ page }) => {
  await open(page, '/design', 'light', { width: 1440, height: 900 });

  await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
});
