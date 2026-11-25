import { describe, expect, it } from 'vitest';
import { paiseOf } from '../money';
import { billDocumentKind, halfRate, splitTax } from './tax';
import { amountInWords, wholeInWords } from './words';

const words = (amount: string) => amountInWords(paiseOf(amount) ?? -1n);

describe('an amount in words, in lakh and crore (ADR 0054)', () => {
  it.each([
    ['0.50', 'Fifty paise only'],
    ['100000.01', 'One lakh rupees and one paisa only'],
    [
      '123456789.99',
      'Twelve crore thirty-four lakh fifty-six thousand seven hundred eighty-nine rupees and ninety-nine paise only',
    ],
    ['0.00', 'Zero rupees only'],
    ['0.01', 'One paisa only'],
    ['1.00', 'One rupee only'],
    ['1.01', 'One rupee and one paisa only'],
    ['19.00', 'Nineteen rupees only'],
    ['20.00', 'Twenty rupees only'],
    ['21.10', 'Twenty-one rupees and ten paise only'],
    ['100.00', 'One hundred rupees only'],
    ['101.00', 'One hundred one rupees only'],
    ['999.99', 'Nine hundred ninety-nine rupees and ninety-nine paise only'],
    ['1000.00', 'One thousand rupees only'],
    ['2249.48', 'Two thousand two hundred forty-nine rupees and forty-eight paise only'],
    ['99999.00', 'Ninety-nine thousand nine hundred ninety-nine rupees only'],
    ['100000.00', 'One lakh rupees only'],
    ['1050000.00', 'Ten lakh fifty thousand rupees only'],
    ['10000000.00', 'One crore rupees only'],
    ['10000001.00', 'One crore one rupees only'],
    [
      '9999999999.99',
      'Nine hundred ninety-nine crore ninety-nine lakh ninety-nine thousand nine hundred ninety-nine rupees and ninety-nine paise only',
    ],
  ])('%s is "%s"', (amount, expected) => {
    expect(words(amount)).toBe(expected);
  });

  it('counts the crores themselves the same way, above a thousand crore', () => {
    expect(wholeInWords(12_345n * 10_000_000n)).toBe('twelve thousand three hundred forty-five crore');
    expect(wholeInWords(100_000n * 10_000_000n)).toBe('one lakh crore');
  });

  it('refuses an amount below zero', () => {
    expect(() => wholeInWords(-1n)).toThrow();
  });
});

describe('the tax of a line in its two halves (ADR 0054)', () => {
  it.each([
    [0n, 0n, 0n],
    [1n, 1n, 0n],
    [2n, 1n, 1n],
    [4998n, 2499n, 2499n],
    [4999n, 2500n, 2499n],
    [1_000_001n, 500_001n, 500_000n],
  ])('%s paise is %s and %s', (tax, cgst, sgst) => {
    expect(splitTax(tax)).toEqual({ cgst, sgst });
    expect(splitTax(tax).cgst + splitTax(tax).sgst).toBe(tax);
  });

  it('halves the rate without trailing zeros', () => {
    expect([halfRate(18), halfRate(5), halfRate('12.5'), halfRate(0)]).toEqual(['9', '2.5', '6.25', '0']);
  });

  it('names the document by what the bill carries', () => {
    expect(billDocumentKind({ origin: 'Standard', taxTotal: 49.98 })).toBe('TaxInvoice');
    expect(billDocumentKind({ origin: 'Standard', taxTotal: 0 })).toBe('BillOfSupply');
    expect(billDocumentKind({ origin: 'Opening', taxTotal: 0 })).toBe('OpeningBalance');
  });
});
