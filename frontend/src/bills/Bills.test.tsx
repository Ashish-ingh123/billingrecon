import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { todayInIndia } from '../format';
import { inTurn, json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { bill, billLine, page, patient, payer, problem, serviceItem } from '../test/records';

const asha = patient();
const ashasPayer = payer();
const draft = bill();
const one = `/api/v1/bills/${draft.id}`;
const clerk = sessionFor(['BillingClerk']);
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** A record of the bill's trail, by the signed-in user unless another is named. */
function trailRecord(id: number, action: string, userId: string | null = clerk.userId) {
  return {
    id,
    occurredAtUtc: '2026-11-20T05:00:00+00:00',
    userId,
    correlationId: `c0000000-0000-4000-8000-00000000000${id}`,
    entityType: 'Bill',
    entityId: draft.id,
    documentNumber: null,
    action,
    before: null,
    after: { Status: 'Draft' },
    reason: null,
  };
}

/** What a bill's page reads: the bill, its patient and payer, and its trail. */
function billRoutes(current: Handler = () => json(200, draft)): Record<string, Handler> {
  return {
    [`GET ${one}`]: current,
    [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
    [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    'GET /api/v1/audit-records': () => json(200, page([trailRecord(1, 'Drafted')])),
  };
}

describe('the list of bills (US-23)', () => {
  it('sends the filters it keeps in the address, and names each bill by its number, patient and payer', async () => {
    const api = routedApi({
      'GET /api/v1/bills': () =>
        json(
          200,
          page([
            draft,
            bill({ id: 'e5000000-0000-4000-8000-000000000002', number: 'BIL/26-27/000001', status: 'Posted' }),
          ]),
        ),
      [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
    });
    renderApp(`/bills?patientId=${asha.id}&status=Draft&from=2026-11-01&to=2026-11-30`, {
      session: clerk,
      fetch: api.fetch,
    });

    expect(await screen.findByRole('link', { name: 'BIL/26-27/000001' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'No number' })).toHaveAttribute('href', `/bills/${draft.id}`);
    expect(await screen.findByText('The bills of', { exact: false })).toHaveTextContent('The bills of Asha Verma.');
    const query = Object.fromEntries(new URL(api.sent('GET', '/api/v1/bills')[0]?.url ?? '').searchParams);
    expect(query).toEqual({
      Status: 'Draft',
      BillDateFrom: '2026-11-01',
      BillDateTo: '2026-11-30',
      PatientId: asha.id,
      Page: '1',
      PageSize: '20',
    });
    expect(screen.getAllByText('1,049.48')).toHaveLength(4);
    expect(screen.getByRole('link', { name: 'New bill' })).toBeInTheDocument();
  });
});

describe('a bill (US-20, US-21)', () => {
  it("shows a draft's lines, the server's totals, its trail, and to a writer what can be done with it", async () => {
    const api = routedApi(billRoutes());
    renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Draft bill' })).toBeInTheDocument();
    const lines = within(screen.getByRole('region', { name: 'Lines' }));
    expect(lines.getByText('Complete blood count')).toBeInTheDocument();
    expect(lines.getAllByText('₹1,049.48')).toHaveLength(2);
    expect(lines.getByText('₹49.98')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Edit' })).toHaveAttribute('href', `/bills/${draft.id}/edit`);
    expect(screen.getByRole('button', { name: 'Post bill' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Void draft' })).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Asha Verma (MRN MRN-1001)' })).toBeInTheDocument();

    const trail = within(screen.getByRole('region', { name: 'Audit trail' }));
    expect(await trail.findByText('Drafted')).toBeInTheDocument();
    expect(trail.getByText('You')).toBeInTheDocument();
    const query = new URL(api.sent('GET', '/api/v1/audit-records')[0]?.url ?? '').searchParams;
    expect(query.get('EntityType')).toBe('Bill');
    expect(query.get('EntityId')).toBe(draft.id);
  });

  it("shows the server's figures in the totals card, the balance under the total, and the history as a timeline (ADR 0049)", async () => {
    const api = routedApi(billRoutes());
    renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    const totals = await screen.findByLabelText('Totals of the bill');
    expect(totals).toHaveTextContent('Subtotal₹1,000.00Discounts₹0.50Tax₹49.98Total₹1,049.48Balance due₹1,049.48');
    const trail = within(screen.getByRole('region', { name: 'Audit trail' }));
    expect(await trail.findByRole('list', { name: 'Changes, oldest first' })).toBeInTheDocument();
    expect(trail.getByRole('listitem')).toHaveTextContent('Drafted');
  });

  it('offers nothing to a reader, and nothing on a posted bill', async () => {
    const posted = bill({ status: 'Posted', number: 'BIL/26-27/000001', fiscalYear: '2026-27' });
    const api = routedApi(billRoutes(() => json(200, posted)));
    renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'BIL/26-27/000001' })).toBeInTheDocument();
    expect(screen.getByText('Fiscal year 2026-27', { exact: false })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Post bill' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument();
  });

  it('shows a reader a draft without its actions', async () => {
    const api = routedApi(billRoutes());
    renderApp(`/bills/${draft.id}`, { session: sessionFor(['Viewer']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'Draft bill' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Post bill' })).not.toBeInTheDocument();
  });

  it('is posted after a confirmation, with its version and a new key, and shows its number', async () => {
    const posted = bill({ status: 'Posted', number: 'BIL/26-27/000001', version: 'AAAAAAAACAE=' });
    const api = routedApi({ ...billRoutes(), [`POST ${one}/post`]: () => json(200, posted) });
    const { user } = renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Post bill' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: post the bill' }));

    expect(await screen.findByText('Posted as BIL/26-27/000001.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'BIL/26-27/000001' })).toBeInTheDocument();
    const request = api.sent('POST', `${one}/post`)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAACAA="');
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
  });

  it('says so when the bill is sent for approval instead (202)', async () => {
    const waiting = bill({ status: 'PendingApproval', version: 'AAAAAAAACAE=' });
    const api = routedApi({ ...billRoutes(), [`POST ${one}/post`]: () => json(202, waiting) });
    const { user } = renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Post bill' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: post the bill' }));

    expect(await screen.findByText('Sent for approval', { exact: false })).toBeInTheDocument();
    expect(screen.getByText('This bill waits for approval', { exact: false })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Bill waiting for approval' })).toBeInTheDocument();
  });

  it('sends the same key again after no answer and while the first is still running, and finishes once', async () => {
    const posted = bill({ status: 'Posted', number: 'BIL/26-27/000002', version: 'AAAAAAAACAE=' });
    const api = routedApi({
      ...billRoutes(),
      [`POST ${one}/post`]: inTurn(
        () => {
          throw new TypeError('Failed to fetch');
        },
        () => json(409, problem(409, 'idempotency_in_progress', 'The first request with this key is still running.')),
        () => json(200, posted),
      ),
    });
    const { user } = renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Post bill' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: post the bill' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The server could not be reached.');

    await user.click(screen.getByRole('button', { name: 'Send it again' }));
    expect(await screen.findByText('The first request with this key is still running.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Send it again' }));
    expect(await screen.findByText('Posted as BIL/26-27/000002.')).toBeInTheDocument();

    const keys = api.sent('POST', `${one}/post`).map((request) => request.headers.get('Idempotency-Key'));
    expect(keys).toHaveLength(3);
    expect(new Set(keys).size).toBe(1);
  });

  it("shows the API's refusal, and a new attempt after it is a new operation with a new key", async () => {
    const api = routedApi({
      ...billRoutes(),
      [`POST ${one}/post`]: () =>
        json(422, problem(422, 'bill_has_no_lines', 'A bill needs at least one line to be posted.')),
    });
    const { user } = renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    for (let attempt = 0; attempt < 2; attempt += 1) {
      await user.click(await screen.findByRole('button', { name: 'Post bill' }));
      await user.click(screen.getByRole('button', { name: 'Confirm: post the bill' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('A bill needs at least one line to be posted.');
      expect(screen.queryByRole('button', { name: 'Send it again' })).not.toBeInTheDocument();
    }

    const [first, second] = api.sent('POST', `${one}/post`).map((request) => request.headers.get('Idempotency-Key'));
    expect(first).not.toBe(second);
  });

  it('shows what changed when someone else changed the draft first, and posts nothing', async () => {
    const changed = bill({ notes: 'Corrected by the ward', version: 'AAAAAAAACAE=' });
    const api = routedApi({
      ...billRoutes(
        inTurn(
          () => json(200, draft),
          () => json(200, changed),
        ),
      ),
      [`POST ${one}/post`]: () =>
        json(409, problem(409, 'concurrency_conflict', 'The record was changed by someone else.')),
    });
    const { user } = renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Post bill' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: post the bill' }));

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Someone else changed this bill before you could post it.');
    expect(notice).toHaveTextContent('Notes: was (empty), now Corrected by the ward');
  });

  it('is voided with the reason the user gives, which is required', async () => {
    const voided = bill({ status: 'Voided', voidReason: 'Raised twice', voidedAtUtc: '2026-11-20T06:00:00+00:00' });
    const api = routedApi({ ...billRoutes(), [`POST ${one}/void`]: () => json(200, voided) });
    const { user } = renderApp(`/bills/${draft.id}`, { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Void draft' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: void the draft' }));
    expect(screen.getByText('Give the reason for voiding the draft.')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Why is the draft voided?'), 'Raised twice');
    await user.click(screen.getByRole('button', { name: 'Confirm: void the draft' }));

    expect(await screen.findByText('The draft is voided.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Voided draft' })).toBeInTheDocument();
    expect(screen.getByText('Raised twice', { exact: false })).toBeInTheDocument();
    const request = api.sent('POST', `${one}/void`)[0];
    expect(await request?.json()).toEqual({ reason: 'Raised twice' });
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAACAA="');
  });
});

describe('a new bill (US-20, BR-201 to BR-203)', () => {
  const cbc = serviceItem();
  const created = bill({ id: 'e5000000-0000-4000-8000-000000000009' });

  function newBillRoutes(create: Handler): Record<string, Handler> {
    return {
      'GET /api/v1/patients': () => json(200, page([asha])),
      'GET /api/v1/payers': () => json(200, page([ashasPayer])),
      'GET /api/v1/service-items': () => json(200, page([cbc])),
      'POST /api/v1/bills': create,
      [`GET /api/v1/bills/${created.id}`]: () => json(200, created),
      [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
      [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
      'GET /api/v1/audit-records': () => json(200, page([])),
    };
  }

  async function fillDraft(user: ReturnType<typeof renderApp>['user']) {
    // The results open while the text is typed (Prompt 10.2b): by the pointer here, by the keyboard for the line.
    await user.type(await screen.findByLabelText('Patient'), 'asha');
    await user.click(await screen.findByRole('option', { name: 'Asha Verma (MRN MRN-1001)' }));
    // The patient's own payer owes the bill unless the user chooses another.
    expect(await screen.findByText("Asha Verma (Self-pay, a patient's own payer)")).toBeInTheDocument();

    await user.type(screen.getByLabelText('Add a line from the catalogue'), 'cbc');
    await screen.findByRole('option', { name: /^LAB\/CBC: Complete blood count/ });
    await user.keyboard('{ArrowDown}{Enter}');
    expect(screen.getByLabelText('Line 1: description')).toHaveValue('Complete blood count');

    await user.click(screen.getByRole('button', { name: 'Add a free-text line' }));
    await user.type(screen.getByLabelText('Line 2: description'), 'Dressing');
    const quantity = screen.getAllByLabelText('Quantity')[1];
    if (quantity === undefined) {
      throw new Error('The second line has no quantity.');
    }

    await user.clear(quantity);
    await user.type(quantity, '2');
    await user.type(screen.getAllByLabelText('Unit price (₹)')[1] ?? quantity, '125.50');
    const rate = screen.getAllByLabelText('Tax rate (%)')[1] ?? quantity;
    await user.clear(rate);
    await user.type(rate, '12');
  }

  it('takes the patient, their payer, a catalogue line and a free-text line, shows the estimate, and creates the draft', async () => {
    const api = routedApi(newBillRoutes(() => json(201, created)));
    const { user, router } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });

    await fillDraft(user);

    // 1 x 450 untaxed, and 2 x 125.50 at 12% (251.00 + 30.12): 731.12, as the server will compute it.
    expect(screen.getByLabelText('Line 2 total')).toHaveTextContent('₹281.12');
    expect(screen.getByLabelText('Estimated totals')).toHaveTextContent('Total₹731.12');

    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/bills/${created.id}`));
    const request = api.sent('POST', '/api/v1/bills')[0];
    expect(request?.headers.get('Idempotency-Key')).toMatch(uuid);
    expect(await request?.json()).toEqual({
      patientId: asha.id,
      payerId: ashasPayer.id,
      billDate: todayInIndia(),
      dueDate: null,
      encounterReference: null,
      notes: null,
      lines: [
        {
          serviceItemId: cbc.id,
          description: 'Complete blood count',
          quantity: 1,
          unitPrice: 450,
          discountAmount: 0,
          taxRatePercent: 0,
        },
        {
          serviceItemId: null,
          description: 'Dressing',
          quantity: 2,
          unitPrice: 125.5,
          discountAmount: 0,
          taxRatePercent: 12,
        },
      ],
    });
  });

  it('sends the same key when saved again unchanged after no answer, and a new one once the draft changed', async () => {
    const api = routedApi(
      newBillRoutes(
        inTurn(
          () => {
            throw new TypeError('Failed to fetch');
          },
          () => {
            throw new TypeError('Failed to fetch');
          },
          () => json(201, created),
        ),
      ),
    );
    const { user, router } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    await fillDraft(user);

    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('Nothing is done twice', { exact: false })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    await screen.findByRole('alert');

    await user.type(screen.getByLabelText('Encounter reference'), 'OPD-1');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    await waitFor(() => expect(router.state.location.pathname).toBe(`/bills/${created.id}`));

    const [first, second, third] = api
      .sent('POST', '/api/v1/bills')
      .map((request) => request.headers.get('Idempotency-Key'));
    expect(second).toBe(first);
    expect(third).not.toBe(first);
  });

  it("puts the API's message for a line on that line's field", async () => {
    const api = routedApi(
      newBillRoutes(() =>
        json(
          400,
          problem(400, 'validation_failed', 'One or more fields are invalid.', {
            'lines[1].unitPrice': ['The unit price has at most 4 decimals.'],
          }),
        ),
      ),
    );
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });
    await fillDraft(user);

    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    expect(await screen.findByText('The unit price has at most 4 decimals.')).toBeInTheDocument();
    expect(screen.getAllByLabelText('Unit price (₹)')[1]).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getAllByLabelText('Unit price (₹)')[0]).toHaveAttribute('aria-invalid', 'false');
    // The first field the API refused takes the focus, and so comes into view (ADR 0049).
    expect(screen.getAllByLabelText('Unit price (₹)')[1]).toHaveFocus();
  });

  it('asks for the patient and the payer, and refuses a discount above the line or a quantity of nothing', async () => {
    const api = routedApi(newBillRoutes(() => json(201, created)));
    const { user } = renderApp('/bills/new', { session: clerk, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Add a free-text line' }));
    await user.type(screen.getByLabelText('Line 1: description'), 'Consultation');
    const quantity = screen.getByLabelText('Quantity');
    await user.clear(quantity);
    await user.type(quantity, '0');
    await user.type(screen.getByLabelText('Unit price (₹)'), '500');
    await user.type(screen.getByLabelText('Discount (₹)'), '600');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));

    expect(await screen.findByText('The quantity is more than 0.')).toBeInTheDocument();
    expect(screen.getByText('Choose the patient.')).toBeInTheDocument();
    expect(screen.getByText('Choose the payer.')).toBeInTheDocument();

    await user.clear(quantity);
    await user.type(quantity, '1');
    await user.click(screen.getByRole('button', { name: 'Save draft' }));
    expect(await screen.findByText('The discount is not more than the line amount.')).toBeInTheDocument();
    expect(api.sent('POST', '/api/v1/bills')).toHaveLength(0);
  });
});

describe('editing a draft (ADR 0040 point 1)', () => {
  const edit = `/bills/${draft.id}/edit`;

  it('sends only the header when only the header changed', async () => {
    const api = routedApi({
      ...billRoutes(),
      [`PATCH ${one}`]: () => json(200, bill({ notes: 'Ward 7', version: 'AAAAAAAACAE=' })),
    });
    const { user, router } = renderApp(edit, { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByLabelText('Notes'), 'Ward 7');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/bills/${draft.id}`));
    const request = api.sent('PATCH', one)[0];
    expect(request?.headers.get('If-Match')).toBe('"AAAAAAAACAA="');
    expect(await request?.json()).toEqual({
      billDate: '2026-11-20',
      dueDate: '2026-12-05',
      encounterReference: 'OPD-7781',
      notes: 'Ward 7',
    });
    expect(api.sent('PUT', `${one}/lines`)).toHaveLength(0);
  });

  it('sends the header, then the lines with the version the header came back with', async () => {
    const api = routedApi({
      ...billRoutes(),
      [`PATCH ${one}`]: () => json(200, bill({ notes: 'Ward 7', version: 'AAAAAAAACAE=' })),
      [`PUT ${one}/lines`]: () => json(200, bill({ notes: 'Ward 7', version: 'AAAAAAAACAI=' })),
    });
    const { user, router } = renderApp(edit, { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByLabelText('Notes'), 'Ward 7');
    const quantity = screen.getByLabelText('Quantity');
    await user.clear(quantity);
    await user.type(quantity, '4');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/bills/${draft.id}`));
    const lines = api.sent('PUT', `${one}/lines`)[0];
    expect(lines?.headers.get('If-Match')).toBe('"AAAAAAAACAE="');
    expect(await lines?.json()).toEqual({
      lines: [
        {
          serviceItemId: billLine().serviceItemId,
          description: 'Complete blood count',
          quantity: 4,
          unitPrice: 333.3333,
          discountAmount: 0.5,
          taxRatePercent: 5,
        },
      ],
    });
  });

  it('says so when the header was saved and the lines refused, and then sends the lines alone', async () => {
    const saved = bill({ notes: 'Ward 7', version: 'AAAAAAAACAE=' });
    const api = routedApi({
      ...billRoutes(),
      [`PATCH ${one}`]: () => json(200, saved),
      [`PUT ${one}/lines`]: inTurn(
        () =>
          json(
            400,
            problem(400, 'validation_failed', 'One or more fields are invalid.', {
              'lines[0].quantity': ['The quantity is at most 999999.999.'],
            }),
          ),
        () => json(200, bill({ notes: 'Ward 7', version: 'AAAAAAAACAI=' })),
      ),
    });
    const { user, router } = renderApp(edit, { session: clerk, fetch: api.fetch });

    await user.type(await screen.findByLabelText('Notes'), 'Ward 7');
    const quantity = screen.getByLabelText('Quantity');
    await user.clear(quantity);
    await user.type(quantity, '999999');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(
      await screen.findByText('The dates, reference and notes were saved; the lines were not.', { exact: false }),
    ).toBeInTheDocument();
    expect(screen.getByText('The quantity is at most 999999.999.')).toBeInTheDocument();

    await user.clear(quantity);
    await user.type(quantity, '2');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/bills/${draft.id}`));
    expect(api.sent('PATCH', one)).toHaveLength(1);
    const [, second] = api.sent('PUT', `${one}/lines`);
    expect(second?.headers.get('If-Match')).toBe('"AAAAAAAACAE="');
  });

  it('is not offered for a bill that is no longer a draft', async () => {
    const api = routedApi(billRoutes(() => json(200, bill({ status: 'Posted', number: 'BIL/26-27/000001' }))));
    renderApp(edit, { session: clerk, fetch: api.fetch });

    expect(await screen.findByText('Only a draft can be changed', { exact: false })).toBeInTheDocument();
  });

  it('is refused to a role that may only read bills', async () => {
    renderApp(edit, { session: sessionFor(['Cashier']) });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
  });
});
