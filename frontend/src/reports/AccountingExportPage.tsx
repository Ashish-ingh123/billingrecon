import { useMutation } from '@tanstack/react-query';
import { useId, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { ApiError, answer, problemOf } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import { problemFrom, unreachableProblem } from '../api/problem';
import { useSession } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, formatMoney, todayInIndia } from '../format';
import {
  exportLedgerLabels,
  type AccountingExportSummary as Summary,
  type ExportLedgerTotal as LedgerTotal,
} from './accountingExport';
import { useReport } from './api';
import { saveFile } from './csv';
import { ReportTable } from './ReportParts';
import { counted, useReportQuery, type ReportColumn, type ReportTotal } from './view';
import styles from './Reports.module.css';

/** The first day of the month of a date in India, `YYYY-MM-DD`. */
function firstOfMonth(date: string): string {
  return `${date.slice(0, 7)}-01`;
}

function readSummary(client: ApiClient, from: string, to: string) {
  return answer(client.GET('/api/v1/reports/accounting-export/summary', { params: { query: { from, to } } }));
}

/** The file of a period, as the API answers it, or the API's refusal as every other is shown. */
async function readFile(client: ApiClient, from: string, to: string): Promise<{ file: Blob; name: string }> {
  let result: { data?: unknown; error?: unknown; response: Response };
  try {
    result = await client.GET('/api/v1/reports/accounting-export', {
      params: { query: { from, to } },
      parseAs: 'blob',
    });
  } catch {
    throw new ApiError(unreachableProblem());
  }

  if (!result.response.ok || !(result.data instanceof Blob)) {
    throw new ApiError(problemFrom(result.response, result.error));
  }

  const named = /filename="?([^";]+)"?/.exec(result.response.headers.get('Content-Disposition') ?? '');
  return { file: result.data, name: named?.[1] ?? `billingrecon-tally-${from}-${to}.xml` };
}

const ledgerColumns: readonly ReportColumn<LedgerTotal>[] = [
  { header: 'Ledger', kind: 'text', value: (row) => exportLedgerLabels[row.ledger] },
  {
    header: 'Name in the books',
    kind: 'text',
    value: (row) => row.name ?? '',
    show: (row) => (row.name === null || row.name === undefined ? <em>Not named yet</em> : row.name),
  },
  { header: 'Debit (₹)', kind: 'amount', value: (row) => row.debit },
  { header: 'Credit (₹)', kind: 'amount', value: (row) => row.credit },
];

function totalsOf(summary: Summary): ReportTotal[] {
  return [
    { label: 'Debit (₹)', kind: 'amount', value: summary.controlTotal, under: 'Debit (₹)' },
    { label: 'Credit (₹)', kind: 'amount', value: summary.controlTotal, under: 'Credit (₹)' },
  ];
}

/**
 * The accountant's export (US-52, BR-904; ADR 0062): the vouchers of a period of business dates as the file Tally
 * imports. Before the file, the summary: how many vouchers, what each ledger of the accountant's books receives, the
 * control total the accountant compares with the package's, and the ledgers the hospital has not named yet, without
 * which no file is made.
 */
export function AccountingExportPage() {
  const client = useApiClient();
  const { roles } = useSession();
  const fromId = useId();
  const toId = useId();
  const { values, change } = useReportQuery(['from', 'to'] as const);
  const [today] = useState(() => todayInIndia());
  const from = values.from === '' ? firstOfMonth(today) : values.from;
  const to = values.to === '' ? today : values.to;
  const [start, setStart] = useState(from);
  const [end, setEnd] = useState(to);
  const [error, setError] = useState<string | null>(null);
  const summary = useReport('accounting-export', { from, to }, (api) => readSummary(api, from, to));
  const [outcome, setOutcome] = useState<{ ok: boolean; message: string } | null>(null);

  const downloading = useMutation({
    mutationFn: () => readFile(client, from, to),
    onMutate: () => setOutcome(null),
    onSuccess: ({ file, name }) => {
      saveFile(name, file);
      setOutcome({ ok: true, message: `Saved ${name}. Import it into the accountant's package once.` });
    },
    onError: (failure) => {
      const problem = problemOf(failure);
      setOutcome({ ok: false, message: problem.detail ?? problem.title });
    },
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (start === '' || end === '') {
      setError('Give both dates.');
      return;
    }

    setError(null);
    change({ from: start, to: end });
  };

  const missing = summary.data?.missing ?? [];
  const canName = can('ManageTenant', roles);

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Accountant's export"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="The vouchers of a period for the accountant's package: one balanced voucher for each posting of the ledger, numbered as our document."
      />
      <div className={styles.toolbar}>
        <form className={listStyles.filters} aria-label="Period" onSubmit={submit}>
          <div className={listStyles.field}>
            <label htmlFor={fromId}>From</label>
            <input
              id={fromId}
              className={listStyles.input}
              type="date"
              value={start}
              onChange={(event) => setStart(event.target.value)}
            />
          </div>
          <div className={listStyles.field}>
            <label htmlFor={toId}>To</label>
            <input
              id={toId}
              className={listStyles.input}
              type="date"
              value={end}
              onChange={(event) => setEnd(event.target.value)}
            />
          </div>
          <button type="submit" className={buttons.secondary}>
            Show
          </button>
          {error !== null && (
            <p className={fieldStyles.error} role="alert">
              {error}
            </p>
          )}
          <p className={styles.period}>
            {summary.data !== undefined && (
              <>
                {formatDate(summary.data.from)} to {formatDate(summary.data.to)}
              </>
            )}
          </p>
        </form>
        <div className={styles.export}>
          <button
            type="button"
            className={buttons.primary}
            disabled={summary.data === undefined || missing.length > 0 || downloading.isPending}
            aria-busy={downloading.isPending}
            onClick={() => downloading.mutate()}
          >
            {downloading.isPending ? 'Preparing the file…' : 'Download the Tally file'}
          </button>
          {outcome !== null && (
            <p className={outcome.ok ? forms.note : fieldStyles.error} role={outcome.ok ? 'status' : 'alert'}>
              {outcome.message}
            </p>
          )}
        </div>
      </div>
      {summary.data === undefined ? (
        <LoadState error={summary.error} what="the export" />
      ) : (
        <>
          {missing.length > 0 && (
            <Banner tone="warning" title="Some ledgers have no name yet">
              The period needs a name in the accountant's books for:{' '}
              {missing.map((ledger) => exportLedgerLabels[ledger]).join(', ')}.
              {canName ? (
                <>
                  {' '}
                  Name them under <Link to="/administration/accounting">Users and settings, Accounts</Link>.
                </>
              ) : (
                ' Ask your administrator to name them under Users and settings, Accounts.'
              )}
            </Banner>
          )}
          <dl className={styles.figures}>
            <div>
              <dt>Vouchers</dt>
              <dd>
                {counted(summary.data.vouchers, 'voucher', 'vouchers')}: {String(summary.data.receipts)} receipts,{' '}
                {String(summary.data.payments)} payments, {String(summary.data.journals)} journals
              </dd>
            </div>
            <div>
              <dt>Control total</dt>
              <dd>{formatMoney(summary.data.controlTotal)}</dd>
            </div>
            <div>
              <dt>The ledger's movements</dt>
              <dd>{formatMoney(summary.data.ledgerTotal)}</dd>
            </div>
          </dl>
          <ReportTable
            label="What each ledger receives"
            columns={ledgerColumns}
            rows={summary.data.ledgers}
            keyOf={(row) => row.ledger}
            count={counted(summary.data.ledgers.length, 'ledger', 'ledgers')}
            totals={totalsOf(summary.data)}
          />
          <p className={styles.period}>
            The control total is the debits of every voucher, which equal their credits and the ledger's movements of
            the period. After the import, the package's total of the period is this figure. Import each period once: a
            file imported twice may double its vouchers.
          </p>
        </>
      )}
    </section>
  );
}
