import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';

const viewer = sessionFor(['Viewer']);

const billRow = (n: number, total: number, balanceDue: number) => ({
  billId: `e5000000-0000-4000-8000-${String(n).padStart(12, '0')}`,
  number: `BIL/26-27/${String(n).padStart(6, '0')}`,
  billDate: '2026-11-20',
  dueDate: '2026-12-05',
  patientId: 'a1000000-0000-4000-8000-000000000001',
  patientName: 'Asha Verma',
  payerId: 'b2000000-0000-4000-8000-000000000002',
  payerName: n === 2 ? '=Verma, Asha' : 'Asha Verma',
  total,
  balanceDue,
  status: 'Posted',
  postedAtUtc: '2026-11-20T06:00:00+00:00',
});

/** A bill register of 120 bills of 10.00 each, two pages of the export; the screen's page holds 20. */
const bills = Array.from({ length: 120 }, (_, index) => billRow(index + 1, 10, 5));
const registerTotals = { bills: 120, total: 1200, balanceDue: 600 };
const period = { from: '2026-11-01', to: '2026-11-30' };

function billRegister(totals = registerTotals) {
  return (request: Request) => {
    const query = new URL(request.url).searchParams;
    const page = Number(query.get('Page'));
    const size = Number(query.get('PageSize'));
    return json(200, {
      period,
      items: bills.slice((page - 1) * size, page * size),
      page,
      pageSize: size,
      totalCount: bills.length,
      totals,
    });
  };
}

/** The files the page handed to the browser to save; jsdom has no object URLs of its own. */
let saved: Blob[] = [];

beforeEach(() => {
  saved = [];
  Object.assign(URL, {
    createObjectURL: (blob: Blob) => {
      saved.push(blob);
      return 'blob:report';
    },
    revokeObjectURL: () => undefined,
  });
});

function bytesOf(blob: Blob): Promise<Uint8Array> {
  return new Promise((resolve) => {
    const reader = new FileReader();
    reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
    reader.readAsArrayBuffer(blob);
  });
}

describe('the reports (BR-901)', () => {
  it('lists the six reports for every role', async () => {
    renderApp('/reports', { session: viewer, fetch: routedApi({}).fetch });

    for (const name of [
      'Bill register',
      'Payment register',
      'Daily collection',
      'Outstanding by payer',
      'Unapplied cash',
      'Available credit',
      'Pending approvals',
      'Reconciliation',
    ]) {
      expect(await screen.findByRole('link', { name })).toBeInTheDocument();
    }
  });

  it("shows a register's page, the period the API answered and its totals over every page", async () => {
    const api = routedApi({ 'GET /api/v1/reports/bill-register': billRegister() });
    renderApp('/reports/bills', { session: viewer, fetch: api.fetch });

    const table = await screen.findByRole('table', { name: 'Bill register' });
    expect(within(table).getAllByRole('row')).toHaveLength(22);
    expect(screen.getByText('01 Nov 2026 to 30 Nov 2026, this month in India')).toBeInTheDocument();
    const footer = within(table).getAllByRole('row').at(-1)!;
    expect(footer).toHaveTextContent('120 bills');
    expect(footer).toHaveTextContent('1,200.00');
    expect(footer).toHaveTextContent('600.00');
  });

  it('sends its filters and keeps them in the address', async () => {
    const api = routedApi({ 'GET /api/v1/reports/bill-register': billRegister() });
    const { user, router } = renderApp('/reports/bills', { session: viewer, fetch: api.fetch });

    await user.type(await screen.findByLabelText('From'), '2026-11-10');
    await user.click(screen.getByRole('button', { name: 'Show' }));
    expect(await screen.findByText('Give both dates, or neither for the current month.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('To'), '2026-11-20');
    await user.click(screen.getByRole('button', { name: 'Show' }));
    await user.selectOptions(screen.getByLabelText('Status'), 'Settled');

    await waitFor(() => expect(router.state.location.search).toBe('?from=2026-11-10&to=2026-11-20&status=Settled'));
    await waitFor(() =>
      expect(api.sent('GET', '/api/v1/reports/bill-register').map((sent) => new URL(sent.url).search)).toContain(
        '?From=2026-11-10&To=2026-11-20&Status=Settled&Page=1&PageSize=20',
      ),
    );
  });

  it('exports every row of the filter, not only the page shown, with its period and totals', async () => {
    const api = routedApi({ 'GET /api/v1/reports/bill-register': billRegister() });
    const { user } = renderApp('/reports/bills', { session: viewer, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Export CSV' }));

    expect(
      await screen.findByText('Exported 120 rows to bill-register-2026-11-01-to-2026-11-30.csv.'),
    ).toBeInTheDocument();
    expect(
      api
        .sent('GET', '/api/v1/reports/bill-register')
        .map((sent) => new URL(sent.url).searchParams)
        .filter((query) => query.get('PageSize') === '100')
        .map((query) => query.get('Page')),
    ).toEqual(['1', '2']);
    const bytes = await bytesOf(saved[0]!);
    expect([...bytes.slice(0, 3)]).toEqual([0xef, 0xbb, 0xbf]);
    const lines = new TextDecoder('utf-8', { ignoreBOM: true }).decode(bytes).slice(1).split('\r\n');
    expect(lines.slice(0, 5)).toEqual([
      'Bill register',
      'Period,2026-11-01,2026-11-30',
      '',
      'Number,Bill date,Due date,Patient,Payer,Status,Total (₹),Balance due (₹)',
      'BIL/26-27/000001,2026-11-20,2026-12-05,Asha Verma,Asha Verma,Posted,10.00,5.00',
    ]);
    expect(lines[5]).toBe(`BIL/26-27/000002,2026-11-20,2026-12-05,Asha Verma,"'=Verma, Asha",Posted,10.00,5.00`);
    expect(lines.filter((line) => line.startsWith('BIL/'))).toHaveLength(120);
    expect(lines.slice(-4)).toEqual(['Bills,120', 'Total (₹),1200.00', 'Balance due (₹),600.00', '']);
  });

  it('refuses an export whose rows do not come to the totals, after reading it twice', async () => {
    const api = routedApi({
      'GET /api/v1/reports/bill-register': billRegister({ ...registerTotals, total: 1200.01 }),
    });
    const { user } = renderApp('/reports/bills', { session: viewer, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Export CSV' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The report changed while it was exported');
    expect(saved).toHaveLength(0);
  });

  it('shows the collection of a day by method, its net the movement of Cash', async () => {
    const method = (name: string, received: number, refunded: number) => ({
      method: name,
      payments: received === 0 ? 0 : 1,
      received,
      reversed: 0,
      refunded,
      net: received - refunded,
    });
    const api = routedApi({
      'GET /api/v1/reports/daily-collection': () =>
        json(200, {
          date: '2026-11-23',
          methods: [method('Cash', 600, 200), method('Card', 0, 0), method('UPI', 0, 50)],
          totals: { payments: 1, received: 600, reversed: 0, refunded: 250, net: 350 },
        }),
    });
    renderApp('/reports/collection?date=2026-11-23', { session: sessionFor(['Cashier']), fetch: api.fetch });

    const table = await screen.findByRole('table', { name: 'Collection by method' });
    expect(within(table).getByText('-50.00')).toBeInTheDocument();
    expect(within(table).getAllByRole('row').at(-1)).toHaveTextContent('350.00');
    expect(new URL(api.sent('GET', '/api/v1/reports/daily-collection')[0]!.url).search).toBe('?date=2026-11-23');
  });

  it('says when the reconciliation finds no drift, and names the drift it finds', async () => {
    const clean = { rows: 0, byIdentity: [], checkedAtUtc: '2026-11-23T06:00:00+00:00' };
    const drift = {
      identity: 'BillBalance',
      subjectId: 'e5000000-0000-4000-8000-000000000001',
      reference: 'BIL/26-27/000001',
      cached: 500,
      ledger: 499.99,
    };
    const answers = [
      { period: null, items: [], page: 1, pageSize: 20, totalCount: 0, totals: clean },
      {
        period: null,
        items: [drift],
        page: 1,
        pageSize: 20,
        totalCount: 1,
        totals: { rows: 1, byIdentity: [{ identity: 'BillBalance', rows: 1 }], checkedAtUtc: clean.checkedAtUtc },
      },
    ];
    const api = routedApi({ 'GET /api/v1/reports/reconciliation': () => json(200, answers.shift()) });
    const { unmount } = renderApp('/reports/reconciliation', { session: viewer, fetch: api.fetch });

    expect(await screen.findByText(/^No drift/)).toHaveAttribute('role', 'status');
    unmount();

    renderApp('/reports/reconciliation', { session: viewer, fetch: api.fetch });
    expect(await screen.findByRole('alert')).toHaveTextContent('1 row of drift');
    expect(screen.getByText('499.99')).toBeInTheDocument();
  });

  it('links each payer with a balance to their statement', async () => {
    const api = routedApi({
      'GET /api/v1/reports/outstanding-by-payer': () =>
        json(200, {
          period: null,
          items: [
            {
              payerId: 'b2000000-0000-4000-8000-000000000002',
              payerName: 'Asha Verma',
              receivable: 500,
              unappliedCash: 400,
              creditAvailable: 0,
              netOutstanding: 100,
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
          totals: { payers: 1, receivable: 500, unappliedCash: 400, creditAvailable: 0, netOutstanding: 100 },
        }),
    });
    renderApp('/reports/outstanding', { session: viewer, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Asha Verma' })).toHaveAttribute(
      'href',
      '/payers/b2000000-0000-4000-8000-000000000002/statement',
    );
  });
});
