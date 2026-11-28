import { useMutation } from '@tanstack/react-query';
import { useId, useState, type FormEvent } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { answer, problemOf } from '../api/call';
import buttons from '../components/buttons.module.css';
import fieldStyles from '../components/Field.module.css';
import forms from '../components/forms.module.css';
import listStyles from '../components/ListControls.module.css';
import { RecordPicker } from '../components/RecordPicker';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { filtersInUse } from '../components/table/listState';
import { formatDate } from '../format';
import { kindLabel, usePayer, type Payer } from '../payers/api';
import { saveCsv, type CellKind } from './csv';
import { ExportChangedError, ExportTooLargeError } from './exportReport';
import styles from './Reports.module.css';
import { shown, type ReportColumn, type ReportTotal } from './view';

const numeric = (kind: CellKind) => kind === 'amount' || kind === 'count';

/**
 * The count of a report's filters in use, in words, and "Clear filters" (Prompt 12.2, ADR 0063): on a line of its own,
 * always there, so that nothing moves when it changes. The period of a register is its scope, not a filter, and stays.
 */
export function FiltersInUse({ count, onClear }: { count: number; onClear: () => void }) {
  return (
    <p className={listStyles.inUse} aria-live="polite">
      <span>{filtersInUse(count)}</span>
      {count > 0 && (
        <button type="button" className={buttons.link} onClick={onClear}>
          Clear filters
        </button>
      )}
    </p>
  );
}

/**
 * The rows of a report and, in its footer, the totals of every page as the server answered them (ADR 0043 point 3): the
 * screen never adds the rows up itself. Drawn by the shared table (Prompt 12.2, ADR 0063): a column the API can order by
 * (`sorts`, by header) sorts from its header; the "Columns" menu hides and moves the columns on screen and in print, and
 * a hidden column's total is hidden with it. The first column and the status are never hidden. The CSV is written from
 * the report's own columns, every one of them, whatever is hidden here.
 */
export function ReportTable<T extends object>({
  label,
  columns,
  rows,
  keyOf,
  count,
  totals,
  sorts = {},
  sort = '',
  onSort,
}: {
  label: string;
  columns: readonly ReportColumn<T>[];
  rows: readonly T[];
  keyOf: (row: T) => string;
  /** What the footer's first cell says: "4 bills". */
  count: string;
  totals: readonly ReportTotal[];
  /** The API's name of the order of each column that has one, by the column's header. */
  sorts?: Readonly<Record<string, string>>;
  sort?: string;
  onSort?: (sort: string) => void;
}) {
  const under = new Map(totals.filter((total) => total.under !== undefined).map((total) => [total.under, total]));
  const beside = totals.filter((total) => total.under === undefined);
  const first = columns[0]?.header;
  const tableColumns: TableColumn<T>[] = columns.map((column) => ({
    id: column.header,
    header: column.header,
    numeric: numeric(column.kind),
    fixed: column.header === first || column.header === 'Status',
    ...(sorts[column.header] === undefined ? {} : { sort: sorts[column.header] }),
    cell: (row: T) => (column.show === undefined ? shown(column.kind, column.value(row)) : column.show(row)),
  }));
  const cells = Object.fromEntries(
    columns.flatMap((column) => {
      const total = under.get(column.header);
      return total === undefined ? [] : [[column.header, shown(total.kind, total.value)]];
    }),
  );

  return (
    <>
      <DataTable
        tableId={`report-${label.toLowerCase().replaceAll(' ', '-')}`}
        label={label}
        columns={tableColumns}
        rows={rows}
        keyOf={keyOf}
        sort={sort}
        {...(onSort === undefined ? {} : { onSort })}
        foot={{ label: count, cells }}
        frameClassName={styles.frame}
        footClassName={styles.totals}
      />
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
