import { useApiClient } from '../api/ApiClientContext';
import { Pagination } from '../components/ListControls';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatInstant } from '../format';
import { readReconciliation, useReport } from './api';
import { driftColumns, driftTotals, driftIsWhole, sameFindings } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/**
 * The reconciliation (US-41, BR-404): every place where a cached balance differs from the ledger, or the ledger or a
 * number series breaks one of its identities. An empty report means the caches equal the ledger and every series is
 * gapless. A row is a defect of the highest severity; the report never corrects anything.
 */
export function ReconciliationPage() {
  const client = useApiClient();
  const { page, change } = useReportQuery([] as const);
  const report = useReport('reconciliation', { page }, (api) => readReconciliation(api, page, pageSize));
  const totals = report.data?.totals;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Reconciliation"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="Every cached balance held against the ledger, and every number series checked for gaps, as of now."
      />
      <div className={styles.toolbar}>
        <span />
        <ExportButton
          fileName="reconciliation.csv"
          build={async () => {
            const whole = await readWholeReport({
              read: (number, size) => readReconciliation(client, number, size),
              isWhole: driftIsWhole,
              sameTotals: sameFindings,
            });
            return {
              rows: whole.rows.length,
              text: reportCsv({
                title: 'Reconciliation',
                columns: driftColumns,
                rows: whole.rows,
                totals: whole.last.totals ? driftTotals(whole.last.totals) : [],
              }),
            };
          }}
        />
      </div>
      {report.data === undefined || totals === null || totals === undefined ? (
        <LoadState error={report.error} what="the reconciliation" />
      ) : Number(totals.rows) === 0 ? (
        <p className={styles.clean} role="status">
          No drift: every cached balance equals the ledger, and every number series is gapless. Checked{' '}
          {formatInstant(totals.checkedAtUtc)}.
        </p>
      ) : (
        <>
          <div className={styles.drift} role="alert">
            <p>
              <strong>
                {String(totals.rows)} {Number(totals.rows) === 1 ? 'row' : 'rows'} of drift
              </strong>{' '}
              found {formatInstant(totals.checkedAtUtc)}:{' '}
              {totals.byIdentity.map((identity) => `${identity.identity} ${String(identity.rows)}`).join(', ')}. A
              cached balance differs from the ledger, or the ledger or a number series breaks one of its rules. Tell the
              administrator; nothing is corrected here.
            </p>
          </div>
          <ReportTable
            label="Drift rows"
            columns={driftColumns}
            rows={report.data.items}
            keyOf={(row) => `${row.identity}-${row.subjectId}`}
            count={counted(totals.rows, 'row', 'rows')}
            totals={[]}
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
