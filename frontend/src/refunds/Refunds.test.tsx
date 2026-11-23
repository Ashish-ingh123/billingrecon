import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { todayInIndia } from '../format';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { page, payer, payment, problem, refund } from '../test/records';

const ashasPayer = payer();
const cash = payment({ unappliedAmount: 600 });
const draft = refund();
const one = `/api/v1/refunds/${draft.id}`;
const cashier = sessionFor(['Cashier']);
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const creditNoteId = 'c9000000-0000-4000-8000-000000000001';

function refundRoutes(current: Handler = () => json(200, draft)): Record<string, Handler> {
  return {
    [`GET ${one}`]: current,
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    [`GET /api/v1/payments/${cash.id}/allocations`]: () => json(200, { payment: cash, allocations: [] }),
    'GET /api/v1/audit-records': () => json(200, page([])),
  };
}

describe('drafting a refund from its source (US-34, BR-307, ADR 0041 point 2)', () => {
  it('from a payment: shows what it holds, refuses more, and sends the draft with a key', async () => {
    const api = routedApi({ ...refundRoutes(), 'POST /api/v1/refunds': () => json(201, draft) });
    const { user, router } = renderApp(`/refunds/new?paymentId=${cash.id}`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Payment PAY/26-27/000001' })).toHaveAttribute(
      'href',
      `/payments/${cash.id}`,
    );
    expect(screen.getByText('₹600.00')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Amount (₹)'), '600.01');
    await user.selectOptions(screen.getByLabelText('Method'), 'UPI');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('The amount is not more than what the source holds, ₹600.00.')).toBeInTheDocument();
    expect(
      screen.getByText('Enter the reference: the UPI id, card authorisation, UTR or cheque number.'),
    ).toBeInTheDocument();
    expect(screen.getByText('Give the reason for the refund.')).toBeInTheDocument();

    const amount = screen.getByLabelText('Amount (₹)');
    await user.clear(amount);
    await user.type(amount, '200');
    await user.type(screen.getByLabelText('Reference'), 'asha@upi');
    await user.type(screen.getByLabelText('Reason'), 'Paid twice at the counter');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/refunds/${draft.id}`));
    const request = api.sent('POST', '/api/v1/refunds')[0];
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
    expect(await request?.json()).toEqual({
      paymentId: cash.id,
      creditNoteId: null,
      refundDate: todayInIndia(),
      amount: 200,
      method: 'UPI',
      reference: 'asha@upi',
      reason: 'Paid twice at the counter',
    });
  });

  it('from a credit note: names it as the source', async () => {
    const fromCredit = refund({ sourceKind: 'CreditNote', paymentId: null, creditNoteId });
    const api = routedApi({
      [`GET /api/v1/credit-notes/${creditNoteId}`]: () =>
        json(200, {
          id: creditNoteId,
          number: 'CRN/26-27/000001',
          creditNoteDate: '2026-11-18',
          payerId: ashasPayer.id,
          availableAmount: 120,
          status: 'Posted',
        }),
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
      'POST /api/v1/refunds': () => json(201, fromCredit),
      [`GET ${one}`]: () => json(200, fromCredit),
      'GET /api/v1/audit-records': () => json(200, page([])),
    });
    const { user } = renderApp(`/refunds/new?creditNoteId=${creditNoteId}`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('Credit note CRN/26-27/000001')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Amount (₹)'), '120');
    await user.selectOptions(screen.getByLabelText('Method'), 'Cash');
    await user.type(screen.getByLabelText('Reason'), 'Overcharged');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => expect(api.sent('POST', '/api/v1/refunds')).toHaveLength(1));
    expect(await api.sent('POST', '/api/v1/refunds')[0]?.json()).toMatchObject({
      paymentId: null,
      creditNoteId,
      amount: 120,
      method: 'Cash',
      reference: null,
    });
  });
});

describe('a refund', () => {
  it('is submitted for approval with its version and a key, and then waits', async () => {
    const waiting = refund({ status: 'PendingApproval', version: 'AAAAAAAACgE=' });
    const api = routedApi({ ...refundRoutes(), [`POST ${one}/post`]: () => json(202, waiting) });
    const { user } = renderApp(`/refunds/${draft.id}`, { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Submit for approval' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: submit the refund' }));

    expect(await screen.findByText('Submitted: the refund waits', { exact: false })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Refund waiting for approval' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
    const request = api.sent('POST', `${one}/post`)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAACgA="');
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
  });

  it("shows the API's refusal of a refund that no longer fits its source", async () => {
    const api = routedApi({
      ...refundRoutes(),
      [`POST ${one}/post`]: () =>
        json(422, problem(422, 'refund_exceeds_available', 'The refund is more than its source holds now.')),
    });
    const { user } = renderApp(`/refunds/${draft.id}`, { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Submit for approval' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: submit the refund' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('The refund is more than its source holds now.');
  });

  it('is voided with a reason', async () => {
    const voided = refund({
      status: 'Voided',
      voidReason: 'Paid in cash instead',
      voidedAtUtc: '2026-11-22T06:00:00+00:00',
    });
    const api = routedApi({ ...refundRoutes(), [`POST ${one}/void`]: () => json(200, voided) });
    const { user } = renderApp(`/refunds/${draft.id}`, { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Void draft' }));
    await user.type(screen.getByLabelText('Why is the draft refund voided?'), 'Paid in cash instead');
    await user.click(screen.getByRole('button', { name: 'Confirm: void the draft' }));

    expect(await screen.findByText('The draft refund is voided.')).toBeInTheDocument();
    expect(await api.sent('POST', `${one}/void`)[0]?.json()).toEqual({ reason: 'Paid in cash instead' });
  });

  it('is corrected with every field but its source, and the version it started from', async () => {
    const api = routedApi({
      ...refundRoutes(),
      [`PUT ${one}`]: () => json(200, refund({ amount: 250, version: 'AAAAAAAACgE=' })),
    });
    const { user, router } = renderApp(`/refunds/${draft.id}/edit`, { session: cashier, fetch: api.fetch });

    const amount = await screen.findByLabelText('Amount (₹)');
    expect(amount).toHaveValue('200.00');
    await user.clear(amount);
    await user.type(amount, '250');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/refunds/${draft.id}`));
    const request = api.sent('PUT', one)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAACgA="');
    expect(await request?.json()).toEqual({
      refundDate: '2026-11-22',
      amount: 250,
      method: 'UPI',
      reference: 'asha@upi',
      reason: 'Paid twice at the counter',
    });
  });

  it('offers a viewer nothing, and is not shown to a billing clerk at all', async () => {
    const api = routedApi(refundRoutes());
    const { unmount } = renderApp(`/refunds/${draft.id}`, { session: sessionFor(['Viewer']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Draft refund' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument();
    unmount();

    renderApp(`/refunds/${draft.id}`, { session: sessionFor(['BillingClerk']), fetch: api.fetch });
    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
  });
});
