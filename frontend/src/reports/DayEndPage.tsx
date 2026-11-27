import { useId, useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { answer } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import { Banner } from '../components/Banner';
import buttons from '../components/buttons.module.css';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { formatDate, todayInIndia } from '../format';
import { shiftStatusLabels, type DayEnd, type DayEndShift } from '../shifts/api';
import { useReport } from './api';
import { reportCsv } from './csv';
import { comesTo, ExportChangedError } from './exportReport';
import { ExportButton, ReportTable } from './ReportParts';
import { counted, useReportQuery, type ReportColumn, type ReportTotal } from './view';
import styles from './Reports.module.css';

function readDayEnd(client: ApiClient, date: string) {
  return answer(client.GET('/api/v1/reports/day-end', { params: { query: date === '' ? {} : { date } } }));
}

/** The columns of the day-end report, one definition for the screen and the CSV (ADR 0043 point 1). */
const dayEndColumns: readonly ReportColumn<DayEndShift>[] = [
  {
    header: 'Cashier',
    kind: 'text',
    value: (row) => row.cashierName ?? '',
    show: (row) => <Link to={`/shifts/${row.shiftId}`}>{row.cashierName ?? 'A cashier'}</Link>,
  },
  { header: 'Status', kind: 'text', value: (row) => shiftStatusLabels[row.status].label },
  { header: 'Opened', kind: 'instant', value: (row) => row.openedAtUtc },
  { header: 'Float (₹)', kind: 'amount', value: (row) => row.openingFloat },
  { header: 'Taken (₹)', kind: 'amount', value: (row) => row.received },
  { header: 'Paid out (₹)', kind: 'amount', value: (row) => row.paidOut },
  { header: 'Net (₹)', kind: 'amount', value: (row) => row.net },
  { header: 'Expected (₹)', kind: 'amount', value: (row) => row.expectedCash },
  { header: 'Counted (₹)', kind: 'amount', value: (row) => row.countedCash },
  { header: 'Difference (₹)', kind: 'amount', value: (row) => row.difference },
];

function dayEndTotals(report: DayEnd): ReportTotal[] {
  return [
    { label: 'Taken (₹)', kind: 'amount', value: report.shiftsTotal.received, under: 'Taken (₹)' },
    { label: 'Paid out (₹)', kind: 'amount', value: report.shiftsTotal.paidOut, under: 'Paid out (₹)' },
    { label: 'Net (₹)', kind: 'amount', value: report.shiftsTotal.net, under: 'Net (₹)' },
    { label: 'Difference (₹)', kind: 'amount', value: report.difference, under: 'Difference (₹)' },
    { label: 'Cash outside the shifts, net (₹)', kind: 'amount', value: report.outsideShifts.net },
    { label: 'Cash of the day, net (₹)', kind: 'amount', value: report.cash.net },
  ];
}

/** The shifts come to the report's totals, to the paisa: the check of an export of one read. */
const dayEndIsWhole = (report: DayEnd) =>
  comesTo(report.shifts, (row) => row.received, report.shiftsTotal.received) &&
  comesTo(report.shifts, (row) => row.paidOut, report.shiftsTotal.paidOut) &&
  comesTo(report.shifts, (row) => row.net, report.shiftsTotal.net);

/**
 * The day-end report (US-48, BR-903; ADR 0060): every shift of one business date in India with its cash, and the cash
 * of that date that no shift holds. The cash of the day is the Cash row of the daily collection, always.
 */
export function DayEndPage() {
  const client = useApiClient();
  const dateId = useId();
  const { values, change } = useReportQuery(['date'] as const);
  const [date, setDate] = useState(values.date === '' ? todayInIndia() : values.date);
  const report = useReport('day-end', values.date, (api) => readDayEnd(api, values.date));

  const submit = (event: FormEvent) => {
    event.preventDefault();
    change({ date });
  };

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Day-end report"
        back={{ to: '/reports', label: 'Reports' }}
        subtitle="Every shift of one day: what its drawer took and paid out, what it should hold, and what its cashier counted."
      />
      <div className={styles.toolbar}>
        <form className={listStyles.filters} aria-label="Day" onSubmit={submit}>
          <div className={listStyles.field}>
            <label htmlFor={dateId}>Date</label>
            <input
              id={dateId}
              className={listStyles.input}
              type="date"
              value={date}
              onChange={(event) => setDate(event.target.value)}
            />
          </div>
          <button type="submit" className={buttons.secondary}>
            Show
          </button>
          {report.data !== undefined && <p className={styles.period}>{formatDate(report.data.date)}</p>}
        </form>
        <ExportButton
          fileName={`day-end-${report.data?.date ?? values.date}.csv`}
          build={async () => {
            const read = await readDayEnd(client, values.date);
            if (!dayEndIsWhole(read)) {
              throw new ExportChangedError();
            }

            return {
              rows: read.shifts.length,
              text: reportCsv({
                title: `Day-end report of ${read.date}`,
                period: { from: read.date, to: read.date },
                columns: dayEndColumns,
                rows: read.shifts,
                totals: dayEndTotals(read),
              }),
            };
          }}
        />
      </div>
      {report.data === undefined ? (
        <LoadState error={report.error} what="the day-end report" />
      ) : (
        <>
          {Number(report.data.unclosedShifts) > 0 && (
            <Banner tone="warning" title="The day is not closed yet">
              {counted(report.data.unclosedShifts, 'shift is', 'shifts are')} still open or waiting for approval: their
              figures may still change.
            </Banner>
          )}
          <ReportTable
            label="Shifts of the day"
            columns={dayEndColumns}
            rows={report.data.shifts}
            keyOf={(row) => row.shiftId}
            count={counted(report.data.shifts.length, 'shift', 'shifts')}
            totals={dayEndTotals(report.data)}
          />
          <p className={styles.period}>
            The cash of the day is what the{' '}
            <Link to={`/reports/collection?date=${report.data.date}`}>daily collection</Link> shows for Cash: the shifts
            and the cash outside them. Cash is outside the shifts when a cash payment was reversed after its shift had
            closed.
          </p>
        </>
      )}
    </section>
  );
}
