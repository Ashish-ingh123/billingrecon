import { screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../../test/app';
import { bill, page, patient, payer } from '../../test/records';
import { periodOf } from './businessDate';
import { orderOf } from './columnPrefs';

/**
 * The tables of Prompt 12.2 (ADR 0063), on the list of bills with the API faked: the API filters, sorts and pages, and
 * the screen sends what the bar and the headers hold; an amount after the pause of typing, a select at once; a stale
 * answer is dropped; the address is replaced, not pushed, and a change goes back to page 1; "Clear filters"; the date
 * shortcuts count from the server's date; `aria-sort`; the "Columns" menu by keyboard, with the columns that are never
 * hidden; and the choice of columns kept for one user and not another.
 */
const asha = patient();
const ashasPayer = payer();
const posted = bill({ id: 'e5000000-0000-4000-8000-000000000002', number: 'BIL/26-27/000001', status: 'Posted' });
const clerk = sessionFor(['BillingClerk']);
const admin = sessionFor(['TenantAdmin']);

function routes(bills: Handler = () => json(200, page([posted]))): Record<string, Handler> {
  return {
    'GET /api/v1/bills': bills,
    [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
  };
}

const sentQueries = (api: ReturnType<typeof routedApi>) =>
  api.sent('GET', '/api/v1/bills').map((request) => Object.fromEntries(new URL(request.url).searchParams));

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('the filter bar of a list', () => {
  it('sends an amount once, after the pause of typing, and nothing before', async () => {
    const api = routedApi(routes());
    const { user, router } = renderApp('/bills', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'BIL/26-27/000001' });
    expect(sentQueries(api)).toHaveLength(1);

    const total = within(screen.getByRole('group', { name: 'Total' }));
    await user.type(total.getByLabelText('From'), '1500');
    // Typed, and the pause not over: nothing more was sent.
    expect(sentQueries(api)).toHaveLength(1);

    await waitFor(() => expect(sentQueries(api)).toHaveLength(2));
    expect(sentQueries(api)[1]).toMatchObject({ TotalFrom: '1500', Page: '1' });
    expect(router.state.location.search).toBe('?totalFrom=1500');
    expect(screen.getByText('1 filter in use')).toBeInTheDocument();
  });

  it('says an amount that is not one, and sends nothing for it', async () => {
    const api = routedApi(routes());
    const { user } = renderApp('/bills', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'BIL/26-27/000001' });

    const total = within(screen.getByRole('group', { name: 'Total' }));
    await user.type(total.getByLabelText('From'), '15.005');

    expect(await total.findByText('An amount, like 1500 or 1500.50')).toBeInTheDocument();
    expect(total.getByLabelText('From')).toHaveAttribute('aria-invalid', 'true');
    await new Promise((resolve) => setTimeout(resolve, 450));
    expect(sentQueries(api)).toHaveLength(1);
  });

  it('keeps the rows on screen until the newer answer, and drops an answer that came too late', async () => {
    const stale = bill({ id: 'e5000000-0000-4000-8000-000000000009', number: 'BIL/26-27/000999', status: 'Draft' });
    const api = routedApi(
      routes(async (request) => {
        const status = new URL(request.url).searchParams.get('Status');
        if (status === 'Draft') {
          // The answer to the first change comes after the second change's.
          await new Promise((resolve) => setTimeout(resolve, 300));
          return json(200, page([stale]));
        }

        return json(200, page([posted]));
      }),
    );
    const { user } = renderApp('/bills', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'BIL/26-27/000001' });

    await user.selectOptions(screen.getByLabelText('Status'), 'Draft');
    // The rows of before stay while the answer is awaited.
    expect(screen.getByRole('link', { name: 'BIL/26-27/000001' })).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Status'), 'Posted');

    await waitFor(() => expect(sentQueries(api).at(-1)).toMatchObject({ Status: 'Posted' }));
    await new Promise((resolve) => setTimeout(resolve, 500));
    expect(screen.getByRole('link', { name: 'BIL/26-27/000001' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'BIL/26-27/000999' })).not.toBeInTheDocument();
  });

  it('replaces the address rather than adding to it, and goes back to page 1 on a change', async () => {
    const api = routedApi(routes(() => json(200, page([posted], 100, 3))));
    const { user, router } = renderApp('/bills?page=3', { session: clerk, fetch: api.fetch });
    await screen.findByRole('link', { name: 'BIL/26-27/000001' });
    expect(sentQueries(api)[0]).toMatchObject({ Page: '3' });

    await user.selectOptions(screen.getByLabelText('Status'), 'Posted');

    await waitFor(() => expect(router.state.location.search).toBe('?status=Posted'));
    expect(router.state.historyAction).toBe('REPLACE');
    await waitFor(() => expect(sentQueries(api).at(-1)).toMatchObject({ Status: 'Posted', Page: '1' }));
  });

  it('empties every filter of the bar with "Clear filters", and keeps the payer the list was opened for', async () => {
    const api = routedApi(routes());
    const { user, router } = renderApp(`/bills?payerId=${ashasPayer.id}&status=Posted&totalFrom=100&sort=-total`, {
      session: clerk,
      fetch: api.fetch,
    });
    await screen.findByRole('link', { name: 'BIL/26-27/000001' });
    expect(screen.getByText('2 filters in use')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear filters' }));

    await waitFor(() => expect(router.state.location.search).toBe(`?payerId=${ashasPayer.id}&sort=-total`));
    expect(screen.getByText('No filter in use')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument();
  });

  it("counts the date shortcuts from the server's date, whatever the browser's clock says", async () => {
    // The browser is a day ahead of the server.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-28T10:00:00+05:30'));
    const api = routedApi({
      ...routes(),
      'GET /api/v1/system/business-date': () => json(200, { today: '2026-11-27' }),
    });
    // A session that has not run out by the browser's clock.
    const { user } = renderApp('/bills', { session: sessionFor(['BillingClerk']), fetch: api.fetch });
    await screen.findByRole('link', { name: 'BIL/26-27/000001' });
    const shortcuts = within(screen.getByRole('group', { name: 'Bill date: shortcuts' }));

    await user.click(shortcuts.getByRole('button', { name: 'This month' }));
    await waitFor(() =>
      expect(sentQueries(api).at(-1)).toMatchObject({ BillDateFrom: '2026-11-01', BillDateTo: '2026-11-27' }),
    );

    await user.click(shortcuts.getByRole('button', { name: 'Today' }));
    await waitFor(() =>
      expect(sentQueries(api).at(-1)).toMatchObject({ BillDateFrom: '2026-11-27', BillDateTo: '2026-11-27' }),
    );

    // Friday the 27th: the week from Monday the 23rd.
    await user.click(shortcuts.getByRole('button', { name: 'This week' }));
    await waitFor(() => expect(sentQueries(api).at(-1)).toMatchObject({ BillDateFrom: '2026-11-23' }));
    expect(api.sent('GET', '/api/v1/system/business-date')).toHaveLength(1);
  });

  it('offers "Made by" to an administrator only, and reads the users for no one else', async () => {
    const users = { items: [{ id: 'u1', displayName: 'Meera Nair' }], page: 1, pageSize: 100, totalCount: 1 };
    const adminApi = routedApi({ ...routes(), 'GET /api/v1/users': () => json(200, users) });
    const first = renderApp('/bills', { session: admin, fetch: adminApi.fetch });
    expect(await screen.findByRole('option', { name: 'Meera Nair' })).toBeInTheDocument();
    first.unmount();

    const clerkApi = routedApi(routes());
    renderApp('/bills', { session: clerk, fetch: clerkApi.fetch });
    await screen.findByRole('link', { name: 'BIL/26-27/000001' });
    expect(screen.queryByLabelText('Made by')).not.toBeInTheDocument();
    expect(clerkApi.sent('GET', '/api/v1/users')).toHaveLength(0);
  });
});

describe('the headers and the columns of a table', () => {
  it('sorts by a header, ascending, then descending, then the list’s own order, and says so', async () => {
    const api = routedApi(routes());
    const { user } = renderApp('/bills', { session: clerk, fetch: api.fetch });
    const grid = await screen.findByRole('table', { name: 'Bills' });
    const header = within(grid).getByRole('columnheader', { name: /Total/ });
    expect(header).toHaveAttribute('aria-sort', 'none');
    // A column the API cannot sort by has no button and no aria-sort.
    expect(within(grid).getByRole('columnheader', { name: 'Patient' })).not.toHaveAttribute('aria-sort');

    await user.click(within(header).getByRole('button', { name: /Total/ }));
    await waitFor(() => expect(sentQueries(api).at(-1)).toMatchObject({ Sort: 'total' }));
    expect(
      within(await screen.findByRole('table', { name: 'Bills' })).getByRole('columnheader', { name: /Total/ }),
    ).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getByText('Sorted by Total (₹), ascending.')).toBeInTheDocument();

    await user.click(within(screen.getByRole('columnheader', { name: /Total/ })).getByRole('button'));
    await waitFor(() => expect(sentQueries(api).at(-1)).toMatchObject({ Sort: '-total' }));
    expect(screen.getByRole('columnheader', { name: /Total/ })).toHaveAttribute('aria-sort', 'descending');

    await user.click(within(screen.getByRole('columnheader', { name: /Total/ })).getByRole('button'));
    await waitFor(() => expect(sentQueries(api).at(-1)).not.toHaveProperty('Sort'));
    expect(screen.getByRole('columnheader', { name: /Total/ })).toHaveAttribute('aria-sort', 'none');
  });

  it('shows, hides and moves columns by keyboard, never hides the number or the status, and resets', async () => {
    const api = routedApi(routes());
    const { user } = renderApp('/bills', { session: clerk, fetch: api.fetch });
    await screen.findByRole('table', { name: 'Bills' });

    const button = screen.getByRole('button', { name: 'Columns' });
    button.focus();
    await user.keyboard('{Enter}');
    const panel = screen.getByRole('group', { name: 'Columns of Bills' });
    expect(button).toHaveAttribute('aria-expanded', 'true');

    // The number and the status are ticked and cannot be unticked, and say why.
    for (const name of ['Number', 'Status']) {
      const fixed = within(panel).getByRole('checkbox', { name });
      expect(fixed).toBeChecked();
      expect(fixed).toBeDisabled();
      expect(fixed).toHaveAccessibleDescription('The number and the status are always shown: a row is known by them.');
    }

    // The first control that can be used has the focus: the bill date. Space hides it.
    expect(within(panel).getByRole('checkbox', { name: 'Bill date' })).toHaveFocus();
    await user.keyboard(' ');
    expect(
      within(screen.getByRole('table', { name: 'Bills' })).queryByRole('columnheader', { name: /Bill date/ }),
    ).not.toBeInTheDocument();

    // Move the payer up, before the patient.
    within(panel).getByRole('button', { name: 'Move Payer up' }).focus();
    await user.keyboard('{Enter}');
    const headers = within(screen.getByRole('table', { name: 'Bills' }))
      .getAllByRole('columnheader')
      .map((cell) => cell.textContent);
    expect(headers.slice(0, 3)).toEqual(['Number', 'Payer', 'Patient']);

    // Escape closes the panel and gives the focus back.
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('group', { name: 'Columns of Bills' })).not.toBeInTheDocument();
    expect(button).toHaveFocus();

    await user.click(button);
    await user.click(
      within(screen.getByRole('group', { name: 'Columns of Bills' })).getByRole('button', { name: 'Reset columns' }),
    );
    const reset = within(screen.getByRole('table', { name: 'Bills' }))
      .getAllByRole('columnheader')
      .map((cell) => cell.textContent);
    expect(reset.slice(0, 4)).toEqual(['Number', 'Bill date', 'Patient', 'Payer']);
  });

  it('remembers the columns of one user, and not for another user', async () => {
    const api = routedApi(routes());
    const first = renderApp('/bills', { session: clerk, fetch: api.fetch });
    await screen.findByRole('table', { name: 'Bills' });
    await first.user.click(screen.getByRole('button', { name: 'Columns' }));
    await first.user.click(
      within(screen.getByRole('group', { name: 'Columns of Bills' })).getByRole('checkbox', { name: 'Payer' }),
    );
    first.unmount();

    const other = renderApp('/bills', {
      session: { ...clerk, userId: 'd0000000-0000-4000-8000-000000000099' },
      fetch: api.fetch,
    });
    expect(
      within(await screen.findByRole('table', { name: 'Bills' })).getByRole('columnheader', { name: 'Payer' }),
    ).toBeInTheDocument();
    other.unmount();

    renderApp('/bills', { session: clerk, fetch: api.fetch });
    expect(
      within(await screen.findByRole('table', { name: 'Bills' })).queryByRole('columnheader', { name: 'Payer' }),
    ).not.toBeInTheDocument();
  });
});

describe('the dates and the order of columns', () => {
  it.each([
    ['today', '2026-11-27', '2026-11-27'],
    ['week', '2026-11-23', '2026-11-27'],
    ['month', '2026-11-01', '2026-11-27'],
    ['last30', '2026-10-29', '2026-11-27'],
  ] as const)('counts %s from the server’s day', (shortcut, from, to) => {
    expect(periodOf(shortcut, '2026-11-27')).toEqual({ from, to });
  });

  it('starts a week on Monday, also on a Sunday and across a month', () => {
    expect(periodOf('week', '2026-11-29').from).toBe('2026-11-23');
    expect(periodOf('week', '2026-12-02').from).toBe('2026-11-30');
    expect(periodOf('last30', '2026-03-01').from).toBe('2026-01-31');
  });

  it('keeps a known order, drops a column that is gone, and puts a new one at the end', () => {
    expect(orderOf(['a', 'b', 'c', 'd'], ['c', 'gone', 'a'])).toEqual(['c', 'a', 'b', 'd']);
  });
});
