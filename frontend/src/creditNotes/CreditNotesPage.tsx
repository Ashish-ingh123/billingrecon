import { Link } from 'react-router';
import { answer } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useBill } from '../bills/api';
import { Badge } from '../components/Badge';
import buttons from '../components/buttons.module.css';
import { EmptyState } from '../components/EmptyState';
import { pageSize } from '../components/listParams';
import { Pagination } from '../components/ListControls';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { DataTable, type TableColumn } from '../components/table/DataTable';
import { FilterBar, type FilterSpec } from '../components/table/FilterBar';
import { known, useListState } from '../components/table/listState';
import { MakerFilter } from '../components/table/MakerFilter';
import { useListQuery } from '../components/table/useListQuery';
import { formatDate, formatDecimal } from '../format';
import { usePayer } from '../payers/api';
import { creditNoteStatuses, creditNoteStatusLabels, type CreditNoteSummary } from './api';

/** Every filter of the credit notes the address may hold (Prompt 12.2, ADR 0063). */
const keys = ['search', 'status', 'from', 'to', 'totalFrom', 'totalTo', 'createdBy', 'billId', 'payerId'] as const;
const clearable = keys.filter((key) => key !== 'billId' && key !== 'payerId');

const filters: readonly FilterSpec<(typeof keys)[number]>[] = [
  { kind: 'search', key: 'search' },
  {
    kind: 'select',
    key: 'status',
    label: 'Status',
    options: creditNoteStatuses.map((status) => ({ value: status, label: creditNoteStatusLabels[status].label })),
  },
  { kind: 'dates', from: 'from', to: 'to', label: 'Date' },
  { kind: 'amounts', from: 'totalFrom', to: 'totalTo', label: 'Total' },
];

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

function BillNumber({ id, link = true }: { id: string; link?: boolean }) {
  const bill = useBill(id);
  const number = bill.data?.number ?? '…';
  return link ? <Link to={`/bills/${id}`}>{number}</Link> : <>{number}</>;
}

const columns: readonly TableColumn<CreditNoteSummary>[] = [
  {
    id: 'number',
    header: 'Number',
    fixed: true,
    sort: 'number',
    cell: (creditNote) => <Link to={`/credit-notes/${creditNote.id}`}>{creditNote.number ?? 'No number'}</Link>,
  },
  {
    id: 'date',
    header: 'Date',
    sort: 'creditNoteDate',
    cell: (creditNote) => formatDate(creditNote.creditNoteDate),
  },
  { id: 'bill', header: 'Bill', cell: (creditNote) => <BillNumber id={creditNote.billId} /> },
  { id: 'payer', header: 'Payer', cell: (creditNote) => <PayerName id={creditNote.payerId} /> },
  {
    id: 'total',
    header: 'Total (₹)',
    numeric: true,
    sort: 'total',
    cell: (creditNote) => formatDecimal(creditNote.total),
  },
  {
    id: 'available',
    header: 'Available (₹)',
    numeric: true,
    sort: 'availableAmount',
    cell: (creditNote) => (creditNote.status === 'Posted' ? formatDecimal(creditNote.availableAmount) : '—'),
  },
  {
    id: 'status',
    header: 'Status',
    fixed: true,
    cell: (creditNote) => (
      <Badge
        tone={creditNoteStatusLabels[creditNote.status].tone}
        icon={creditNoteStatusLabels[creditNote.status].icon}
      >
        {creditNoteStatusLabels[creditNote.status].label}
      </Badge>
    ),
  },
];

/**
 * The credit notes (US-24): drafts, those waiting for approval, posted and voided, narrowed by any filter of the bar,
 * for one bill or one payer. A credit note is drafted from the page of the posted bill it corrects (ADR 0042), so this
 * list offers no new one. The API filters, sorts and pages (Prompt 12.2, ADR 0063).
 */
export function CreditNotesPage() {
  const client = useApiClient();
  const state = useListState(keys, clearable);
  const { values, sort, page } = state;
  const status = known(values.status, creditNoteStatuses);
  const creditNotes = useListQuery(['credit-notes', 'list', values, sort, page], (signal) =>
    answer(
      client.GET('/api/v1/credit-notes', {
        params: {
          query: {
            ...(status === undefined ? {} : { Status: status }),
            ...(values.billId === '' ? {} : { BillId: values.billId }),
            ...(values.payerId === '' ? {} : { PayerId: values.payerId }),
            ...(values.search === '' ? {} : { Search: values.search }),
            ...(values.from === '' ? {} : { CreditNoteDateFrom: values.from }),
            ...(values.to === '' ? {} : { CreditNoteDateTo: values.to }),
            ...(values.totalFrom === '' ? {} : { TotalFrom: values.totalFrom }),
            ...(values.totalTo === '' ? {} : { TotalTo: values.totalTo }),
            ...(values.createdBy === '' ? {} : { CreatedBy: values.createdBy }),
            ...(sort === '' ? {} : { Sort: sort }),
            Page: page,
            PageSize: pageSize,
          },
        },
        signal,
      }),
    ),
  );

  const showAll = (
    <button type="button" className={buttons.link} onClick={() => state.change({ billId: '', payerId: '' })}>
      Show every credit note
    </button>
  );

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Credit notes"
        subtitle={
          values.billId !== '' ? (
            <>
              The credit notes of bill <BillNumber id={values.billId} link={false} />. {showAll}
            </>
          ) : values.payerId !== '' ? (
            <>
              The credit notes of <PayerName id={values.payerId} />. {showAll}
            </>
          ) : (
            'Corrections of posted bills. A credit note is drafted from the page of the bill it corrects.'
          )
        }
      />
      <FilterBar state={state} filters={filters} busy={creditNotes.isFetching}>
        <MakerFilter value={values.createdBy} onChange={(createdBy) => state.change({ createdBy })} />
      </FilterBar>
      {creditNotes.data === undefined ? (
        <LoadState error={creditNotes.error} what="credit notes" />
      ) : creditNotes.data.items.length === 0 ? (
        <EmptyState icon="receipt-minus">No credit note matches.</EmptyState>
      ) : (
        <>
          <DataTable
            tableId="credit-notes"
            label="Credit notes"
            columns={columns}
            rows={creditNotes.data.items}
            keyOf={(creditNote) => creditNote.id}
            sort={sort}
            onSort={state.setSort}
            busy={creditNotes.isFetching}
          />
          <Pagination page={page} totalCount={Number(creditNotes.data.totalCount)} onChange={state.setPage} />
        </>
      )}
    </section>
  );
}
