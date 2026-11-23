/**
 * How values are shown (ADR 0039): dates as in India, numbers with Indian grouping (12,80,000.00). Formatting never
 * changes a value: the figures are the server's, and only their display is made here.
 */

const businessDate = new Intl.DateTimeFormat('en-IN', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  timeZone: 'UTC',
});

const instant = new Intl.DateTimeFormat('en-IN', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Asia/Kolkata',
});

/** A business date (`yyyy-MM-dd`, a date in India) as "05 Oct 2026"; an empty value as an empty string. */
export function formatDate(value: string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }

  return businessDate.format(new Date(`${value}T00:00:00Z`));
}

/** An instant (ISO 8601 in UTC) as the date and time in India. */
export function formatInstant(value: string | null | undefined): string {
  return value === null || value === undefined || value === '' ? '' : instant.format(new Date(value));
}

/** A decimal with Indian grouping and at least two decimals, at most `maxDecimals`: 1500 as "1,500.00". */
export function formatDecimal(value: number | string, maxDecimals = 2): string {
  return new Intl.NumberFormat('en-IN', {
    minimumFractionDigits: Math.min(2, maxDecimals),
    maximumFractionDigits: maxDecimals,
  }).format(Number(value));
}

/** A rate as "18%" or "12.5%". */
export function formatPercent(value: number | string): string {
  return `${new Intl.NumberFormat('en-IN', { maximumFractionDigits: 2 }).format(Number(value))}%`;
}

const isoDateInIndia = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata' });

/** Today's business date in India (`yyyy-MM-dd`), as the API's clock reckons it (BR-003). */
export function todayInIndia(now: Date = new Date()): string {
  return isoDateInIndia.format(now);
}
