import type { CSSProperties, ReactNode } from 'react';
import { Area, AreaChart, ResponsiveContainer } from 'recharts';
import { formatDecimal } from '../format';
import type { SummaryFigure } from './api';
import { changeTone, compactRupees, type GoodWhen } from './compact';
import styles from './Dashboard.module.css';

const percent = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2, signDisplay: 'always' });

/**
 * One card of the summary (US-44, BR-902; ADR 0044 point 2): the figure, compact for an amount with the exact amount in
 * its tooltip and for screen readers; the change against the previous period, coloured by meaning; and a sparkline of
 * the period's days. The card's colour is one token (`--series-n`), mixed into the surface.
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
}: {
  label: string;
  icon: ReactNode;
  /** The colour token of the card, 1 to 6. */
  series: number;
  figure: SummaryFigure;
  kind: 'amount' | 'count';
  goodWhen: GoodWhen;
  /** "vs previous 30 days" */
  against: string;
  spark?: readonly number[];
  /** Said instead of a change when the figure has none to compare with (the drift). */
  note?: string;
}) {
  const exact = kind === 'amount' ? `₹${formatDecimal(figure.value)}` : String(figure.value);
  const shown = kind === 'amount' ? compactRupees(figure.value) : String(figure.value);
  const change = figure.changePercent === null ? null : Number(figure.changePercent);
  const tone = change === null ? 'neutral' : changeTone(change, goodWhen);
  const style = { '--card-color': `var(--series-${String(series)})` } as CSSProperties;

  return (
    <li className={styles.card} style={style}>
      <div className={styles.cardHead}>
        <span className={styles.icon} aria-hidden="true">
          {icon}
        </span>
        <h2 className={styles.cardTitle}>{label}</h2>
      </div>
      <p className={styles.value} title={exact}>
        <span aria-hidden="true">{shown}</span>
        <span className={styles.visuallyHidden}>{exact}</span>
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
            <span aria-hidden="true">{change > 0 ? '↑ ' : change < 0 ? '↓ ' : ''}</span>
            {percent.format(change)}%
            <span className={styles.visuallyHidden}>
              {tone === 'good' ? ', a good change' : tone === 'bad' ? ', a change for the worse' : ''}
            </span>
          </p>
          <p className={styles.against}>{against}</p>
        </>
      )}
      {spark !== undefined && spark.length > 1 && (
        <div className={styles.spark} aria-hidden="true">
          <ResponsiveContainer width="100%" height="100%">
            <AreaChart
              data={spark.map((value, index) => ({ index, value }))}
              margin={{ top: 4, right: 0, bottom: 0, left: 0 }}
            >
              <defs>
                <linearGradient id={`spark-${String(series)}`} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" style={{ stopColor: 'var(--card-color)', stopOpacity: 0.35 }} />
                  <stop offset="100%" style={{ stopColor: 'var(--card-color)', stopOpacity: 0 }} />
                </linearGradient>
              </defs>
              <Area
                type="monotone"
                dataKey="value"
                stroke="var(--card-color)"
                strokeWidth={2}
                fill={`url(#spark-${String(series)})`}
                isAnimationActive={false}
                dot={false}
              />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}
    </li>
  );
}
