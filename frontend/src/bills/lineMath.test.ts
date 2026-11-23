import { describe, expect, it } from 'vitest';
import sharedCases from './lineCases.json';
import { billTotals, lineAmounts, rupees, type LineAmounts } from './lineMath';

const line = (quantity: string, unitPrice: string, discountAmount: string, taxRatePercent: string) =>
  lineAmounts({ quantity, unitPrice, discountAmount, taxRatePercent });

const asRupees = (amounts: LineAmounts | null) =>
  amounts === null
    ? null
    : {
        lineAmount: rupees(amounts.lineAmount),
        discountAmount: rupees(amounts.discountAmount),
        taxAmount: rupees(amounts.taxAmount),
        lineTotal: rupees(amounts.lineTotal),
      };

describe("a bill line's arithmetic, as the server's (BR-202, BR-203, ADR 0040 point 3)", () => {
  it('gives the example of docs/api.md: 3 x 333.3333 less 0.50 at 5% is 1049.48', () => {
    expect(asRupees(line('3', '333.3333', '0.50', '5'))).toEqual({
      lineAmount: '1000.00',
      discountAmount: '0.50',
      taxAmount: '49.98',
      lineTotal: '1049.48',
    });
  });

  // The cases the server's LineCalculator is held to as well (LineCalculatorSharedCasesTests): midpoints where a binary
  // float lands below the half paisa, thirds, a discount of the whole line, the largest line.
  it.each(sharedCases.cases)(
    '$quantity x $unitPrice less $discountAmount at $taxRatePercent% comes to $lineTotal',
    ({ quantity, unitPrice, discountAmount, taxRatePercent, lineAmount, taxAmount, lineTotal }) => {
      const amounts = asRupees(line(quantity, unitPrice, discountAmount, taxRatePercent));
      expect(amounts).toMatchObject({ lineAmount, taxAmount, lineTotal });
    },
  );

  it('reads the shared cases', () => {
    expect(sharedCases.cases.length).toBeGreaterThan(10);
  });

  it('takes a blank discount as none, and is not a line while a number is missing or not a number', () => {
    expect(asRupees(line('2', '50', ' ', '0'))?.lineTotal).toBe('100.00');
    expect(line('', '50', '', '0')).toBeNull();
    expect(line('2', 'abc', '', '0')).toBeNull();
    expect(line('2', '50', '1e2', '0')).toBeNull();
    expect(line('2', '50', '', '-5')).toBeNull();
  });

  it('adds the lines into the totals without rounding them again', () => {
    const lines = [
      line('3', '333.3333', '0.50', '5'),
      line('1', '0.335', '', '18'),
      line('2', '100', '10', '0'),
    ].filter((amounts): amounts is LineAmounts => amounts !== null);
    const totals = billTotals(lines);

    expect(rupees(totals.subTotal)).toBe('1200.34');
    expect(rupees(totals.discountTotal)).toBe('10.50');
    expect(rupees(totals.taxTotal)).toBe('50.04');
    expect(rupees(totals.total)).toBe('1239.88');
    expect(rupees(billTotals([]).total)).toBe('0.00');
  });

  it('writes paise as rupees with two places', () => {
    expect(rupees(0n)).toBe('0.00');
    expect(rupees(5n)).toBe('0.05');
    expect(rupees(104948n)).toBe('1049.48');
    expect(rupees(-150n)).toBe('-1.50');
  });
});
