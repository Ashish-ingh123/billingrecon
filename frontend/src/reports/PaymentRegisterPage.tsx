import { useId } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { AmountRange } from '../components/table/FilterBar';
import { methodLabels, paymentMethods, paymentStatuses, paymentStatusLabels } from '../payments/api';
import {
  readPaymentRegister,
  useReport,
  type PaymentRegisterFilter,
  type ReportPaymentMethod,
  type ReportPaymentStatus,
  type ReportExtras,
} from './api';
import { paymentRegisterColumns, paymentRegisterTotals, paymentRegisterIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, FiltersInUse, PayerFilter, PeriodFilter, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/** The orders the API takes, by the header of the column (Prompt 12.2, ADR 0063). */
const sorts = {
  Number: 'number',
  'Payment date': 'paymentDate',
  'Amount (₹)': 'amount',
  'Unapplied (₹)': 'unappliedAmount',
};

const names = ['from', 'to', 'payerId', 'method', 'status', 'order', 'amountFrom', 'amountTo', 'sort'] as const;

/** The filters of the bar, which "Clear filters" empties; the period and the order stay. */
const barFilters = ['payerId', 'method', 'status', 'amountFrom', 'amountTo'] as const;

/**
 * The payment register (BR-901): the payments of a period by payment date, reversed ones included, the oldest first or,
 * when asked, the newest first, or in the order of a column; narrowed by payer, method, status and amount. The totals
 * are of the payments the filters leave, and the CSV reads the same filters and order with every column (Prompt 12.2).
 */
export function PaymentRegisterPage() {
  const client = useApiClient();
  const methodId = useId();
  const statusId = useId();
  const orderId = useId();
  const query = useReportQuery(names);
  const { values, page, change } = query;
  const filter: PaymentRegisterFilter = {
    from: values.from,
    to: values.to,
    payerId: values.payerId,
    method: paymentMethods.includes(values.method as ReportPaymentMethod)
      ? (values.method as ReportPaymentMethod)
      : undefined,
    status: paymentStatuses.includes(values.status as ReportPaymentStatus)
      ? (values.status as ReportPaymentStatus)
      : undefined,
    order: values.order === 'NewestFirst' ? 'NewestFirst' : 'OldestFirst',
  };
  const extras: ReportExtras = { sort: values.sort, amountFrom: values.amountFrom, amountTo: values.amountTo };
  const report = useReport('payment-register', { filter, extras, page }, (api) =>
    readPaymentRegister(api, filter, page, pageSize, extras),
  );
  const period = report.data?.period;
  const totals = report.data?.totals;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Payment register"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="The payments of a period by payment date, reversed ones included, with what each still holds unapplied."
      />
      <div className={styles.toolbar}>
        <PeriodFilter from={filter.from} to={filter.to} answered={period} onChange={(next) => change(next)} />
        <ExportButton
          fileName={`payment-register-${period?.from ?? 'month'}-to-${period?.to ?? 'month'}.csv`}
          build={async (progress) => {
            const whole = await readWholeReport({
              read: (number, size) => readPaymentRegister(client, filter, number, size, extras),
              isWhole: paymentRegisterIsWhole,
              onPage: progress,
            });
            return {
              rows: whole.rows.length,
              text: reportCsv({
                title: 'Payment register',
                period: whole.last.period,
                columns: paymentRegisterColumns,
                rows: whole.rows,
                totals: whole.last.totals ? paymentRegisterTotals(whole.last.totals) : [],
              }),
            };
          }}
        />
      </div>
      <div className={listStyles.filters}>
        <PayerFilter payerId={filter.payerId} onChange={(payerId) => change({ payerId })} />
        <div className={listStyles.field}>
          <label htmlFor={methodId}>Method</label>
          <select
            id={methodId}
            className={listStyles.input}
            value={filter.method ?? ''}
            onChange={(event) => change({ method: event.target.value })}
          >
            <option value="">All</option>
            {paymentMethods.map((method) => (
              <option key={method} value={method}>
                {methodLabels[method]}
              </option>
            ))}
          </select>
        </div>
        <div className={listStyles.field}>
          <label htmlFor={statusId}>Status</label>
          <select
            id={statusId}
            className={listStyles.input}
            value={filter.status ?? ''}
            onChange={(event) => change({ status: event.target.value })}
          >
            <option value="">All</option>
            {paymentStatuses.map((status) => (
              <option key={status} value={status}>
                {paymentStatusLabels[status].label}
              </option>
            ))}
          </select>
        </div>
        <div className={listStyles.field}>
          <label htmlFor={orderId}>Order</label>
          <select
            id={orderId}
            className={listStyles.input}
            value={filter.order}
            onChange={(event) => change({ order: event.target.value === 'NewestFirst' ? 'NewestFirst' : '', sort: '' })}
          >
            <option value="OldestFirst">Oldest first</option>
            <option value="NewestFirst">Newest first</option>
          </select>
        </div>
        <AmountRange<(typeof names)[number]>
          state={query}
          filter={{ kind: 'amounts', from: 'amountFrom', to: 'amountTo', label: 'Amount' }}
        />
        <FiltersInUse
          count={query.inUse(barFilters)}
          onClear={() => change(Object.fromEntries(barFilters.map((name) => [name, ''])))}
        />
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the payment register" />
      ) : report.data.items.length === 0 ? (
        <p className={table.empty}>No payment in this period matches.</p>
      ) : (
        <>
          <ReportTable
            label="Payment register"
            columns={paymentRegisterColumns}
            rows={report.data.items}
            keyOf={(row) => row.paymentId}
            count={counted(totals?.payments ?? report.data.totalCount, 'payment', 'payments')}
            totals={totals ? paymentRegisterTotals(totals).filter((total) => total.under !== undefined) : []}
            sorts={sorts}
            sort={values.sort}
            onSort={(sort) => change({ sort })}
          />
          <Pagination
            page={page}
            totalCount={Number(report.data.totalCount)}
            onChange={(next) => change({ page: String(next) })}
          />
        </>
      )}
    </section>
  );
}
