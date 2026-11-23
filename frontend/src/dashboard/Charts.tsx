import {
  Cell,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  CartesianGrid,
} from 'recharts';
import table from '../components/DataTable.module.css';
import { formatDate, formatDecimal } from '../format';
import type { Ageing, DashboardTrend } from './api';
import { compactRupees } from './compact';
import styles from './Dashboard.module.css';

const tickStyle = { fill: 'var(--text-muted)', fontSize: 12 };

/**
 * Billing against collections (US-44): the period's running totals by business date, billed solid and collected dashed,
 * a tooltip with both exact values, and the figures as a table for whoever does not read the chart.
 */
export function TrendChart({ trend }: { trend: DashboardTrend }) {
  const data = trend.points.map((point) => ({
    label: formatDate(point.to),
    billed: Number(point.cumulativeBilled),
    collected: Number(point.cumulativeCollected),
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
        role="img"
        aria-label={
          last === undefined
            ? 'No figures in this period.'
            : `By ${formatDate(last.to)}, billed ₹${formatDecimal(last.cumulativeBilled)} and collected ₹${formatDecimal(last.cumulativeCollected)} in the period.`
        }
      >
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={data} margin={{ top: 8, right: 16, bottom: 0, left: 8 }}>
            <CartesianGrid stroke="var(--border)" vertical={false} />
            <XAxis dataKey="label" tick={tickStyle} stroke="var(--border)" minTickGap={24} />
            <YAxis
              tick={tickStyle}
              stroke="var(--border)"
              tickFormatter={(value: number) => compactRupees(value)}
              width={64}
            />
            <Tooltip
              formatter={(value, name) => [
                `₹${formatDecimal(Number(value))}`,
                name === 'billed' ? 'Billed' : 'Collected',
              ]}
              contentStyle={{
                background: 'var(--surface-raised)',
                border: '1px solid var(--border)',
                color: 'var(--text)',
              }}
            />
            <Line
              type="monotone"
              dataKey="billed"
              stroke="var(--series-1)"
              strokeWidth={2}
              dot={false}
              isAnimationActive={false}
            />
            <Line
              type="monotone"
              dataKey="collected"
              stroke="var(--series-3)"
              strokeWidth={2}
              strokeDasharray="6 4"
              dot={false}
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

const bucketSeries = ['var(--series-1)', 'var(--series-3)', 'var(--series-2)', 'var(--series-5)'];

/** The ageing of what is owed (BR-902): a donut with the total in its centre, and a legend of amounts and shares. */
export function AgeingChart({ ageing }: { ageing: Ageing }) {
  const data = ageing.buckets.map((bucket) => ({ name: bucket.bucket, value: Number(bucket.amount) }));

  return (
    <div className={styles.ageing}>
      <div
        className={styles.donut}
        role="img"
        aria-label={`₹${formatDecimal(ageing.total)} outstanding, by days past the due date.`}
      >
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={data}
              dataKey="value"
              nameKey="name"
              innerRadius="62%"
              outerRadius="92%"
              stroke="var(--surface-raised)"
              isAnimationActive={false}
            >
              {data.map((slice, index) => (
                <Cell key={slice.name} fill={bucketSeries[index % bucketSeries.length]} />
              ))}
            </Pie>
          </PieChart>
        </ResponsiveContainer>
        <div className={styles.donutCentre} aria-hidden="true">
          <strong title={`₹${formatDecimal(ageing.total)}`}>{compactRupees(ageing.total)}</strong>
          <span>Outstanding</span>
        </div>
      </div>
      <div>
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
            {ageing.buckets.map((bucket, index) => (
              <tr key={bucket.bucket}>
                <td>
                  <span
                    className={styles.dot}
                    style={{ background: bucketSeries[index % bucketSeries.length] }}
                    aria-hidden="true"
                  />
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
        <p className={styles.small}>
          Days past the due date on {formatDate(ageing.asOf)} (today in India). A bill not yet due counts in 0–30.
        </p>
      </div>
    </div>
  );
}
