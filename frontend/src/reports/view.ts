import type { ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { formatDate, formatDecimal, formatInstant } from '../format';
import type { CellKind, CellValue, CsvColumn, CsvTotal } from './csv';

/** A column of a report on the screen: as in the CSV, with a cell of its own where the screen links or marks it. */
export interface ReportColumn<T> extends CsvColumn<T> {
  show?: (row: T) => ReactNode;
}

/** A total of a report; `under` names the column it sits beneath, or none for a total shown beside the table. */
export interface ReportTotal extends CsvTotal {
  under?: string;
}

/** A value as the screen shows it: amounts with Indian grouping, dates as in India. */
export function shown(kind: CellKind, value: CellValue): string {
  if (value === null || value === undefined || value === '') {
    return '—';
  }

  switch (kind) {
    case 'amount':
      return formatDecimal(value);
    case 'date':
      return formatDate(String(value));
    case 'instant':
      return formatInstant(String(value));
    case 'count':
      return String(value);
    case 'text':
      return String(value);
  }
}

/** The filters of a report kept in the address (ADR 0039 point 1), by name; a change goes back to the first page. */
export function useReportQuery<N extends string>(names: readonly N[]) {
  const [params, setParams] = useSearchParams();
  const values = Object.fromEntries(names.map((name) => [name, params.get(name) ?? ''])) as Record<N, string>;
  const parsed = Number.parseInt(params.get('page') ?? '1', 10);
  const page = Number.isFinite(parsed) && parsed >= 1 ? parsed : 1;

  const change = (next: Partial<Record<N | 'page', string>>, options?: { replace?: boolean }) => {
    const merged: Record<string, string> = { ...values, page: String(page), ...next };
    if (!('page' in next)) {
      merged.page = '1';
    }

    const query = new URLSearchParams();
    for (const [name, value] of Object.entries(merged)) {
      if (value !== '' && !(name === 'page' && value === '1')) {
        query.set(name, value);
      }
    }

    setParams(query, { replace: options?.replace === true });
  };

  return { values, page, change };
}

/** A count with its noun: "1 bill", "120 bills". */
export function counted(value: number | string, one: string, many: string): string {
  return `${String(value)} ${Number(value) === 1 ? one : many}`;
}
