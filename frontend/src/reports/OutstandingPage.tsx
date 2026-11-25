import { useId } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { useLiveSearch } from '../components/useLiveSearch';
import { readOutstanding, useReport } from './api';
import { outstandingColumns, outstandingTotals, outstandingIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/**
 * Outstanding by payer (US-40, BR-901): every payer with a balance, the largest net outstanding first; each links to the
 * payer's statement. A payer whose three balances are zero is left out.
 */
export function OutstandingPage() {
  const client = useApiClient();
  const searchId = useId();
  const { values, page, change } = useReportQuery(['search'] as const);
  // The name is searched while it is typed (ADR 0039, amended 2026-11-25); the page shown stays until the next arrives.
  const search = useLiveSearch(values.search, (text) => change({ search: text }, { replace: true }));
  const report = useReport('outstanding-by-payer', { search: values.search, page }, (api, signal) =>
    readOutstanding(api, values.search, page, pageSize, signal),
  );
  const totals = report.data?.totals;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Outstanding by payer"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="What each payer owes, less the cash and credit they hold, the largest first. Each payer opens their statement."
      />
      <div className={styles.toolbar}>
        <form className={listStyles.filters} role="search" onSubmit={(event) => event.preventDefault()}>
          <div className={listStyles.field}>
            <label htmlFor={searchId}>Payer name</label>
            <input id={searchId} className={listStyles.input} type="search" autoComplete="off" {...search} />
          </div>
        </form>
        <ExportButton
          fileName="outstanding-by-payer.csv"
          build={async (progress) => {
            const whole = await readWholeReport({
              read: (number, size) => readOutstanding(client, values.search, number, size),
              isWhole: outstandingIsWhole,
              onPage: progress,
            });
            return {
              rows: whole.rows.length,
              text: reportCsv({
                title: 'Outstanding by payer',
                columns: outstandingColumns,
                rows: whole.rows,
                totals: whole.last.totals ? outstandingTotals(whole.last.totals) : [],
              }),
            };
          }}
        />
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the balances" />
      ) : report.data.items.length === 0 ? (
        <p className={table.empty}>No payer owes or holds anything{values.search === '' ? '' : ' by that name'}.</p>
      ) : (
        <>
          <ReportTable
            label="Outstanding by payer"
            columns={outstandingColumns}
            rows={report.data.items}
            keyOf={(row) => row.payerId}
            count={counted(totals?.payers ?? report.data.totalCount, 'payer', 'payers')}
            totals={totals ? outstandingTotals(totals).filter((total) => total.under !== undefined) : []}
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
