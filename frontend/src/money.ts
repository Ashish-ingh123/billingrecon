import { rupees, type Paise } from './bills/lineMath';

/**
 * Amounts of money compared in the browser (ADR 0041 point 6): exact paise, never binary floats. A typed amount and a
 * figure of the server are read the same way, so "not more than the balance due" means exactly that.
 */

const amountShape = /^\d+(\.\d{1,2})?$/;

/** A typed amount of rupees with at most two decimals, in paise; null when it is not one. */
export function paiseOf(text: string): Paise | null {
  const value = text.trim();
  if (!amountShape.test(value)) {
    return null;
  }

  const [whole = '0', fraction = ''] = value.split('.');
  return BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
}

/** An amount the server answered (a JSON number, or a string the document allows), in paise. */
export function paiseOfServer(value: number | string): Paise {
  const paise = paiseOf(typeof value === 'number' ? value.toFixed(2) : value);
  if (paise === null) {
    throw new Error(`The server answered an amount that is not one: ${String(value)}.`);
  }

  return paise;
}

/** Paise as rupees with two places, for a field or a message: 104948n as "1049.48". */
export function asRupees(paise: Paise): string {
  return rupees(paise);
}

/**
 * The message for a typed amount above zero, at most two decimals and at most `cap` paise, or true when it is one.
 * `capText` says what the cap is ("the unapplied cash of ₹500.00").
 */
export function checkAmount(text: string, cap: Paise | null, capText: string): true | string {
  if (text.trim() === '') {
    return 'Enter the amount.';
  }

  const paise = paiseOf(text);
  if (paise === null) {
    return 'Write the amount as rupees with at most 2 decimals.';
  }

  if (paise <= 0n) {
    return 'The amount is more than 0.';
  }

  return cap === null || paise <= cap || `The amount is not more than ${capText}.`;
}
