import { useApiClient } from '../api/ApiClientContext';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { readAvailableCredit, useReport } from './api';
import { availableCreditColumns, availableCreditTotals, availableCreditIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, PayerFilter, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/**
 * Available credit (BR-901): the posted credit notes that still hold credit, the oldest first; the total is the balance
 * of the CreditAvailable account.
 */
export function AvailableCreditPage() {
  const client = useApiClient();
  const { values, page, change } = useReportQuery(['payerId'] as const);
  const report = useReport('available-credit', { payerId: values.payerId, page }, (api) =>
    readAvailableCredit(api, values.payerId, page, pageSize),
  );
  const totals = report.data?.totals;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Available credit"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="The posted credit notes that still hold credit, the oldest first. Each opens its credit note, where the credit is applied or refunded."
      />
      <div className={styles.toolbar}>
        <div className={listStyles.filters}>
          <PayerFilter payerId={values.payerId} onChange={(payerId) => change({ payerId })} />
        </div>
        <ExportButton
          fileName="available-credit.csv"
          build={async (progress) => {
            const whole = await readWholeReport({
              read: (number, size) => readAvailableCredit(client, values.payerId, number, size),
              isWhole: availableCreditIsWhole,
              onPage: progress,
            });
            return {
              rows: whole.rows.length,
              text: reportCsv({
                title: 'Available credit',
                columns: availableCreditColumns,
                rows: whole.rows,
                totals: whole.last.totals ? availableCreditTotals(whole.last.totals) : [],
              }),
            };
          }}
        />
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the available credit" />
      ) : report.data.items.length === 0 ? (
        <p className={table.empty}>No credit note holds credit.</p>
      ) : (
        <>
          <ReportTable
            label="Available credit"
            columns={availableCreditColumns}
            rows={report.data.items}
            keyOf={(row) => row.creditNoteId}
            count={counted(totals?.creditNotes ?? report.data.totalCount, 'credit note', 'credit notes')}
            totals={totals ? availableCreditTotals(totals).filter((total) => total.under !== undefined) : []}
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
