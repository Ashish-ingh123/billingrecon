import { useId, useState, type KeyboardEvent } from 'react';
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import table from '../components/DataTable.module.css';
import { formatDate, formatDecimal } from '../format';
import type { Ageing, DashboardTrend } from './api';
import { compactRupees } from './compact';
import styles from './Dashboard.module.css';
import { TipBox } from './Tooltip';
import { useChartTip } from './useChartTip';

const tickStyle = { fill: 'var(--text-muted)', fontSize: 12 };
const rupees = (value: number | string) => `₹${formatDecimal(value)}`;

/**
 * Billing against collections (US-44, ADR 0050): the period's running totals by business date, billed solid and
 * collected dashed. Pointing at the chart, tapping it, or focusing it and using the arrow keys moves a crosshair from
 * point to point, with a dot on each line and a tooltip of the date, billed and collected to date, and that day's or
 * week's billed and collected, as the server answered them. The figures are also a table.
 */
export function TrendChart({ trend }: { trend: DashboardTrend }) {
  const tip = useChartTip();
  const week = trend.interval === 'Week';
  const data = trend.points.map((point) => ({
    label: formatDate(point.to),
    billed: Number(point.cumulativeBilled),
    collected: Number(point.cumulativeCollected),
    point,
  }));
  const last = trend.points.at(-1);

  return (
    <>
      <ul className={styles.legend}>
        <li>
          <span className={styles.swatch} aria-hidden="true" />
          Billed
        </li>
        <li>
          <span className={`${styles.swatch} ${styles.dashed}`} aria-hidden="true" />
          Collected
        </li>
      </ul>
      <div
        className={styles.chart}
        role="group"
        aria-label={
          last === undefined
            ? 'No figures in this period.'
            : `Billing against collections. By ${formatDate(last.to)}, billed ${rupees(last.cumulativeBilled)} and collected ${rupees(last.cumulativeCollected)} in the period. Focus the chart and use the arrow keys to go from point to point.`
        }
        {...tip.handlers}
      >
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 8, right: 16, bottom: 0, left: 8 }} accessibilityLayer>
            <CartesianGrid stroke="var(--border-soft)" vertical={false} />
            <XAxis dataKey="label" tick={tickStyle} stroke="var(--border)" minTickGap={24} />
            <YAxis
              tick={tickStyle}
              stroke="var(--border)"
              tickFormatter={(value: number) => compactRupees(value)}
              width={64}
            />
            <Tooltip
              active={tip.active}
              isAnimationActive={false}
              allowEscapeViewBox={{ x: false, y: false }}
              offset={16}
              cursor={{ stroke: 'var(--border-strong)', strokeWidth: 1 }}
              content={({ active, payload }) => {
                const shown = (payload as readonly { payload?: (typeof data)[number] }[] | undefined)?.[0]?.payload;
                if (active !== true || shown === undefined) return null;
                const { point } = shown;
                return (
                  <TipBox
                    title={
                      point.from === point.to
                        ? formatDate(point.to)
                        : `${formatDate(point.from)} to ${formatDate(point.to)}`
                    }
                    rows={[
                      { label: 'Billed to date', value: rupees(point.cumulativeBilled), series: 1, strong: true },
                      { label: 'Collected to date', value: rupees(point.cumulativeCollected), series: 3, strong: true },
                      { label: week ? 'Billed that week' : 'Billed that day', value: rupees(point.billed) },
                      { label: week ? 'Collected that week' : 'Collected that day', value: rupees(point.collected) },
                    ]}
                  />
                );
              }}
            />
            <Line
              type="monotone"
              dataKey="billed"
              stroke="var(--series-1)"
              strokeWidth={2}
              dot={false}
              activeDot={{ r: 5, stroke: 'var(--surface-raised)', strokeWidth: 2, fill: 'var(--series-1)' }}
              isAnimationActive={false}
            />
            <Line
              type="monotone"
              dataKey="collected"
              stroke="var(--series-3)"
              strokeWidth={2}
              strokeDasharray="6 4"
              dot={false}
              activeDot={{ r: 5, stroke: 'var(--surface-raised)', strokeWidth: 2, fill: 'var(--series-3)' }}
              isAnimationActive={false}
            />
          </LineChart>
        </ResponsiveContainer>
      </div>
      <details>
        <summary>Show the figures as a table</summary>
        <div className={table.wrap}>
          <table className={table.table} aria-label="Billing against collections">
            <thead>
              <tr>
                <th scope="col">From</th>
                <th scope="col">To</th>
                <th scope="col" className={table.number}>
                  Billed (₹)
                </th>
                <th scope="col" className={table.number}>
                  Collected (₹)
                </th>
                <th scope="col" className={table.number}>
                  Billed so far (₹)
                </th>
                <th scope="col" className={table.number}>
                  Collected so far (₹)
                </th>
              </tr>
            </thead>
            <tbody>
              {trend.points.map((point) => (
                <tr key={point.from}>
                  <td>{formatDate(point.from)}</td>
                  <td>{formatDate(point.to)}</td>
                  <td className={table.number}>{formatDecimal(point.billed)}</td>
                  <td className={table.number}>{formatDecimal(point.collected)}</td>
                  <td className={table.number}>{formatDecimal(point.cumulativeBilled)}</td>
                  <td className={table.number}>{formatDecimal(point.cumulativeCollected)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </>
  );
}

const bucketNames: Record<string, string> = {
  '0-30': '0–30 days',
  '31-60': '31–60 days',
  '61-90': '61–90 days',
  '90+': 'Over 90 days',
};

const bucketClass = (index: number) => styles[`bucket${String((index % 4) + 1)}`] ?? '';

/** The path of one slice of a ring, from one angle to another (radians, clockwise from the top), in the viewBox's units. */
function slicePath(from: number, to: number, inner: number, outer: number): string {
  const point = (radius: number, angle: number) =>
    `${(radius * Math.sin(angle)).toFixed(3)} ${(-radius * Math.cos(angle)).toFixed(3)}`;
  // A whole ring is drawn as two halves: an arc cannot end where it starts.
  if (to - from >= Math.PI * 2 - 1e-6) {
    return `${slicePath(0, Math.PI, inner, outer)} ${slicePath(Math.PI, Math.PI * 2, inner, outer)}`;
  }

  const large = to - from > Math.PI ? 1 : 0;
  return [
    `M ${point(outer, from)}`,
    `A ${String(outer)} ${String(outer)} 0 ${String(large)} 1 ${point(outer, to)}`,
    `L ${point(inner, to)}`,
    `A ${String(inner)} ${String(inner)} 0 ${String(large)} 0 ${point(inner, from)}`,
    'Z',
  ].join(' ');
}

/**
 * The ageing of what is owed (BR-902, ADR 0050): a donut, drawn here, with the total in its centre. Pointing at a slice,
 * tapping it, or focusing the donut and using the arrow keys picks a bucket: its slice thickens, the centre shows its
 * exact amount and name, and a tooltip gives the bucket, the amount and the share as the API answers them. Escape, blur
 * and a second tap let it go. The legend is the table of the same figures.
 */
export function AgeingChart({ ageing }: { ageing: Ageing }) {
  const tipId = useId();
  const [chosen, setChosen] = useState<number | null>(null);
  const total = Number(ageing.total);
  const buckets = ageing.buckets;

  // Each slice's angles, clockwise from the top; the geometry of the drawing, not a figure shown.
  const sweeps = buckets.map((bucket) => (total > 0 ? (Number(bucket.amount) / total) * Math.PI * 2 : 0));
  const slices = sweeps.map((sweep, index) => {
    const from = sweeps.slice(0, index).reduce((sum, each) => sum + each, 0);
    return { index, from, to: from + sweep };
  });

  const picked = chosen === null ? undefined : buckets[chosen];
  const move = (step: number) =>
    setChosen((current) => {
      const from = current ?? (step > 0 ? -1 : 0);
      return (from + step + buckets.length) % buckets.length;
    });
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
      event.preventDefault();
      move(1);
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
      event.preventDefault();
      move(-1);
    } else if (event.key === 'Escape') {
      setChosen(null);
    }
  };

  return (
    <div className={styles.ageing}>
      <div
        className={styles.donut}
        role="slider"
        tabIndex={0}
        aria-label={`${rupees(ageing.total)} outstanding, by days past the due date. Use the arrow keys to go from bucket to bucket.`}
        aria-valuemin={0}
        aria-valuemax={Math.max(buckets.length - 1, 0)}
        aria-valuenow={chosen ?? 0}
        aria-valuetext={
          picked === undefined
            ? 'No bucket chosen'
            : `${bucketNames[picked.bucket] ?? picked.bucket}: ${rupees(picked.amount)}, ${formatDecimal(picked.share)}%`
        }
        aria-describedby={picked === undefined ? undefined : tipId}
        onKeyDown={onKeyDown}
        onBlur={() => setChosen(null)}
        onPointerLeave={(event) => {
          if (event.pointerType === 'mouse') setChosen(null);
        }}
      >
        <svg viewBox="-110 -110 220 220" aria-hidden="true">
          {slices.map((slice) =>
            slice.to > slice.from ? (
              <path
                key={slice.index}
                className={`${styles.slice} ${bucketClass(slice.index)} ${chosen !== null && chosen !== slice.index ? styles.dim : ''}`}
                d={slicePath(slice.from, slice.to, 62, chosen === slice.index ? 104 : 94)}
                onPointerEnter={(event) => {
                  if (event.pointerType === 'mouse') setChosen(slice.index);
                }}
                onClick={(event) => {
                  if ((event.nativeEvent as PointerEvent).pointerType !== 'mouse') {
                    setChosen((current) => (current === slice.index ? null : slice.index));
                  }
                }}
              />
            ) : null,
          )}
        </svg>
        <div className={styles.donutCentre} aria-hidden="true">
          {picked === undefined ? (
            <>
              <strong>{compactRupees(ageing.total)}</strong>
              <span>Outstanding</span>
            </>
          ) : (
            <>
              <strong>{rupees(picked.amount)}</strong>
              <span>{bucketNames[picked.bucket] ?? picked.bucket}</span>
            </>
          )}
        </div>
        {picked !== undefined && chosen !== null && (
          <div role="tooltip" id={tipId} className={styles.donutTip}>
            <TipBox
              title={bucketNames[picked.bucket] ?? picked.bucket}
              rows={[
                { label: 'Amount', value: rupees(picked.amount), strong: true },
                { label: 'Share', value: `${formatDecimal(picked.share)}%` },
                { label: 'Bills', value: String(picked.bills) },
              ]}
            />
          </div>
        )}
      </div>
      <div>
        {/* A frame that may scroll is reachable by the keyboard, and named (axe: scrollable-region-focusable). */}
        <div className={table.wrap} tabIndex={0} role="region" aria-label="Outstanding dues ageing, the table">
          <table className={table.table} aria-label="Outstanding dues ageing">
            <thead>
              <tr>
                <th scope="col">Past due date</th>
                <th scope="col" className={table.number}>
                  Amount (₹)
                </th>
                <th scope="col" className={table.number}>
                  Share
                </th>
              </tr>
            </thead>
            <tbody>
              {buckets.map((bucket, index) => (
                <tr key={bucket.bucket}>
                  <td>
                    <span className={`${styles.dot} ${bucketClass(index)}`} aria-hidden="true" />
                    {bucketNames[bucket.bucket] ?? bucket.bucket}
                  </td>
                  <td className={table.number}>{formatDecimal(bucket.amount)}</td>
                  <td className={table.number}>{formatDecimal(bucket.share)}%</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row">Total</th>
                <td className={table.number}>{formatDecimal(ageing.total)}</td>
                <td className={table.number}>100%</td>
              </tr>
            </tfoot>
          </table>
        </div>
        <p className={styles.small}>
          Days past the due date on {formatDate(ageing.asOf)} (today in India). A bill not yet due counts in 0–30.
        </p>
      </div>
    </div>
  );
}
