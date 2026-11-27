import { Link } from 'react-router';
import { answer } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import { useCan } from '../auth/permissions';
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
import { methodLabels, paymentMethods, paymentStatuses, paymentStatusLabels, type Payment } from './api';

/** Every filter of the payments the address may hold (ADR 0039 point 1; Prompt 12.2, ADR 0063). */
const keys = [
  'search',
  'status',
  'method',
  'from',
  'to',
  'amountFrom',
  'amountTo',
  'createdBy',
  'origin',
  'payerId',
] as const;

const clearable = keys.filter((key) => key !== 'payerId');
const origins = ['Standard', 'Opening'] as const;

const filters: readonly FilterSpec<(typeof keys)[number]>[] = [
  { kind: 'search', key: 'search' },
  {
    kind: 'select',
    key: 'status',
    label: 'Status',
    options: paymentStatuses.map((status) => ({ value: status, label: paymentStatusLabels[status].label })),
  },
  {
    kind: 'select',
    key: 'method',
    label: 'Method',
    options: paymentMethods.map((method) => ({ value: method, label: methodLabels[method] })),
  },
  {
    kind: 'select',
    key: 'origin',
    label: 'Origin',
    options: [
      { value: 'Standard', label: 'Received here' },
      { value: 'Opening', label: 'Brought in at the cut-over' },
    ],
  },
  { kind: 'dates', from: 'from', to: 'to', label: 'Date' },
  { kind: 'amounts', from: 'amountFrom', to: 'amountTo', label: 'Amount' },
];

function PayerName({ id }: { id: string }) {
  const payer = usePayer(id);
  return <>{payer.data?.name ?? '…'}</>;
}

const columns: readonly TableColumn<Payment>[] = [
  {
    id: 'number',
    header: 'Number',
    fixed: true,
    sort: 'number',
    cell: (payment) => <Link to={`/payments/${payment.id}`}>{payment.number}</Link>,
  },
  { id: 'date', header: 'Date', sort: 'paymentDate', cell: (payment) => formatDate(payment.paymentDate) },
  { id: 'payer', header: 'Payer', cell: (payment) => <PayerName id={payment.payerId} /> },
  { id: 'method', header: 'Method', cell: (payment) => methodLabels[payment.method] },
  { id: 'reference', header: 'Reference', cell: (payment) => payment.reference },
  {
    id: 'amount',
    header: 'Amount (₹)',
    numeric: true,
    sort: 'amount',
    cell: (payment) => formatDecimal(payment.amount),
  },
  {
    id: 'unapplied',
    header: 'Unapplied (₹)',
    numeric: true,
    sort: 'unappliedAmount',
    cell: (payment) => formatDecimal(payment.unappliedAmount),
  },
  {
    id: 'status',
    header: 'Status',
    fixed: true,
    cell: (payment) => (
      <Badge tone={paymentStatusLabels[payment.status].tone} icon={paymentStatusLabels[payment.status].icon}>
        {paymentStatusLabels[payment.status].label}
      </Badge>
    ),
  },
];

/**
 * The payments received (US-30): newest first unless another order is chosen; narrowed by any filter of the bar, or to
 * those of one payer. The API filters, sorts and pages every payment (Prompt 12.2, ADR 0063).
 */
export function PaymentsPage() {
  const canRecord = useCan('RecordPayments');
  const client = useApiClient();
  const state = useListState(keys, clearable);
  const { values, sort, page } = state;
  const status = known(values.status, paymentStatuses);
  const method = known(values.method, paymentMethods);
  const origin = known(values.origin, origins);
  const payments = useListQuery(['payments', 'list', values, sort, page], (signal) =>
    answer(
      client.GET('/api/v1/payments', {
        params: {
          query: {
            ...(status === undefined ? {} : { Status: status }),
            ...(method === undefined ? {} : { Method: method }),
            ...(values.from === '' ? {} : { PaymentDateFrom: values.from }),
            ...(values.to === '' ? {} : { PaymentDateTo: values.to }),
            ...(values.payerId === '' ? {} : { PayerId: values.payerId }),
            ...(values.search === '' ? {} : { Search: values.search }),
            ...(values.amountFrom === '' ? {} : { AmountFrom: values.amountFrom }),
            ...(values.amountTo === '' ? {} : { AmountTo: values.amountTo }),
            ...(values.createdBy === '' ? {} : { CreatedBy: values.createdBy }),
            ...(origin === undefined ? {} : { Origin: origin }),
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
        title="Payments"
        subtitle={
          values.payerId !== '' ? (
            <>
              The payments of <PayerName id={values.payerId} />.{' '}
              <button type="button" className={buttons.link} onClick={() => state.change({ payerId: '' })}>
                Show every payment
              </button>
            </>
          ) : (
            'Money received, what is left of it to apply, and payments waiting for or past their reversal.'
          )
        }
        actions={
          canRecord && (
            <Link
              className={buttons.primary}
              to={values.payerId === '' ? '/payments/new' : `/payments/new?payerId=${values.payerId}`}
            >
              Record payment
            </Link>
          )
        }
      />
      <FilterBar state={state} filters={filters} busy={payments.isFetching}>
        <MakerFilter value={values.createdBy} onChange={(createdBy) => state.change({ createdBy })} />
      </FilterBar>
      {payments.data === undefined ? (
        <LoadState error={payments.error} what="payments" />
      ) : payments.data.items.length === 0 ? (
        <EmptyState icon="wallet">No payment matches.</EmptyState>
      ) : (
        <>
          <DataTable
            tableId="payments"
            label="Payments"
            columns={columns}
            rows={payments.data.items}
            keyOf={(payment) => payment.id}
            sort={sort}
            onSort={state.setSort}
            busy={payments.isFetching}
          />
          <Pagination page={page} totalCount={Number(payments.data.totalCount)} onChange={state.setPage} />
        </>
      )}
    </section>
  );
}
