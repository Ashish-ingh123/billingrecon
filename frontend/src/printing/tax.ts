import type { Paise } from '../bills/lineMath';
import { paiseOfServer } from '../money';

// What a printed bill derives from the stored figures and never stores (Prompt 10.4b, ADR 0054 point 4). No figure is
// computed differently from the server's: the tax of a line is the stored one, only shown in its two halves.

/**
 * The stored tax of a line in its central and state halves. CGST is half of it, rounded half away from zero as every
 * amount is (BR-001), and SGST is the rest, so the two always sum to the stored tax to the paisa.
 */
export function splitTax(tax: Paise): { cgst: Paise; sgst: Paise } {
  const cgst = (tax + 1n) / 2n;
  return { cgst, sgst: tax - cgst };
}

/** The rate of each half, as text: half the line's rate, without trailing zeros (18 gives 9, 5 gives 2.5). */
export function halfRate(ratePercent: number | string): string {
  const half = Number(ratePercent) / 2;
  return String(Number(half.toFixed(3)));
}

export type BillDocumentKind = 'TaxInvoice' | 'BillOfSupply' | 'OpeningBalance';

/**
 * What the printed bill is: the statement of an opening balance for a bill brought in from the system before, which was
 * not issued here; a tax invoice when the bill carries tax; a bill of supply when it carries none.
 */
export function billDocumentKind(bill: {
  origin: 'Standard' | 'Opening';
  taxTotal: number | string;
}): BillDocumentKind {
  if (bill.origin === 'Opening') {
    return 'OpeningBalance';
  }

  return paiseOfServer(bill.taxTotal) > 0n ? 'TaxInvoice' : 'BillOfSupply';
}

export const billDocumentTitles: Record<BillDocumentKind, string> = {
  TaxInvoice: 'Tax Invoice',
  BillOfSupply: 'Bill of Supply',
  OpeningBalance: 'Statement of Opening Balance',
};

/** The line a bill of supply carries in place of tax. */
export const exemptWording =
  'Health care services by a clinical establishment: exempt from GST. No tax is charged on this bill.';
