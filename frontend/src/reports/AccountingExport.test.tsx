import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import { problem } from '../test/records';

/**
 * The accountant's export and the names of its ledgers in the browser (Prompt 11.4b, ADR 0062), with the API faked:
 * the card of the export for those who may make it; the summary of a period with its control total and what each
 * ledger receives; the ledgers without a name said, and the file held back until they have one; the file saved as the
 * API named it; and the twelve names saved by the administrator with the version read, an empty one as none.
 */
const viewer = sessionFor(['Viewer']);
const admin = sessionFor(['TenantAdmin']);
const noVersion = 'AAAAAAAAAAA=';

const summary = (overrides: object = {}) => ({
  from: '2026-10-01',
  to: '2026-10-31',
  vouchers: 18,
  receipts: 5,
  payments: 2,
  journals: 11,
  controlTotal: 5234.5,
  ledgerTotal: 5234.5,
  ledgers: [
    { ledger: 'Receivable', name: 'Patient Receivables', debit: 2500, credit: 1800 },
    { ledger: 'Revenue', name: 'Hospital Income', debit: 100, credit: 1900 },
    { ledger: 'TaxOutput', name: 'GST Output', debit: 26, credit: 74 },
    { ledger: 'Card', name: 'HDFC Current A/c', debit: 150, credit: 150 },
  ],
  missing: [],
  ...overrides,
});

const unnamed = summary({
  ledgers: [
    { ledger: 'Receivable', name: 'Patient Receivables', debit: 2500, credit: 1800 },
    { ledger: 'TaxOutput', name: null, debit: 26, credit: 74 },
    { ledger: 'Cheque', name: null, debit: 500, credit: 0 },
  ],
  missing: ['TaxOutput', 'Cheque'],
});

const noNames = {
  receivable: null,
  unappliedCash: null,
  creditAvailable: null,
  revenue: null,
  taxOutput: null,
  adjustments: null,
  openingBalances: null,
  cash: null,
  card: null,
  upi: null,
  bankTransfer: null,
  cheque: null,
  version: noVersion,
};

/** The files the page handed to the browser to save; jsdom has no object URLs of its own. */
let saved: Blob[] = [];

beforeEach(() => {
  saved = [];
  Object.assign(URL, {
    createObjectURL: (blob: Blob) => {
      saved.push(blob);
      return 'blob:export';
    },
    revokeObjectURL: () => undefined,
  });
});

describe("the accountant's export (US-52)", () => {
  it('is offered on the reports to a Viewer, and not to a Cashier or a BillingClerk', async () => {
    const first = renderApp('/reports', { session: viewer, fetch: routedApi({}).fetch });
    expect(await screen.findByRole('link', { name: "Accountant's export" })).toHaveAttribute(
      'href',
      '/reports/accounting-export',
    );
    first.unmount();

    renderApp('/reports', { session: sessionFor(['Cashier', 'BillingClerk']), fetch: routedApi({}).fetch });
    await screen.findByRole('link', { name: 'Bill register' });
    expect(screen.queryByRole('link', { name: "Accountant's export" })).not.toBeInTheDocument();
  });

  it("shows the period's vouchers, its control total beside the ledger's and what each ledger receives", async () => {
    const api = routedApi({ 'GET /api/v1/reports/accounting-export/summary': () => json(200, summary()) });
    renderApp('/reports/accounting-export?from=2026-10-01&to=2026-10-31', { session: viewer, fetch: api.fetch });

    const table = await screen.findByRole('table', { name: 'What each ledger receives' });
    const query = new URL(api.sent('GET', '/api/v1/reports/accounting-export/summary')[0]!.url).searchParams;
    expect([query.get('from'), query.get('to')]).toEqual(['2026-10-01', '2026-10-31']);
    expect(screen.getByText('01 Oct 2026 to 31 Oct 2026')).toBeInTheDocument();
    expect(screen.getByText('18 vouchers: 5 receipts, 2 payments, 11 journals')).toBeInTheDocument();
    expect(screen.getAllByText('₹5,234.50')).toHaveLength(2);

    const rows = within(table).getAllByRole('row');
    expect(rows[1]).toHaveTextContent('What payers owe (receivables)');
    expect(rows[1]).toHaveTextContent('Patient Receivables');
    expect(rows[3]).toHaveTextContent('Tax billed (GST output)');
    expect(rows.at(-1)).toHaveTextContent('4 ledgers');
    expect(rows.at(-1)).toHaveTextContent('5,234.50');
    expect(screen.queryByText('Some ledgers have no name yet')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download the Tally file' })).toBeEnabled();
  });

  it('asks for the current month when no period is given, and sends a new one from the form', async () => {
    const api = routedApi({ 'GET /api/v1/reports/accounting-export/summary': () => json(200, summary()) });
    const { user, router } = renderApp('/reports/accounting-export', { session: viewer, fetch: api.fetch });

    await screen.findByRole('table', { name: 'What each ledger receives' });
    const first = new URL(api.sent('GET', '/api/v1/reports/accounting-export/summary')[0]!.url).searchParams;
    expect(first.get('from')).toMatch(/^\d{4}-\d{2}-01$/);
    expect(first.get('to')).toMatch(/^\d{4}-\d{2}-\d{2}$/);

    await user.clear(screen.getByLabelText('From'));
    await user.type(screen.getByLabelText('From'), '2026-09-01');
    await user.clear(screen.getByLabelText('To'));
    await user.type(screen.getByLabelText('To'), '2026-09-30');
    await user.click(screen.getByRole('button', { name: 'Show' }));

    await waitFor(() => expect(router.state.location.search).toBe('?from=2026-09-01&to=2026-09-30'));
    await waitFor(() => {
      const last = new URL(api.sent('GET', '/api/v1/reports/accounting-export/summary').at(-1)!.url).searchParams;
      expect([last.get('from'), last.get('to')]).toEqual(['2026-09-01', '2026-09-30']);
    });
  });

  it('says which ledgers have no name, holds the file back, and sends an administrator to name them', async () => {
    const api = routedApi({ 'GET /api/v1/reports/accounting-export/summary': () => json(200, unnamed) });
    const first = renderApp('/reports/accounting-export?from=2026-10-01&to=2026-10-31', {
      session: admin,
      fetch: api.fetch,
    });

    const banner = await screen.findByText('Some ledgers have no name yet');
    const notice = banner.closest('[role]') ?? banner.parentElement!;
    expect(notice).toHaveTextContent('Tax billed (GST output), Money received by cheque');
    expect(within(notice as HTMLElement).getByRole('link', { name: 'Users and settings, Accounts' })).toHaveAttribute(
      'href',
      '/administration/accounting',
    );
    expect(screen.getAllByText('Not named yet')).toHaveLength(2);
    expect(screen.getByRole('button', { name: 'Download the Tally file' })).toBeDisabled();
    first.unmount();

    // A Viewer is told to ask the administrator, with no link to a page it may not open.
    renderApp('/reports/accounting-export?from=2026-10-01&to=2026-10-31', { session: viewer, fetch: api.fetch });
    expect(await screen.findByText(/Ask your administrator to name them/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Users and settings, Accounts' })).not.toBeInTheDocument();
  });

  it('saves the file under the name the API gave it, and says to import it once', async () => {
    const api = routedApi({
      'GET /api/v1/reports/accounting-export/summary': () => json(200, summary()),
      'GET /api/v1/reports/accounting-export': () =>
        Promise.resolve(
          new Response('<?xml version="1.0" encoding="utf-8"?>\r\n<ENVELOPE />', {
            status: 200,
            headers: {
              'Content-Type': 'application/xml; charset=utf-8',
              'Content-Disposition': 'attachment; filename="billingrecon-tally-20261001-20261031.xml"',
            },
          }),
        ),
    });
    const { user } = renderApp('/reports/accounting-export?from=2026-10-01&to=2026-10-31', {
      session: viewer,
      fetch: api.fetch,
    });

    await user.click(await screen.findByRole('button', { name: 'Download the Tally file' }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      "Saved billingrecon-tally-20261001-20261031.xml. Import it into the accountant's package once.",
    );
    const query = new URL(api.sent('GET', '/api/v1/reports/accounting-export')[0]!.url).searchParams;
    expect([query.get('from'), query.get('to')]).toEqual(['2026-10-01', '2026-10-31']);
    expect(saved).toHaveLength(1);
    expect(await saved[0]!.text()).toContain('<ENVELOPE />');
  });

  it("shows the API's refusal of the file as it is", async () => {
    const api = routedApi({
      'GET /api/v1/reports/accounting-export/summary': () => json(200, summary()),
      'GET /api/v1/reports/accounting-export': () =>
        json(
          422,
          problem(
            422,
            'accounting_ledger_not_mapped',
            "The accountant's export needs a ledger name for: Cheque. An administrator sets them under Administration, Settings.",
          ),
        ),
    });
    const { user } = renderApp('/reports/accounting-export?from=2026-10-01&to=2026-10-31', {
      session: viewer,
      fetch: api.fetch,
    });

    await user.click(await screen.findByRole('button', { name: 'Download the Tally file' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      "The accountant's export needs a ledger name for: Cheque.",
    );
    expect(saved).toHaveLength(0);
  });
});

describe("the names of the accountant's ledgers (ADR 0062)", () => {
  it('saves the twelve names with the version read, trimmed, and an empty one as none', async () => {
    const answered = {
      ...noNames,
      receivable: 'Patient Receivables',
      card: 'HDFC Current A/c',
      version: 'AAAAAAAAD1E=',
    };
    const api = routedApi({
      'GET /api/v1/settings/accounting-ledgers': () => json(200, noNames),
      'PUT /api/v1/settings/accounting-ledgers': () => json(200, answered),
    });
    const { user } = renderApp('/administration/accounting', { session: admin, fetch: api.fetch });

    const form = await screen.findByRole('form', { name: "The accountant's ledgers" });
    expect(screen.getByRole('link', { name: 'Accounts' })).toHaveAttribute('href', '/administration/accounting');
    expect(within(form).getAllByRole('textbox')).toHaveLength(12);
    expect(within(form).getByRole('link', { name: "accountant's export" })).toHaveAttribute(
      'href',
      '/reports/accounting-export',
    );

    await user.type(within(form).getByLabelText('What payers owe (receivables)'), '  Patient Receivables ');
    await user.type(within(form).getByLabelText('Money received by card'), 'HDFC Current A/c');
    await user.click(within(form).getByRole('button', { name: 'Save the ledger names' }));

    expect(await screen.findByText('The next export names these ledgers.')).toBeInTheDocument();
    const sent = api.sent('PUT', '/api/v1/settings/accounting-ledgers')[0]!;
    expect(sent.headers.get('If-Match')).toBe(`"${noVersion}"`);
    expect(await sent.json()).toEqual({
      ...noNames,
      version: undefined,
      receivable: 'Patient Receivables',
      card: 'HDFC Current A/c',
    });
    expect(within(form).getByLabelText('What payers owe (receivables)')).toHaveValue('Patient Receivables');
  });

  it("places the API's refusal at its field, and sends the version it answered with the next save", async () => {
    const api = routedApi({
      'GET /api/v1/settings/accounting-ledgers': () => json(200, { ...noNames, cash: 'Cash', version: 'AAAAAAAAD1E=' }),
      'PUT /api/v1/settings/accounting-ledgers': () =>
        json(
          400,
          problem(400, 'validation_failed', 'The request is not valid. The errors are listed per field.', {
            cash: ["A ledger's name has at most 100 characters, on one line."],
          }),
        ),
    });
    const { user } = renderApp('/administration/accounting', { session: admin, fetch: api.fetch });

    const form = await screen.findByRole('form', { name: "The accountant's ledgers" });
    expect(within(form).getByLabelText('Money received in cash')).toHaveValue('Cash');
    await user.click(within(form).getByRole('button', { name: 'Save the ledger names' }));

    expect(
      await within(form).findByText("A ledger's name has at most 100 characters, on one line."),
    ).toBeInTheDocument();
    expect(api.sent('PUT', '/api/v1/settings/accounting-ledgers')[0]!.headers.get('If-Match')).toBe('"AAAAAAAAD1E="');
  });

  it("is the administrator's alone", async () => {
    renderApp('/administration/accounting', { session: sessionFor(['Supervisor']), fetch: routedApi({}).fetch });

    expect(await screen.findByRole('heading', { level: 1 })).not.toHaveTextContent('Users and settings');
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
  });
});
