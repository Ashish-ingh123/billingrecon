import { useId, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router';
import { problemOf } from '../api/call';
import { Icon } from '../brand/Icon';
import buttons from '../components/buttons.module.css';
import table from '../components/DataTable.module.css';
import listStyles from '../components/ListControls.module.css';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, formatDecimal, todayInIndia } from '../format';
import { useAgeing, useSummary, useTrend, type DashboardPeriod, type DailyFigures } from './api';
import { AgeingChart, TrendChart } from './Charts';
import styles from './Dashboard.module.css';
import { DriftPanel, Panel, PendingApprovalsPanel, RecentPaymentsPanel, TopPayersPanel } from './Panels';
import { SummaryCard, type SparkPoint } from './SummaryCard';

/** The icon of each card, from the shared set (ADR 0047): beside its label, never alone. */
const icons = {
  bill: <Icon name="receipt" />,
  cash: <Icon name="wallet" />,
  clock: <Icon name="clock" />,
  stack: <Icon name="card" />,
  check: <Icon name="check-circle" />,
  alert: <Icon name="alert" />,
};

const ranges = ['7', '30', '90'] as const;

/** A date in India `days` before another, as `yyyy-MM-dd`. */
function daysBefore(date: string, days: number): string {
  const moment = new Date(`${date}T00:00:00Z`);
  moment.setUTCDate(moment.getUTCDate() - days);
  return moment.toISOString().slice(0, 10);
}

/**
 * The period of the dashboard, kept in the address: the last 7, 30 or 90 days ending today in India, or a custom range
 * (BR-902). The last 30 days is the API's default and sends no dates.
 */
function usePeriod(): { range: string; period: DashboardPeriod; choose: (next: Record<string, string>) => void } {
  const [params, setParams] = useSearchParams();
  const range = params.get('range') ?? '30';
  const today = todayInIndia();
  const period: DashboardPeriod =
    range === 'custom'
      ? { from: params.get('from') ?? '', to: params.get('to') ?? '' }
      : range === '7' || range === '90'
        ? { from: daysBefore(today, Number(range) - 1), to: today }
        : { from: '', to: '' };

  return { range, period, choose: (next) => setParams(next) };
}

function PeriodPicker({ range, period, choose }: ReturnType<typeof usePeriod>) {
  const rangeId = useId();
  const fromId = useId();
  const toId = useId();
  const [from, setFrom] = useState(period.from);
  const [to, setTo] = useState(period.to);

  const apply = (event: FormEvent) => {
    event.preventDefault();
    if (from !== '' && to !== '') {
      choose({ range: 'custom', from, to });
    }
  };

  const pick = (value: string) =>
    choose(value === '30' ? {} : value === 'custom' ? { range: 'custom', from, to } : { range: value });

  return (
    <form className={styles.period} aria-label="Choose the period" onSubmit={apply}>
      <div className={styles.segments} role="radiogroup" aria-label="Period" id={rangeId}>
        {[...ranges.map((days) => [days, `Last ${days} days`] as const), ['custom', 'Custom'] as const].map(
          ([value, words]) => (
            <label key={value} className={styles.segment}>
              <input type="radio" name={rangeId} value={value} checked={range === value} onChange={() => pick(value)} />
              <span>{words}</span>
            </label>
          ),
        )}
      </div>
      {range === 'custom' && (
        <>
          <div className={listStyles.field}>
            <label htmlFor={fromId}>From</label>
            <input
              id={fromId}
              className={listStyles.input}
              type="date"
              value={from}
              max={todayInIndia()}
              onChange={(event) => setFrom(event.target.value)}
            />
          </div>
          <div className={listStyles.field}>
            <label htmlFor={toId}>To</label>
            <input
              id={toId}
              className={listStyles.input}
              type="date"
              value={to}
              max={todayInIndia()}
              onChange={(event) => setTo(event.target.value)}
            />
          </div>
          <button type="submit" className={buttons.secondary}>
            Apply
          </button>
        </>
      )}
    </form>
  );
}

const series = (
  daily: readonly DailyFigures[] | undefined,
  pick: (day: DailyFigures) => number | string,
): SparkPoint[] | undefined => daily?.map((day) => ({ date: day.date, value: pick(day) }));

/**
 * The days behind the sparklines as a table (ADR 0050): nothing is shown only in a tooltip.
 */
function DailyTable({ daily }: { daily: readonly DailyFigures[] | undefined }) {
  return (
    <details className={styles.daily}>
      <summary>Show the daily figures as a table</summary>
      <div className={table.wrap}>
        <table className={table.table} aria-label="The figures of each day">
          <thead>
            <tr>
              <th scope="col">Date</th>
              <th scope="col" className={table.number}>
                Billed (₹)
              </th>
              <th scope="col" className={table.number}>
                Collected (₹)
              </th>
              <th scope="col" className={table.number}>
                Outstanding dues (₹)
              </th>
              <th scope="col" className={table.number}>
                Unapplied cash (₹)
              </th>
              <th scope="col" className={table.number}>
                Pending approvals
              </th>
            </tr>
          </thead>
          <tbody>
            {(daily ?? []).map((day) => (
              <tr key={day.date}>
                <td>{formatDate(day.date)}</td>
                <td className={table.number}>{formatDecimal(day.billed)}</td>
                <td className={table.number}>{formatDecimal(day.collected)}</td>
                <td className={table.number}>{formatDecimal(day.outstandingDues)}</td>
                <td className={table.number}>{formatDecimal(day.unappliedCash)}</td>
                <td className={table.number}>{String(day.pendingApprovals)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  );
}

/**
 * The dashboard (US-44, BR-902; ADR 0044), for a TenantAdmin, a Supervisor or a Viewer: the figures of a period against
 * the previous one, the trend of billing against collections, the ageing of what is owed, and what needs attention.
 * Every figure is the server's; each panel loads and fails on its own.
 */
export function DashboardPage() {
  const picked = usePeriod();
  const summary = useSummary(picked.period);
  const trend = useTrend(picked.period);
  const ageing = useAgeing();
  const daily = trend.data?.daily;
  const data = summary.data;
  const days =
    data === undefined
      ? null
      : Math.round((Date.parse(data.period.to) - Date.parse(data.period.from)) / 86_400_000) + 1;
  const against = days === null ? '' : `vs previous ${String(days)} days`;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Dashboard"
        subtitle={
          // The line keeps the height of the period's dates from the first paint, so nothing moves when they arrive.
          <span className={styles.periodLine}>
            {data === undefined
              ? 'The figures of a period against the previous one.'
              : `${formatDate(data.period.from)} to ${formatDate(data.period.to)}, compared with ${formatDate(data.previousPeriod.from)} to ${formatDate(data.previousPeriod.to)} (IST business dates)`}
          </span>
        }
        actions={<PeriodPicker key={`${picked.range}${picked.period.from}`} {...picked} />}
      />
      {data === undefined ? (
        summary.error !== null ? (
          <ProblemAlert problem={problemOf(summary.error)} />
        ) : (
          // Six placeholders the size of the cards, so that nothing below moves when the figures arrive (ADR 0050).
          <ul className={styles.cards} role="status" aria-label="Loading the summary" aria-busy="true">
            {[1, 2, 3, 4, 5, 6].map((place) => (
              <li key={place} className={`${styles.card} ${styles.cardPlaceholder}`} />
            ))}
          </ul>
        )
      ) : (
        <ul className={styles.cards} aria-label="Summary">
          <SummaryCard
            label="Total billing"
            icon={icons.bill}
            series={1}
            figure={data.totalBilled}
            kind="amount"
            goodWhen="rise"
            against={against}
            spark={series(daily, (day) => day.billed)}
          />
          <SummaryCard
            label="Amount collected"
            icon={icons.cash}
            series={3}
            figure={data.amountCollected}
            kind="amount"
            goodWhen="rise"
            against={against}
            spark={series(daily, (day) => day.collected)}
          />
          <SummaryCard
            label="Outstanding dues"
            icon={icons.clock}
            series={5}
            figure={data.outstandingDues}
            kind="amount"
            goodWhen="fall"
            against={against}
            spark={series(daily, (day) => day.outstandingDues)}
          />
          <SummaryCard
            label="Unapplied cash"
            icon={icons.stack}
            series={2}
            figure={data.unappliedCash}
            kind="amount"
            goodWhen="fall"
            against={against}
            spark={series(daily, (day) => day.unappliedCash)}
          />
          <SummaryCard
            label="Pending approvals"
            icon={icons.check}
            series={4}
            figure={data.pendingApprovals}
            kind="count"
            goodWhen="fall"
            against={against}
            spark={series(daily, (day) => day.pendingApprovals)}
          />
          <SummaryCard
            label="Reconciliation issues"
            icon={icons.alert}
            series={6}
            figure={data.reconciliationDrift}
            kind="count"
            goodWhen="fall"
            against={against}
            note="No trend: drift rows are not kept over time."
          />
        </ul>
      )}
      <DailyTable daily={daily} />
      <div className={styles.grid}>
        <Panel
          title="Billing against collections"
          className={styles.chartPanel}
          query={trend}
          isEmpty={(answer) => answer.points.length === 0}
          empty="No figures in this period."
        >
          {(answer) => <TrendChart trend={answer} />}
        </Panel>
        <Panel
          title="Outstanding dues ageing"
          className={styles.chartPanel}
          viewAll={{ to: '/reports/outstanding' }}
          query={ageing}
          isEmpty={(answer) => Number(answer.total) === 0}
          empty="Nothing is owed on a posted bill."
        >
          {(answer) => <AgeingChart ageing={answer} />}
        </Panel>
        <RecentPaymentsPanel period={picked.period} />
        <TopPayersPanel />
        <PendingApprovalsPanel />
        <DriftPanel />
      </div>
    </section>
  );
}
