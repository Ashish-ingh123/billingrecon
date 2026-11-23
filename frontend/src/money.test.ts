import { describe, expect, it } from 'vitest';
import { asRupees, checkAmount, paiseOf, paiseOfServer } from './money';

describe('amounts compared in the browser, as exact paise (ADR 0041 point 6)', () => {
  it.each([
    ['0', 0n],
    ['1', 100n],
    ['1.5', 150n],
    ['1049.48', 104948n],
    [' 0.05 ', 5n],
    ['9999999999.99', 999999999999n],
  ])('reads %j as %s paise', (text, paise) => {
    expect(paiseOf(text)).toBe(paise);
  });

  it.each(['', '1.234', '-1', '1,000', 'abc', '.5'])('is not an amount: %j', (text) => {
    expect(paiseOf(text)).toBeNull();
  });

  it("reads the server's figures the same way, whatever a binary float makes of them", () => {
    expect(paiseOfServer(1049.48)).toBe(104948n);
    expect(paiseOfServer(0.1 + 0.2)).toBe(30n);
    expect(paiseOfServer('2511.07')).toBe(251107n);
    expect(asRupees(paiseOfServer(450.52))).toBe('450.52');
  });

  it('checks an amount against its cap to the paisa', () => {
    expect(checkAmount('600', 60000n, 'the cap')).toBe(true);
    expect(checkAmount('600.01', 60000n, 'the cap')).toBe('The amount is not more than the cap.');
    expect(checkAmount('0', 60000n, 'the cap')).toBe('The amount is more than 0.');
    expect(checkAmount('', null, 'the cap')).toBe('Enter the amount.');
    expect(checkAmount('1.001', null, 'the cap')).toBe('Write the amount as rupees with at most 2 decimals.');
  });
});
