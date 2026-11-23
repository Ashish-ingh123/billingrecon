/**
 * The arithmetic of a bill line in the browser, exactly as the server's `LineCalculator` does it (BR-202, BR-203,
 * ADR 0040 point 3), for the editor's live totals:
 *
 *   lineAmount = round(quantity x unitPrice)
 *   taxAmount  = round((lineAmount - discountAmount) x taxRatePercent / 100)
 *   lineTotal  = lineAmount - discountAmount + taxAmount
 *
 * each product rounded once to the paisa, half away from zero; sums of lines are never rounded again. Decimals are
 * scaled integers (`BigInt`), never binary floats, so a midpoint rounds as the server rounds it. The screen shows these
 * figures as an estimate until the draft is saved; after a save it shows the server's.
 */

/** An amount in paise: rupees times 100, exactly. */
export type Paise = bigint;

interface Decimal {
  units: bigint;
  scale: number;
}

const decimalShape = /^\d+(\.\d+)?$/;

function parse(text: string): Decimal | null {
  const value = text.trim();
  if (!decimalShape.test(value)) {
    return null;
  }

  const [whole = '0', fraction = ''] = value.split('.');
  return { units: BigInt(`${whole}${fraction}`), scale: fraction.length };
}

const ten = (power: number): bigint => 10n ** BigInt(power);

/** A decimal rounded to two places, half away from zero, as paise. */
function toPaise({ units, scale }: Decimal): Paise {
  if (scale <= 2) {
    return units * ten(2 - scale);
  }

  const divisor = ten(scale - 2);
  const negative = units < 0n;
  const magnitude = negative ? -units : units;
  let quotient = magnitude / divisor;
  if ((magnitude % divisor) * 2n >= divisor) {
    quotient += 1n;
  }

  return negative ? -quotient : quotient;
}

function times(a: Decimal, b: Decimal): Decimal {
  return { units: a.units * b.units, scale: a.scale + b.scale };
}

/** The four numbers of a line as a user typed them; a blank discount is none. */
export interface LineInput {
  quantity: string;
  unitPrice: string;
  discountAmount: string;
  taxRatePercent: string;
}

export interface LineAmounts {
  lineAmount: Paise;
  discountAmount: Paise;
  taxAmount: Paise;
  lineTotal: Paise;
}

/** The amounts of a line, or null while one of its numbers is not a number yet. */
export function lineAmounts(input: LineInput): LineAmounts | null {
  const quantity = parse(input.quantity);
  const unitPrice = parse(input.unitPrice);
  const discount = input.discountAmount.trim() === '' ? { units: 0n, scale: 0 } : parse(input.discountAmount);
  const taxRate = parse(input.taxRatePercent);
  if (quantity === null || unitPrice === null || discount === null || taxRate === null) {
    return null;
  }

  const lineAmount = toPaise(times(quantity, unitPrice));
  const discountAmount = toPaise(discount);
  const taxable = lineAmount - discountAmount;
  // (paise / 100) x (rate / 100): the taxable amount in rupees times the rate as a fraction, rounded to the paisa.
  const taxAmount = toPaise({ units: taxable * taxRate.units, scale: 2 + taxRate.scale + 2 });

  return { lineAmount, discountAmount, taxAmount, lineTotal: taxable + taxAmount };
}

export interface BillTotals {
  subTotal: Paise;
  discountTotal: Paise;
  taxTotal: Paise;
  total: Paise;
}

/** The totals of a bill: the sums of its lines, never rounded again. */
export function billTotals(lines: readonly LineAmounts[]): BillTotals {
  const sum = (pick: (line: LineAmounts) => Paise) => lines.reduce((total, line) => total + pick(line), 0n);
  const subTotal = sum((line) => line.lineAmount);
  const discountTotal = sum((line) => line.discountAmount);
  const taxTotal = sum((line) => line.taxAmount);

  return { subTotal, discountTotal, taxTotal, total: subTotal - discountTotal + taxTotal };
}

/** Paise as a decimal with two places: 104948n as "1049.48". */
export function rupees(paise: Paise): string {
  const negative = paise < 0n;
  const magnitude = negative ? -paise : paise;
  const whole = magnitude / 100n;
  const fraction = (magnitude % 100n).toString().padStart(2, '0');
  return `${negative ? '-' : ''}${whole}.${fraction}`;
}
