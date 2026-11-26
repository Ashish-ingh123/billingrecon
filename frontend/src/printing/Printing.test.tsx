import { configure, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { json, renderApp, routedApi, sessionFor, type Handler } from '../test/app';
import { allocation, bill, billLine, patient, payer, payment, printSettings, problem, refund } from '../test/records';

/**
 * The printed documents (Prompt 10.4b, ADR 0054), with the API faked: each page shows the fields of its document on the
 * hospital's letterhead, with the amount in words; a bill is a tax invoice, a bill of supply or the statement of an
 * opening balance by what it carries; Print records the print before the dialog opens, and the page says Original for
 * the first and Duplicate for every later one; and what cannot be printed says so and offers no Print.
 */
// Each page loads on demand and reads several records before it answers.
configure({ asyncUtilTimeout: 3000 });

const cashier = sessionFor(['Cashier', 'BillingClerk']);
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
const posted = bill({
  status: 'Posted',
  number: 'BIL/26-27/000123',
  fiscalYear: '2026-27',
  lines: [billLine({ sacCode: '999311' })],
});
const asha = patient();
const ashasPayer = payer();
const shared: Record<string, Handler> = {
  'GET /api/v1/settings/letterhead': () => json(200, letterhead),
  // Since Prompt 10.4c the pages read how the hospital prints: here, as a hospital that set nothing.
  'GET /api/v1/settings/print': () => json(200, printSettings()),
  [`GET /api/v1/patients/${asha.id}`]: () => json(200, asha),
  [`GET /api/v1/payers/${ashasPayer.id}`]: () => json(200, ashasPayer),
};
const printed = (copyNumber: number) => ({
  copyNumber,
  original: copyNumber === 1,
  printedAtUtc: '2026-11-25T06:30:00+00:00',
});

let print: ReturnType<typeof vi.fn>;
beforeEach(() => {
  print = vi.fn();
  vi.stubGlobal('print', print);
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the printed bill', () => {
  it('is a tax invoice with the stored tax in its two halves, on the letterhead, with the amount in words', async () => {
    const api = routedApi({ ...shared, [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted) });
    renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: api.fetch });

    const sheet = await screen.findByRole('article', { name: 'Tax Invoice' });
    expect(within(sheet).getByText('City Hospital Private Limited')).toBeInTheDocument();
    expect(within(sheet).getByText('12 Example Road')).toBeInTheDocument();
    expect(within(sheet).getByText('Pune, Maharashtra 411001')).toBeInTheDocument();
    expect(within(sheet).getByText('GSTIN 27AAAAA0000A1Z5')).toBeInTheDocument();
    expect(within(sheet).getByText('BIL/26-27/000123')).toBeInTheDocument();
    expect(await within(sheet).findByText('Asha Verma (MRN MRN-1001)')).toBeInTheDocument();
    expect(within(sheet).getByText('Place of supply').nextElementSibling).toHaveTextContent('Maharashtra');

    // The line: its SAC code, and 49.98 of tax at 5% as 24.99 and 24.99 at 2.5% each.
    const row = within(sheet).getAllByRole('row')[1];
    expect(row).toHaveTextContent('Complete blood count');
    expect(row).toHaveTextContent('999311');
    expect(row).toHaveTextContent('₹24.99 (2.5%)');
    expect(within(sheet).getByText('CGST', { selector: 'dt' }).nextElementSibling).toHaveTextContent('₹24.99');
    expect(within(sheet).getByText('SGST', { selector: 'dt' }).nextElementSibling).toHaveTextContent('₹24.99');
    expect(within(sheet).getByText('Total', { selector: 'dt' }).nextElementSibling).toHaveTextContent('₹1,049.48');
    expect(within(sheet).getByText(/One thousand forty-nine rupees and forty-eight paise only/)).toBeInTheDocument();
    expect(within(sheet).getByText('Accounts Officer')).toBeInTheDocument();
    expect(within(sheet).queryByText(/exempt from GST/)).not.toBeInTheDocument();
  });

  it('records the print before the dialog opens, and says Original, then Duplicate', async () => {
    const answers = [printed(1), printed(2)];
    const api = routedApi({
      ...shared,
      [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted),
      [`POST /api/v1/bills/${posted.id}/prints`]: () => json(201, answers.shift()),
    });
    const { user } = renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('Unrecorded copy')).toBeInTheDocument();
    expect(print).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Print' }));
    expect(await screen.findByText(/^Original/)).toBeInTheDocument();
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole('button', { name: 'Print again' }));
    expect(await screen.findByText(/^Duplicate \(copy 2\)/)).toBeInTheDocument();
    await waitFor(() => expect(print).toHaveBeenCalledTimes(2));

    // Each print is an operation of its own, with a key of its own.
    const [first, second] = api.sent('POST', `/api/v1/bills/${posted.id}/prints`);
    expect(first?.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect(second?.headers.get('Idempotency-Key')).not.toBe(first?.headers.get('Idempotency-Key'));
  });

  it('opens no dialog when the print is refused, and says why', async () => {
    const api = routedApi({
      ...shared,
      [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted),
      [`POST /api/v1/bills/${posted.id}/prints`]: () =>
        json(422, problem(422, 'document_not_printable', 'Only a posted document is printed.')),
    });
    const { user } = renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Print' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Only a posted document is printed.');
    expect(screen.getByText('Unrecorded copy')).toBeInTheDocument();
    expect(print).not.toHaveBeenCalled();
  });

  it('is a bill of supply with the exempt wording when it carries no tax', async () => {
    const untaxed = {
      ...posted,
      taxTotal: 0,
      total: 999.5,
      lines: [billLine({ taxRatePercent: 0, taxAmount: 0, lineTotal: 999.5 })],
    };
    const api = routedApi({ ...shared, [`GET /api/v1/bills/${posted.id}`]: () => json(200, untaxed) });
    renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: api.fetch });

    const sheet = await screen.findByRole('article', { name: 'Bill of Supply' });
    expect(within(sheet).getByText(/exempt from GST/)).toBeInTheDocument();
    expect(within(sheet).queryByText('CGST')).not.toBeInTheDocument();
    expect(within(sheet).getByText(/Nine hundred ninety-nine rupees and fifty paise only/)).toBeInTheDocument();
  });

  it('is the statement of an opening balance for a bill brought in, with the number it had before', async () => {
    const opening = {
      ...posted,
      origin: 'Opening',
      legacyNumber: 'OLD/26-27/00017',
      number: 'OBR/27-28/000001',
      taxTotal: 0,
    };
    const api = routedApi({ ...shared, [`GET /api/v1/bills/${posted.id}`]: () => json(200, opening) });
    renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: api.fetch });

    const sheet = await screen.findByRole('article', { name: 'Statement of Opening Balance' });
    expect(within(sheet).getByText('Earlier bill number').nextElementSibling).toHaveTextContent('OLD/26-27/00017');
    expect(within(sheet).getByText(/not a new bill/)).toBeInTheDocument();
    expect(within(sheet).queryByText('Place of supply')).not.toBeInTheDocument();
  });

  it('is not printed while it is a draft', async () => {
    const api = routedApi({ ...shared, [`GET /api/v1/bills/${posted.id}`]: () => json(200, bill()) });
    renderApp(`/bills/${posted.id}/print`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('This bill is not printed')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Print' })).not.toBeInTheDocument();
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
  });
});

describe('the receipt and the refund voucher', () => {
  const paid = payment();
  const applied = allocation({ billId: posted.id });

  it('prints a receipt with the amount in words and the bills it was applied to', async () => {
    const api = routedApi({
      ...shared,
      [`GET /api/v1/payments/${paid.id}/allocations`]: () =>
        json(200, {
          payment: { ...paid, unappliedAmount: 1000 },
          allocations: [
            applied,
            allocation({ id: 'f7000000-0000-4000-8000-000000000002', status: 'Reversed', amount: 75 }),
          ],
        }),
      [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted),
      [`POST /api/v1/payments/${paid.id}/prints`]: () => json(201, printed(1)),
    });
    const { user } = renderApp(`/payments/${paid.id}/print`, { session: cashier, fetch: api.fetch });

    const sheet = await screen.findByRole('article', { name: 'Receipt' });
    expect(within(sheet).getByText('PAY/26-27/000001')).toBeInTheDocument();
    expect(await within(sheet).findByText('Asha Verma')).toBeInTheDocument();
    expect(within(sheet).getByText('Amount received').nextElementSibling).toHaveTextContent('₹1,500.00');
    expect(within(sheet).getByText(/One thousand five hundred rupees only/)).toBeInTheDocument();

    // The active allocation alone, by the number of its bill; a reversed one is not on a receipt.
    const rows = within(sheet).getAllByRole('row');
    expect(rows).toHaveLength(2);
    expect(await within(sheet).findByText('BIL/26-27/000123')).toBeInTheDocument();
    expect(rows[1]).toHaveTextContent('₹500.00');
    expect(within(sheet).getByText(/Not yet applied to a bill, as of this print: ₹1,000.00/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Print' }));
    expect(await screen.findByText(/^Original/)).toBeInTheDocument();
    expect(api.sent('POST', `/api/v1/payments/${paid.id}/prints`)).toHaveLength(1);
  });

  it('prints no receipt for a reversed payment', async () => {
    const api = routedApi({
      ...shared,
      [`GET /api/v1/payments/${paid.id}/allocations`]: () =>
        json(200, { payment: { ...paid, status: 'Reversed' }, allocations: [] }),
    });
    renderApp(`/payments/${paid.id}/print`, { session: cashier, fetch: api.fetch });

    expect(await screen.findByText('This payment has no receipt')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Print' })).not.toBeInTheDocument();
  });

  it('prints the voucher of a posted refund with its source and reason, and none of a draft', async () => {
    const repaid = refund({ status: 'Posted', number: 'REF/26-27/000004', fiscalYear: '2026-27' });
    const api = routedApi({
      ...shared,
      [`GET /api/v1/refunds/${repaid.id}`]: () => json(200, repaid),
      [`GET /api/v1/payments/${paid.id}/allocations`]: () => json(200, { payment: paid, allocations: [] }),
    });
    const first = renderApp(`/refunds/${repaid.id}/print`, { session: cashier, fetch: api.fetch });

    const sheet = await screen.findByRole('article', { name: 'Refund Voucher' });
    expect(within(sheet).getByText('REF/26-27/000004')).toBeInTheDocument();
    expect(await within(sheet).findByText('Payment PAY/26-27/000001')).toBeInTheDocument();
    expect(within(sheet).getByText('Reason').nextElementSibling).toHaveTextContent('Paid twice at the counter');
    expect(within(sheet).getByText('Paid by').nextElementSibling).toHaveTextContent('UPI, reference asha@upi');
    expect(within(sheet).getByText(/Two hundred rupees only/)).toBeInTheDocument();
    first.unmount();

    const draft = routedApi({ ...shared, [`GET /api/v1/refunds/${repaid.id}`]: () => json(200, refund()) });
    renderApp(`/refunds/${repaid.id}/print`, { session: cashier, fetch: draft.fetch });
    expect(await screen.findByText('This refund has no voucher')).toBeInTheDocument();
  });
});

describe('the way to a printed page', () => {
  it('is offered on a posted bill and not on a draft', async () => {
    const trail = { 'GET /api/v1/audit-records': () => json(200, { items: [], page: 1, pageSize: 20, totalCount: 0 }) };
    const others = {
      ...shared,
      ...trail,
      'GET /api/v1/payments': () => json(200, { items: [], page: 1, pageSize: 20, totalCount: 0 }),
      'GET /api/v1/credit-notes': () => json(200, { items: [], page: 1, pageSize: 20, totalCount: 0 }),
      'GET /api/v1/adjustments': () => json(200, { items: [], page: 1, pageSize: 20, totalCount: 0 }),
    };
    const api = routedApi({ ...others, [`GET /api/v1/bills/${posted.id}`]: () => json(200, posted) });
    const first = renderApp(`/bills/${posted.id}`, { session: cashier, fetch: api.fetch });
    expect(await screen.findByRole('link', { name: 'Print the bill' })).toHaveAttribute(
      'href',
      `/bills/${posted.id}/print`,
    );
    first.unmount();

    const draft = routedApi({ ...others, [`GET /api/v1/bills/${posted.id}`]: () => json(200, bill()) });
    renderApp(`/bills/${posted.id}`, { session: cashier, fetch: draft.fetch });
    await screen.findByRole('heading', { level: 1 });
    expect(screen.queryByRole('link', { name: 'Print the bill' })).not.toBeInTheDocument();
  });
});
