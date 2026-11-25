import type { Paise } from '../bills/lineMath';

// An amount in words, in the Indian system of lakh and crore (Prompt 10.4b, ADR 0054): what a printed document says
// beside its total, so that a figure cannot be altered by a stroke. Exact: the amount is a whole number of paise.

const ones = [
  'zero',
  'one',
  'two',
  'three',
  'four',
  'five',
  'six',
  'seven',
  'eight',
  'nine',
  'ten',
  'eleven',
  'twelve',
  'thirteen',
  'fourteen',
  'fifteen',
  'sixteen',
  'seventeen',
  'eighteen',
  'nineteen',
] as const;
const tens = ['', '', 'twenty', 'thirty', 'forty', 'fifty', 'sixty', 'seventy', 'eighty', 'ninety'] as const;

/** A whole number below 100. */
function belowHundred(value: number): string {
  if (value < 20) {
    return ones[value] ?? '';
  }

  const unit = value % 10;
  return `${tens[Math.floor(value / 10)] ?? ''}${unit === 0 ? '' : `-${ones[unit] ?? ''}`}`;
}

/** A whole number below 1,000. */
function belowThousand(value: number): string {
  const hundreds = Math.floor(value / 100);
  const rest = value % 100;
  const parts = [hundreds === 0 ? '' : `${ones[hundreds] ?? ''} hundred`, rest === 0 ? '' : belowHundred(rest)];
  return parts.filter((part) => part !== '').join(' ');
}

/**
 * A whole number in words as India counts: thousand, lakh (a hundred thousand), crore (ten million), and above a crore
 * the crores themselves counted the same way (a thousand crore, a lakh crore).
 */
export function wholeInWords(value: bigint): string {
  if (value < 0n) {
    throw new Error('An amount in words is not below zero.');
  }

  if (value === 0n) {
    return 'zero';
  }

  const crore = value / 10_000_000n;
  const belowCrore = Number(value % 10_000_000n);
  const lakh = Math.floor(belowCrore / 100_000);
  const thousand = Math.floor((belowCrore % 100_000) / 1000);
  const rest = belowCrore % 1000;

  const parts = [
    crore === 0n ? '' : `${wholeInWords(crore)} crore`,
    lakh === 0 ? '' : `${belowHundred(lakh)} lakh`,
    thousand === 0 ? '' : `${belowHundred(thousand)} thousand`,
    rest === 0 ? '' : belowThousand(rest),
  ];
  return parts.filter((part) => part !== '').join(' ');
}

/**
 * An amount of money in words: "Twelve thousand five hundred rupees and fifty paise only". One rupee and one paisa are
 * singular; an amount below a rupee names its paise alone; nothing at all is "Zero rupees only".
 */
export function amountInWords(paise: Paise): string {
  const rupees = paise / 100n;
  const fraction = paise % 100n;
  const rupeePart = rupees === 0n ? '' : `${wholeInWords(rupees)} ${rupees === 1n ? 'rupee' : 'rupees'}`;
  const paisePart = fraction === 0n ? '' : `${wholeInWords(fraction)} ${fraction === 1n ? 'paisa' : 'paise'}`;
  const words = [rupeePart, paisePart].filter((part) => part !== '').join(' and ') || 'zero rupees';

  return `${words.charAt(0).toUpperCase()}${words.slice(1)} only`;
}
