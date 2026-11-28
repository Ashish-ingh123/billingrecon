import { useApiClient } from '../api/ApiClientContext';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { DateRange } from '../components/table/FilterBar';
import { readUnappliedCash, useReport, type ReportExtras } from './api';
import { unappliedColumns, unappliedTotals, unappliedIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, FiltersInUse, PayerFilter, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

/** The orders the API takes, by the header of the column (Prompt 12.2, ADR 0063). */
const sorts = {
  Payment: 'number',
  'Payment date': 'paymentDate',
  'Unapplied (₹)': 'unappliedAmount',
};

const names = ['payerId', 'from', 'to', 'sort'] as const;

/** The filters of the bar, which "Clear filters" empties. */
const barFilters = ['payerId', 'from', 'to'] as const;

/**
 * Unapplied cash (BR-901): the payments that still hold money not applied to a bill, the oldest first unless another order is chosen, narrowed by payer and a period of dates; the total is the
 * balance of the UnappliedCash account. A payment waiting for its reversal still holds its money.
 */
export function UnappliedCashPage() {
  const client = useApiClient();
  const query = useReportQuery(names);
  const { values, page, change } = query;
  const extras: ReportExtras = { sort: values.sort, dateFrom: values.from, dateTo: values.to };
  const report = useReport('unapplied-cash', { payerId: values.payerId, extras, page }, (api) =>
    readUnappliedCash(api, values.payerId, page, pageSize, extras),
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
          <DateRange<(typeof names)[number]>
            state={query}
            filter={{ kind: 'dates', from: 'from', to: 'to', label: 'Payment date' }}
          />
          <FiltersInUse count={query.inUse(barFilters)} onClear={() => change({ payerId: '', from: '', to: '' })} />
        </div>
        <ExportButton
          fileName="unapplied-cash.csv"
          build={async (progress) => {
            const whole = await readWholeReport({
              read: (number, size) => readUnappliedCash(client, values.payerId, number, size, extras),
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
