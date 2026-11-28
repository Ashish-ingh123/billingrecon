import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router';
import { problemOf } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useCan } from '../auth/permissions';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import { EmptyState } from '../components/EmptyState';
import { Pagination } from '../components/ListControls';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { FilterBar, type FilterSpec } from '../components/table/FilterBar';
import { known, useListState } from '../components/table/listState';
import { formatDate, formatDecimal, formatInstant } from '../format';
import {
  directionLabels,
  directions,
  lineStatuses,
  lineStatusLabels,
  runMatching,
  runSummary,
  statementsChanged,
  useChannels,
  useImports,
  useLines,
  type LineListParams,
  type SettlementChannel,
  type StatementImport,
  type StatementLine,
} from './api';
import styles from './Reconciliation.module.css';

/** Every filter of the lines the address may hold (ADR 0063); `import` comes from the link of an import. */
const keys = ['search', 'channel', 'status', 'direction', 'from', 'to', 'import'] as const;

const count = (value: number | string) => Number(value).toLocaleString('en-IN');

function filtersOf(channels: readonly SettlementChannel[]): readonly FilterSpec<(typeof keys)[number]>[] {
  return [
    { kind: 'search', key: 'search', label: 'Reference or description' },
    {
      kind: 'select',
      key: 'channel',
      label: 'Account',
      options: channels
        .filter((channel) => channel.kind === 'Bank')
        .map((channel) => ({ value: channel.id, label: channel.name })),
    },
    {
      kind: 'select',
      key: 'status',
      label: 'Status',
      options: lineStatuses.map((status) => ({ value: status, label: lineStatusLabels[status].label })),
    },
    {
      kind: 'select',
      key: 'direction',
      label: 'Direction',
      options: directions.map((direction) => ({ value: direction, label: directionLabels[direction] })),
    },
    { kind: 'dates', from: 'from', to: 'to', label: 'Transaction date' },
  ];
}

const lineColumns = (nameOf: (id: string) => string): readonly TableColumn<StatementLine>[] => [
  {
    id: 'date',
    header: 'Date',
    fixed: true,
    sort: 'transactionDate',
    cell: (line) => formatDate(line.transactionDate),
  },
  { id: 'account', header: 'Account', cell: (line) => nameOf(line.channelId) },
  {
    id: 'description',
    header: 'Description',
    cell: (line) => <span className={styles.wrapCell}>{line.description}</span>,
  },
  { id: 'reference', header: 'Reference', cell: (line) => line.reference ?? '—' },
  { id: 'direction', header: 'Direction', cell: (line) => directionLabels[line.direction] },
  { id: 'amount', header: 'Amount (₹)', numeric: true, sort: 'amount', cell: (line) => formatDecimal(line.amount) },
  {
    id: 'balance',
    header: 'Balance (₹)',
    numeric: true,
    cell: (line) => (line.balance === null || line.balance === undefined ? '—' : formatDecimal(line.balance)),
  },
  {
    id: 'status',
    header: 'Status',
    fixed: true,
    cell: (line) => <Badge tone={lineStatusLabels[line.status].tone}>{lineStatusLabels[line.status].label}</Badge>,
  },
  // Since 13.2 (ADR 0066): why the line is matched, or the likeliest proposal and how many there are.
  {
    id: 'match',
    header: 'Match',
    cell: (line) => <span className={styles.wrapCell}>{matchText(line)}</span>,
  },
];

/** What a line's Match column says: the reason of its match, or its best proposal with its score. */
function matchText(line: StatementLine): string {
  if (line.matchReason !== null && line.matchReason !== undefined) {
    return line.matchReason;
  }

  if (line.proposalReason !== null && line.proposalReason !== undefined) {
    const more = Number(line.proposalCount) > 1 ? ` (and ${String(Number(line.proposalCount) - 1)} more)` : '';
    return `Proposed, ${String(line.proposalScore)} of 100: ${line.proposalReason}${more}`;
  }

  return '—';
}

const importColumns = (nameOf: (id: string) => string): readonly TableColumn<StatementImport>[] => [
  {
    id: 'file',
    header: 'File',
    fixed: true,
    cell: (item) => <Link to={`/reconciliation?import=${item.id}`}>{item.fileName}</Link>,
  },
  { id: 'account', header: 'Account', cell: (item) => nameOf(item.channelId) },
  {
    id: 'period',
    header: 'Period',
    cell: (item) =>
      item.firstDate === null || item.firstDate === undefined
        ? '—'
        : `${formatDate(item.firstDate)} to ${formatDate(item.lastDate)}`,
  },
  { id: 'added', header: 'Lines added', numeric: true, cell: (item) => count(item.linesAdded) },
  { id: 'held', header: 'Held already', numeric: true, cell: (item) => count(item.linesAlreadyHeld) },
  { id: 'refused', header: 'Rows refused', numeric: true, cell: (item) => count(item.rowsRefused) },
  { id: 'when', header: 'Imported', cell: (item) => formatInstant(item.createdAtUtc) },
];

/**
 * The hospital's bank statements (Prompt 13.1c, US-61; BR-1004, BR-1005; ADR 0065): the latest imports, and the lines
 * as they were read, filtered by account, status, direction, a period and a search of a reference or a description.
 * Since 13.2, a line says what it is matched to or what is proposed for it, and Match now runs matching over the account
 * chosen; matching by hand comes with 13.3.
 */
export function StatementsPage() {
  const canImport = useCan('ImportStatements');
  const channels = useChannels();
  const state = useListState(keys, ['search', 'channel', 'status', 'direction', 'from', 'to']);
  const { values, sort, page } = state;
  const params: LineListParams = {
    channelId: values.channel,
    importId: values.import,
    status: known(values.status, lineStatuses),
    direction: known(values.direction, directions),
    from: values.from,
    to: values.to,
    search: values.search,
    sort,
    page,
  };
  const lines = useLines(params);
  const imports = useImports(values.channel, 1);
  const all = channels.data ?? [];
  const nameOf = (id: string) => all.find((channel) => channel.id === id)?.name ?? '—';
  const client = useApiClient();
  const queryClient = useQueryClient();
  // Since 13.2 (ADR 0066): a matching run over the account chosen in the filters.
  const matching = useMutation({
    mutationFn: (channelId: string) => runMatching(client, channelId),
    onSettled: () => statementsChanged(queryClient),
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Bank reconciliation"
        subtitle="The bank statements, line by line, as the bank wrote them."
        actions={
          canImport && (
            <Link className={buttons.primary} to="/reconciliation/import">
              Import a statement
            </Link>
          )
        }
      />

      <section className={styles.section} aria-labelledby="imports-title">
        <h2 id="imports-title">Latest imports</h2>
        {imports.data === undefined ? (
          <LoadState error={imports.error} what="imports" />
        ) : imports.data.items.length === 0 ? (
          <EmptyState icon="inbox">No statement is imported yet.</EmptyState>
        ) : (
          <DataTable
            tableId="statement-imports"
            label="Latest imports"
            columns={importColumns(nameOf)}
            rows={imports.data.items}
            keyOf={(item) => item.id}
            busy={imports.isFetching}
          />
        )}
      </section>

      <section className={styles.section} aria-labelledby="lines-title">
        <h2 id="lines-title">Lines</h2>
        {values.import !== '' && (
          <p className={styles.note}>
            The lines of one import.{' '}
            <button type="button" className={buttons.link} onClick={() => state.change({ import: '' })}>
              Show every line
            </button>
          </p>
        )}
        <FilterBar state={state} filters={filtersOf(all)} busy={lines.isFetching} />
        {canImport && (
          <div className={styles.matchBar}>
            <button
              type="button"
              className={buttons.secondary}
              disabled={values.channel === '' || matching.isPending}
              aria-describedby="match-note"
              onClick={() => matching.mutate(values.channel)}
            >
              {matching.isPending ? 'Matching…' : 'Match now'}
            </button>
            <p id="match-note" className={styles.note} aria-live="polite">
              {matching.data !== undefined
                ? `Matched the unmatched lines of ${nameOf(values.channel)}: ${runSummary(matching.data)}`
                : values.channel === ''
                  ? 'Choose an account to match its unmatched lines with the payments.'
                  : 'Matches what is certain and proposes the rest. Nothing is posted.'}
            </p>
          </div>
        )}
        {matching.error !== null && <ProblemAlert problem={problemOf(matching.error)} />}
        {lines.data === undefined ? (
          <LoadState error={lines.error} what="lines" />
        ) : lines.data.items.length === 0 ? (
          <EmptyState icon="inbox">No line matches.</EmptyState>
        ) : (
          <>
            <DataTable
              tableId="statement-lines"
              label="Statement lines"
              columns={lineColumns(nameOf)}
              rows={lines.data.items}
              keyOf={(line) => line.id}
              sort={sort}
              onSort={state.setSort}
              busy={lines.isFetching}
            />
            <Pagination page={page} totalCount={Number(lines.data.totalCount)} onChange={state.setPage} />
          </>
        )}
      </section>
    </section>
  );
}
