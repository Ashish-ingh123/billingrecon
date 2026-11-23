import type { Paise } from '../bills/lineMath';
import { signedPaise } from './csv';

/** A page of a report as the API answers it (`docs/api.md` section 8.15): its rows, and the totals of every page. */
export interface ReportPageOf<T, U> {
  period?: { from: string; to: string } | null;
  items: T[];
  page: number | string;
  pageSize: number | string;
  totalCount: number | string;
  totals?: U | null;
}

/** The rows an export reads at most: a hundred pages of a hundred (ADR 0043 point 1). */
export const mostExportRows = 10_000;

/** The API's largest page. */
export const exportPageSize = 100;

/** The filter holds more rows than an export reads. */
export class ExportTooLargeError extends Error {
  readonly rows: number;

  constructor(rows: number) {
    super(
      `This report has ${String(rows)} rows; an export reads at most ${String(mostExportRows)}. Narrow the filter.`,
    );
    this.rows = rows;
  }
}

/** The report changed while it was read, twice: the rows do not come to the totals. */
export class ExportChangedError extends Error {
  constructor() {
    super('The report changed while it was exported, so its rows do not come to its totals. Export it again.');
  }
}

/** Every row of a report and the page that answered last, which carries the totals and the period. */
export interface WholeReport<T, U> {
  rows: T[];
  last: ReportPageOf<T, U>;
}

/**
 * Reads every page of a report, one after another, for an export. The rows must be as many as the report counts and
 * must come to its totals (`isWhole`), and the totals must be the same on the first and last pages: a report that
 * changed while it was read (a payment recorded meanwhile) is read once more, and refused if it changed again. Totals
 * that carry the moment of the reading (the reconciliation's `checkedAtUtc`) are compared by `sameTotals`.
 */
export async function readWholeReport<T, U>({
  read,
  isWhole,
  sameTotals = (a, b) => JSON.stringify(a) === JSON.stringify(b),
}: {
  read: (page: number, pageSize: number) => Promise<ReportPageOf<T, U>>;
  isWhole: (rows: readonly T[], totals: U) => boolean;
  sameTotals?: (first: U, last: U) => boolean;
}): Promise<WholeReport<T, U>> {
  for (let attempt = 0; attempt < 2; attempt += 1) {
    const first = await read(1, exportPageSize);
    const count = Number(first.totalCount);
    if (count > mostExportRows) {
      throw new ExportTooLargeError(count);
    }

    const rows = [...first.items];
    let last = first;
    const pages = Math.ceil(count / exportPageSize);
    for (let page = 2; page <= pages; page += 1) {
      last = await read(page, exportPageSize);
      rows.push(...last.items);
    }

    const before = first.totals;
    const totals = last.totals;
    if (
      before !== null &&
      before !== undefined &&
      totals !== null &&
      totals !== undefined &&
      Number(last.totalCount) === count &&
      rows.length === count &&
      sameTotals(before, totals) &&
      isWhole(rows, totals)
    ) {
      return { rows, last };
    }
  }

  throw new ExportChangedError();
}

/** The sum of an amount over rows, in paise, for the check of an export against its totals. */
export function sumOf<T>(rows: readonly T[], amount: (row: T) => number | string): Paise {
  return rows.reduce((sum, row) => sum + signedPaise(amount(row)), 0n);
}

/** Whether the rows come to a total of the server, to the paisa. */
export function comesTo<T>(rows: readonly T[], amount: (row: T) => number | string, total: number | string): boolean {
  return sumOf(rows, amount) === signedPaise(total);
}
