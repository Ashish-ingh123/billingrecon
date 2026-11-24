// The arithmetic of the load test (ADR 0046): percentiles, the summary of one operation, the Server-Timing of an
// answer, and the verdict on the targets of docs/requirements.md section 5. No I/O, so `node --test` can hold it to
// known figures (stats.test.mjs).

/**
 * The nearest-rank percentile of sorted values: the smallest value that at least `p` percent of the values do not
 * exceed. Null for no values.
 */
export function percentile(sorted, p) {
  if (sorted.length === 0) return null;
  if (!(p > 0 && p <= 100)) throw new RangeError(`A percentile is above 0 and at most 100, not ${p}.`);
  const rank = Math.ceil((p / 100) * sorted.length);
  return sorted[rank - 1];
}

/** Count, p50, p95, p99 and max of durations in milliseconds; the figures are null when there are none. */
export function summarise(values) {
  const sorted = [...values].sort((a, b) => a - b);
  return {
    count: sorted.length,
    p50: percentile(sorted, 50),
    p95: percentile(sorted, 95),
    p99: percentile(sorted, 99),
    max: sorted.length === 0 ? null : sorted[sorted.length - 1],
  };
}

/**
 * The entries of a `Server-Timing` header as milliseconds by name: `number-wait;dur=1.25, number-hold;dur=6.8` is
 * `{ 'number-wait': 1.25, 'number-hold': 6.8 }`. An entry without a duration is left out; no header is `{}`.
 */
export function parseServerTiming(header) {
  const timings = {};
  if (!header) return timings;
  for (const entry of header.split(',')) {
    const [name, ...parameters] = entry.trim().split(';');
    const duration = parameters.map((parameter) => parameter.trim()).find((parameter) => parameter.startsWith('dur='));
    if (!name || duration === undefined) continue;
    const value = Number(duration.slice('dur='.length));
    if (Number.isFinite(value)) timings[name.trim()] = value;
  }

  return timings;
}

/**
 * The verdict on the targets: each judged operation's p95 under `p95Ms`, each series' p95 hold under `holdMs`, no
 * error in the measured phase, and no drift row. A figure that was never measured fails: a target nobody measured is
 * not met.
 */
export function judge({ operations, series, errors, driftRows }, { p95Ms, holdMs }) {
  const verdicts = [];
  for (const [name, summary] of Object.entries(operations)) {
    verdicts.push({
      target: `${name}: p95 under ${p95Ms} ms`,
      pass: summary.p95 !== null && summary.p95 < p95Ms,
      measured: summary.p95 === null ? 'not measured' : `${format(summary.p95)} ms`,
    });
  }

  for (const [name, timing] of Object.entries(series)) {
    verdicts.push({
      target: `${name} series: hold p95 under ${holdMs} ms`,
      pass: timing.hold.p95 !== null && timing.hold.p95 < holdMs,
      measured: timing.hold.p95 === null ? 'not measured' : `${format(timing.hold.p95)} ms`,
    });
  }

  verdicts.push({ target: 'no error while measuring', pass: errors === 0, measured: `${errors}` });
  verdicts.push({ target: 'reconciliation: no drift row', pass: driftRows === 0, measured: `${driftRows}` });

  return verdicts;
}

/** A duration for the report: one decimal, or a dash when there is none. */
export function format(milliseconds) {
  return milliseconds === null || milliseconds === undefined ? '-' : milliseconds.toFixed(1);
}

/** Rows of cells as a plain table, each column as wide as its widest cell; numbers on the right. */
export function table(header, rows) {
  const all = [header, ...rows].map((row) => row.map((cell) => String(cell)));
  const widths = header.map((_, column) => Math.max(...all.map((row) => row[column].length)));
  const line = (row) =>
    row.map((cell, column) => (column === 0 ? cell.padEnd(widths[column]) : cell.padStart(widths[column]))).join('  ');
  return [line(all[0]), widths.map((width) => '-'.repeat(width)).join('  '), ...all.slice(1).map(line)].join('\n');
}
