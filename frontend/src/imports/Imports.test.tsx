import { configure, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import { problem } from '../test/records';
import type { OpeningBatch, OpeningBatchCheck, PartyImport } from './api';
import { fileLimits, requestBytes, tooLarge } from './api';
import { findingsCsv, placeOf } from './findingsCsv';

/**
 * The screens that bring in a hospital's records and opening balances (Prompt 10.3b, ADR 0053), with the API faked: the
 * files are checked before anything is written, a rejected row keeps Import and Submit closed, a file too large for one
 * request is refused before it is sent, a submission carries a key it reuses on a retry, and a batch shows where it
 * stands.
 */
// Each page loads on demand and reads its files before it answers: what follows is waited for longer than the second
// a test waits by default, as the tests of the live search do.
configure({ asyncUtilTimeout: 3000 });

const admin = sessionFor(['TenantAdmin']);
const parties = '/api/v1/imports/parties';
const dryRun = '/api/v1/opening-batches/dry-run';
const batches = '/api/v1/opening-batches';
const page = (items: object[]) => ({ items, page: 1, pageSize: 50, totalCount: items.length });
const csv = (name: string, text: string) => new File([text], name, { type: 'text/csv' });

const checked: PartyImport = {
  dryRun: true,
  written: false,
  patientsCreated: 1998,
  patientsMatched: 2,
  payersCreated: 1998,
  guarantorsCreated: 12,
  guarantorsMatched: 0,
  rejected: [],
};
const accepted: OpeningBatchCheck = {
  cutOverDate: '2027-04-01',
  accepted: true,
  receivables: 300,
  receivableTotal: 6584094.91,
  advances: 55,
  advanceTotal: 540270.61,
  creditBalanceTotal: 174519,
  rejected: [],
};
const batch: OpeningBatch = {
  id: 'fd000000-0000-4000-8000-000000000001',
  cutOverDate: '2027-04-01',
  status: 'PendingApproval',
  receivables: 300,
  receivableTotal: 6584094.91,
  advances: 55,
  advanceTotal: 540270.61,
  submittedAtUtc: '2027-04-02T05:00:00+00:00',
  submittedByUserId: 'd0000000-0000-4000-8000-000000000002',
  decidedAtUtc: null,
  version: 'AAAAAAAADgA=',
};

async function bodyOf(request: Request | undefined): Promise<Record<string, unknown>> {
  return (await request?.clone().json()) as Record<string, unknown>;
}

describe('the import of a hospital, for its administrator', () => {
  it('shows the two steps in their order and the batches that were submitted', async () => {
    const api = routedApi({
      [`GET ${batches}`]: () =>
        json(200, page([batch, { ...batch, id: 'fd000000-0000-4000-8000-000000000002', status: 'Rejected' }])),
    });
    renderApp('/administration/import', { session: admin, fetch: api.fetch });

    const tabs = await screen.findByRole('navigation', { name: 'Users and settings' });
    expect(within(tabs).getByRole('link', { name: 'Import' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Import patients and guarantors' })).toHaveAttribute(
      'href',
      '/administration/import/patients',
    );
    expect(screen.getByRole('link', { name: 'Import opening balances' })).toHaveAttribute(
      'href',
      '/administration/import/opening-balances',
    );

    const rows = within(await screen.findByRole('table', { name: 'Opening batches' })).getAllByRole('row');
    expect(rows).toHaveLength(3);
    expect(rows[1]).toHaveTextContent('Waiting for approval');
    expect(rows[1]).toHaveTextContent('65,84,094.91');
    expect(rows[2]).toHaveTextContent('Rejected');
  });

  it('says so when no batch was submitted, and is closed to anyone but the administrator', async () => {
    const api = routedApi({ [`GET ${batches}`]: () => json(200, page([])) });
    const first = renderApp('/administration/import', { session: admin, fetch: api.fetch });
    expect(await screen.findByText('No opening batch was submitted yet.')).toBeInTheDocument();
    first.unmount();

    renderApp('/administration/import/opening-balances', { session: sessionFor(['Supervisor']), fetch: api.fetch });
    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
  });
});

describe('importing patients and guarantors', () => {
  it('checks the files first, and offers Import only when no row was rejected', async () => {
    const answers = [checked, { ...checked, dryRun: false, written: true }];
    const api = routedApi({ [`POST ${parties}`]: () => json(200, answers.shift()) });
    const { user } = renderApp('/administration/import/patients', { session: admin, fetch: api.fetch });

    const check = await screen.findByRole('button', { name: 'Check the files' });
    const importIt = screen.getByRole('button', { name: 'Import' });
    expect(check).toBeDisabled();
    expect(importIt).toBeDisabled();

    await user.upload(screen.getByLabelText(/^Patients/), csv('patients.csv', 'mrn,full_name\nNF-1,Asha Rao'));
    await user.upload(screen.getByLabelText(/^Guarantors/), csv('guarantors.csv', 'guarantor_ref,name\nG-1,Mohan Rao'));
    await waitFor(() => expect(check).toBeEnabled());
    expect(importIt).toBeDisabled();

    await user.click(check);
    expect(await screen.findByText('The files can be imported')).toBeInTheDocument();
    expect(await bodyOf(api.sent('POST', parties)[0])).toEqual({
      patientsCsv: 'mrn,full_name\nNF-1,Asha Rao',
      guarantorsCsv: 'guarantor_ref,name\nG-1,Mohan Rao',
      dryRun: true,
    });
    expect(screen.getByText('New patients').nextElementSibling).toHaveTextContent('1,998');
    expect(screen.getByText('Patients already here').nextElementSibling).toHaveTextContent('2');

    await user.click(importIt);
    expect(await screen.findByText('Imported')).toBeInTheDocument();
    expect((await bodyOf(api.sent('POST', parties)[1])).dryRun).toBe(false);
    expect(screen.getByText('Patients registered').nextElementSibling).toHaveTextContent('1,998');
    // What was imported is not offered again without a new check.
    expect(importIt).toBeDisabled();
  });

  it('names every rejected row, keeps Import closed, and closes it again when a file changes', async () => {
    const rejected = [
      { file: 'patients.csv', line: 3, reason: 'MRN NF-1 is on line 2 too (BR-101). A patient is in the file once.' },
      { file: 'patients.csv', line: 1, reason: 'The header lacks: full_name.' },
      { file: 'guarantors.csv', line: 0, reason: 'The file has 5001 rows; one import takes at most 5000. Split it.' },
    ];
    const answers = [{ ...checked, rejected }, checked];
    const api = routedApi({ [`POST ${parties}`]: () => json(200, answers.shift()) });
    const { user } = renderApp('/administration/import/patients', { session: admin, fetch: api.fetch });

    await user.upload(await screen.findByLabelText(/^Patients/), csv('patients.csv', 'mrn\nNF-1\nNF-1'));
    await user.click(screen.getByRole('button', { name: 'Check the files' }));

    expect(await screen.findByRole('heading', { name: '3 rows cannot be brought in' })).toBeInTheDocument();
    const rows = within(screen.getByRole('table')).getAllByRole('row');
    expect(rows[1]).toHaveTextContent('patients.csvLine 3MRN NF-1 is on line 2 too');
    expect(rows[2]).toHaveTextContent('patients.csvHeader');
    expect(rows[3]).toHaveTextContent('guarantors.csvThe file');
    expect(screen.getByRole('button', { name: 'Download the findings' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import' })).toBeDisabled();
    expect(screen.queryByText('The files can be imported')).not.toBeInTheDocument();

    // The corrected file is checked anew: the answer of the file before says nothing about it.
    await user.upload(screen.getByLabelText(/^Patients/), csv('patients.csv', 'mrn,full_name\nNF-1,Asha Rao'));
    await waitFor(() => expect(screen.queryByRole('table')).not.toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Import' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Check the files' }));
    expect(await screen.findByText('The files can be imported')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import' })).toBeEnabled();
  });

  it('refuses a file too large for one request before anything is sent', async () => {
    const api = routedApi({ [`POST ${parties}`]: () => json(200, checked) });
    const { user } = renderApp('/administration/import/patients', { session: admin, fetch: api.fetch });

    const large = `mrn,full_name\n${'NF-1,Asha Rao\n'.repeat(Math.ceil(fileLimits.patients / 14))}`;
    await user.upload(await screen.findByLabelText(/^Patients/), csv('patients.csv', large));
    await user.click(screen.getByRole('button', { name: 'Check the files' }));

    expect(await screen.findByText('Too large to send')).toBeInTheDocument();
    expect(screen.getByText(/patients\.csv is too large for one import/)).toBeInTheDocument();
    expect(api.sent('POST', parties)).toHaveLength(0);
  });

  it('shows the refusal of the API as any screen does', async () => {
    const api = routedApi({ [`POST ${parties}`]: () => json(403, problem(403, 'forbidden', 'You may not import.')) });
    const { user } = renderApp('/administration/import/patients', { session: admin, fetch: api.fetch });

    await user.upload(await screen.findByLabelText(/^Patients/), csv('patients.csv', 'mrn,full_name\nNF-1,Asha Rao'));
    await user.click(screen.getByRole('button', { name: 'Check the files' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('You may not import.');
  });
});

describe('importing opening balances', () => {
  async function choose(user: ReturnType<typeof renderApp>['user']) {
    await user.type(await screen.findByLabelText(/^Cut-over date/), '2027-04-01');
    await user.upload(
      screen.getByLabelText(/^Trial balance/),
      csv('trial_balance.csv', 'account,amount\nReceivable,10.00'),
    );
    await user.upload(screen.getByLabelText(/^Unpaid bills/), csv('bills.csv', 'legacy_number,mrn\nOLD-1,NF-1'));
  }

  it('checks the files against the trial balance and submits them with a key, then shows the batch', async () => {
    const api = routedApi({
      [`POST ${dryRun}`]: () => json(200, accepted),
      [`POST ${batches}`]: () => json(201, batch),
      [`GET ${batches}/${batch.id}`]: () => json(200, batch),
      'GET /api/v1/audit-records': () => json(200, page([])),
    });
    const { user, router } = renderApp('/administration/import/opening-balances', { session: admin, fetch: api.fetch });

    const check = await screen.findByRole('button', { name: 'Check the files' });
    expect(check).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument();
    await choose(user);
    await waitFor(() => expect(check).toBeEnabled());
    await user.click(check);

    expect(await screen.findByText('The files agree with the trial balance')).toBeInTheDocument();
    expect(screen.getByText('Owed in all').nextElementSibling).toHaveTextContent('65,84,094.91');
    expect(screen.getByText('Held in all').nextElementSibling).toHaveTextContent('5,40,270.61');
    expect(await bodyOf(api.sent('POST', dryRun)[0])).toEqual({
      cutOverDate: '2027-04-01',
      billsCsv: 'legacy_number,mrn\nOLD-1,NF-1',
      advancesCsv: null,
      trialBalanceCsv: 'account,amount\nReceivable,10.00',
      comment: null,
    });

    await user.type(screen.getByLabelText(/^Note for the person who approves/), ' Agreed with the March closing ');
    await user.click(screen.getByRole('button', { name: 'Submit for approval' }));

    await waitFor(() => expect(router.state.location.pathname).toBe(`/administration/import/batches/${batch.id}`));
    const sent = api.sent('POST', batches)[0];
    expect(sent?.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect((await bodyOf(sent)).comment).toBe('Agreed with the March closing');
    expect(await screen.findByText('Waiting for approval', { selector: 'p' })).toBeInTheDocument();
  });

  it('offers no submission while a row is rejected', async () => {
    const rejected = [
      {
        file: 'trial_balance.csv',
        line: 2,
        reason: 'The trial balance says 10.00 for Receivable; the rows of the files come to 12.00.',
      },
    ];
    const api = routedApi({ [`POST ${dryRun}`]: () => json(200, { ...accepted, accepted: false, rejected }) });
    const { user } = renderApp('/administration/import/opening-balances', { session: admin, fetch: api.fetch });

    await choose(user);
    await user.click(screen.getByRole('button', { name: 'Check the files' }));

    expect(await screen.findByText('The files cannot be submitted as they are')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '1 row cannot be brought in' })).toBeInTheDocument();
    expect(screen.getByRole('table')).toHaveTextContent('trial_balance.csvLine 2The trial balance says 10.00');
    expect(screen.queryByRole('button', { name: 'Submit for approval' })).not.toBeInTheDocument();
  });

  it('sends the same key again after a fault, and shows a duplicate batch in the words of the API', async () => {
    const answers: (() => Response | Promise<Response>)[] = [
      () => Promise.reject(new TypeError('Failed to fetch')),
      () =>
        json(
          409,
          problem(
            409,
            'opening_batch_duplicate',
            'These files were submitted before for this cut-over date; opening balances are brought in once.',
          ),
        ),
    ];
    const api = routedApi({
      [`POST ${dryRun}`]: () => json(200, accepted),
      [`POST ${batches}`]: () => (answers.shift() ?? (() => json(201, batch)))(),
    });
    const { user, router } = renderApp('/administration/import/opening-balances', { session: admin, fetch: api.fetch });

    await choose(user);
    await user.click(screen.getByRole('button', { name: 'Check the files' }));
    await user.click(await screen.findByRole('button', { name: 'Submit for approval' }));
    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Submit for approval' }));

    expect(await screen.findByText(/opening balances are brought in once/)).toBeInTheDocument();
    const [first, second] = api.sent('POST', batches);
    expect(second?.headers.get('Idempotency-Key')).toBe(first?.headers.get('Idempotency-Key'));
    expect(router.state.location.pathname).toBe('/administration/import/opening-balances');
  });
});

describe('an opening batch', () => {
  const trail = { 'GET /api/v1/audit-records': () => json(200, page([])) };

  it('says that it waits, with its figures and the way to its review', async () => {
    const api = routedApi({ [`GET ${batches}/${batch.id}`]: () => json(200, batch), ...trail });
    renderApp(`/administration/import/batches/${batch.id}`, { session: admin, fetch: api.fetch });

    expect(await screen.findByText('Waiting for approval', { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Review it in Approvals' })).toHaveAttribute(
      'href',
      '/approvals?documentType=OpeningBatch',
    );
    expect(screen.getByText('Owed in all').nextElementSibling).toHaveTextContent('65,84,094.91');
    expect(screen.getByText('Opening receivables').nextElementSibling).toHaveTextContent('300');
    expect(screen.getByText('Decided').nextElementSibling).toHaveTextContent('Not yet');
  });

  it.each([
    ['Approved', 'Approved and posted'],
    ['Rejected', 'Rejected'],
  ] as const)('says when it was %s, and offers no review', async (status, title) => {
    const decided = { ...batch, status, decidedAtUtc: '2027-04-02T07:00:00+00:00' };
    const api = routedApi({ [`GET ${batches}/${batch.id}`]: () => json(200, decided), ...trail });
    renderApp(`/administration/import/batches/${batch.id}`, { session: admin, fetch: api.fetch });

    expect(await screen.findByText(title, { selector: 'p' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Review it in Approvals' })).not.toBeInTheDocument();
  });
});

describe('the limits and the file of findings', () => {
  it('weighs a request as it travels, and refuses what the proxy would', () => {
    const file = { name: 'bills.csv', text: 'é'.repeat(10) };
    // Eleven characters of JSON around it, and two bytes for the one letter.
    expect(requestBytes({ text: 'é' })).toBe(13);
    expect(tooLarge({ a: file.text }, [[file, 20]])).toBeNull();
    expect(tooLarge({ a: file.text }, [[file, 9]])).toMatch(/^bills\.csv is too large for one import/);
    expect(tooLarge({ a: 'x'.repeat(900_001) }, [[null, 1]])).toMatch(/too large to send together/);
  });

  it('writes the findings as a file a spreadsheet opens, and never as a formula', () => {
    const text = findingsCsv([
      { file: 'bills.csv', line: 4, reason: '=HYPERLINK("http://evil.example") is not a patient here' },
      { file: 'bills.csv', line: 0, reason: 'The file is required.' },
    ]);

    expect(text.charCodeAt(0)).toBe(0xfeff);
    expect(text.slice(1).split('\r\n')).toEqual([
      'File,Line,Reason',
      'bills.csv,4,"\'=HYPERLINK(""http://evil.example"") is not a patient here"',
      'bills.csv,0,The file is required.',
      '',
    ]);
    expect([placeOf(0), placeOf(1), placeOf('7')]).toEqual(['The file', 'Header', 'Line 7']);
  });
});
