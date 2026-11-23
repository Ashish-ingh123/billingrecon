import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { pendingApproval, problem, reportPage } from '../test/records';
import { changeTone, compactRupees } from './compact';
import { SummaryCard } from './SummaryCard';

const supervisor = sessionFor(['Supervisor']);
const period = { from: '2026-10-25', to: '2026-11-23' };
const figure = (value: number, previous: number | null, changePercent: number | null) => ({
  value,
  previous,
  changePercent,
});

/** Every panel of the dashboard answered, after a month of one hospital. */
function dashboardRoutes(overrides: Record<string, Handler> = {}): Record<string, Handler> {
  return {
    'GET /api/v1/dashboard/summary': () =>
      json(200, {
        period,
        previousPeriod: { from: '2026-09-25', to: '2026-10-24' },
        totalBilled: figure(1910000, 1369176.92, 39.5),
        amountCollected: figure(1690000, 1318252.73, 28.2),
        outstandingDues: figure(1814675.5, 1435661.79, 26.4),
        unappliedCash: figure(350000, 199316.63, 75.6),
        pendingApprovals: figure(11, 7, 57.14),
        reconciliationDrift: figure(0, null, null),
      }),
    'GET /api/v1/dashboard/trend': () =>
      json(200, {
        period,
        interval: 'Day',
        points: [
          {
            from: '2026-10-25',
            to: '2026-10-25',
            billed: 1000,
            collected: 500,
            cumulativeBilled: 1000,
            cumulativeCollected: 500,
          },
          {
            from: '2026-10-26',
            to: '2026-10-26',
            billed: 0,
            collected: 250,
            cumulativeBilled: 1000,
            cumulativeCollected: 750,
          },
        ],
        daily: [],
      }),
    'GET /api/v1/dashboard/ageing': () =>
      json(200, {
        asOf: '2026-11-23',
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
      json(
        200,
        reportPage(
          [
            {
              payerId: 'b2000000-0000-4000-8000-000000000002',
              payerName: 'Vikram Mishra',
              receivable: 178651,
              unappliedCash: 0,
              creditAvailable: 0,
              netOutstanding: 178651,
            },
            {
              payerId: 'b2000000-0000-4000-8000-000000000003',
              payerName: 'Nandini Das',
              receivable: 89325.5,
              unappliedCash: 0,
              creditAvailable: 0,
              netOutstanding: 89325.5,
            },
          ],
          { payers: 36, receivable: 0, unappliedCash: 0, creditAvailable: 0, netOutstanding: 0 },
          36,
        ),
      ),
    'GET /api/v1/dashboard/recent-payments': () =>
      json(
        200,
        reportPage(
          [
            {
              paymentId: 'f6000000-0000-4000-8000-000000000001',
              number: 'PAY/26-27/001504',
              paymentDate: '2026-11-21',
              payerId: 'b2000000-0000-4000-8000-000000000002',
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
      ),
    'GET /api/v1/dashboard/pending-approvals': () =>
      json(
        200,
        reportPage(
          [
            pendingApproval({
              documentType: 'Refund',
              documentId: 'f8000000-0000-4000-8000-000000000001',
              waitingDays: 8,
            }),
            pendingApproval({
              approvalRequestId: 'fb000000-0000-4000-8000-000000000002',
              makerUserId: supervisor.userId,
              waitingDays: 4,
            }),
          ],
          { requests: 11, oldestRequestedAtUtc: '2026-11-13T05:00:00+00:00' },
          11,
        ),
      ),
    'GET /api/v1/dashboard/drift': () =>
      json(200, reportPage([], { rows: 0, byIdentity: [], checkedAtUtc: '2026-11-23T06:00:00+00:00' })),
    ...overrides,
  };
}

describe('the compact money of a card', () => {
  it('shows rupees below a lakh, lakhs from a lakh and crores from a crore, exactly rounded', () => {
    expect(compactRupees(99999)).toBe('₹99,999');
    expect(compactRupees(100000)).toBe('₹1L');
    expect(compactRupees(1280000)).toBe('₹12.8L');
    expect(compactRupees(10000000)).toBe('₹1Cr');
    expect(compactRupees(9999999)).toBe('₹1Cr');
    expect(compactRupees(12550000)).toBe('₹1.3Cr');
    expect(compactRupees(1814675.5)).toBe('₹18.1L');
    expect(compactRupees(0)).toBe('₹0');
  });
});

describe('a summary card', () => {
  it('colours a change by meaning, not by direction', () => {
    expect(changeTone(39.5, 'rise')).toBe('good');
    expect(changeTone(-3, 'rise')).toBe('bad');
    expect(changeTone(26.4, 'fall')).toBe('bad');
    expect(changeTone(-12, 'fall')).toBe('good');
    expect(changeTone(0, 'fall')).toBe('neutral');
  });

  it('shows a compact figure with the exact one in its tooltip and for screen readers', () => {
    render(
      <ul>
        <SummaryCard
          label="Outstanding dues"
          icon={null}
          series={5}
          figure={figure(1814675.5, 1435661.79, 26.4)}
          kind="amount"
          goodWhen="fall"
          against="vs previous 30 days"
        />
      </ul>,
    );

    const value = screen.getByTitle('₹18,14,675.50');
    expect(value).toHaveTextContent('₹18.1L');
    expect(within(value).getByText('₹18,14,675.50')).toBeInTheDocument();
    const change = screen.getByText(/26\.4%/);
    expect(change).toHaveAttribute('data-tone', 'bad');
    expect(change).toHaveTextContent('↑ +26.4%, a change for the worse');
  });

  it('says so when there is no previous figure, and shows no change as neutral', () => {
    render(
      <ul>
        <SummaryCard
          label="Total billing"
          icon={null}
          series={1}
          figure={figure(500, 0, null)}
          kind="amount"
          goodWhen="rise"
          against="vs previous 7 days"
        />
        <SummaryCard
          label="Pending approvals"
          icon={null}
          series={4}
          figure={figure(0, 0, 0)}
          kind="count"
          goodWhen="fall"
          against="vs previous 7 days"
        />
      </ul>,
    );

    expect(screen.getByText('No change to show')).toBeInTheDocument();
    expect(screen.getByText('+0%')).toHaveAttribute('data-tone', 'neutral');
  });
});

describe('the dashboard (US-44)', () => {
  it('shows the six cards, the ageing and the four panels from the server', async () => {
    const api = routedApi(dashboardRoutes());
    renderApp('/dashboard', { session: supervisor, fetch: api.fetch });

    const cards = await screen.findByRole('list', { name: 'Summary' });
    expect(within(cards).getAllByRole('listitem')).toHaveLength(6);
    expect(within(cards).getByTitle('₹19,10,000.00')).toHaveTextContent('₹19.1L');
    expect(within(cards).getByText('No trend: drift rows are not kept over time.')).toBeInTheDocument();
    expect(
      screen.getByText('25 Oct 2026 to 23 Nov 2026, compared with 25 Sept 2026 to 24 Oct 2026 (IST business dates)', {
        exact: false,
      }),
    ).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'PAY/26-27/001504' })).toBeInTheDocument();
    expect(screen.getByText('36 payers owe or hold something today.')).toBeInTheDocument();
    expect(
      within(screen.getByRole('table', { name: 'Outstanding dues ageing' })).getByText('45.06%'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('No drift: every cached balance equals the ledger and every number series is gapless.'),
    ).toBeInTheDocument();
    expect(
      within(screen.getByRole('table', { name: 'Billing against collections' })).getByText('750.00'),
    ).toBeInTheDocument();
  });

  it("offers a checker a review of another's request, never of their own", async () => {
    renderApp('/dashboard', { session: supervisor, fetch: routedApi(dashboardRoutes()).fetch });

    const pending = await screen.findByRole('table', { name: 'Pending approvals' });
    expect(within(pending).getAllByRole('link', { name: 'Review' })).toHaveLength(1);
    expect(within(pending).getByText('Your request')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'View all →' }).map((link) => link.getAttribute('href'))).toContain(
      '/approvals',
    );
  });

  it('keeps the other panels when one fails, and says when a panel has nothing', async () => {
    const api = routedApi(
      dashboardRoutes({
        'GET /api/v1/dashboard/top-payers': () =>
          json(503, problem(503, 'internal_error', 'The payers could not be read.')),
        'GET /api/v1/dashboard/recent-payments': () =>
          json(200, reportPage([], { payments: 0, amount: 0, unappliedAmount: 0 })),
      }),
    );
    renderApp('/dashboard', { session: supervisor, fetch: api.fetch });

    expect(await screen.findByText('The payers could not be read.')).toBeInTheDocument();
    expect(await screen.findByText('No payment in this period.')).toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Summary' })).toBeInTheDocument();
  });

  it('sends the period chosen, and keeps it in the address', async () => {
    const api = routedApi(dashboardRoutes());
    const { user, router } = renderApp('/dashboard', { session: supervisor, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('Period'), 'Last 7 days');

    expect(router.state.location.search).toBe('?range=7');
    const searches = api.sent('GET', '/api/v1/dashboard/summary').map((sent) => new URL(sent.url).searchParams);
    expect(searches[0]?.get('from')).toBeNull();
    await screen.findByRole('list', { name: 'Summary' });
    const last = api.sent('GET', '/api/v1/dashboard/summary').at(-1);
    expect(new URL(last!.url).searchParams.get('to')).not.toBeNull();
  });

  it.each(['light', 'dark'])('takes every colour from a token in the %s theme', async (theme) => {
    document.documentElement.dataset.theme = theme;
    const { container } = renderApp('/dashboard', { session: supervisor, fetch: routedApi(dashboardRoutes()).fetch });
    await screen.findByRole('table', { name: 'Outstanding dues ageing' });

    const raw = /#[0-9a-f]{3,8}\b|\b(rgba?|hsla?)\(|\b(white|black|red|green|blue)\b/i;
    for (const element of container.querySelectorAll('[style], [fill], [stroke], [stop-color]')) {
      for (const name of ['style', 'fill', 'stroke', 'stop-color']) {
        const value = element.getAttribute(name);
        if (value !== null) {
          expect(value, `${element.tagName} ${name}`).not.toMatch(raw);
        }
      }
    }

    delete document.documentElement.dataset.theme;
  });

  it('is not shown to a billing clerk or a cashier', async () => {
    const api = routedApi({});
    renderApp('/dashboard', { session: sessionFor(['BillingClerk', 'Cashier']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
  });
});
