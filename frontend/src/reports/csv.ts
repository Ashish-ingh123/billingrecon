import type { Paise } from '../bills/lineMath';
import { asRupees, paiseOfServer } from '../money';

/**
 * The CSV of a report (ADR 0043 point 1): what a spreadsheet opens as the same rows and figures the screen shows.
 *
 * - UTF-8 with a byte order mark, so that a spreadsheet reads ₹ and names in any script; lines end in CRLF (RFC 4180).
 * - An amount is a plain decimal with two places (`1049.48`), exact from the server's figure, never grouped, so that a
 *   spreadsheet takes it as a number; a business date is `yyyy-MM-dd`, an instant the server's UTC text.
 * - A field holding a comma, a quote or a line break is quoted, its quotes doubled.
 * - A text field that starts with `=`, `+`, `-`, `@`, a tab or a carriage return is prefixed with `'`, so that a
 *   spreadsheet never runs it as a formula (CSV injection). Numbers are never prefixed.
 */

/** How a column's values are written, on the screen and in the CSV. */
export type CellKind = 'text' | 'amount' | 'date' | 'instant' | 'count';

export type CellValue = string | number | null | undefined;

/** Written first, so that a spreadsheet reads the file as UTF-8. */
const byteOrderMark = '\uFEFF';

const formulaStart = /^[=+\-@\t\r]/;

/** A text field as a spreadsheet must take it: never as a formula. */
export function guardText(value: string): string {
  return formulaStart.test(value) ? `'${value}` : value;
}

/** One field of a line, quoted when it has to be. */
export function quote(field: string): string {
  return /[",\r\n]/.test(field) ? `"${field.replace(/"/g, '""')}"` : field;
}

/** An amount of the server, which a report may answer below zero (a day's net collection), in paise. */
export function signedPaise(value: number | string): Paise {
  const text = typeof value === 'number' ? value.toFixed(2) : value.trim();
  return text.startsWith('-') ? -paiseOfServer(text.slice(1)) : paiseOfServer(text);
}

function amountText(value: number | string): string {
  const paise = signedPaise(value);
  return paise < 0n ? `-${asRupees(-paise)}` : asRupees(paise);
}

/** A value as the CSV writes it, by the kind of its column. */
export function cellText(kind: CellKind, value: CellValue): string {
  if (value === null || value === undefined || value === '') {
    return '';
  }

  switch (kind) {
    case 'amount':
      return amountText(value);
    case 'count':
      return String(value);
    case 'date':
    case 'instant':
      return String(value);
    case 'text':
      return guardText(String(value));
  }
}

/** A column of a report: its heading, the kind of its values, and its value in a row. */
export interface CsvColumn<T> {
  header: string;
  kind: CellKind;
  value: (row: T) => CellValue;
}

/** A total of a report, written under its rows. */
export interface CsvTotal {
  label: string;
  kind: CellKind;
  value: CellValue;
}

/** The whole file: the title, the period when it has one, the rows under their headings, then the totals. */
export function reportCsv<T>({
  title,
  period,
  columns,
  rows,
  totals,
}: {
  title: string;
  period?: { from: string; to: string } | null;
  columns: readonly CsvColumn<T>[];
  rows: readonly T[];
  totals: readonly CsvTotal[];
}): string {
  const lines: string[][] = [[guardText(title)]];
  if (period !== null && period !== undefined) {
    lines.push(['Period', period.from, period.to]);
  }

  lines.push([]);
  lines.push(columns.map((column) => guardText(column.header)));
  for (const row of rows) {
    lines.push(columns.map((column) => cellText(column.kind, column.value(row))));
  }

  lines.push([]);
  for (const total of totals) {
    lines.push([guardText(total.label), cellText(total.kind, total.value)]);
  }

  return `${byteOrderMark}${lines.map((line) => line.map(quote).join(',')).join('\r\n')}\r\n`;
}

/** Hands the file to the browser to save. */
export function saveCsv(fileName: string, text: string): void {
  saveFile(fileName, new Blob([text], { type: 'text/csv;charset=utf-8' }));
}

/** Hands a file to the browser to save under `fileName`, as the CSV of a report is saved. */
export function saveFile(fileName: string, file: Blob): void {
  const url = URL.createObjectURL(file);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
