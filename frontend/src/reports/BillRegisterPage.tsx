import { useId } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { billStatusLabels } from '../bills/api';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { readBillRegister, useReport, type BillRegisterFilter, type ReportBillStatus } from './api';
import { billRegisterColumns, billRegisterTotals, billRegisterIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, PayerFilter, PeriodFilter, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

const statuses: readonly ReportBillStatus[] = ['Posted', 'PartiallyPaid', 'Settled'];

/** The bill register (BR-901): the posted bills of a period, by bill date, with what they came to and still owe. */
export function BillRegisterPage() {
  const client = useApiClient();
  const statusId = useId();
  const { values, page, change } = useReportQuery(['from', 'to', 'payerId', 'status'] as const);
  const filter: BillRegisterFilter = {
    from: values.from,
    to: values.to,
    payerId: values.payerId,
    status: statuses.includes(values.status as ReportBillStatus) ? (values.status as ReportBillStatus) : undefined,
  };
  const report = useReport('bill-register', { filter, page }, (api) => readBillRegister(api, filter, page, pageSize));
  const period = report.data?.period;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Bill register"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="The posted bills of a period by bill date: what each came to and what it still owes. Drafts and bills waiting for approval are in no register."
      />
      <div className={styles.toolbar}>
        <PeriodFilter from={filter.from} to={filter.to} answered={period} onChange={(next) => change(next)} />
        <ExportButton
          fileName={`bill-register-${period?.from ?? 'month'}-to-${period?.to ?? 'month'}.csv`}
          build={async () => {
            const whole = await readWholeReport({
              read: (number, size) => readBillRegister(client, filter, number, size),
              isWhole: billRegisterIsWhole,
            });
            return {
              rows: whole.rows.length,
              text: reportCsv({
                title: 'Bill register',
                period: whole.last.period,
                columns: billRegisterColumns,
                rows: whole.rows,
                totals:
                  whole.last.totals === null || whole.last.totals === undefined
                    ? []
                    : billRegisterTotals(whole.last.totals),
              }),
            };
          }}
        />
      </div>
      <div className={listStyles.filters}>
        <PayerFilter payerId={filter.payerId} onChange={(payerId) => change({ payerId })} />
        <div className={listStyles.field}>
          <label htmlFor={statusId}>Status</label>
          <select
            id={statusId}
            className={listStyles.input}
            value={filter.status ?? ''}
            onChange={(event) => change({ status: event.target.value })}
          >
            <option value="">All</option>
            {statuses.map((status) => (
              <option key={status} value={status}>
                {billStatusLabels[status]}
              </option>
            ))}
          </select>
        </div>
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the bill register" />
      ) : report.data.items.length === 0 ? (
        <p className={table.empty}>No posted bill in this period matches.</p>
      ) : (
        <>
          <ReportTable
            label="Bill register"
            columns={billRegisterColumns}
            rows={report.data.items}
            keyOf={(row) => row.billId}
            count={counted(report.data.totals?.bills ?? report.data.totalCount, 'bill', 'bills')}
            totals={
              report.data.totals === null || report.data.totals === undefined
                ? []
                : billRegisterTotals(report.data.totals).filter((total) => total.under !== undefined)
            }
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
