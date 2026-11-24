import { useId } from 'react';
import { Link } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { approvalDocumentTypes, documentTypeLabels } from '../approvals/api';
import { useCan } from '../auth/permissions';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { readPendingApprovals, useReport, type ApprovalDocumentType } from './api';
import { pendingColumns, pendingTotals, pendingIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/**
 * Pending approvals (BR-901): every request that waits for a second person, the oldest first, for every role to read. A
 * checker decides them in the inbox (ADR 0042), which this report links to.
 */
export function PendingApprovalsReportPage() {
  const client = useApiClient();
  const canDecide = useCan('Decide');
  const typeId = useId();
  const { values, page, change } = useReportQuery(['documentType'] as const);
  const documentType = approvalDocumentTypes.includes(values.documentType as ApprovalDocumentType)
    ? (values.documentType as ApprovalDocumentType)
    : undefined;
  const report = useReport('pending-approvals', { documentType, page }, (api) =>
    readPendingApprovals(api, documentType, page, pageSize),
  );
  const totals = report.data?.totals;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Pending approvals"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle={
          <>
            What waits for a second person, the oldest first, and for how many days.
            {canDecide && (
              <>
                {' '}
                <Link to="/approvals">Decide them in Approvals.</Link>
              </>
            )}
          </>
        }
      />
      <div className={styles.toolbar}>
        <div className={listStyles.filters}>
          <div className={listStyles.field}>
            <label htmlFor={typeId}>Waiting</label>
            <select
              id={typeId}
              className={listStyles.input}
              value={documentType ?? ''}
              onChange={(event) => change({ documentType: event.target.value })}
            >
              <option value="">Everything</option>
              {approvalDocumentTypes.map((value) => (
                <option key={value} value={value}>
                  {documentTypeLabels[value]}
                </option>
              ))}
            </select>
          </div>
        </div>
        <ExportButton
          fileName="pending-approvals.csv"
          build={async (progress) => {
            const whole = await readWholeReport({
              read: (number, size) => readPendingApprovals(client, documentType, number, size),
              isWhole: pendingIsWhole,
              onPage: progress,
            });
            return {
              rows: whole.rows.length,
              text: reportCsv({
                title: 'Pending approvals',
                columns: pendingColumns,
                rows: whole.rows,
                totals: whole.last.totals ? pendingTotals(whole.last.totals) : [],
              }),
            };
          }}
        />
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the requests that wait" />
      ) : report.data.items.length === 0 ? (
        <p className={table.empty}>Nothing waits for a decision.</p>
      ) : (
        <>
          <ReportTable
            label="Pending approvals"
            columns={pendingColumns}
            rows={report.data.items}
            keyOf={(row) => row.approvalRequestId}
            count={counted(totals?.requests ?? report.data.totalCount, 'request', 'requests')}
            totals={totals ? pendingTotals(totals).filter((total) => total.kind === 'instant') : []}
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
