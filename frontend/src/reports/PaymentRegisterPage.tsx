import { useId } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { methodLabels, paymentMethods, paymentStatuses, paymentStatusLabels } from '../payments/api';
import {
  readPaymentRegister,
  useReport,
  type PaymentRegisterFilter,
  type ReportPaymentMethod,
  type ReportPaymentStatus,
} from './api';
import { paymentRegisterColumns, paymentRegisterTotals, paymentRegisterIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, PayerFilter, PeriodFilter, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/**
 * The payment register (BR-901): the payments of a period by payment date, reversed ones included, the oldest first or,
 * when asked, the newest first.
 */
export function PaymentRegisterPage() {
  const client = useApiClient();
  const methodId = useId();
  const statusId = useId();
  const orderId = useId();
  const { values, page, change } = useReportQuery(['from', 'to', 'payerId', 'method', 'status', 'order'] as const);
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
  const report = useReport('payment-register', { filter, page }, (api) =>
    readPaymentRegister(api, filter, page, pageSize),
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
          build={async () => {
            const whole = await readWholeReport({
              read: (number, size) => readPaymentRegister(client, filter, number, size),
              isWhole: paymentRegisterIsWhole,
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
            onChange={(event) => change({ order: event.target.value === 'NewestFirst' ? 'NewestFirst' : '' })}
          >
            <option value="OldestFirst">Oldest first</option>
            <option value="NewestFirst">Newest first</option>
          </select>
        </div>
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
