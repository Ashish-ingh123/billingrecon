import { expect, test, type Page } from '@playwright/test';
import { open, screenshots, type Answers } from './stubbed';

// The dashboard of Prompt 9.4 in a real browser (ADR 0050), with no API behind it: the helper of e2e/stubbed.ts puts a
// session in the tab, gives the API's answers and fails on a request it did not expect, under nginx's content security
// policy. Its tooltips by pointer and by keyboard, the crosshair moved by the arrow keys, both themes, two widths and
// reduced motion. It needs the built app (`npm run build`, `npm run preview`, E2E_BASE_URL=http://localhost:4173).
// E2E_SCREENSHOTS=1 also writes docs/ui/dashboard-built*.png (the reference, docs/ui/dashboard.png, is never written).

const figure = (value: number, previous: number | null, changePercent: number | null) => ({
  value,
  previous,
  changePercent,
});
const days = Array.from({ length: 30 }, (_, index) => {
  const date = new Date(Date.UTC(2026, 9, 25 + index)).toISOString().slice(0, 10);
  return {
    date,
    billed: 40000 + ((index * 7919) % 50000),
    collected: 35000 + ((index * 6151) % 45000),
    outstandingDues: 1600000 + index * 7000,
    unappliedCash: 300000 + ((index * 1543) % 60000),
    pendingApprovals: 6 + (index % 6),
  };
});
const points = days.reduce<
  {
    from: string;
    to: string;
    billed: number;
    collected: number;
    cumulativeBilled: number;
    cumulativeCollected: number;
  }[]
>((all, day) => {
  const before = all.at(-1);
  return [
    ...all,
    {
      from: day.date,
      to: day.date,
      billed: day.billed,
      collected: day.collected,
      cumulativeBilled: (before?.cumulativeBilled ?? 0) + day.billed,
      cumulativeCollected: (before?.cumulativeCollected ?? 0) + day.collected,
    },
  ];
}, []);
const period = { from: days[0]?.date ?? '', to: days.at(-1)?.date ?? '' };
const reportPage = (items: object[], totals: object, totalCount = items.length) => ({
  items,
  page: 1,
  pageSize: 5,
  totalCount,
  totals,
  period: null,
});
const payer = (id: number, name: string, receivable: number, unappliedCash: number, creditAvailable: number) => ({
  payerId: `b2000000-0000-4000-8000-00000000000${String(id)}`,
  payerName: name,
  receivable,
  unappliedCash,
  creditAvailable,
  netOutstanding: receivable - unappliedCash - creditAvailable,
});

const answers: Answers = {
  'GET /api/v1/dashboard/summary': () => ({
    period,
    previousPeriod: { from: '2026-09-25', to: '2026-10-24' },
    totalBilled: figure(1910000, 1369176.92, 39.5),
    amountCollected: figure(1690000, 1318252.73, 28.2),
    outstandingDues: figure(1814675.5, 1435661.79, 26.4),
    unappliedCash: figure(350000, 199316.63, 75.6),
    pendingApprovals: figure(11, 7, 57.14),
    reconciliationDrift: figure(0, null, null),
  }),
  'GET /api/v1/dashboard/trend': () => ({ period, interval: 'Day', points, daily: days }),
  'GET /api/v1/dashboard/ageing': () => ({
    asOf: period.to,
    bills: 3,
    total: 1814675.5,
    buckets: [
      { bucket: '0-30', bills: 1, amount: 817705, share: 45.06 },
      { bucket: '31-60', bills: 1, amount: 107490, share: 5.92 },
      { bucket: '61-90', bills: 0, amount: 149867.5, share: 8.26 },
      { bucket: '90+', bills: 1, amount: 739613, share: 40.76 },
    ],
  }),
  'GET /api/v1/dashboard/top-payers': () =>
    reportPage(
      [
        payer(1, 'Vikram Mishra', 178651, 0, 0),
        payer(2, 'Shalini Krishnan', 150319, 6000, 0),
        payer(3, 'Nandini Das', 129805, 0, 0),
        payer(4, 'Meenakshi Bhat', 110351.5, 0, 5000),
        payer(5, 'Pooja Yadav', 104170, 0, 0),
      ],
      { payers: 36, receivable: 0, unappliedCash: 0, creditAvailable: 0, netOutstanding: 0 },
      36,
    ),
  'GET /api/v1/dashboard/recent-payments': () =>
    reportPage(
      [
        {
          paymentId: 'f6000000-0000-4000-8000-000000000001',
          number: 'PAY/26-27/001504',
          paymentDate: period.to,
          payerId: 'b2000000-0000-4000-8000-000000000001',
          payerName: 'Thomas Shaikh',
          method: 'Card',
          reference: 'AUTH-1',
          amount: 1250.5,
          unappliedAmount: 0,
          status: 'Posted',
          reversedAtUtc: null,
        },
      ],
      { payments: 1, amount: 1250.5, unappliedAmount: 0 },
    ),
  'GET /api/v1/dashboard/pending-approvals': () =>
    reportPage(
      [
        {
          approvalRequestId: 'fb000000-0000-4000-8000-000000000001',
          documentType: 'Refund',
          action: 'Post',
          documentId: 'f8000000-0000-4000-8000-000000000001',
          parentDocumentId: 'f6000000-0000-4000-8000-000000000001',
          makerUserId: 'e1000000-0000-4000-8000-000000000009',
          makerComment: 'Paid twice',
          requestedAtUtc: '2026-11-13T05:00:00+00:00',
          waitingDays: 8,
          payerId: 'b2000000-0000-4000-8000-000000000001',
          payerName: 'Thomas Shaikh',
          amount: 3300,
        },
      ],
      { requests: 11, oldestRequestedAtUtc: '2026-11-13T05:00:00+00:00' },
      11,
    ),
  'GET /api/v1/dashboard/drift': () =>
    reportPage([], { rows: 0, byIdentity: [], checkedAtUtc: '2026-11-23T06:00:00+00:00' }),
};

/** The dashboard with every panel drawn. */
async function ready(page: Page) {
  await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();
  await expect(page.getByRole('list', { name: 'Summary' }).getByRole('listitem')).toHaveCount(6);
  await expect(page.getByRole('table', { name: 'Outstanding dues ageing' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'PAY/26-27/001504' })).toBeVisible();
  await expect(page.locator('.recharts-line').first()).toBeVisible();
}

for (const theme of ['light', 'dark'] as const) {
  for (const [label, size] of [
    ['desktop', { width: 1440, height: 900 }],
    ['phone', { width: 390, height: 844 }],
  ] as const) {
    test(`the dashboard in the ${theme} theme on a ${label}: nothing refused, no sideways scroll, no shift`, async ({
      page,
    }) => {
      const { errors, unexpected, settle, probe } = await open(page, '/dashboard', theme, size, answers);

      await ready(page);
      await settle();
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(size.width);
      const { violations, shift, moved } = await probe();
      expect(violations, 'refused by the content security policy').toEqual([]);
      expect(errors, 'errors in the console').toEqual([]);
      expect(unexpected, 'requests the test did not expect').toEqual([]);
      expect(shift, `layout shift after the first paint: ${moved.join(', ')}`).toBeLessThan(0.01);

      if (screenshots) {
        await page.screenshot({
          path: `../docs/ui/dashboard-built${label === 'phone' ? '-phone' : ''}${theme === 'dark' ? '-dark' : ''}.png`,
          fullPage: true,
        });
      }
    });
  }
}

/** A tooltip on the page that is within the window. */
async function inWindow(page: Page, text: string) {
  const tip = page.getByText(text, { exact: false }).last();
  await expect(tip).toBeVisible();
  const box = await tip.boundingBox();
  const size = page.viewportSize();
  expect(box, text).not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.y).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(size?.width ?? 0);
}

test('the crosshair goes from point to point by the arrow keys, with the server figures, and Escape closes it', async ({
  page,
}) => {
  const { unexpected } = await open(page, '/dashboard', 'light', { width: 1440, height: 900 }, answers);
  await ready(page);

  const chart = page.getByRole('group', { name: /^Billing against collections\./ });
  await chart.locator('.recharts-surface').focus();
  await page.keyboard.press('ArrowRight');
  await inWindow(page, 'Billed to date');
  const first = await chart.locator('.recharts-tooltip-wrapper').innerText();
  await page.keyboard.press('ArrowRight');
  const second = await chart.locator('.recharts-tooltip-wrapper').innerText();
  expect(second).not.toBe(first);
  expect(second).toContain('Billed that day');
  await expect(chart.locator('.recharts-active-dot')).toHaveCount(2);

  await page.keyboard.press('Escape');
  await expect(chart.getByText('Billed to date')).toBeHidden();
  expect(unexpected).toEqual([]);
});

test('the tooltips of the cards, the sparklines, the donut and the bars, each inside the window and off the pointer', async ({
  page,
}) => {
  await open(page, '/dashboard', 'light', { width: 390, height: 844 }, answers);
  await ready(page);

  // The last card's figure on a phone: its tooltip keeps to the window's edge.
  const drift = page.getByRole('list', { name: 'Summary' }).getByRole('button', { name: '0', exact: true });
  await drift.hover();
  await inWindow(page, 'As evaluated now');
  const tip = await page.getByRole('tooltip').boundingBox();
  const figureBox = await drift.boundingBox();
  expect(tip!.y).toBeGreaterThan(figureBox!.y + figureBox!.height - 1);
  await page.mouse.move(0, 0);

  // A sparkline by the keyboard.
  const spark = page.getByRole('group', { name: 'Total billing by day' });
  await spark.locator('.recharts-surface').focus();
  await page.keyboard.press('ArrowRight');
  await expect(spark.locator('.recharts-tooltip-wrapper')).toContainText('Total billing₹');
  await page.keyboard.press('Escape');

  // The donut by the keyboard.
  const donut = page.getByRole('slider', { name: /outstanding, by days past the due date/ });
  await donut.focus();
  await page.keyboard.press('ArrowRight');
  await inWindow(page, 'Share45.06%');
  await page.keyboard.press('Escape');

  // A bar by a tap.
  const bar = page.getByRole('button', { name: 'The figures of Shalini Krishnan' });
  await bar.dispatchEvent('pointerup', { pointerType: 'touch' });
  await inWindow(page, 'Unapplied cash₹6,000.00');
});

test('with reduced motion nothing on the dashboard animates, tooltips included', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await open(page, '/dashboard', 'light', { width: 1440, height: 900 }, answers);
  await ready(page);

  await page.getByRole('button', { name: '₹18,14,675.50' }).hover();
  await expect(page.getByRole('tooltip')).toBeVisible();
  expect(await page.evaluate(() => document.getAnimations().length)).toBe(0);
});
