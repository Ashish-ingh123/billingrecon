import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { formatDecimal, todayInIndia } from '../format';
import { inTurn, json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { allocation, billSummary, page, payer, payment, problem } from '../test/records';

const ashasPayer = payer();
const cash = payment();
const one = `/api/v1/payments/${cash.id}`;
const cashier = sessionFor(['Cashier']);
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** Two of the payer's bills that owe something: an older posted one and a newer partly paid one. */
const older = billSummary();
const newer = billSummary({
  id: 'e5000000-0000-4000-8000-000000000012',
  number: 'BIL/26-27/000012',
  billDate: '2026-11-15',
  status: 'PartiallyPaid',
  total: 3000,
  balanceDue: 2511.07,
});

/** What a payment's page reads: the payment with its allocations, its payer, its bills and its trail. */
function paymentRoutes(
  current: Handler = () => json(200, { payment: cash, allocations: [] }),
): Record<string, Handler> {
  return {
    [`GET ${one}/allocations`]: current,
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    [`GET /api/v1/bills/${older.id}`]: () => json(200, { ...older, lines: [] }),
    'GET /api/v1/audit-records': () => json(200, page([])),
  };
}

describe('recording a payment (US-30, BR-301)', () => {
  it('records a cash payment from the payer it was opened for, with the key of the operation, and opens it', async () => {
    const api = routedApi({
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
      'POST /api/v1/payments': () => json(201, cash),
      ...paymentRoutes(),
    });
    const { user, router } = renderApp(`/payments/new?payerId=${ashasPayer.id}`, {
      session: cashier,
      fetch: api.fetch,
    });

    expect(await screen.findByText("Asha Verma (Self-pay, a patient's own payer)")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Method'), 'Cash');
    await user.type(screen.getByLabelText('Amount (₹)'), '1500');
    await user.click(screen.getByRole('button', { name: 'Record payment' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/payments/${cash.id}`));
    const request = api.sent('POST', '/api/v1/payments')[0];
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
    expect(await request?.json()).toEqual({
      payerId: ashasPayer.id,
      paymentDate: todayInIndia(),
      method: 'Cash',
      reference: null,
      amount: 1500,
      notes: null,
    });
  });

  it('asks for the reference of every method but cash, and for an amount above zero', async () => {
    const api = routedApi({ [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer) });
    const { user } = renderApp(`/payments/new?payerId=${ashasPayer.id}`, { session: cashier, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('Method'), 'UPI');
    await user.type(screen.getByLabelText('Amount (₹)'), '0');
    await user.click(screen.getByRole('button', { name: 'Record payment' }));

    expect(
      await screen.findByText('Enter the reference: the UPI id, card authorisation, UTR or cheque number.'),
    ).toBeInTheDocument();
    expect(screen.getByText('The amount is more than 0.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/payments')).toHaveLength(0);
  });
});

describe('a payment (US-30 to US-33)', () => {
  it('shows what was received and where it went, and to a cashier every action', async () => {
    const applied = { payment: { ...cash, unappliedAmount: 1000 }, allocations: [allocation()] };
    const api = routedApi(paymentRoutes(() => json(200, applied)));
    renderApp(`/payments/${cash.id}`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'PAY/26-27/000001' })).toBeInTheDocument();
    expect(screen.getByText('₹1,000.00')).toBeInTheDocument();
    expect(screen.getByLabelText('Figures of the payment')).toHaveTextContent(
      `Amount₹${formatDecimal(cash.amount)}Unapplied cash₹1,000.00`,
    );
    const allocations = within(screen.getByRole('region', { name: 'Allocations' }));
    expect(await allocations.findByRole('link', { name: 'BIL/26-27/000011' })).toBeInTheDocument();
    expect(allocations.getByText('You')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Allocate to bills' })).toHaveAttribute(
      'href',
      `/payments/${cash.id}/allocate`,
    );
    expect(screen.getByRole('link', { name: 'Draft a refund' })).toHaveAttribute(
      'href',
      `/refunds/new?paymentId=${cash.id}`,
    );
    expect(screen.getAllByRole('button', { name: 'Request reversal' })).toHaveLength(2);
  });

  it('offers a billing clerk no refund, and a viewer nothing', async () => {
    const api = routedApi(paymentRoutes());
    const { unmount } = renderApp(`/payments/${cash.id}`, { session: sessionFor(['BillingClerk']), fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Allocate to bills' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Draft a refund' })).not.toBeInTheDocument();
    unmount();

    renderApp(`/payments/${cash.id}`, { session: sessionFor(['Viewer']), fetch: api.fetch });
    expect(await screen.findByRole('heading', { name: 'PAY/26-27/000001' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Allocate to bills' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Request reversal' })).not.toBeInTheDocument();
  });

  it('asks the reversal of the payment with a reason, its version and a key, and says that it waits', async () => {
    const waiting = {
      payment: { ...cash, status: 'PendingReversal' as const, version: 'AAAAAAAACQE=' },
      allocations: [],
    };
    const api = routedApi({ ...paymentRoutes(), [`POST ${one}/reverse`]: () => json(202, waiting) });
    const { user } = renderApp(`/payments/${cash.id}`, { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Request reversal' }));
    await user.click(screen.getByRole('button', { name: 'Ask for the reversal' }));
    expect(screen.getByText('Give the reason.')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Why should this payment be reversed/), 'Cheque dishonoured');
    await user.click(screen.getByRole('button', { name: 'Ask for the reversal' }));

    expect(await screen.findByText('The reversal is asked for', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('This payment waits for its reversal', { exact: false })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Allocate to bills' })).not.toBeInTheDocument();
    const request = api.sent('POST', `${one}/reverse`)[0];
    expect(await request?.json()).toEqual({ reason: 'Cheque dishonoured' });
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAACQA="');
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
  });

  it("shows the API's refusal of reversing a payment that a refund was paid from", async () => {
    const api = routedApi({
      ...paymentRoutes(),
      [`POST ${one}/reverse`]: () =>
        json(422, problem(422, 'payment_refunded', 'A refund was paid from this payment.')),
    });
    const { user } = renderApp(`/payments/${cash.id}`, { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Request reversal' }));
    await user.type(screen.getByLabelText(/Why should this payment be reversed/), 'Cheque dishonoured');
    await user.click(screen.getByRole('button', { name: 'Ask for the reversal' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('A refund was paid from this payment.');
  });

  it('asks the reversal of one allocation by its id', async () => {
    const applied = { payment: { ...cash, unappliedAmount: 1000 }, allocations: [allocation()] };
    const waiting = {
      payment: { ...applied.payment, version: 'AAAAAAAACQE=' },
      allocations: [allocation({ status: 'PendingReversal' })],
    };
    const path = `${one}/allocations/${allocation().id}/reverse`;
    const api = routedApi({ ...paymentRoutes(() => json(200, applied)), [`POST ${path}`]: () => json(202, waiting) });
    const { user } = renderApp(`/payments/${cash.id}`, { session: cashier, fetch: api.fetch });

    const allocations = within(await screen.findByRole('region', { name: 'Allocations' }));
    await user.click(allocations.getByRole('button', { name: 'Request reversal' }));
    await user.type(screen.getByLabelText('Why should this allocation be reversed?'), 'Applied to the wrong bill');
    await user.click(screen.getByRole('button', { name: 'Ask for the reversal' }));

    expect(
      await screen.findByText('An allocation of this payment waits for its reversal', { exact: false }),
    ).toBeInTheDocument();
    expect(await api.sent('POST', path)[0]?.json()).toEqual({ reason: 'Applied to the wrong bill' });
  });
});

describe('allocating a payment (US-31, BR-303, ADR 0041 point 1)', () => {
  function allocateRoutes(allocations: Handler): Record<string, Handler> {
    return {
      ...paymentRoutes(),
      'GET /api/v1/bills': (request) =>
        json(200, page(new URL(request.url).searchParams.get('Status') === 'Posted' ? [older] : [newer])),
      [`POST ${one}/allocations`]: allocations,
    };
  }

  it('lists the bills that owe something oldest first, fills them oldest first, and sends the amounts with the version and a key', async () => {
    const after = { payment: { ...cash, unappliedAmount: 0 }, allocations: [] };
    const api = routedApi(allocateRoutes(() => json(200, after)));
    const { user, router } = renderApp(`/payments/${cash.id}/allocate`, { session: cashier, fetch: api.fetch });

    const rows = await screen.findAllByRole('row');
    expect(rows.map((row) => row.textContent)).toEqual([
      expect.stringContaining('Bill'),
      expect.stringContaining('BIL/26-27/000011'),
      expect.stringContaining('BIL/26-27/000012'),
    ]);

    await user.click(screen.getByRole('button', { name: 'Apply oldest first' }));
    expect(screen.getByLabelText('Amount to apply to BIL/26-27/000011')).toHaveValue('1049.48');
    expect(screen.getByLabelText('Amount to apply to BIL/26-27/000012')).toHaveValue('450.52');
    expect(screen.getByText('₹0.00')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Allocate' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/payments/${cash.id}`));
    const request = api.sent('POST', `${one}/allocations`)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAACQA="');
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
    expect(await request?.json()).toEqual({
      allocations: [
        { billId: older.id, amount: 1049.48 },
        { billId: newer.id, amount: 450.52 },
      ],
    });
  });

  it('refuses an amount above a bill, a total above the unapplied cash, and nothing at all, before sending', async () => {
    const api = routedApi(allocateRoutes(() => json(200, {})));
    const { user } = renderApp(`/payments/${cash.id}/allocate`, { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Allocate' }));
    expect(await screen.findByText('Enter an amount for at least one bill.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Amount to apply to BIL/26-27/000011'), '1049.49');
    await user.click(screen.getByRole('button', { name: 'Allocate' }));
    expect(await screen.findByText("Not more than the bill's balance due, ₹1,049.48.")).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Amount to apply to BIL/26-27/000011'));
    await user.type(screen.getByLabelText('Amount to apply to BIL/26-27/000011'), '1000');
    await user.type(screen.getByLabelText('Amount to apply to BIL/26-27/000012'), '500.01');
    // The meter says so in words as soon as the amounts pass the cap, before anything is sent (ADR 0049).
    expect(
      screen.getByText('More than the unapplied cash by ₹0.01: lower an amount before sending.', { exact: false }),
    ).toHaveAttribute('role', 'alert');
    expect(screen.getByRole('meter', { name: 'Allocated here of the unapplied cash' })).toHaveAttribute(
      'value',
      '150000',
    );
    await user.click(screen.getByRole('button', { name: 'Allocate' }));
    expect(
      await screen.findByText('The amounts come to ₹1,500.01, more than the ₹1,500.00 unapplied.'),
    ).toBeInTheDocument();

    expect(api.sent('POST', `${one}/allocations`)).toHaveLength(0);
  });

  it("puts the API's refusal of a line at that bill", async () => {
    const api = routedApi(
      allocateRoutes(() =>
        json(
          422,
          problem(422, 'allocation_exceeds_balance_due', 'The amount is more than the bill is owed.', {
            'allocations[1].amount': ['The bill is owed less than this now.'],
          }),
        ),
      ),
    );
    const { user } = renderApp(`/payments/${cash.id}/allocate`, { session: cashier, fetch: api.fetch });

    await user.type(await screen.findByLabelText('Amount to apply to BIL/26-27/000011'), '100');
    await user.type(screen.getByLabelText('Amount to apply to BIL/26-27/000012'), '200');
    await user.click(screen.getByRole('button', { name: 'Allocate' }));

    expect(await screen.findByText('The bill is owed less than this now.')).toBeInTheDocument();
    expect(screen.getByLabelText('Amount to apply to BIL/26-27/000012')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('alert')).toHaveTextContent('The amount is more than the bill is owed.');
  });

  it('says so when someone else changed the payment or a bill first', async () => {
    const api = routedApi({
      ...allocateRoutes(() =>
        json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else.')),
      ),
      [`GET ${one}/allocations`]: inTurn(
        () => json(200, { payment: cash, allocations: [] }),
        () => json(200, { payment: { ...cash, unappliedAmount: 500, version: 'AAAAAAAACQE=' }, allocations: [] }),
      ),
    });
    const { user } = renderApp(`/payments/${cash.id}/allocate`, { session: cashier, fetch: api.fetch });

    await user.type(await screen.findByLabelText('Amount to apply to BIL/26-27/000011'), '100');
    await user.click(screen.getByRole('button', { name: 'Allocate' }));

    expect(
      await screen.findByText('Someone else changed this payment or one of these bills first.', { exact: false }),
    ).toBeInTheDocument();
    expect(await screen.findByText('Unapplied cash: ₹500.00', { exact: false })).toBeInTheDocument();
  });
});

describe('the payer statement (US-40, ADR 0041 point 3)', () => {
  it('shows the balances, the bills, the unapplied payments and the credit, with what a cashier can do', async () => {
    const api = routedApi({
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
      [`GET /api/v1/reports/payers/${ashasPayer.id}/balance`]: () =>
        json(200, {
          payerId: ashasPayer.id,
          payerName: 'Asha Verma',
          receivable: 3560.55,
          unappliedCash: 1500,
          creditAvailable: 120,
          netOutstanding: 1940.55,
        }),
      'GET /api/v1/bills': (request) =>
        json(200, page(new URL(request.url).searchParams.get('Status') === 'Posted' ? [older] : [newer])),
      'GET /api/v1/reports/unapplied-cash': () =>
        json(200, {
          ...page([
            {
              paymentId: cash.id,
              number: cash.number,
              paymentDate: cash.paymentDate,
              payerId: ashasPayer.id,
              payerName: 'Asha Verma',
              method: 'Cash',
              amount: 1500,
              unappliedAmount: 1500,
              status: 'Posted',
            },
          ]),
          totals: {},
          period: null,
        }),
      'GET /api/v1/reports/available-credit': () =>
        json(200, {
          ...page([
            {
              creditNoteId: 'c9000000-0000-4000-8000-000000000001',
              number: 'CRN/26-27/000001',
              creditNoteDate: '2026-11-18',
              billId: older.id,
              billNumber: older.number,
              payerId: ashasPayer.id,
              payerName: 'Asha Verma',
              total: 200,
              availableAmount: 120,
            },
          ]),
          totals: {},
          period: null,
        }),
    });
    renderApp(`/payers/${ashasPayer.id}/statement`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Statement of Asha Verma' })).toBeInTheDocument();
    expect(await screen.findByText('₹1,940.55')).toBeInTheDocument();
    expect(screen.getByText('₹3,560.55')).toBeInTheDocument();
    expect(screen.getByLabelText("The payer's figures")).toHaveTextContent(
      'Owed on bills₹3,560.55Unapplied cash₹1,500.00Available credit₹120.00Net outstanding₹1,940.55',
    );
    const bills = within(screen.getByRole('region', { name: 'Outstanding bills' }));
    expect(await bills.findByRole('link', { name: 'BIL/26-27/000011' })).toBeInTheDocument();
    const payments = within(screen.getByRole('region', { name: 'Payments with unapplied cash' }));
    expect(await payments.findByRole('link', { name: 'Allocate' })).toHaveAttribute(
      'href',
      `/payments/${cash.id}/allocate`,
    );
    expect(payments.getByRole('link', { name: 'Refund' })).toHaveAttribute('href', `/refunds/new?paymentId=${cash.id}`);
    const credit = within(screen.getByRole('region', { name: 'Credit notes with available credit' }));
    expect(await credit.findByRole('link', { name: 'Refund' })).toHaveAttribute(
      'href',
      '/refunds/new?creditNoteId=c9000000-0000-4000-8000-000000000001',
    );
  });
});

describe('the list of payments', () => {
  it('sends the filters it keeps in the address and names the payer of each payment', async () => {
    const api = routedApi({
      'GET /api/v1/payments': () => json(200, page([cash])),
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    });
    renderApp('/payments?status=Posted&method=Cash&from=2026-11-01&to=2026-11-30', {
      session: cashier,
      fetch: api.fetch,
    });

    expect(await screen.findByRole('link', { name: 'PAY/26-27/000001' })).toBeInTheDocument();
    expect(await screen.findByText('Asha Verma')).toBeInTheDocument();
    const query = Object.fromEntries(new URL(api.sent('GET', '/api/v1/payments')[0]?.url ?? '').searchParams);
    expect(query).toEqual({
      Status: 'Posted',
      Method: 'Cash',
      PaymentDateFrom: '2026-11-01',
      PaymentDateTo: '2026-11-30',
      Page: '1',
      PageSize: '20',
    });
  });
});
