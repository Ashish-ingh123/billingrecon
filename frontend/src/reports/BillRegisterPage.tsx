import { useId } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { billStatusLabels } from '../bills/api';
import table from '../components/DataTable.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { pageSize } from '../components/listParams';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { AmountRange } from '../components/table/FilterBar';
import { readBillRegister, useReport, type BillRegisterFilter, type ReportBillStatus, type ReportExtras } from './api';
import { billRegisterColumns, billRegisterTotals, billRegisterIsWhole } from './columns';
import { reportCsv } from './csv';
import { readWholeReport } from './exportReport';
import { ExportButton, FiltersInUse, PayerFilter, PeriodFilter, ReportTable } from './ReportParts';
import { useReportQuery, counted } from './view';
import styles from './Reports.module.css';

const statuses: readonly ReportBillStatus[] = ['Posted', 'PartiallyPaid', 'Settled'];

/** The orders the API takes, by the header of the column (Prompt 12.2, ADR 0063). */
const sorts = { Number: 'number', 'Bill date': 'billDate', 'Total (₹)': 'total', 'Balance due (₹)': 'balanceDue' };

/** The filters of the bar, which "Clear filters" empties; the period is the register's scope and stays. */
const barFilters = ['payerId', 'status', 'totalFrom', 'totalTo'] as const;

const names = ['from', 'to', 'payerId', 'status', 'totalFrom', 'totalTo', 'sort'] as const;

/**
 * The bill register (BR-901): the posted bills of a period, by bill date unless another order is chosen, with what they
 * came to and still owe; narrowed by payer, status and total. The totals are of the bills the filters leave, over every
 * page, and the CSV reads the same filters and order with every column (Prompt 12.2, ADR 0063).
 */
export function BillRegisterPage() {
  const client = useApiClient();
  const statusId = useId();
  const query = useReportQuery(names);
  const { values, page, change } = query;
  const filter: BillRegisterFilter = {
    from: values.from,
    to: values.to,
    payerId: values.payerId,
    status: statuses.includes(values.status as ReportBillStatus) ? (values.status as ReportBillStatus) : undefined,
  };
  const extras: ReportExtras = { sort: values.sort, totalFrom: values.totalFrom, totalTo: values.totalTo };
  const report = useReport('bill-register', { filter, extras, page }, (api) =>
    readBillRegister(api, filter, page, pageSize, extras),
  );
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
          build={async (progress) => {
            const whole = await readWholeReport({
              read: (number, size) => readBillRegister(client, filter, number, size, extras),
              isWhole: billRegisterIsWhole,
              onPage: progress,
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
        <AmountRange<(typeof names)[number]>
          state={query}
          filter={{ kind: 'amounts', from: 'totalFrom', to: 'totalTo', label: 'Total' }}
        />
        <FiltersInUse
          count={query.inUse(barFilters)}
          onClear={() => change(Object.fromEntries(barFilters.map((name) => [name, ''])))}
        />
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
