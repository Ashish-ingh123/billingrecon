import { signedPaise } from '../reports/csv';

/**
 * Money as a card shows it (ADR 0044 point 3): below a lakh the rupees with Indian grouping (₹99,999); from a lakh, lakhs
 * with one decimal (₹12.8L); from a crore, crores (₹1.3Cr). Exact arithmetic on paise, half away from zero. The card
 * keeps the exact amount for its tooltip and for screen readers; this is the only figure the client makes.
 */
const rupee = 100n;
const lakh = 100_000n * rupee;
const crore = 100n * lakh;

/** `value` divided by `unit`, to `decimals` places, half away from zero, as text without a trailing ".0". */
function scaled(paise: bigint, unit: bigint, decimals: number): string {
  const step = unit / 10n ** BigInt(decimals);
  const units = (paise + step / 2n) / step;
  const whole = units / 10n ** BigInt(decimals);
  const fraction = units % 10n ** BigInt(decimals);
  return decimals === 0 || fraction === 0n
    ? String(whole)
    : `${String(whole)}.${String(fraction).padStart(decimals, '0')}`;
}

const grouping = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 });

export function compactRupees(value: number | string): string {
  const signed = signedPaise(value);
  const sign = signed < 0n ? '-' : '';
  const paise = signed < 0n ? -signed : signed;
  const rupees = (paise + rupee / 2n) / rupee;

  if (rupees < 100_000n) {
    return `${sign}₹${grouping.format(rupees)}`;
  }

  const lakhs = scaled(paise, lakh, 1);
  if (Number(lakhs) < 100) {
    return `${sign}₹${lakhs}L`;
  }

  return `${sign}₹${scaled(paise, crore, 1)}Cr`;
}

/** Which way a change of a figure is good: a rise in billing, a fall in what is owed or waits; or neither. */
export type GoodWhen = 'rise' | 'fall' | 'neither';

/** How a change reads: good or bad by the meaning of the figure, not by its direction; no change is neutral. */
export function changeTone(change: number, goodWhen: GoodWhen): 'good' | 'bad' | 'neutral' {
  if (change === 0 || goodWhen === 'neither') {
    return 'neutral';
  }

  return change > 0 === (goodWhen === 'rise') ? 'good' : 'bad';
}
