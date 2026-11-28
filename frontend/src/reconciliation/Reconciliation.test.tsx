import { configure, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { json, renderApp, routedApi, sessionFor } from '../test/app';
import type { SettlementChannel, StatementDryRun, StatementImport, StatementLine } from './api';
import { emptyMapping, headersOf } from './api';

/**
 * The screens of R2's statements (Prompt 13.1c, US-60, US-61; ADR 0065), with the API faked: a TenantAdmin sets up a
 * bank with its mapping, offered the names of a sample's header; an Accountant checks a statement and imports exactly the
 * file the check read, under a key; a refused row is named; and the lines are listed with their filters.
 */
configure({ asyncUtilTimeout: 3000 });

const admin = sessionFor(['TenantAdmin']);
const accountant = sessionFor(['Accountant']);
const channels = '/api/v1/settlement-channels';
const dryRun = '/api/v1/statement-imports/dry-run';
const imports = '/api/v1/statement-imports';
const lines = '/api/v1/statement-lines';
const page = (items: object[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });
const csv = (name: string, text: string) => new File([text], name, { type: 'text/csv' });

const bank: SettlementChannel = {
  id: 'ec000000-0000-4000-8000-000000000001',
  kind: 'Bank',
  name: 'HDFC current',
  accountLabel: '4521',
  gateway: null,
  bankChannelId: null,
  dateWindowDays: 3,
  mapping: {
    ...emptyMapping,
    dateColumn: 'Txn Date',
    descriptionColumn: 'Narration',
    referenceColumn: 'Ref No',
    debitColumn: 'Debit',
    creditColumn: 'Credit',
  },
  status: 'Active',
  createdAtUtc: '2026-10-16T05:00:00+00:00',
  updatedAtUtc: '2026-10-16T05:00:00+00:00',
  version: 'AAAAAAAAB9E=',
};

const checked: StatementDryRun = {
  fileHash: 'A'.repeat(64),
  alreadyImported: false,
  rowsRead: 5,
  linesToAdd: 4,
  linesAlreadyHeld: 0,
  rowsRefused: 1,
  moneyIn: 13000,
  moneyOut: 59,
  firstDate: '2026-10-16',
  lastDate: '2026-10-16',
  refusals: [{ rowNumber: 6, reason: 'the debit "abc" is not a number.' }],
  lines: [
    {
      rowNumber: 2,
      transactionDate: '2026-10-16',
      valueDate: null,
      direction: 'In',
      amount: 12000,
      reference: 'N3341',
      description: 'NEFT CR RAVI KUMAR',
      balance: 152300.5,
      alreadyHeld: false,
    },
  ],
};

const imported: StatementImport = {
  id: 'ed000000-0000-4000-8000-000000000001',
  channelId: bank.id,
  fileName: 'hdfc-16-oct.csv',
  fileHash: checked.fileHash,
  rowsRead: 5,
  linesAdded: 4,
  linesAlreadyHeld: 0,
  rowsRefused: 1,
  firstDate: '2026-10-16',
  lastDate: '2026-10-16',
  createdAtUtc: '2026-10-17T05:00:00+00:00',
  createdByUserId: 'd0000000-0000-4000-8000-000000000002',
};

const line: StatementLine = {
  id: 'ee000000-0000-4000-8000-000000000001',
  channelId: bank.id,
  importId: imported.id,
  rowNumber: 2,
  transactionDate: '2026-10-16',
  valueDate: null,
  direction: 'In',
  amount: 12000,
  reference: 'N3341',
  description: 'NEFT CR RAVI KUMAR',
  balance: 152300.5,
  rawText: '16/10/2026,NEFT CR RAVI KUMAR,N3341,,"12,000.00","1,52,300.50"',
  status: 'Unmatched',
  version: 'AAAAAAAAB9F=',
  matchReason: null,
  proposalReason: 'The amount ₹12,000.00 agrees with PAY/26-27/000233 of 16 Oct 2026, the same day.',
  proposalScore: 70,
  proposalCount: 2,
};

async function bodyOf(request: Request | undefined): Promise<Record<string, unknown>> {
  return (await request?.clone().json()) as Record<string, unknown>;
}

describe('the header of a sample statement', () => {
  it('is read from the line the mapping names, with quoted names and a byte order mark', () => {
    const text = '﻿Statement of account\nAccount 4521\n"Txn Date",Narration,"Debit, INR",Credit\n16/10/2026,X,,1\n';
    expect(headersOf(text, 3)).toEqual(['Txn Date', 'Narration', 'Debit, INR', 'Credit']);
    expect(headersOf(text, 9)).toEqual([]);
  });
});

describe('the settlement channels, for the administrator', () => {
  // Ten fields are typed one key at a time: the test is given longer than the default five seconds.
  it('sets up a bank with its mapping and goes back to the list', async () => {
    let saved: SettlementChannel[] = [];
    const api = routedApi({
      [`GET ${channels}`]: () => json(200, page(saved)),
      [`POST ${channels}`]: () => {
        saved = [bank];
        return json(201, bank);
      },
    });
    const { user } = renderApp('/administration/channels/new', { session: admin, fetch: api.fetch });

    await user.type(await screen.findByLabelText(/^Name/), 'HDFC current');
    await user.type(screen.getByLabelText(/^Last digits/), '4521');
    await user.upload(
      screen.getByLabelText(/^A sample of the statement/),
      csv('sample.csv', 'Txn Date,Narration,Ref No,Debit,Credit\n'),
    );
    expect(await screen.findByText('Found 5 columns on line 1.')).toBeInTheDocument();
    await user.type(screen.getByLabelText(/^Date column/), 'Txn Date');
    await user.type(screen.getByLabelText(/^Description column/), 'Narration');
    await user.type(screen.getByLabelText(/^Reference column/), 'Ref No');
    await user.type(screen.getByLabelText(/^Debit column/), 'Debit');
    await user.type(screen.getByLabelText(/^Credit column/), 'Credit');
    await user.click(screen.getByRole('button', { name: 'Set up the channel' }));

    await waitFor(() => expect(api.sent('POST', channels)).toHaveLength(1));
    const body = await bodyOf(api.sent('POST', channels)[0]);
    expect(body).toMatchObject({
      kind: 'Bank',
      name: 'HDFC current',
      accountLabel: '4521',
      dateWindowDays: 3,
      gateway: null,
    });
    expect(body.mapping).toMatchObject({
      headerRow: 1,
      dateColumn: 'Txn Date',
      dateFormat: 'dd/MM/yyyy',
      descriptionColumn: 'Narration',
      referenceColumn: 'Ref No',
      amountLayout: 'TwoColumns',
      amountColumn: null,
      debitColumn: 'Debit',
      creditColumn: 'Credit',
      valueDateColumn: null,
      balanceColumn: null,
    });
    expect(await screen.findByRole('table', { name: 'Settlement channels' })).toBeInTheDocument();
  }, 20_000);

  it('lists the channels in the Channels tab, which only the administrator opens', async () => {
    const api = routedApi({ [`GET ${channels}`]: () => json(200, page([bank])) });
    renderApp('/administration/channels', { session: admin, fetch: api.fetch });

    const tabs = await screen.findByRole('navigation', { name: 'Users and settings' });
    expect(within(tabs).getByRole('link', { name: 'Channels' })).toHaveAttribute('aria-current', 'page');
    const rows = within(await screen.findByRole('table', { name: 'Settlement channels' })).getAllByRole('row');
    expect(rows[1]).toHaveTextContent('HDFC current');
    expect(rows[1]).toHaveTextContent('…4521');
    expect(rows[1]).toHaveTextContent('Active');
  });
});

describe('importing a bank statement, for the accountant', () => {
  it('checks the file, names the refused row, and imports exactly the file the check read under a key', async () => {
    const api = routedApi({
      [`GET ${channels}`]: () => json(200, page([bank])),
      [`POST ${dryRun}`]: () => json(200, checked),
      [`POST ${imports}`]: () => json(201, imported),
      [`POST ${channels}/${bank.id}/matching-runs`]: () =>
        json(200, { linesRead: 4, matched: 3, proposed: 1, left: 0, skipped: 0 }),
    });
    const { user } = renderApp('/reconciliation/import', { session: accountant, fetch: api.fetch });

    const check = await screen.findByRole('button', { name: 'Check the file' });
    const importIt = screen.getByRole('button', { name: 'Import' });
    expect(check).toBeDisabled();
    await user.selectOptions(screen.getByLabelText(/^Bank account/), bank.id);
    await user.upload(screen.getByLabelText(/^Statement/), csv('hdfc-16-oct.csv', 'Txn Date,Narration\n'));
    await waitFor(() => expect(check).toBeEnabled());
    expect(importIt).toBeDisabled();

    await user.click(check);
    expect(await screen.findByText('The file can be imported')).toBeInTheDocument();
    expect(screen.getByText('New lines').nextElementSibling).toHaveTextContent('4');
    expect(screen.getByRole('heading', { name: '1 row cannot be read' })).toBeInTheDocument();
    expect(screen.getByText('the debit "abc" is not a number.')).toBeInTheDocument();
    expect(await bodyOf(api.sent('POST', dryRun)[0])).toEqual({
      channelId: bank.id,
      fileName: 'hdfc-16-oct.csv',
      text: 'Txn Date,Narration\n',
      fileHash: null,
    });

    await user.click(importIt);
    expect(await screen.findByText('Imported')).toBeInTheDocument();
    // Since 13.2: the import is followed by a matching run over the account, and its result is said.
    expect(await screen.findByText(/Matching: 3 matched, 1 proposed, 0 left for you\./)).toBeInTheDocument();
    expect(api.sent('POST', `${channels}/${bank.id}/matching-runs`)).toHaveLength(1);
    const sent = api.sent('POST', imports)[0];
    expect(sent?.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    expect((await bodyOf(sent)).fileHash).toBe(checked.fileHash);
    expect(screen.getByRole('link', { name: 'See the lines of this import' })).toHaveAttribute(
      'href',
      `/reconciliation?import=${imported.id}`,
    );
    expect(importIt).toBeDisabled();
  });

  it('offers no import for a file the account took before', async () => {
    const api = routedApi({
      [`GET ${channels}`]: () => json(200, page([bank])),
      [`POST ${dryRun}`]: () => json(200, { ...checked, alreadyImported: true, linesToAdd: 0, linesAlreadyHeld: 4 }),
    });
    const { user } = renderApp('/reconciliation/import', { session: accountant, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText(/^Bank account/), bank.id);
    await user.upload(screen.getByLabelText(/^Statement/), csv('again.csv', 'x\n'));
    await user.click(screen.getByRole('button', { name: 'Check the file' }));

    expect(await screen.findByText('This file was imported already')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import' })).toBeDisabled();
  });
});

describe('the statements, for whoever reads the reconciliation', () => {
  it('lists the latest imports and the lines, and a viewer cannot import', async () => {
    const api = routedApi({
      [`GET ${channels}`]: () => json(200, page([bank])),
      [`GET ${imports}`]: () => json(200, page([imported])),
      [`GET ${lines}`]: () => json(200, page([line])),
    });
    renderApp('/reconciliation?import=' + imported.id, { session: sessionFor(['Viewer']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { level: 1, name: 'Bank reconciliation' })).toBeInTheDocument();
    const importRows = within(await screen.findByRole('table', { name: 'Latest imports' })).getAllByRole('row');
    expect(importRows[1]).toHaveTextContent('hdfc-16-oct.csv');
    const lineRows = within(await screen.findByRole('table', { name: 'Statement lines' })).getAllByRole('row');
    expect(lineRows[1]).toHaveTextContent('NEFT CR RAVI KUMAR');
    expect(lineRows[1]).toHaveTextContent('12,000.00');
    expect(lineRows[1]).toHaveTextContent('Unmatched');
    expect(lineRows[1]).toHaveTextContent(
      'Proposed, 70 of 100: The amount ₹12,000.00 agrees with PAY/26-27/000233 of 16 Oct 2026, the same day. (and 1 more)',
    );
    expect(screen.queryByRole('button', { name: 'Match now' })).not.toBeInTheDocument();
    expect(screen.getByText(/The lines of one import/)).toBeInTheDocument();
    expect(new URL(api.sent('GET', lines)[0]!.url).searchParams.get('ImportId')).toBe(imported.id);
    expect(screen.queryByRole('link', { name: 'Import a statement' })).not.toBeInTheDocument();
  });
});

describe('matching from the lines, for the accountant', () => {
  it('runs matching over the account chosen and says what it did', async () => {
    const api = routedApi({
      [`GET ${channels}`]: () => json(200, page([bank])),
      [`GET ${imports}`]: () => json(200, page([imported])),
      [`GET ${lines}`]: () =>
        json(200, page([{ ...line, status: 'Matched', matchReason: 'The reference N3341 agrees.' }])),
      [`POST ${channels}/${bank.id}/matching-runs`]: () =>
        json(200, { linesRead: 9, matched: 2, proposed: 3, left: 3, skipped: 1 }),
    });
    const { user } = renderApp(`/reconciliation?channel=${bank.id}`, { session: accountant, fetch: api.fetch });

    const lineRows = within(await screen.findByRole('table', { name: 'Statement lines' })).getAllByRole('row');
    expect(lineRows[1]).toHaveTextContent('The reference N3341 agrees.');
    await user.click(screen.getByRole('button', { name: 'Match now' }));

    expect(
      await screen.findByText(
        'Matched the unmatched lines of HDFC current: 2 matched, 3 proposed, 3 left for you, 1 skipped, changed meanwhile.',
      ),
    ).toBeInTheDocument();
    expect(api.sent('POST', `${channels}/${bank.id}/matching-runs`)).toHaveLength(1);
  });

  it('keeps Match now closed until an account is chosen', async () => {
    const api = routedApi({
      [`GET ${channels}`]: () => json(200, page([bank])),
      [`GET ${imports}`]: () => json(200, page([])),
      [`GET ${lines}`]: () => json(200, page([])),
    });
    renderApp('/reconciliation', { session: accountant, fetch: api.fetch });

    expect(await screen.findByRole('button', { name: 'Match now' })).toBeDisabled();
    expect(screen.getByText('Choose an account to match its unmatched lines with the payments.')).toBeInTheDocument();
  });
});
