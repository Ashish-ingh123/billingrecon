import { useId, useState, type FormEvent, type ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { problemOf } from '../api/call';
import buttons from '../components/buttons.module.css';
import listStyles from '../components/ListControls.module.css';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, todayInIndia } from '../format';
import { useAgeing, useSummary, useTrend, type DashboardPeriod, type DailyFigures } from './api';
import { AgeingChart, TrendChart } from './Charts';
import styles from './Dashboard.module.css';
import { DriftPanel, Panel, PendingApprovalsPanel, RecentPaymentsPanel, TopPayersPanel } from './Panels';
import { SummaryCard } from './SummaryCard';

/** A line icon of the cards, drawn here: no icon package (ADR 0044 point 1). */
function Icon({ children }: { children: ReactNode }) {
  return (
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {children}
    </svg>
  );
}

const icons = {
  bill: (
    <Icon>
      <path d="M6 2h9l4 4v16H6z" />
      <path d="M9 12h7M9 16h7M9 8h3" />
    </Icon>
  ),
  cash: (
    <Icon>
      <rect x="2" y="6" width="20" height="12" rx="2" />
      <circle cx="12" cy="12" r="3" />
    </Icon>
  ),
  clock: (
    <Icon>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 3" />
    </Icon>
  ),
  stack: (
    <Icon>
      <ellipse cx="12" cy="6" rx="8" ry="3" />
      <path d="M4 6v6c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6" />
    </Icon>
  ),
  check: (
    <Icon>
      <rect x="3" y="3" width="18" height="18" rx="3" />
      <path d="M8 12l3 3 5-6" />
    </Icon>
  ),
  alert: (
    <Icon>
      <path d="M12 3l10 18H2z" />
      <path d="M12 10v5M12 18v.01" />
    </Icon>
  ),
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

  return (
    <form className={styles.period} aria-label="Choose the period" onSubmit={apply}>
      <div className={listStyles.field}>
        <label htmlFor={rangeId}>Period</label>
        <select
          id={rangeId}
          className={listStyles.input}
          value={range}
          onChange={(event) =>
            choose(
              event.target.value === '30'
                ? {}
                : event.target.value === 'custom'
                  ? { range: 'custom', from, to }
                  : { range: event.target.value },
            )
          }
        >
          {ranges.map((days) => (
            <option key={days} value={days}>
              Last {days} days
            </option>
          ))}
          <option value="custom">Custom</option>
        </select>
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

const series = (daily: readonly DailyFigures[] | undefined, pick: (day: DailyFigures) => number | string) =>
  daily?.map((day) => Number(pick(day)));

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
          data === undefined
            ? 'The figures of a period against the previous one.'
            : `${formatDate(data.period.from)} to ${formatDate(data.period.to)}, compared with ${formatDate(data.previousPeriod.from)} to ${formatDate(data.previousPeriod.to)} (IST business dates)`
        }
        actions={<PeriodPicker key={`${picked.range}${picked.period.from}`} {...picked} />}
      />
      {data === undefined ? (
        summary.error !== null ? (
          <ProblemAlert problem={problemOf(summary.error)} />
        ) : (
          <div className={styles.skeleton} role="status" aria-label="Loading the summary" />
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
      <div className={styles.grid}>
        <Panel
          title="Billing against collections"
          query={trend}
          isEmpty={(answer) => answer.points.length === 0}
          empty="No figures in this period."
        >
          {(answer) => <TrendChart trend={answer} />}
        </Panel>
        <Panel
          title="Outstanding dues ageing"
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
