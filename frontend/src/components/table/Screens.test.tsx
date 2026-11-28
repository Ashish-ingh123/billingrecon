import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../../test/app';
import { page, patient, payer, pendingApproval, serviceItem } from '../../test/records';

/**
 * The screens of Prompt 12.2c on the shared table (ADR 0063): the lists of the hospital's records, of users, of shifts,
 * the inbox's order, and the reports, whose totals row follows the columns shown and whose CSV follows the filters and
 * the order with every column, whatever is hidden.
 */
const clerk = sessionFor(['BillingClerk']);
const admin = sessionFor(['TenantAdmin']);
const viewer = sessionFor(['Viewer']);
const supervisor = sessionFor(['Supervisor']);

const sent = (api: ReturnType<typeof routedApi>, path: string) =>
  api.sent('GET', path).map((request) => Object.fromEntries(new URL(request.url).searchParams));

const billRow = (n: number) => ({
  billId: `e5000000-0000-4000-8000-${String(n).padStart(12, '0')}`,
  number: `BIL/26-27/${String(n).padStart(6, '0')}`,
  billDate: '2026-11-20',
  dueDate: '2026-12-05',
  patientId: 'a1000000-0000-4000-8000-000000000001',
  patientName: 'Asha Verma',
  payerId: 'b2000000-0000-4000-8000-000000000002',
  payerName: 'Asha Verma',
  total: 10,
  balanceDue: 5,
  status: 'Posted',
  postedAtUtc: '2026-11-20T06:00:00+00:00',
});

const bills = Array.from({ length: 30 }, (_, index) => billRow(index + 1));

function billRegister(request: Request) {
  const query = new URL(request.url).searchParams;
  const number = Number(query.get('Page'));
  const size = Number(query.get('PageSize'));
  return json(200, {
    period: { from: '2026-11-01', to: '2026-11-30' },
    items: bills.slice((number - 1) * size, number * size),
    page: number,
    pageSize: size,
    totalCount: bills.length,
    totals: { bills: 30, total: 300, balanceDue: 150 },
  });
}

let saved: Blob[] = [];

beforeEach(() => {
  window.localStorage.clear();
  saved = [];
  Object.assign(URL, {
    createObjectURL: (blob: Blob) => {
      saved.push(blob);
      return 'blob:report';
    },
    revokeObjectURL: () => undefined,
  });
});

const textOf = (blob: Blob) =>
  new Promise<string>((resolve) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.readAsText(blob);
  });

describe('the lists of the hospital and of its users', () => {
  it('sorts the patients by a header, replaces the address, and goes back to page 1', async () => {
    const asha = patient();
    const api = routedApi({ 'GET /api/v1/patients': () => json(200, page([asha], 45, 2)) });
    const { user, router } = renderApp('/patients?page=2', { session: clerk, fetch: api.fetch });
    const grid = await screen.findByRole('table', { name: 'Patients' });

    await user.click(within(within(grid).getByRole('columnheader', { name: /Name/ })).getByRole('button'));

    await waitFor(() => expect(router.state.location.search).toBe('?sort=fullName'));
    expect(router.state.historyAction).toBe('REPLACE');
    await waitFor(() => expect(sent(api, '/api/v1/patients').at(-1)).toMatchObject({ Sort: 'fullName', Page: '1' }));
    // The name and the status are never hidden.
    await user.click(screen.getByRole('button', { name: 'Columns' }));
    const panel = screen.getByRole('group', { name: 'Columns of Patients' });
    expect(within(panel).getByRole('checkbox', { name: 'Name' })).toBeDisabled();
    expect(within(panel).getByRole('checkbox', { name: 'Status' })).toBeDisabled();
    expect(within(panel).getByRole('checkbox', { name: 'MRN' })).toBeEnabled();
  });

  it('counts the filters of a list in words, and empties them with "Clear filters", keeping the order', async () => {
    const api = routedApi({ 'GET /api/v1/patients': () => json(200, page([patient()])) });
    const { user, router } = renderApp('/patients?search=asha&status=Active&sort=-mrn', {
      session: clerk,
      fetch: api.fetch,
    });
    await screen.findByRole('table', { name: 'Patients' });
    expect(screen.getByText('2 filters in use')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear filters' }));

    await waitFor(() => expect(router.state.location.search).toBe('?sort=-mrn'));
    expect(screen.getByText('No filter in use')).toBeInTheDocument();
  });

  it('narrows the payers to a kind', async () => {
    const api = routedApi({ 'GET /api/v1/payers': () => json(200, page([payer()])) });
    const { user, router } = renderApp('/payers', { session: clerk, fetch: api.fetch });
    await screen.findByRole('table', { name: 'Payers' });

    await user.selectOptions(screen.getByLabelText('Kind'), 'SelfPay');

    await waitFor(() => expect(router.state.location.search).toBe('?kind=SelfPay'));
    await waitFor(() => expect(sent(api, '/api/v1/payers').at(-1)).toMatchObject({ Kind: 'SelfPay' }));
  });

  it('sends a category of the catalogue once, after the pause of typing', async () => {
    const api = routedApi({ 'GET /api/v1/service-items': () => json(200, page([serviceItem()])) });
    const { user } = renderApp('/service-items', { session: clerk, fetch: api.fetch });
    await screen.findByRole('table', { name: 'Service items' });

    await user.type(screen.getByLabelText('Category'), 'Lab');
    expect(sent(api, '/api/v1/service-items')).toHaveLength(1);

    await waitFor(() => expect(sent(api, '/api/v1/service-items')).toHaveLength(2));
    expect(sent(api, '/api/v1/service-items')[1]).toMatchObject({ Category: 'Lab', Page: '1' });
  });

  it('sorts the users by when they last signed in', async () => {
    const meera = {
      id: 'd0000000-0000-4000-8000-000000000010',
      email: 'meera@city.example',
      displayName: 'Meera Nair',
      roles: ['Cashier'],
      status: 'Active',
      lastSignInAtUtc: null,
      version: 'AAAA',
    };
    const api = routedApi({ 'GET /api/v1/users': () => json(200, page([meera])) });
    const { user } = renderApp('/administration', { session: admin, fetch: api.fetch });
    const grid = await screen.findByRole('table', { name: 'Users' });

    await user.click(within(within(grid).getByRole('columnheader', { name: /Last signed in/ })).getByRole('button'));

    await waitFor(() => expect(sent(api, '/api/v1/users').at(-1)).toMatchObject({ Sort: 'lastSignInAtUtc' }));
  });
});

describe('the shifts and the inbox', () => {
  it('filters the shifts by a period of business dates from the server, and sorts them', async () => {
    const api = routedApi({
      'GET /api/v1/cashier-shifts': () => json(200, page([])),
      'GET /api/v1/system/business-date': () => json(200, { today: '2026-11-27' }),
    });
    const { user } = renderApp('/shifts', { session: supervisor, fetch: api.fetch });
    const shortcuts = within(await screen.findByRole('group', { name: 'Business date: shortcuts' }));

    await user.click(shortcuts.getByRole('button', { name: 'Last 30 days' }));

    await waitFor(() =>
      expect(sent(api, '/api/v1/cashier-shifts').at(-1)).toMatchObject({
        DateFrom: '2026-10-29',
        DateTo: '2026-11-27',
      }),
    );
  });

  it('shows the requests that wait, the newest first when asked', async () => {
    const api = routedApi({
      'GET /api/v1/reports/pending-approvals': () =>
        json(200, { ...page([pendingApproval()]), totals: { requests: 1, oldestRequestedAtUtc: null } }),
    });
    const { user, router } = renderApp('/approvals', { session: supervisor, fetch: api.fetch });
    expect(await screen.findByRole('list', { name: 'Requests that wait, oldest first' })).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Order'), 'Newest first');

    await waitFor(() => expect(router.state.location.search).toBe('?sort=-requestedAtUtc'));
    expect(router.state.historyAction).toBe('REPLACE');
    expect(await screen.findByRole('list', { name: 'Requests that wait, newest first' })).toBeInTheDocument();
    expect(sent(api, '/api/v1/reports/pending-approvals').at(-1)).toMatchObject({ Sort: '-requestedAtUtc' });
  });
});

describe('the reports on the shared table', () => {
  it('hides a column with its total, and still exports every column, read with the filters and the order', async () => {
    const api = routedApi({ 'GET /api/v1/reports/bill-register': billRegister });
    const { user } = renderApp('/reports/bills?totalFrom=5&sort=-total', { session: viewer, fetch: api.fetch });
    const grid = await screen.findByRole('table', { name: 'Bill register' });
    expect(within(grid).getByRole('columnheader', { name: /Total/ })).toHaveAttribute('aria-sort', 'descending');
    expect(sent(api, '/api/v1/reports/bill-register')[0]).toMatchObject({ TotalFrom: '5', Sort: '-total' });
    expect(within(grid).getAllByRole('row').at(-1)).toHaveTextContent('300.00');

    await user.click(screen.getByRole('button', { name: 'Columns' }));
    await user.click(
      within(screen.getByRole('group', { name: 'Columns of Bill register' })).getByRole('checkbox', {
        name: 'Total (₹)',
      }),
    );
    await user.keyboard('{Escape}');

    const footer = within(screen.getByRole('table', { name: 'Bill register' }))
      .getAllByRole('row')
      .at(-1)!;
    expect(footer).toHaveTextContent('30 bills');
    expect(footer).not.toHaveTextContent('300.00');
    expect(footer).toHaveTextContent('150.00');

    await user.click(screen.getByRole('button', { name: 'Export CSV' }));
    expect(await screen.findByText(/Exported 30 rows/)).toBeInTheDocument();
    const exportReads = sent(api, '/api/v1/reports/bill-register').filter((query) => query.PageSize === '100');
    expect(exportReads).not.toHaveLength(0);
    for (const query of exportReads) {
      expect(query).toMatchObject({ TotalFrom: '5', Sort: '-total' });
    }

    const text = await textOf(saved[0]!);
    const lines = (text.charCodeAt(0) === 0xfeff ? text.slice(1) : text).split(String.fromCharCode(13, 10));
    expect(lines[3]).toBe('Number,Bill date,Due date,Patient,Payer,Status,Total (₹),Balance due (₹)');
    expect(lines[4]).toBe('BIL/26-27/000001,2026-11-20,2026-12-05,Asha Verma,Asha Verma,Posted,10.00,5.00');
  });

  it('counts the filters of a register and clears them, keeping its period', async () => {
    const api = routedApi({ 'GET /api/v1/reports/bill-register': billRegister });
    const { user, router } = renderApp('/reports/bills?from=2026-11-01&to=2026-11-30&status=Settled&totalTo=900', {
      session: viewer,
      fetch: api.fetch,
    });
    await screen.findByRole('table', { name: 'Bill register' });
    expect(screen.getByText('2 filters in use')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Clear filters' }));

    await waitFor(() => expect(router.state.location.search).toBe('?from=2026-11-01&to=2026-11-30'));
  });

  it('narrows unapplied cash to a period of payment dates', async () => {
    const api = routedApi({
      'GET /api/v1/reports/unapplied-cash': () =>
        json(200, { items: [], page: 1, pageSize: 20, totalCount: 0, totals: { payments: 0, unappliedAmount: 0 } }),
    });
    const { user } = renderApp('/reports/unapplied-cash', { session: viewer, fetch: api.fetch });
    const dates = within(await screen.findByRole('group', { name: 'Payment date' }));

    await user.type(dates.getByLabelText('From'), '2026-11-01');

    await waitFor(() =>
      expect(sent(api, '/api/v1/reports/unapplied-cash').at(-1)).toMatchObject({ DateFrom: '2026-11-01' }),
    );
  });
});
