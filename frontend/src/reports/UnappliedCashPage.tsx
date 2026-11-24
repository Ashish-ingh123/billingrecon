import { useApiClient } from '../api/ApiClientContext';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { readUnappliedCash, useReport } from './api';
import { unappliedColumns, unappliedTotals, unappliedIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, PayerFilter, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/**
 * Unapplied cash (BR-901): the payments that still hold money not applied to a bill, the oldest first; the total is the
 * balance of the UnappliedCash account. A payment waiting for its reversal still holds its money.
 */
export function UnappliedCashPage() {
  const client = useApiClient();
  const { values, page, change } = useReportQuery(['payerId'] as const);
  const report = useReport('unapplied-cash', { payerId: values.payerId, page }, (api) =>
    readUnappliedCash(api, values.payerId, page, pageSize),
  );
  const totals = report.data?.totals;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Unapplied cash"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="The payments that still hold money not applied to a bill, the oldest first. Each opens its payment, where it is allocated or refunded."
      />
      <div className={styles.toolbar}>
        <div className={listStyles.filters}>
          <PayerFilter payerId={values.payerId} onChange={(payerId) => change({ payerId })} />
        </div>
        <ExportButton
          fileName="unapplied-cash.csv"
          build={async (progress) => {
            const whole = await readWholeReport({
              read: (number, size) => readUnappliedCash(client, values.payerId, number, size),
              isWhole: unappliedIsWhole,
              onPage: progress,
            });
            return {
              rows: whole.rows.length,
              text: reportCsv({
                title: 'Unapplied cash',
                columns: unappliedColumns,
                rows: whole.rows,
                totals: whole.last.totals ? unappliedTotals(whole.last.totals) : [],
              }),
            };
          }}
        />
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the unapplied cash" />
      ) : report.data.items.length === 0 ? (
        <p className={table.empty}>No payment holds unapplied cash.</p>
      ) : (
        <>
          <ReportTable
            label="Unapplied cash"
            columns={unappliedColumns}
            rows={report.data.items}
            keyOf={(row) => row.paymentId}
            count={counted(totals?.payments ?? report.data.totalCount, 'payment', 'payments')}
            totals={totals ? unappliedTotals(totals).filter((total) => total.under !== undefined) : []}
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
