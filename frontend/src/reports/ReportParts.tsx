import { useMutation } from '@tanstack/react-query';
import { useId, useState, type FormEvent } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { answer, problemOf } from '../api/call';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import listStyles from '../components/ListControls.module.css';
import { RecordPicker } from '../components/RecordPicker';
import { formatDate } from '../format';
import { kindLabel, usePayer, type Payer } from '../payers/api';
import { saveCsv, type CellKind } from './csv';
import { ExportChangedError, ExportTooLargeError } from './exportReport';
import styles from './Reports.module.css';
import { shown, type ReportColumn, type ReportTotal } from './view';

const numeric = (kind: CellKind) => kind === 'amount' || kind === 'count';

/**
 * The rows of a report and, in its footer, the totals of every page as the server answered them (ADR 0043 point 3): the
 * screen never adds the rows up itself.
 */
export function ReportTable<T>({
  label,
  columns,
  rows,
  keyOf,
  count,
  totals,
}: {
  label: string;
  columns: readonly ReportColumn<T>[];
  rows: readonly T[];
  keyOf: (row: T) => string;
  /** What the footer's first cell says: "4 bills". */
  count: string;
  totals: readonly ReportTotal[];
}) {
  const under = new Map(totals.filter((total) => total.under !== undefined).map((total) => [total.under, total]));
  const beside = totals.filter((total) => total.under === undefined);

  return (
    <>
      <div className={`${table.wrap} ${styles.frame}`}>
        <table className={table.table} aria-label={label}>
          <thead>
            <tr>
              {columns.map((column) => (
                <th key={column.header} scope="col" className={numeric(column.kind) ? table.number : undefined}>
                  {column.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={keyOf(row)}>
                {columns.map((column) => (
                  <td key={column.header} className={numeric(column.kind) ? table.number : undefined}>
                    {column.show === undefined ? shown(column.kind, column.value(row)) : column.show(row)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
          <tfoot className={styles.totals}>
            <tr>
              {columns.map((column, index) => {
                const total = under.get(column.header);
                return index === 0 ? (
                  <th key={column.header} scope="row">
                    {count}
                  </th>
                ) : (
                  <td key={column.header} className={numeric(column.kind) ? table.number : undefined}>
                    {total === undefined ? '' : shown(total.kind, total.value)}
                  </td>
                );
              })}
            </tr>
          </tfoot>
        </table>
      </div>
      {beside.length > 0 && (
        <dl className={styles.beside}>
          {beside.map((total) => (
            <div key={total.label}>
              <dt>{total.label}</dt>
              <dd>{shown(total.kind, total.value)}</dd>
            </div>
          ))}
        </dl>
      )}
    </>
  );
}

/**
 * The export of a report (ADR 0043 point 1): reads every row of the filter, writes the CSV and hands it to the browser;
 * says how many rows went out, or why none did.
 */
export function ExportButton({
  fileName,
  build,
}: {
  fileName: string;
  /** Reads the report and writes its CSV, telling `progress` after each page it reads. */
  build: (progress: (page: number, pages: number) => void) => Promise<{ text: string; rows: number }>;
}) {
  const [outcome, setOutcome] = useState<{ ok: boolean; message: string } | null>(null);
  // How far the reading has come: shown while the export runs (Prompt 9.4b).
  const [reading, setReading] = useState<{ page: number; pages: number } | null>(null);
  const exporting = useMutation({
    mutationFn: () => build((page, pages) => setReading({ page, pages })),
    onMutate: () => {
      setOutcome(null);
      setReading(null);
    },
    onSettled: () => setReading(null),
    onSuccess: ({ text, rows }) => {
      saveCsv(fileName, text);
      setOutcome({ ok: true, message: `Exported ${String(rows)} ${rows === 1 ? 'row' : 'rows'} to ${fileName}.` });
    },
    onError: (error) =>
      setOutcome({
        ok: false,
        message:
          error instanceof ExportTooLargeError || error instanceof ExportChangedError
            ? error.message
            : `The export failed: ${problemOf(error).detail ?? problemOf(error).title}`,
      }),
  });

  return (
    <div className={styles.export}>
      <button
        type="button"
        className={buttons.secondary}
        disabled={exporting.isPending}
        aria-busy={exporting.isPending}
        onClick={() => exporting.mutate()}
      >
        {exporting.isPending ? 'Exporting…' : 'Export CSV'}
      </button>
      {exporting.isPending && (
        <p className={styles.progress} role="status">
          {reading === null
            ? 'Reading the report…'
            : `Reading page ${String(reading.page)} of ${String(reading.pages)}…`}
          <progress className={styles.progressBar} max={reading?.pages ?? 1} value={reading?.page} aria-hidden="true" />
        </p>
      )}
      {outcome !== null && (
        <p className={outcome.ok ? forms.note : fieldStyles.error} role={outcome.ok ? 'status' : 'alert'}>
          {outcome.message}
        </p>
      )}
    </div>
  );
}

/**
 * The period of a register (BR-003): two business dates in India, both or neither; neither is the current month, which
 * the period the API answered then names.
 */
export function PeriodFilter({
  from,
  to,
  answered,
  onChange,
}: {
  from: string;
  to: string;
  answered: { from: string; to: string } | null | undefined;
  onChange: (period: { from: string; to: string }) => void;
}) {
  const [start, setStart] = useState(from);
  const [end, setEnd] = useState(to);
  const [error, setError] = useState<string | null>(null);
  const fromId = useId();
  const toId = useId();

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if ((start === '') !== (end === '')) {
      setError('Give both dates, or neither for the current month.');
      return;
    }

    setError(null);
    onChange({ from: start, to: end });
  };

  return (
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
      {/* The period the report answered: its line stands from the first paint, so nothing moves when it is filled. */}
      <p className={styles.period}>
        {answered !== null && answered !== undefined && (
          <>
            {formatDate(answered.from)} to {formatDate(answered.to)}
            {from === '' && ', this month in India'}
          </>
        )}
      </p>
    </form>
  );
}

/** A payer to narrow a report to, any payer, active or not: a deactivated payer's balances are still reported (US-12). */
export function PayerFilter({ payerId, onChange }: { payerId: string; onChange: (payerId: string) => void }) {
  const client = useApiClient();
  const payer = usePayer(payerId, payerId !== '');

  return (
    <div className={styles.payer}>
      <RecordPicker<Payer>
        kind="report-payer"
        label="Payer"
        hint="Leave it empty for every payer."
        selected={payerId === '' ? null : (payer.data ?? null)}
        onSelect={(chosen) => onChange(chosen?.id ?? '')}
        find={async (text, signal) =>
          (
            await answer(
              client.GET('/api/v1/payers', { params: { query: { Search: text, Page: 1, PageSize: 10 } }, signal }),
            )
          ).items
        }
        describe={(chosen) => `${chosen.name} (${kindLabel(chosen.kind)})`}
        keyOf={(chosen) => chosen.id}
      />
    </div>
  );
}
