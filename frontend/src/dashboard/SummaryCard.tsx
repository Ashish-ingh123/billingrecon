import type { ReactNode } from 'react';
import { Area, AreaChart, ResponsiveContainer, Tooltip } from 'recharts';
import { formatDate, formatDecimal } from '../format';
import type { SummaryFigure } from './api';
import { changeTone, compactRupees, type GoodWhen } from './compact';
import styles from './Dashboard.module.css';
import { Tip, TipBox } from './Tooltip';
import { useChartTip } from './useChartTip';

const percent = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2, signDisplay: 'always' });

/** One day of a sparkline: its business date and the server's figure of that day. */
export interface SparkPoint {
  date: string;
  value: number | string;
}

const toneWords = { good: 'a good change', bad: 'a change for the worse', neutral: 'no change for better or worse' };

/**
 * One card of the summary (US-44, BR-902; ADR 0044 point 2, ADR 0050): the figure, compact for an amount, with a tooltip
 * of the exact amount, the previous period's figure and the change, and the exact amount for screen readers; the change
 * against the previous period, coloured by meaning and said in words; and a sparkline of the period's days, whose tooltip
 * gives each day's date and figure. The card's colour is one token (`--series-n`), mixed into the surface.
 */
export function SummaryCard({
  label,
  icon,
  series,
  figure,
  kind,
  goodWhen,
  against,
  spark,
  note,
  align = 'start',
}: {
  label: string;
  icon: ReactNode;
  /** The colour token of the card, 1 to 6. */
  series: 1 | 2 | 3 | 4 | 5 | 6;
  figure: SummaryFigure;
  kind: 'amount' | 'count';
  goodWhen: GoodWhen;
  /** "vs previous 30 days" */
  against: string;
  spark?: readonly SparkPoint[];
  /** Said instead of a change when the figure has none to compare with (the drift). */
  note?: string;
  /** Which edge of the figure its tooltip keeps to: the end for a card on the right of the row. */
  align?: 'start' | 'end';
}) {
  const exactOf = (value: number | string) => (kind === 'amount' ? `₹${formatDecimal(value)}` : String(value));
  const exact = exactOf(figure.value);
  const shown = kind === 'amount' ? compactRupees(figure.value) : String(figure.value);
  const change = figure.changePercent === null ? null : Number(figure.changePercent);
  const tone = change === null ? 'neutral' : changeTone(change, goodWhen);
  const arrow = change === null ? '' : change > 0 ? '↑ ' : change < 0 ? '↓ ' : '';

  return (
    <li className={`${styles.card} ${styles[`series${String(series)}`] ?? ''}`}>
      <div className={styles.cardHead}>
        <span className={styles.icon} aria-hidden="true">
          {icon}
        </span>
        <h2 className={styles.cardTitle}>{label}</h2>
      </div>
      <p className={styles.value}>
        <Tip
          align={align}
          tip={
            <TipBox
              title={label}
              rows={[
                { label: 'This period', value: exact, strong: true },
                ...(note === undefined
                  ? [
                      {
                        label: 'Previous period',
                        value: figure.previous === null ? 'None' : exactOf(figure.previous),
                      },
                      {
                        label: 'Change',
                        value:
                          change === null ? 'None to show' : `${arrow}${percent.format(change)}%, ${toneWords[tone]}`,
                      },
                    ]
                  : []),
              ]}
              note={note ?? against}
            />
          }
        >
          <span aria-hidden="true">{shown}</span>
          <span className={styles.visuallyHidden}>{exact}</span>
        </Tip>
      </p>
      {note !== undefined ? (
        <>
          <p className={styles.against}>As evaluated now</p>
          <p className={styles.note}>{note}</p>
        </>
      ) : change === null ? (
        <>
          <p className={`${styles.change} ${styles.neutral}`}>No change to show</p>
          <p className={styles.against}>nothing in the previous period</p>
        </>
      ) : (
        <>
          <p className={`${styles.change} ${styles[tone]}`} data-tone={tone}>
            <span aria-hidden="true">{arrow}</span>
            {percent.format(change)}%
            <span className={styles.visuallyHidden}>
              {tone === 'good' ? ', a good change' : tone === 'bad' ? ', a change for the worse' : ''}
            </span>
          </p>
          <p className={styles.against}>{against}</p>
        </>
      )}
      {spark !== undefined && spark.length > 1 && (
        <Sparkline label={label} series={series} points={spark} exactOf={exactOf} />
      )}
    </li>
  );
}

/** The days of a card's figure, with a tooltip of each day's date and figure, by pointer, by tap and by the arrow keys. */
function Sparkline({
  label,
  series,
  points,
  exactOf,
}: {
  label: string;
  series: number;
  points: readonly SparkPoint[];
  exactOf: (value: number | string) => string;
}) {
  const tip = useChartTip();
  const data = points.map((point) => ({ date: point.date, value: Number(point.value), exact: point.value }));

  return (
    <div className={styles.spark} role="group" aria-label={`${label} by day`} {...tip.handlers}>
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 4, right: 0, bottom: 0, left: 0 }} accessibilityLayer>
          <defs>
            <linearGradient id={`spark-${String(series)}`} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--card-color)" stopOpacity={0.35} />
              <stop offset="100%" stopColor="var(--card-color)" stopOpacity={0} />
            </linearGradient>
          </defs>
          <Tooltip
            active={tip.active}
            isAnimationActive={false}
            allowEscapeViewBox={{ x: false, y: true }}
            offset={12}
            cursor={{ stroke: 'var(--border-strong)', strokeWidth: 1 }}
            content={({ active, payload }) => {
              const day = (payload as readonly { payload?: (typeof data)[number] }[] | undefined)?.[0]?.payload;
              return active === true && day !== undefined ? (
                <TipBox title={formatDate(day.date)} rows={[{ label, value: exactOf(day.exact), strong: true }]} />
              ) : null;
            }}
          />
          <Area
            type="monotone"
            dataKey="value"
            stroke="var(--card-color)"
            strokeWidth={2}
            fill={`url(#spark-${String(series)})`}
            isAnimationActive={false}
            dot={false}
            activeDot={{ r: 3, stroke: 'var(--surface-raised)', strokeWidth: 2, fill: 'var(--card-color)' }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
