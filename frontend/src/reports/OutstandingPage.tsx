import { useId, useState, type FormEvent } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
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
  const [search, setSearch] = useState(values.search);
  const report = useReport('outstanding-by-payer', { search: values.search, page }, (api) =>
    readOutstanding(api, values.search, page, pageSize),
  );
  const totals = report.data?.totals;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    change({ search: search.trim() });
  };

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Outstanding by payer"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="What each payer owes, less the cash and credit they hold, the largest first. Each payer opens their statement."
      />
      <div className={styles.toolbar}>
        <form className={listStyles.filters} role="search" onSubmit={submit}>
          <div className={listStyles.field}>
            <label htmlFor={searchId}>Payer name</label>
            <input
              id={searchId}
              className={listStyles.input}
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
          <button type="submit" className={buttons.secondary}>
            Search
          </button>
        </form>
        <ExportButton
          fileName="outstanding-by-payer.csv"
          build={async () => {
            const whole = await readWholeReport({
              read: (number, size) => readOutstanding(client, values.search, number, size),
              isWhole: outstandingIsWhole,
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
