import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { todayInIndia } from '../format';
import { inTurn, json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import {
  adjustment,
  bill,
  billLine,
  billSummary,
  creditApplication,
  creditNote,
  creditNoteLine,
  page,
  payer,
  problem,
} from '../test/records';

const clerk = sessionFor(['BillingClerk']);
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const ashasPayer = payer();

/** A posted bill of two lines: the blood count (1,049.48) and a consultation (590.00). */
const consultation = billLine({
  id: 'd4000000-0000-4000-8000-000000000002',
  lineNo: 2,
  serviceItemId: null,
  description: 'Consultation',
  quantity: 1,
  unitPrice: 500,
  lineAmount: 500,
  discountAmount: 0,
  taxRatePercent: 18,
  taxAmount: 90,
  lineTotal: 590,
});
const posted = bill({
  status: 'Posted',
  number: 'BIL/26-27/000001',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-11-20T06:00:00+00:00',
  lines: [billLine(), consultation],
  subTotal: 1500,
  taxTotal: 139.98,
  total: 1639.48,
  balanceDue: 1639.48,
  version: 'AAAAAAAACAI=',
});
const draft = creditNote();
const one = `/api/v1/credit-notes/${draft.id}`;

/** What a credit note's page reads: the credit note, its bill, its payer and its trail. */
function creditNoteRoutes(current: Handler = () => json(200, draft)): Record<string, Handler> {
  return {
    [`GET ${one}`]: current,
    [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted),
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    'GET /api/v1/audit-records': () => json(200, page([])),
  };
}

describe('drafting a credit note (US-24, BR-209 to BR-211)', () => {
  it('credits a quantity of a line of the bill and a free-text line, and sends them with a key', async () => {
    const api = routedApi({ ...creditNoteRoutes(), 'POST /api/v1/credit-notes': () => json(201, draft) });
    const { user, router } = renderApp(`/credit-notes/new?billId=${posted.id}`, { session: clerk, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('A line of the bill'), billLine().id);
    await user.click(screen.getByRole('button', { name: 'Credit this line' }));
    const quantity = screen.getByLabelText('Quantity to credit');
    expect(quantity).toHaveValue('3');

    await user.clear(quantity);
    await user.type(quantity, '4');
    await user.type(screen.getByLabelText('Reason'), 'Test not done');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('Not more than the 3 billed.')).toBeInTheDocument();

    await user.clear(quantity);
    await user.type(quantity, '1');
    await user.click(screen.getByRole('button', { name: 'Add a free-text line' }));
    await user.type(screen.getByLabelText('Line 2: description'), 'Goodwill');
    await user.type(screen.getByLabelText('Unit price (₹)'), '100');
    // 1 x 333.3333 = 333.33, less 0.50, with 5% tax 16.64: 349.47; and 100.00.
    expect(screen.getByLabelText('Line 1 total')).toHaveTextContent('₹349.47');
    expect(within(screen.getByLabelText('Estimated totals')).getByText('₹449.47')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/credit-notes/${draft.id}`));
    const sent = api.sent('POST', '/api/v1/credit-notes')[0];
    expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
    expect(await sent?.json()).toEqual({
      billId: posted.id,
      creditNoteDate: todayInIndia(),
      reason: 'Test not done',
      lines: [
        {
          billLineId: billLine().id,
          description: null,
          quantity: 1,
          unitPrice: null,
          discountAmount: 0.5,
          taxRatePercent: null,
        },
        {
          billLineId: null,
          description: 'Goodwill',
          quantity: 1,
          unitPrice: 100,
          discountAmount: 0,
          taxRatePercent: 0,
        },
      ],
    });
  });

  it('refuses a line above its bill line and a total above the bill, before sending', async () => {
    const api = routedApi(creditNoteRoutes());
    const { user } = renderApp(`/credit-notes/new?billId=${posted.id}`, { session: clerk, fetch: api.fetch });

    // The whole blood count without its discount comes to 1,050.00, a rupee and more above the line's 1,049.48.
    await user.selectOptions(await screen.findByLabelText('A line of the bill'), billLine().id);
    await user.click(screen.getByRole('button', { name: 'Credit this line' }));
    await user.clear(screen.getByLabelText('Discount (₹)'));
    await user.type(screen.getByLabelText('Reason'), 'Test not done');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText("The line comes to more than the bill's line, ₹1,049.48.")).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Remove' }));
    await user.click(screen.getByRole('button', { name: 'Add a free-text line' }));
    await user.type(screen.getByLabelText('Line 1: description'), 'Goodwill');
    await user.type(screen.getByLabelText('Unit price (₹)'), '1639.49');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('The credit note comes to more than the bill, ₹1,639.48.')).toBeInTheDocument();

    expect(api.sent('POST', '/api/v1/credit-notes')).toHaveLength(0);
  });

  it("puts the API's refusal of a line at that line, and only there", async () => {
    const refusal = "The credit notes of this line come to more than the bill's line.";
    const api = routedApi({
      ...creditNoteRoutes(),
      'POST /api/v1/credit-notes': () =>
        json(
          422,
          problem(422, 'credit_note_line_exceeds_bill_line', 'A line is credited above its bill line.', {
            'lines[0].quantity': [refusal],
          }),
        ),
    });
    const { user } = renderApp(`/credit-notes/new?billId=${posted.id}`, { session: clerk, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('A line of the bill'), consultation.id);
    await user.click(screen.getByRole('button', { name: 'Credit this line' }));
    await user.type(screen.getByLabelText('Reason'), 'Not seen');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    expect(await screen.findByText('A line is credited above its bill line.')).toBeInTheDocument();
    expect(screen.getAllByText(refusal)).toHaveLength(1);
    expect(screen.getByLabelText('Quantity to credit')).toHaveAttribute('aria-invalid', 'true');
  });

  it('corrects a draft: the date and reason, then the lines, each with the version before it', async () => {
    const patched = creditNote({ reason: 'Test not done: sample lost', version: 'AAAAAAAACwE=' });
    const api = routedApi({
      ...creditNoteRoutes(),
      [`PATCH ${one}`]: () => json(200, patched),
      [`PUT ${one}/lines`]: () =>
        json(200, { ...patched, lines: [creditNoteLine({ quantity: 2 })], version: 'AAAAAAAACwI=' }),
    });
    const { user, router } = renderApp(`/credit-notes/${draft.id}/edit`, { session: clerk, fetch: api.fetch });

    const reason = await screen.findByLabelText('Reason');
    await user.clear(reason);
    await user.type(reason, 'Test not done: sample lost');
    const quantity = screen.getByLabelText('Quantity to credit');
    await user.clear(quantity);
    await user.type(quantity, '2');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/credit-notes/${draft.id}`));
    const header = api.sent('PATCH', one)[0];
    expect(header?.headers.get('If-Match')).toBe('"AAAAAAAACwA="');
    expect(await header?.json()).toEqual({
      creditNoteDate: draft.creditNoteDate,
      reason: 'Test not done: sample lost',
    });
    const lines = api.sent('PUT', `${one}/lines`)[0];
    expect(lines?.headers.get('If-Match')).toBe('"AAAAAAAACwE="');
    expect((await lines?.json()) as unknown).toMatchObject({ lines: [{ billLineId: billLine().id, quantity: 2 }] });
  });
});

describe('a credit note', () => {
  it('offers its maker the draft actions, submits it for approval with its version and a key, and then waits', async () => {
    const waiting = creditNote({ status: 'PendingApproval', version: 'AAAAAAAACwE=' });
    const api = routedApi({ ...creditNoteRoutes(), [`POST ${one}/post`]: () => json(202, waiting) });
    const { user } = renderApp(`/credit-notes/${draft.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Draft credit note' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Edit' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Void draft' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'BIL/26-27/000001' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Submit for approval' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: submit the credit note' }));

    expect(await screen.findByText('Submitted: the credit note waits', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('This credit note waits for approval', { exact: false })).toBeInTheDocument();
    const sent = api.sent('POST', `${one}/post`)[0];
    expect(sent?.headers.get('If-Match')).toBe('"AAAAAAAACwA="');
    expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
  });

  it('is read by a cashier, who changes nothing of it', async () => {
    const api = routedApi(creditNoteRoutes());
    renderApp(`/credit-notes/${draft.id}`, { session: sessionFor(['Cashier']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Draft credit note' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument();
  });

  it('once posted, offers its credit to bills and to a refund, and asks the reversal of a later application only', async () => {
    const later = creditApplication({
      id: 'f9100000-0000-4000-8000-000000000002',
      billId: posted.id,
      amount: 100,
      appliedAtPosting: false,
      allocatedByUserId: clerk.userId,
    });
    const postedNote = creditNote({
      status: 'Posted',
      number: 'CN/26-27/000001',
      fiscalYear: '2026-27',
      availableAmount: 649.48,
      applications: [creditApplication({ billId: posted.id }), later],
    });
    const api = routedApi({
      ...creditNoteRoutes(() => json(200, postedNote)),
      [`POST ${one}/applications/${later.id}/reverse`]: () =>
        json(202, {
          ...postedNote,
          applications: [postedNote.applications[0], { ...later, status: 'PendingReversal' }],
        }),
    });
    const { user, unmount } = renderApp(`/credit-notes/${draft.id}`, {
      session: sessionFor(['BillingClerk', 'Cashier']),
      fetch: api.fetch,
    });

    expect(await screen.findByRole('heading', { name: 'CN/26-27/000001' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Apply credit to bills' })).toHaveAttribute(
      'href',
      `/credit-notes/${draft.id}/apply`,
    );
    expect(screen.getByRole('link', { name: 'Draft a refund' })).toHaveAttribute(
      'href',
      `/refunds/new?creditNoteId=${draft.id}`,
    );
    expect(screen.getByText('Made by the posting; not reversed.')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Request reversal' })).toHaveLength(1);

    await user.click(screen.getByRole('button', { name: 'Request reversal' }));
    await user.type(screen.getByLabelText(/Why should this application of credit be reversed/), 'Wrong bill');
    await user.click(screen.getByRole('button', { name: 'Ask for the reversal' }));

    expect(await screen.findByText('The reversal is asked for', { exact: false })).toBeInTheDocument();
    const sent = api.sent('POST', `${one}/applications/${later.id}/reverse`)[0];
    expect(await sent?.json()).toEqual({ reason: 'Wrong bill' });
    expect(sent?.headers.get('If-Match')).toBe(`"${postedNote.version}"`);
    expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
    unmount();

    // A viewer reads it, with no action.
    renderApp(`/credit-notes/${draft.id}`, { session: sessionFor(['Viewer']), fetch: api.fetch });
    expect(await screen.findByRole('heading', { name: 'CN/26-27/000001' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Apply credit to bills' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Request reversal' })).not.toBeInTheDocument();
  });

  it("applies its credit to the payer's bills oldest first, with its version and a key", async () => {
    const postedNote = creditNote({ status: 'Posted', number: 'CN/26-27/000001', availableAmount: 1200 });
    const older = billSummary();
    const newer = billSummary({
      id: 'e5000000-0000-4000-8000-000000000012',
      number: 'BIL/26-27/000012',
      billDate: '2026-11-15',
    });
    const api = routedApi({
      ...creditNoteRoutes(() => json(200, postedNote)),
      'GET /api/v1/bills': (request) =>
        json(200, page(new URL(request.url).searchParams.get('Status') === 'Posted' ? [newer, older] : [])),
      [`POST ${one}/applications`]: () => json(200, postedNote),
    });
    const { user, router } = renderApp(`/credit-notes/${draft.id}/apply`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Apply oldest first' }));
    expect(screen.getByLabelText('Amount to apply to BIL/26-27/000011')).toHaveValue('1049.48');
    expect(screen.getByLabelText('Amount to apply to BIL/26-27/000012')).toHaveValue('150.52');
    await user.click(screen.getByRole('button', { name: 'Apply credit' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/credit-notes/${draft.id}`));
    const sent = api.sent('POST', `${one}/applications`)[0];
    expect(await sent?.json()).toEqual({
      applications: [
        { billId: older.id, amount: 1049.48 },
        { billId: newer.id, amount: 150.52 },
      ],
    });
    expect(sent?.headers.get('If-Match')).toBe(`"${postedNote.version}"`);
    expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
  });
});

describe("a posted bill's corrections", () => {
  const billRoutes: Record<string, Handler> = {
    [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted),
    [`GET /api/v1/patients/${posted.patientId}`]: () => json(404, problem(404, 'patient_not_found', 'Not found.')),
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    'GET /api/v1/audit-records': () => json(200, page([])),
    'GET /api/v1/credit-notes': () => json(200, page([creditNote({ status: 'PendingApproval' })])),
    'GET /api/v1/adjustments': () => json(200, page([adjustment({ status: 'Posted', number: 'ADJ/26-27/000001' })])),
  };

  it('offers a clerk a credit note and an adjustment, and lists both kinds', async () => {
    const api = routedApi(billRoutes);
    renderApp(`/bills/${posted.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Draft a credit note' })).toHaveAttribute(
      'href',
      `/credit-notes/new?billId=${posted.id}`,
    );
    expect(screen.getByRole('link', { name: 'Draft an adjustment' })).toHaveAttribute(
      'href',
      `/adjustments/new?billId=${posted.id}`,
    );
    const creditNotes = await screen.findByRole('list', { name: 'Credit notes of the bill' });
    expect(within(creditNotes).getByText('Waiting for approval')).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'ADJ/26-27/000001' })).toBeInTheDocument();
  });

  it('offers a cashier neither, and never asks for adjustments, which a cashier may not read', async () => {
    const api = routedApi(billRoutes);
    renderApp(`/bills/${posted.id}`, { session: sessionFor(['Cashier']), fetch: api.fetch });

    expect(await screen.findByRole('list', { name: 'Credit notes of the bill' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Draft a credit note' })).not.toBeInTheDocument();
    expect(api.sent('GET', '/api/v1/adjustments')).toHaveLength(0);
  });

  it('offers no adjustment of a settled bill, which owes nothing', async () => {
    const settled = { ...posted, status: 'Settled' as const, balanceDue: 0 };
    const api = routedApi({ ...billRoutes, [`GET /api/v1/bills/${posted.id}`]: inTurn(() => json(200, settled)) });
    renderApp(`/bills/${posted.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('link', { name: 'Draft a credit note' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Draft an adjustment' })).not.toBeInTheDocument();
  });
});
