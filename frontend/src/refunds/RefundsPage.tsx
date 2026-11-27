import { Link } from 'react-router';
import { answer } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
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
import { methodLabels, paymentMethods } from '../payments/api';
import { usePayer } from '../payers/api';
import { refundStatuses, refundStatusLabels, type Refund } from './api';

/** Every filter of the refunds the address may hold (Prompt 12.2, ADR 0063). */
const keys = ['search', 'status', 'method', 'from', 'to', 'amountFrom', 'amountTo', 'createdBy', 'payerId'] as const;
const clearable = keys.filter((key) => key !== 'payerId');

const filters: readonly FilterSpec<(typeof keys)[number]>[] = [
  { kind: 'search', key: 'search' },
  {
    kind: 'select',
    key: 'status',
    label: 'Status',
    options: refundStatuses.map((status) => ({ value: status, label: refundStatusLabels[status].label })),
  },
  {
    kind: 'select',
    key: 'method',
    label: 'Method',
    options: paymentMethods.map((method) => ({ value: method, label: methodLabels[method] })),
  },
  { kind: 'dates', from: 'from', to: 'to', label: 'Refund date' },
  { kind: 'amounts', from: 'amountFrom', to: 'amountTo', label: 'Amount' },
];

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

const columns: readonly TableColumn<Refund>[] = [
  {
    id: 'number',
    header: 'Number',
    fixed: true,
    sort: 'number',
    cell: (refund) => <Link to={`/refunds/${refund.id}`}>{refund.number ?? 'No number'}</Link>,
  },
  { id: 'date', header: 'Refund date', sort: 'refundDate', cell: (refund) => formatDate(refund.refundDate) },
  { id: 'payer', header: 'Payer', cell: (refund) => <PayerName id={refund.payerId} /> },
  {
    id: 'source',
    header: 'From',
    cell: (refund) => (refund.sourceKind === 'Payment' ? 'A payment' : 'A credit note'),
  },
  { id: 'method', header: 'Method', cell: (refund) => methodLabels[refund.method] },
  { id: 'amount', header: 'Amount (₹)', numeric: true, sort: 'amount', cell: (refund) => formatDecimal(refund.amount) },
  {
    id: 'status',
    header: 'Status',
    fixed: true,
    cell: (refund) => (
      <Badge tone={refundStatusLabels[refund.status].tone} icon={refundStatusLabels[refund.status].icon}>
        {refundStatusLabels[refund.status].label}
      </Badge>
    ),
  },
];

/**
 * The refunds (US-34): drafts, those waiting for approval, posted and voided, narrowed by any filter of the bar, or for
 * one payer. A refund is drafted from its source, a payment or a credit note (ADR 0041 point 2), so this list offers no
 * new refund. The API filters, sorts and pages (Prompt 12.2, ADR 0063).
 */
export function RefundsPage() {
  const client = useApiClient();
  const state = useListState(keys, clearable);
  const { values, sort, page } = state;
  const status = known(values.status, refundStatuses);
  const method = known(values.method, paymentMethods);
  const refunds = useListQuery(['refunds', 'list', values, sort, page], (signal) =>
    answer(
      client.GET('/api/v1/refunds', {
        params: {
          query: {
            ...(status === undefined ? {} : { Status: status }),
            ...(values.payerId === '' ? {} : { PayerId: values.payerId }),
            ...(values.search === '' ? {} : { Search: values.search }),
            ...(method === undefined ? {} : { Method: method }),
            ...(values.from === '' ? {} : { RefundDateFrom: values.from }),
            ...(values.to === '' ? {} : { RefundDateTo: values.to }),
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

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Refunds"
        subtitle={
          values.payerId !== '' ? (
            <>
              The refunds of <PayerName id={values.payerId} />.{' '}
              <button type="button" className={buttons.link} onClick={() => state.change({ payerId: '' })}>
                Show every refund
              </button>
            </>
          ) : (
            "Money paid back from a payment's unapplied cash or a credit note's available credit. A refund is drafted from its payment, or from the payer's statement."
          )
        }
      />
      <FilterBar state={state} filters={filters} busy={refunds.isFetching}>
        <MakerFilter value={values.createdBy} onChange={(createdBy) => state.change({ createdBy })} />
      </FilterBar>
      {refunds.data === undefined ? (
        <LoadState error={refunds.error} what="refunds" />
      ) : refunds.data.items.length === 0 ? (
        <EmptyState icon="undo">No refund matches.</EmptyState>
      ) : (
        <>
          <DataTable
            tableId="refunds"
            label="Refunds"
            columns={columns}
            rows={refunds.data.items}
            keyOf={(refund) => refund.id}
            sort={sort}
            onSort={state.setSort}
            busy={refunds.isFetching}
          />
          <Pagination page={page} totalCount={Number(refunds.data.totalCount)} onChange={state.setPage} />
        </>
      )}
    </section>
  );
}
