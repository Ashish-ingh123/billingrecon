/**
 * Decimals a user types (ADR 0039 point 5): checked as text, sent as a JSON number. A JSON number keeps at most 15
 * significant digits exactly in a browser, so a value with more is refused rather than sent changed.
 */

export const mostSignificantDigits = 15;

/** The significant digits of a decimal written as text: its digits without the leading and trailing zeros that mean nothing. */
export function significantDigits(text: string): number {
  const [whole = '', fraction = ''] = text.split('.');
  const digits = `${whole}${fraction}`.replace(/^0+/, '');
  const trimmed = fraction === '' ? digits : digits.replace(/0+$/, '');
  return trimmed.length;
}

/**
 * The message for a decimal that is not `0` or more with at most `decimals` decimal places and `mostSignificantDigits`
 * digits in all, or true when it is one.
 */
export function checkDecimal(text: string, decimals: number, what: string): true | string {
  const value = text.trim();
  if (value === '') {
    return `Enter the ${what}.`;
  }

  const shape = new RegExp(`^\\d+(\\.\\d{1,${decimals}})?$`);
  if (!shape.test(value)) {
    return `Write the ${what} as a number of 0 or more, with at most ${decimals} decimals.`;
  }

  if (significantDigits(value) > mostSignificantDigits) {
    return `The ${what} has more than ${mostSignificantDigits} digits.`;
  }

  return true;
}

/** The number a checked decimal means. */
export function toNumber(text: string): number {
  return Number(text.trim());
}
