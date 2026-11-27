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
import { adjustmentKinds, adjustmentStatuses, adjustmentStatusLabels, kindLabels, type Adjustment } from './api';

/** Every filter of the adjustments the address may hold (Prompt 12.2, ADR 0063). */
const keys = [
  'search',
  'status',
  'kind',
  'from',
  'to',
  'amountFrom',
  'amountTo',
  'createdBy',
  'billId',
  'payerId',
] as const;
const clearable = keys.filter((key) => key !== 'billId' && key !== 'payerId');

const filters: readonly FilterSpec<(typeof keys)[number]>[] = [
  { kind: 'search', key: 'search' },
  {
    kind: 'select',
    key: 'status',
    label: 'Status',
    options: adjustmentStatuses.map((status) => ({ value: status, label: adjustmentStatusLabels[status].label })),
  },
  {
    kind: 'select',
    key: 'kind',
    label: 'Kind',
    options: adjustmentKinds.map((kind) => ({ value: kind, label: kindLabels[kind] })),
  },
  { kind: 'dates', from: 'from', to: 'to', label: 'Date' },
  { kind: 'amounts', from: 'amountFrom', to: 'amountTo', label: 'Amount' },
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

const columns: readonly TableColumn<Adjustment>[] = [
  {
    id: 'number',
    header: 'Number',
    fixed: true,
    sort: 'number',
    cell: (adjustment) => <Link to={`/adjustments/${adjustment.id}`}>{adjustment.number ?? 'No number'}</Link>,
  },
  {
    id: 'date',
    header: 'Date',
    sort: 'adjustmentDate',
    cell: (adjustment) => formatDate(adjustment.adjustmentDate),
  },
  { id: 'kind', header: 'Kind', cell: (adjustment) => kindLabels[adjustment.kind] },
  { id: 'bill', header: 'Bill', cell: (adjustment) => <BillNumber id={adjustment.billId} /> },
  { id: 'payer', header: 'Payer', cell: (adjustment) => <PayerName id={adjustment.payerId} /> },
  {
    id: 'amount',
    header: 'Amount (₹)',
    numeric: true,
    sort: 'amount',
    cell: (adjustment) => formatDecimal(adjustment.amount),
  },
  {
    id: 'status',
    header: 'Status',
    fixed: true,
    cell: (adjustment) => (
      <Badge
        tone={adjustmentStatusLabels[adjustment.status].tone}
        icon={adjustmentStatusLabels[adjustment.status].icon}
      >
        {adjustmentStatusLabels[adjustment.status].label}
      </Badge>
    ),
  },
];

/**
 * The adjustments (BR-501 to BR-503): discounts, write-offs and roundings, narrowed by any filter of the bar, for one
 * bill or one payer. An adjustment is drafted from the page of the posted bill it lowers (ADR 0042), so this list
 * offers no new one. The API filters, sorts and pages (Prompt 12.2, ADR 0063).
 */
export function AdjustmentsPage() {
  const client = useApiClient();
  const state = useListState(keys, clearable);
  const { values, sort, page } = state;
  const status = known(values.status, adjustmentStatuses);
  const kind = known(values.kind, adjustmentKinds);
  const adjustments = useListQuery(['adjustments', 'list', values, sort, page], (signal) =>
    answer(
      client.GET('/api/v1/adjustments', {
        params: {
          query: {
            ...(status === undefined ? {} : { Status: status }),
            ...(kind === undefined ? {} : { Kind: kind }),
            ...(values.billId === '' ? {} : { BillId: values.billId }),
            ...(values.payerId === '' ? {} : { PayerId: values.payerId }),
            ...(values.search === '' ? {} : { Search: values.search }),
            ...(values.from === '' ? {} : { AdjustmentDateFrom: values.from }),
            ...(values.to === '' ? {} : { AdjustmentDateTo: values.to }),
            ...(values.amountFrom === '' ? {} : { AmountFrom: values.amountFrom }),
            ...(values.amountTo === '' ? {} : { AmountTo: values.amountTo }),
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
      Show every adjustment
    </button>
  );

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Adjustments"
        subtitle={
          values.billId !== '' ? (
            <>
              The adjustments of bill <BillNumber id={values.billId} link={false} />. {showAll}
            </>
          ) : values.payerId !== '' ? (
            <>
              The adjustments of <PayerName id={values.payerId} />. {showAll}
            </>
          ) : (
            'Discounts, write-offs and roundings of posted bills. An adjustment is drafted from the page of the bill it lowers.'
          )
        }
      />
      <FilterBar state={state} filters={filters} busy={adjustments.isFetching}>
        <MakerFilter value={values.createdBy} onChange={(createdBy) => state.change({ createdBy })} />
      </FilterBar>
      {adjustments.data === undefined ? (
        <LoadState error={adjustments.error} what="adjustments" />
      ) : adjustments.data.items.length === 0 ? (
        <EmptyState icon="sliders">No adjustment matches.</EmptyState>
      ) : (
        <>
          <DataTable
            tableId="adjustments"
            label="Adjustments"
            columns={columns}
            rows={adjustments.data.items}
            keyOf={(adjustment) => adjustment.id}
            sort={sort}
            onSort={state.setSort}
            busy={adjustments.isFetching}
          />
          <Pagination page={page} totalCount={Number(adjustments.data.totalCount)} onChange={state.setPage} />
        </>
      )}
    </section>
  );
}
