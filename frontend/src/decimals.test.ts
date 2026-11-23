import { describe, expect, it } from 'vitest';
import { checkDecimal, significantDigits, toNumber } from './decimals';
import { formatDate, formatDecimal, formatInstant, formatPercent } from './format';

describe('decimals typed by a user (ADR 0039 point 5)', () => {
  it.each([
    ['0', 0],
    ['0.5', 1],
    ['1500', 4],
    ['1500.10', 5],
    ['007.25', 3],
    ['123456789012345', 15],
    ['1234567890123.4567', 17],
  ])('%s has %i significant digits', (text, digits) => {
    expect(significantDigits(text)).toBe(digits);
  });

  it.each([
    ['0', true],
    ['450', true],
    ['1500.1234', true],
    [' 12.5 ', true],
    ['12345678901.2345', true],
    ['', 'Enter the price.'],
    ['12.34567', 'Write the price as a number of 0 or more, with at most 4 decimals.'],
    ['-1', 'Write the price as a number of 0 or more, with at most 4 decimals.'],
    ['1e3', 'Write the price as a number of 0 or more, with at most 4 decimals.'],
    ['.5', 'Write the price as a number of 0 or more, with at most 4 decimals.'],
    ['123456789012.3456', 'The price has more than 15 digits.'],
  ])('%j is %j', (text, expected) => {
    expect(checkDecimal(text, 4, 'price')).toBe(expected);
  });

  it('sends what was typed: every accepted value survives the trip through a JSON number', () => {
    for (const text of ['0.0001', '1500.1234', '12345678901.2345', '99999999999.9999']) {
      expect(JSON.stringify(toNumber(text))).toBe(String(Number(text)));
      expect(Number(JSON.stringify(toNumber(text)))).toBe(Number(text));
    }
  });
});

describe('how values are shown', () => {
  it('writes amounts with Indian grouping', () => {
    expect(formatDecimal(99999)).toBe('99,999.00');
    expect(formatDecimal(100000)).toBe('1,00,000.00');
    expect(formatDecimal(1280000)).toBe('12,80,000.00');
    expect(formatDecimal(10000000)).toBe('1,00,00,000.00');
    expect(formatDecimal('1500.1234', 4)).toBe('1,500.1234');
    expect(formatDecimal(450, 4)).toBe('450.00');
  });

  it('writes rates, business dates and instants as in India', () => {
    expect(formatPercent(18)).toBe('18%');
    expect(formatPercent('12.5')).toBe('12.5%');
    expect(formatDate('2026-10-05')).toBe('05 Oct 2026');
    expect(formatDate(null)).toBe('');
    // 18:45 UTC is 00:15 the next day in India.
    expect(formatInstant('2026-10-05T18:45:00+00:00')).toMatch(/^06 Oct 2026, 12:15\s?am$/i);
  });
});
