import { guardText, quote } from '../reports/csv';
import type { ImportFinding } from './api';

// Written first, so that a spreadsheet reads the file as UTF-8.
const byteOrderMark = String.fromCharCode(0xfeff);

/** Where a finding is: its line, the header, or the file as a whole. */
export function placeOf(line: number | string): string {
  const number = Number(line);
  return number === 0 ? 'The file' : number === 1 ? 'Header' : `Line ${String(number)}`;
}

/** The findings as a CSV file a spreadsheet opens: one line each, the reason guarded against being read as a formula. */
export function findingsCsv(findings: readonly ImportFinding[]): string {
  const lines = [
    ['File', 'Line', 'Reason'],
    ...findings.map((finding) => [finding.file, String(finding.line), guardText(finding.reason)]),
  ];

  return `${byteOrderMark}${lines.map((line) => line.map(quote).join(',')).join('\r\n')}\r\n`;
}
