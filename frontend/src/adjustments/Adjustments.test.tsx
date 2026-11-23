import { screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { todayInIndia } from '../format';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { adjustment, bill, page, payer, problem } from '../test/records';

const clerk = sessionFor(['BillingClerk']);
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const ashasPayer = payer();
const posted = bill({
  status: 'PartiallyPaid',
  number: 'BIL/26-27/000001',
  balanceDue: 549.48,
  version: 'AAAAAAAACAI=',
});
const draft = adjustment();
const one = `/api/v1/adjustments/${draft.id}`;

/** What an adjustment's page and form read: the adjustment, its bill, its payer and its trail. */
function adjustmentRoutes(current: Handler = () => json(200, draft)): Record<string, Handler> {
  return {
    [`GET ${one}`]: current,
    [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted),
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    'GET /api/v1/audit-records': () => json(200, page([])),
  };
}

describe('drafting an adjustment (BR-501 to BR-503)', () => {
  it('refuses a rounding of a rupee or more, and sends one below it with a key', async () => {
    const rounding = adjustment({ kind: 'Rounding', amount: 0.99 });
    const api = routedApi({ ...adjustmentRoutes(), 'POST /api/v1/adjustments': () => json(201, rounding) });
    const { user, router } = renderApp(`/adjustments/new?billId=${posted.id}`, { session: clerk, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('Kind'), 'Rounding');
    await user.type(screen.getByLabelText('Amount (₹)'), '1.00');
    await user.type(screen.getByLabelText('Reason'), 'Paise rounded off');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('A rounding is less than ₹1.00.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/adjustments')).toHaveLength(0);

    await user.clear(screen.getByLabelText('Amount (₹)'));
    await user.type(screen.getByLabelText('Amount (₹)'), '0.99');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/adjustments/${draft.id}`));
    const sent = api.sent('POST', '/api/v1/adjustments')[0];
    expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
    expect(await sent?.json()).toEqual({
      billId: posted.id,
      adjustmentDate: todayInIndia(),
      kind: 'Rounding',
      amount: 0.99,
      reason: 'Paise rounded off',
    });
  });

  it('refuses an amount above what the bill still owes, to the paisa, before sending', async () => {
    const api = routedApi(adjustmentRoutes());
    const { user } = renderApp(`/adjustments/new?billId=${posted.id}`, { session: clerk, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('Kind'), 'WriteOff');
    await user.type(screen.getByLabelText('Amount (₹)'), '549.49');
    await user.type(screen.getByLabelText('Reason'), 'Patient deceased');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    expect(await screen.findByText("The amount is not more than the bill's balance due, ₹549.48.")).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/adjustments')).toHaveLength(0);
  });

  it('corrects a draft as a whole, with the version it started from', async () => {
    const api = routedApi({
      ...adjustmentRoutes(),
      [`PUT ${one}`]: () => json(200, adjustment({ amount: 40, version: 'AAAAAAAADAE=' })),
    });
    const { user, router } = renderApp(`/adjustments/${draft.id}/edit`, { session: clerk, fetch: api.fetch });

    const amount = await screen.findByLabelText('Amount (₹)');
    await user.clear(amount);
    await user.type(amount, '40');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/adjustments/${draft.id}`));
    const sent = api.sent('PUT', one)[0];
    expect(sent?.headers.get('If-Match')).toBe('"AAAAAAAADAA="');
    expect(await sent?.json()).toEqual({
      adjustmentDate: draft.adjustmentDate,
      kind: 'Discount',
      amount: 40,
      reason: 'Staff discount',
    });
  });
});

describe('an adjustment', () => {
  it('is submitted for approval with its version and a key, and then waits', async () => {
    const waiting = adjustment({ status: 'PendingApproval', version: 'AAAAAAAADAE=' });
    const api = routedApi({ ...adjustmentRoutes(), [`POST ${one}/post`]: () => json(202, waiting) });
    const { user } = renderApp(`/adjustments/${draft.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Draft adjustment' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Submit for approval' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: submit the adjustment' }));

    expect(await screen.findByText('Submitted: the adjustment waits', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('This adjustment waits for approval', { exact: false })).toBeInTheDocument();
    const sent = api.sent('POST', `${one}/post`)[0];
    expect(sent?.headers.get('If-Match')).toBe('"AAAAAAAADAA="');
    expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
  });

  it("shows the API's refusal of one that no longer fits the bill", async () => {
    const api = routedApi({
      ...adjustmentRoutes(),
      [`POST ${one}/post`]: () =>
        json(422, problem(422, 'adjustment_exceeds_balance_due', 'The adjustment is more than the bill owes.')),
    });
    const { user } = renderApp(`/adjustments/${draft.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Submit for approval' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: submit the adjustment' }));

    expect(await screen.findByText('The adjustment is more than the bill owes.')).toBeInTheDocument();
  });

  it('is voided with a reason', async () => {
    const api = routedApi({
      ...adjustmentRoutes(),
      [`POST ${one}/void`]: () => json(200, adjustment({ status: 'Voided', voidReason: 'Raised twice' })),
    });
    const { user } = renderApp(`/adjustments/${draft.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Void draft' }));
    await user.type(screen.getByLabelText('Why is the draft adjustment voided?'), 'Raised twice');
    await user.click(screen.getByRole('button', { name: 'Confirm: void the draft' }));

    expect(await screen.findByText('The draft adjustment is voided.')).toBeInTheDocument();
    expect(await api.sent('POST', `${one}/void`)[0]?.json()).toEqual({ reason: 'Raised twice' });
  });

  it('is closed to a cashier, and read by a viewer without an action', async () => {
    const api = routedApi(adjustmentRoutes(() => json(200, adjustment({ status: 'PendingApproval' }))));
    const { unmount } = renderApp('/adjustments', { session: sessionFor(['Cashier']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
    unmount();

    renderApp(`/adjustments/${draft.id}`, { session: sessionFor(['Viewer']), fetch: api.fetch });
    expect(await screen.findByRole('heading', { name: 'Adjustment waiting for approval' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Review it in Approvals' })).not.toBeInTheDocument();
  });

  it('while it waits, points a checker to the inbox', async () => {
    const api = routedApi(adjustmentRoutes(() => json(200, adjustment({ status: 'PendingApproval' }))));
    renderApp(`/adjustments/${draft.id}`, { session: sessionFor(['Supervisor']), fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Review it in Approvals' })).toHaveAttribute(
      'href',
      '/approvals?documentType=Adjustment',
    );
  });
});
