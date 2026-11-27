import { configure, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { todayInIndia } from '../format';
import { inTurn, json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { page, payer, payment, printSettings, problem } from '../test/records';
import type { CashierShift } from './api';

/**
 * The screens of the cashier's shift (Prompt 11.2b, ADR 0060), with the API faked: a user opens a shift, counts the
 * drawer in exact paise against the expected cash the API answered, and closes it or sends its closing for approval;
 * the day-end report lists every shift; the closing slip prints without being recorded; and recording cash asks for an
 * open shift and takes its date.
 */
// Each page loads on demand and reads several records before it answers.
configure({ asyncUtilTimeout: 3000 });

const cashier = sessionFor(['Cashier']);
const supervisor = sessionFor(['Supervisor']);
const someoneElse = 'd0000000-0000-4000-8000-000000000077';
const shifts = 'GET /api/v1/cashier-shifts';

/** An open shift of the signed-in user: 1,000.00 of float, 250.50 taken, nothing paid out. */
function shift(overrides: Partial<CashierShift> = {}): CashierShift {
  return {
    id: 'fe000000-0000-4000-8000-000000000001',
    cashierUserId: cashier.userId,
    cashierName: 'Chetan Cashier',
    businessDate: '2026-11-27',
    status: 'Open',
    openingFloat: 1000,
    openedAtUtc: '2026-11-27T03:00:00+00:00',
    cashDocuments: 1,
    cashReceived: 250.5,
    cashPaidOut: 0,
    expectedCash: 1250.5,
    count: null,
    looseChange: null,
    countedCash: null,
    difference: null,
    differenceReason: null,
    countedAtUtc: null,
    closedAtUtc: null,
    version: 'AAAAAAAAEAA=',
    ...overrides,
  };
}

const open = shift();
const one = `/api/v1/cashier-shifts/${open.id}`;
const counted = {
  count: [
    { denomination: 2000, pieces: 0 },
    { denomination: 500, pieces: 2 },
    { denomination: 200, pieces: 1 },
    { denomination: 100, pieces: 0 },
    { denomination: 50, pieces: 1 },
    { denomination: 20, pieces: 0 },
    { denomination: 10, pieces: 0 },
    { denomination: 5, pieces: 0 },
    { denomination: 2, pieces: 0 },
    { denomination: 1, pieces: 0 },
  ],
  looseChange: 0.5,
  countedAtUtc: '2026-11-27T11:30:00+00:00',
};
const closed = shift({
  ...counted,
  status: 'Closed',
  countedCash: 1250.5,
  difference: 0,
  closedAtUtc: '2026-11-27T11:30:00+00:00',
  version: 'AAAAAAAAEQA=',
});
const waiting = shift({
  ...counted,
  looseChange: 0,
  status: 'PendingApproval',
  countedCash: 1250,
  difference: -0.5,
  differenceReason: 'A 50 paise coin is missing',
  version: 'AAAAAAAAEgA=',
});
const letterhead = {
  name: 'City Hospital',
  legalName: 'City Hospital Private Limited',
  addressLine1: '12 Example Road',
  addressLine2: null,
  city: 'Pune',
  state: 'Maharashtra',
  pinCode: '411001',
  phone: '+91 00000 00000',
  email: null,
  gstin: '27AAAAA0000A1Z5',
  signatory: 'Accounts Officer',
  signatoryDesignation: null,
  logoSha256: null,
  signatureSha256: null,
};
const printing: Record<string, Handler> = {
  'GET /api/v1/settings/letterhead': () => json(200, letterhead),
  'GET /api/v1/settings/print': () => json(200, printSettings()),
};

async function typePieces(user: ReturnType<typeof renderApp>['user'], denomination: number, pieces: string) {
  await user.type(screen.getByLabelText(`₹${String(denomination)}`), pieces);
}

describe('the shifts of the cash counter (BR-310 to BR-315)', () => {
  it('tells a cashier without a shift to open one, and lists the shifts they opened', async () => {
    const api = routedApi({
      [shifts]: inTurn(
        () => json(200, page([])),
        () => json(200, page([closed])),
      ),
    });
    renderApp('/shifts', { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('You have no open shift')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open a shift' })).toHaveAttribute('href', '/shifts/new');

    const row = (await screen.findByRole('link', { name: '27 Nov 2026' })).closest('tr');
    expect(within(row!).getByText('Chetan Cashier')).toBeInTheDocument();
    expect(within(row!).getByText('Closed')).toBeInTheDocument();
    expect(within(row!).getByText('1,250.50', { selector: 'td:nth-child(8)' })).toBeInTheDocument();
    expect(within(row!).getByText('None: the count is the expected cash')).toBeInTheDocument();
    // The day-end report is not a cashier's (BR-903).
    expect(screen.queryByRole('link', { name: 'Day-end report' })).not.toBeInTheDocument();
  });

  it('shows an open shift with what its drawer should hold, and the way to close it', async () => {
    const api = routedApi({ [shifts]: () => json(200, page([open])) });
    renderApp('/shifts', { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('Your shift of 27 Nov 2026 is open')).toBeInTheDocument();
    expect(screen.getByText(/Your drawer should hold ₹1,250\.50 now/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Count the drawer and close the shift' })).toHaveAttribute(
      'href',
      `/shifts/${open.id}/close`,
    );
  });

  it('opens a shift with the float that was typed, and shows it', async () => {
    const fresh = shift({ openingFloat: 500, cashReceived: 0, expectedCash: 500, cashDocuments: 0 });
    const api = routedApi({
      'POST /api/v1/cashier-shifts': () => json(201, fresh),
      [`GET ${one}`]: () => json(200, fresh),
    });
    const { user, router } = renderApp('/shifts/new', { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Open shift' }));
    expect(await screen.findByText('Enter the float; 0 when the drawer is empty.')).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Opening float/), '500');
    await user.click(screen.getByRole('button', { name: 'Open shift' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/shifts/${fresh.id}`));
    const sent = api.sent('POST', '/api/v1/cashier-shifts')[0];
    expect(await sent?.json()).toEqual({ openingFloat: 500 });
    // Not a financial document: no key of an operation (ADR 0060 point 11).
    expect(sent?.headers.get('Idempotency-Key')).toBeNull();
    expect(await screen.findByText('This shift is open')).toBeInTheDocument();
  });

  it("shows the API's refusal of a second shift", async () => {
    const api = routedApi({
      'POST /api/v1/cashier-shifts': () =>
        json(422, problem(422, 'shift_already_open', 'You have a shift that is open or waits for approval.')),
    });
    const { user } = renderApp('/shifts/new', { session: cashier, fetch: api.fetch });

    await user.type(await screen.findByLabelText(/Opening float/), '0');
    await user.click(screen.getByRole('button', { name: 'Open shift' }));

    expect(await screen.findByText('You have a shift that is open or waits for approval.')).toBeInTheDocument();
  });
});

describe('closing a shift (BR-312 to BR-314)', () => {
  it('adds the count up in exact paise against the expected cash, and closes the shift when they are equal', async () => {
    const api = routedApi({
      [`GET ${one}`]: inTurn(
        () => json(200, open),
        () => json(200, closed),
      ),
      [`POST ${one}/close`]: () => json(200, closed),
    });
    const { user, router } = renderApp(`/shifts/${open.id}/close`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('Expected cash')).toBeInTheDocument();
    const figure = (term: string) => screen.getByText(term).nextElementSibling;
    expect(figure('Expected cash')).toHaveTextContent('₹1,250.50');
    expect(figure('Counted cash')).toHaveTextContent('₹0.00');
    expect(figure('Difference')).toHaveTextContent('Short by ₹1,250.50');

    await typePieces(user, 500, '2');
    await typePieces(user, 200, '1');
    await typePieces(user, 50, '1');
    expect(figure('Counted cash')).toHaveTextContent('₹1,250.00');
    expect(figure('Difference')).toHaveTextContent('Short by ₹0.50');
    expect(screen.getByRole('button', { name: 'Send the closing for approval' })).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Loose change/), '0.5');
    expect(figure('Counted cash')).toHaveTextContent('₹1,250.50');
    expect(figure('Difference')).toHaveTextContent('None: the count is the expected cash');
    // No difference: no reason is asked for.
    expect(screen.queryByLabelText('Why the count differs')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Close the shift' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/shifts/${open.id}`));
    const sent = api.sent('POST', `${one}/close`)[0];
    expect(sent?.headers.get('If-Match')).toBe(`"${open.version}"`);
    expect(sent?.headers.get('Idempotency-Key')).toBeNull();
    expect(await sent?.json()).toEqual({
      count: [
        { denomination: 500, pieces: 2 },
        { denomination: 200, pieces: 1 },
        { denomination: 50, pieces: 1 },
      ],
      looseChange: 0.5,
      reason: null,
    });
    expect(await screen.findByText('This shift is closed')).toBeInTheDocument();
  });

  it('asks why a count differs, and sends the closing for approval with the reason', async () => {
    const api = routedApi({
      [`GET ${one}`]: inTurn(
        () => json(200, open),
        () => json(200, waiting),
      ),
      [`POST ${one}/close`]: () => json(202, waiting),
    });
    const { user, router } = renderApp(`/shifts/${open.id}/close`, { session: cashier, fetch: api.fetch });

    await screen.findByText('Expected cash');
    await typePieces(user, 500, '2');
    await typePieces(user, 200, '1');
    await typePieces(user, 50, '1');
    await user.click(screen.getByRole('button', { name: 'Send the closing for approval' }));

    expect(await screen.findByText('Say why the count differs from the expected cash.')).toBeInTheDocument();
    expect(api.sent('POST', `${one}/close`)).toHaveLength(0);

    await user.type(screen.getByLabelText('Why the count differs'), ' A 50 paise coin is missing ');
    await user.click(screen.getByRole('button', { name: 'Send the closing for approval' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/shifts/${open.id}`));
    expect(await api.sent('POST', `${one}/close`)[0]?.json()).toMatchObject({
      looseChange: 0,
      reason: 'A 50 paise coin is missing',
    });
    expect(await screen.findByText('The closing waits for approval')).toBeInTheDocument();
    expect(screen.getByText('Short by ₹0.50')).toBeInTheDocument();
    expect(screen.getByText('A 50 paise coin is missing')).toBeInTheDocument();
  });

  it('refuses pieces that are no whole number and loose change of a rupee or more, and sends nothing', async () => {
    const api = routedApi({ [`GET ${one}`]: () => json(200, open) });
    const { user } = renderApp(`/shifts/${open.id}/close`, { session: cashier, fetch: api.fetch });

    await screen.findByText('Expected cash');
    await typePieces(user, 100, '2.5');
    expect(screen.getByText('A whole number from 0 to 99,999.')).toBeInTheDocument();
    expect(screen.getByLabelText('₹100')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('Counted cash').nextElementSibling).toHaveTextContent('—');
    expect(screen.getByRole('button', { name: 'Close the shift' })).toBeDisabled();

    await user.clear(screen.getByLabelText('₹100'));
    await user.type(screen.getByLabelText(/Loose change/), '1');
    expect(screen.getByText('The loose change is what is below one rupee: 0 to 0.99.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Close the shift' })).toBeDisabled();
    expect(api.sent('POST', `${one}/close`)).toHaveLength(0);
  });

  it('starts again from the shift as it is now when cash joined it while it was counted', async () => {
    const grown = shift({ cashReceived: 350.5, expectedCash: 1350.5, cashDocuments: 2, version: 'AAAAAAAAEwA=' });
    const api = routedApi({
      [`GET ${one}`]: inTurn(
        () => json(200, open),
        () => json(200, grown),
      ),
      [`POST ${one}/close`]: () =>
        json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else. Nothing was saved.')),
    });
    const { user } = renderApp(`/shifts/${open.id}/close`, { session: cashier, fetch: api.fetch });

    await screen.findByText('Expected cash');
    await typePieces(user, 500, '2');
    await typePieces(user, 200, '1');
    await typePieces(user, 50, '1');
    await user.type(screen.getByLabelText(/Loose change/), '0.5');
    await user.click(screen.getByRole('button', { name: 'Close the shift' }));

    // The count is typed again against the new figure: nothing is closed on the old one.
    await waitFor(() => expect(screen.getByText('Expected cash').nextElementSibling).toHaveTextContent('₹1,350.50'));
    expect(screen.getByLabelText('₹500')).toHaveValue('');
  });

  it("offers no count of another cashier's shift, nor of one that is counted", async () => {
    const theirs = shift({ cashierUserId: someoneElse });
    const api = routedApi({
      [`GET ${one}`]: inTurn(
        () => json(200, theirs),
        () => json(200, closed),
      ),
    });
    const first = renderApp(`/shifts/${open.id}/close`, { session: supervisor, fetch: api.fetch });

    expect(await screen.findByText('A shift is counted and closed by its own cashier.')).toBeInTheDocument();
    expect(screen.queryByLabelText('₹500')).not.toBeInTheDocument();
    first.unmount();

    renderApp(`/shifts/${open.id}/close`, { session: cashier, fetch: api.fetch });
    expect(await screen.findByText(/The shift is counted already/)).toBeInTheDocument();
  });
});

describe('a shift, its slip and the day-end report', () => {
  let print: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    print = vi.fn();
    vi.stubGlobal('print', print);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows a counted shift with its count, and its trail to who may read it', async () => {
    const api = routedApi({
      [`GET ${one}`]: () => json(200, waiting),
      'GET /api/v1/audit-records': () => json(200, page([])),
    });
    renderApp(`/shifts/${open.id}`, { session: supervisor, fetch: api.fetch });

    expect(await screen.findByText('The closing waits for approval')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Review it in Approvals' })).toHaveAttribute(
      'href',
      '/approvals?documentType=CashierShift',
    );
    const count = screen.getByRole('heading', { name: 'The count' }).parentElement!;
    expect(within(count).getByText('₹500').closest('tr')).toHaveTextContent('21,000.00');
    expect(within(count).queryByText('₹2000')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Print the closing slip' })).toHaveAttribute(
      'href',
      `/shifts/${open.id}/print`,
    );
    // A supervisor is not its cashier: no count is offered. The trail was asked for.
    expect(screen.queryByRole('link', { name: 'Count and close' })).not.toBeInTheDocument();
    await waitFor(() => expect(api.sent('GET', '/api/v1/audit-records')).toHaveLength(1));
  });

  it('prints the closing slip on the letterhead without recording a print', async () => {
    const api = routedApi({ ...printing, [`GET ${one}`]: () => json(200, waiting) });
    const { user } = renderApp(`/shifts/${open.id}/print`, { session: cashier, fetch: api.fetch });

    const sheet = await screen.findByRole('article', { name: 'Shift Closing Slip' });
    expect(within(sheet).getByText('City Hospital Private Limited')).toBeInTheDocument();
    expect(within(sheet).getByText('Chetan Cashier')).toBeInTheDocument();
    expect(within(sheet).getByText('Waiting for approval')).toBeInTheDocument();
    expect(within(sheet).getByText('Expected cash').nextElementSibling).toHaveTextContent('₹1,250.50');
    expect(within(sheet).getByText('Counted cash').nextElementSibling).toHaveTextContent('₹1,250.00');
    expect(within(sheet).getByText('Short by ₹0.50')).toBeInTheDocument();
    expect(within(sheet).getByText(/one thousand two hundred fifty/i)).toBeInTheDocument();
    expect(within(sheet).getByText('A 50 paise coin is missing')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Print' }));

    expect(print).toHaveBeenCalledTimes(1);
    expect(api.requests.filter((request) => request.method === 'POST')).toHaveLength(0);
  });

  it('has no slip for a shift that is still open', async () => {
    const api = routedApi({ ...printing, [`GET ${one}`]: () => json(200, open) });
    renderApp(`/shifts/${open.id}/print`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('This shift has no closing slip yet')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Print' })).not.toBeInTheDocument();
  });

  it('lists every shift of a day with the totals and the cash outside the shifts', async () => {
    const report = {
      date: '2026-11-27',
      shifts: [
        {
          shiftId: closed.id,
          cashierUserId: cashier.userId,
          cashierName: 'Chetan Cashier',
          status: 'Closed',
          openedAtUtc: '2026-11-27T03:00:00+00:00',
          closedAtUtc: '2026-11-27T11:30:00+00:00',
          openingFloat: 1000,
          received: 500,
          paidOut: 250,
          net: 250,
          expectedCash: 1250,
          countedCash: 1255,
          difference: 5,
        },
        {
          shiftId: 'fe000000-0000-4000-8000-000000000002',
          cashierUserId: someoneElse,
          cashierName: 'Kavya Clerk',
          status: 'Open',
          openedAtUtc: '2026-11-27T04:00:00+00:00',
          closedAtUtc: null,
          openingFloat: 0,
          received: 150,
          paidOut: 0,
          net: 150,
          expectedCash: 150,
          countedCash: null,
          difference: null,
        },
      ],
      shiftsTotal: { received: 650, paidOut: 250, net: 400 },
      outsideShifts: { received: 0, paidOut: 300, net: -300 },
      cash: { received: 650, paidOut: 550, net: 100 },
      difference: 5,
      unclosedShifts: 1,
    };
    const api = routedApi({ 'GET /api/v1/reports/day-end': () => json(200, report) });
    renderApp('/reports/day-end?date=2026-11-27', { session: supervisor, fetch: api.fetch });

    const table = await screen.findByRole('table', { name: 'Shifts of the day' });
    expect(within(table).getByRole('link', { name: 'Chetan Cashier' })).toHaveAttribute('href', `/shifts/${closed.id}`);
    expect(within(table).getByText('Kavya Clerk').closest('tr')).toHaveTextContent('Open');
    const footer = within(table).getByText('2 shifts').closest('tr')!;
    expect(footer).toHaveTextContent('650.00');
    expect(footer).toHaveTextContent('400.00');
    expect(screen.getByText('The day is not closed yet')).toBeInTheDocument();
    expect(screen.getByText('Cash outside the shifts, net (₹)').parentElement).toHaveTextContent('-300.00');
    expect(screen.getByText('Cash of the day, net (₹)').parentElement).toHaveTextContent('100.00');
    expect(new URL(api.sent('GET', '/api/v1/reports/day-end')[0]!.url).searchParams.get('date')).toBe('2026-11-27');
  });

  it('is not a cashier’s to read, and is not among their reports', async () => {
    const api = routedApi({});
    const first = renderApp('/reports/day-end', { session: cashier, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
    first.unmount();

    renderApp('/reports', { session: cashier, fetch: api.fetch });
    expect(await screen.findByRole('link', { name: 'Daily collection' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Day-end report' })).not.toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
  });
});

describe('recording cash (BR-311)', () => {
  const ashasPayer = payer();
  const cash = payment();
  const payerRoute = { [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer) };

  it('asks for an open shift before cash is recorded, and sends nothing without one', async () => {
    const api = routedApi({ ...payerRoute, [shifts]: () => json(200, page([])) });
    const { user } = renderApp(`/payments/new?payerId=${ashasPayer.id}`, { session: cashier, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('Method'), 'Cash');

    expect(await screen.findByText('You have no open shift')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open a shift' })).toHaveAttribute('href', '/shifts/new');
    expect(screen.getByRole('button', { name: 'Record payment' })).toBeDisabled();

    // Another method needs no shift.
    await user.selectOptions(screen.getByLabelText('Method'), 'UPI');
    expect(screen.queryByText('You have no open shift')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Record payment' })).toBeEnabled();
    expect(api.sent('POST', '/api/v1/payments')).toHaveLength(0);
  });

  it('dates cash the business date of the open shift, whatever today is', async () => {
    const yesterdays = shift({ businessDate: '2026-11-26' });
    const api = routedApi({
      ...payerRoute,
      [shifts]: () => json(200, page([yesterdays])),
      'POST /api/v1/payments': () => json(201, cash),
      [`GET /api/v1/payments/${cash.id}/allocations`]: () => json(200, { payment: cash, allocations: [] }),
      'GET /api/v1/audit-records': () => json(200, page([])),
    });
    const { user, router } = renderApp(`/payments/new?payerId=${ashasPayer.id}`, {
      session: cashier,
      fetch: api.fetch,
    });

    await user.selectOptions(await screen.findByLabelText('Method'), 'Cash');
    expect(
      await screen.findByText('Cash is dated 26 Nov 2026, the business date of your open shift.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText(/Payment date/)).toHaveValue('2026-11-26');
    expect('2026-11-26').not.toBe(todayInIndia());
    await user.type(screen.getByLabelText('Amount (₹)'), '40');
    await user.click(screen.getByRole('button', { name: 'Record payment' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/payments/${cash.id}`));
    expect(await api.sent('POST', '/api/v1/payments')[0]?.json()).toMatchObject({
      paymentDate: '2026-11-26',
      method: 'Cash',
      amount: 40,
    });
  });
});
