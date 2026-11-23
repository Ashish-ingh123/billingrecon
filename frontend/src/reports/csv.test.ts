import { describe, expect, it } from 'vitest';
import { cellText, guardText, quote, reportCsv } from './csv';
import { comesTo, ExportChangedError, ExportTooLargeError, readWholeReport, type ReportPageOf } from './exportReport';

describe('the CSV of a report (ADR 0043 point 1)', () => {
  it('writes amounts exactly with two places and no grouping, below zero too', () => {
    expect(cellText('amount', 1049.48)).toBe('1049.48');
    expect(cellText('amount', 1280000)).toBe('1280000.00');
    expect(cellText('amount', '0.1')).toBe('0.10');
    expect(cellText('amount', -25.5)).toBe('-25.50');
    expect(cellText('amount', null)).toBe('');
  });

  it('quotes a field with a comma, a quote or a line break, and never lets a text run as a formula', () => {
    expect(quote('Verma, Asha')).toBe('"Verma, Asha"');
    expect(quote('The "best" payer')).toBe('"The ""best"" payer"');
    expect(quote('two\nlines')).toBe('"two\nlines"');
    for (const start of ['=SUM(A1)', '+91', '-1', '@cmd']) {
      expect(guardText(start)).toBe(`'${start}`);
    }

    expect(cellText('text', '=HYPERLINK("x")')).toBe(`'=HYPERLINK("x")`);
    expect(cellText('amount', -1)).toBe('-1.00');
  });

  it('writes the title, the period, the rows under their headings and the totals, with a BOM and CRLF', () => {
    const text = reportCsv({
      title: 'Bill register',
      period: { from: '2026-11-01', to: '2026-11-30' },
      columns: [
        { header: 'Payer', kind: 'text', value: (row: { payer: string; total: number }) => row.payer },
        { header: 'Total (₹)', kind: 'amount', value: (row) => row.total },
      ],
      rows: [
        { payer: 'Verma, Asha', total: 1049.48 },
        { payer: '=cmd', total: 590 },
      ],
      totals: [{ label: 'Total (₹)', kind: 'amount', value: 1639.48 }],
    });

    expect(text.charCodeAt(0)).toBe(0xfeff);
    expect(text.slice(1).split('\r\n')).toEqual([
      'Bill register',
      'Period,2026-11-01,2026-11-30',
      '',
      'Payer,Total (₹)',
      '"Verma, Asha",1049.48',
      "'=cmd,590.00",
      '',
      'Total (₹),1639.48',
      '',
    ]);
  });
});

/** A report of `count` rows of 1.00 each, answered a page at a time, its totals over every page. */
function report(count: number, total: string = String(count)) {
  const pages: number[] = [];
  const read = (page: number, pageSize: number): Promise<ReportPageOf<{ amount: number }, { amount: string }>> => {
    pages.push(page);
    const items = Array.from({ length: Math.max(0, Math.min(pageSize, count - (page - 1) * pageSize)) }, () => ({
      amount: 1,
    }));
    return Promise.resolve({ items, page, pageSize, totalCount: count, totals: { amount: total } });
  };
  return { read, pages };
}

const isWhole = (rows: readonly { amount: number }[], totals: { amount: string }) =>
  comesTo(rows, (row) => row.amount, totals.amount);

describe('reading every row for an export', () => {
  it('reads every page, a hundred rows at a time, and checks the rows against the totals', async () => {
    const { read, pages } = report(250);

    const whole = await readWholeReport({ read, isWhole });

    expect(whole.rows).toHaveLength(250);
    expect(pages).toEqual([1, 2, 3]);
  });

  it('refuses more than ten thousand rows', async () => {
    await expect(readWholeReport({ read: report(10_001).read, isWhole })).rejects.toBeInstanceOf(ExportTooLargeError);
  });

  it('reads once more when the rows do not come to the totals, then refuses', async () => {
    const { read, pages } = report(3, '4.00');

    await expect(readWholeReport({ read, isWhole })).rejects.toBeInstanceOf(ExportChangedError);
    expect(pages).toEqual([1, 1]);
  });

  it('takes the second reading when the report changed only during the first', async () => {
    let call = 0;
    const read = (page: number, pageSize: number) => {
      call += 1;
      // The first reading's last page says one row more than its first: a payment was recorded meanwhile.
      const count = call === 2 ? 101 : 100;
      return Promise.resolve({
        items: Array.from({ length: page === 1 ? 100 : 0 }, () => ({ amount: 1 })),
        page,
        pageSize,
        totalCount: call === 1 ? 101 : count,
        totals: { amount: call <= 2 ? '101' : '100' },
      });
    };

    const whole = await readWholeReport({ read, isWhole });

    expect(whole.rows).toHaveLength(100);
  });
});
