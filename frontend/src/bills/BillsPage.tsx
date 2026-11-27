import { Link } from 'react-router';
import { answer } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useCan } from '../auth/permissions';
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
import { usePatient } from '../patients/api';
import { usePayer } from '../payers/api';
import { billStatuses, billStatusLabels, type BillSummary } from './api';

const origins = ['Standard', 'Opening'] as const;
import { BillStatusBadge } from './BillStatusBadge';

/** Every filter of the bills the address may hold (ADR 0039 point 1; Prompt 12.2, ADR 0063). */
const keys = [
  'search',
  'status',
  'from',
  'to',
  'totalFrom',
  'totalTo',
  'balanceDueFrom',
  'balanceDueTo',
  'createdBy',
  'origin',
  'patientId',
  'payerId',
] as const;

/** The filters of the bar: a list opened for one patient or payer keeps them through "Clear filters". */
const clearable = keys.filter((key) => key !== 'patientId' && key !== 'payerId');

const filters: readonly FilterSpec<(typeof keys)[number]>[] = [
  { kind: 'search', key: 'search' },
  {
    kind: 'select',
    key: 'status',
    label: 'Status',
    options: billStatuses.map((status) => ({ value: status, label: billStatusLabels[status] })),
  },
  {
    kind: 'select',
    key: 'origin',
    label: 'Origin',
    options: [
      { value: 'Standard', label: 'Billed here' },
      { value: 'Opening', label: 'Brought in at the cut-over' },
    ],
  },
  { kind: 'dates', from: 'from', to: 'to', label: 'Bill date' },
  { kind: 'amounts', from: 'totalFrom', to: 'totalTo', label: 'Total' },
  { kind: 'amounts', from: 'balanceDueFrom', to: 'balanceDueTo', label: 'Balance due' },
];

function PatientName({ id }: { id: string }) {
  const patient = usePatient(id);
  return <>{patient.data?.fullName ?? '…'}</>;
}

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

const columns: readonly TableColumn<BillSummary>[] = [
  {
    id: 'number',
    header: 'Number',
    fixed: true,
    sort: 'number',
    cell: (bill) => <Link to={`/bills/${bill.id}`}>{bill.number ?? 'No number'}</Link>,
  },
  { id: 'billDate', header: 'Bill date', sort: 'billDate', cell: (bill) => formatDate(bill.billDate) },
  { id: 'patient', header: 'Patient', cell: (bill) => <PatientName id={bill.patientId} /> },
  { id: 'payer', header: 'Payer', cell: (bill) => <PayerName id={bill.payerId} /> },
  { id: 'total', header: 'Total (₹)', numeric: true, sort: 'total', cell: (bill) => formatDecimal(bill.total) },
  {
    id: 'balanceDue',
    header: 'Balance due (₹)',
    numeric: true,
    sort: 'balanceDue',
    cell: (bill) => formatDecimal(bill.balanceDue),
  },
  { id: 'status', header: 'Status', fixed: true, cell: (bill) => <BillStatusBadge status={bill.status} /> },
];

/**
 * The bills (US-20, US-23): newest bill date first unless another order is chosen; narrowed by any filter of the bar,
 * or to those of one patient or payer. The API filters, sorts and pages every bill; the screen shows its page
 * (Prompt 12.2, ADR 0063).
 */
export function BillsPage() {
  const canDraft = useCan('WriteBills');
  const client = useApiClient();
  const state = useListState(keys, clearable);
  const { values, sort, page } = state;
  const bills = useListQuery(['bills', 'list', values, sort, page], (signal) =>
    answer(
      client.GET('/api/v1/bills', {
        params: {
          query: {
            ...(known(values.status, billStatuses) === undefined ? {} : { Status: known(values.status, billStatuses) }),
            ...(values.from === '' ? {} : { BillDateFrom: values.from }),
            ...(values.to === '' ? {} : { BillDateTo: values.to }),
            ...(values.patientId === '' ? {} : { PatientId: values.patientId }),
            ...(values.payerId === '' ? {} : { PayerId: values.payerId }),
            ...(values.search === '' ? {} : { Search: values.search }),
            ...(values.totalFrom === '' ? {} : { TotalFrom: values.totalFrom }),
            ...(values.totalTo === '' ? {} : { TotalTo: values.totalTo }),
            ...(values.balanceDueFrom === '' ? {} : { BalanceDueFrom: values.balanceDueFrom }),
            ...(values.balanceDueTo === '' ? {} : { BalanceDueTo: values.balanceDueTo }),
            ...(values.createdBy === '' ? {} : { CreatedBy: values.createdBy }),
            ...(known(values.origin, origins) === undefined ? {} : { Origin: known(values.origin, origins) }),
            ...(sort === '' ? {} : { Sort: sort }),
            Page: page,
            PageSize: pageSize,
          },
        },
        signal,
      }),
    ),
  );
  const narrowed = values.patientId !== '' || values.payerId !== '';

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Bills"
        subtitle={
          narrowed ? (
            <>
              The bills of{' '}
              {values.patientId !== '' ? <PatientName id={values.patientId} /> : <PayerName id={values.payerId} />}.{' '}
              <button
                type="button"
                className={buttons.link}
                onClick={() => state.change({ patientId: '', payerId: '' })}
              >
                Show every bill
              </button>
            </>
          ) : (
            'Drafts, bills waiting for approval, and posted bills with what is still owed.'
          )
        }
        actions={
          canDraft && (
            <Link className={buttons.primary} to="/bills/new">
              New bill
            </Link>
          )
        }
      />
      <FilterBar state={state} filters={filters} busy={bills.isFetching}>
        <MakerFilter value={values.createdBy} onChange={(createdBy) => state.change({ createdBy })} />
      </FilterBar>
      {bills.data === undefined ? (
        <LoadState error={bills.error} what="bills" />
      ) : bills.data.items.length === 0 ? (
        <EmptyState icon="receipt">No bill matches.</EmptyState>
      ) : (
        <>
          <DataTable
            tableId="bills"
            label="Bills"
            columns={columns}
            rows={bills.data.items}
            keyOf={(bill) => bill.id}
            sort={sort}
            onSort={state.setSort}
            busy={bills.isFetching}
          />
          <Pagination page={page} totalCount={Number(bills.data.totalCount)} onChange={state.setPage} />
        </>
      )}
    </section>
  );
}
