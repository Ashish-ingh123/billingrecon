import type { components } from '../api/schema';

/** The accountant's export and the names of its ledgers (`docs/api.md` section 8.21, ADR 0062). */
export type AccountingExportSummary = components['schemas']['AccountingExportSummaryDto'];
export type ExportLedgerTotal = components['schemas']['ExportLedgerTotalDto'];
export type ExportLedger = components['schemas']['ExportLedger'];
export type AccountingLedgers = components['schemas']['AccountingLedgersDto'];

/** What each ledger of the export is, in the words of the screens. */
export const exportLedgerLabels: Record<ExportLedger, string> = {
  Receivable: 'What payers owe (receivables)',
  UnappliedCash: 'Advances held (unapplied cash)',
  CreditAvailable: 'Credit notes not yet applied',
  Revenue: 'Income (taxable value)',
  TaxOutput: 'Tax billed (GST output)',
  Adjustments: 'Discounts and write-offs',
  OpeningBalances: 'Opening balances of the cut-over',
  Cash: 'Money received in cash',
  Card: 'Money received by card',
  Upi: 'Money received by UPI',
  BankTransfer: 'Money received by bank transfer',
  Cheque: 'Money received by cheque',
};

/** The query of the hospital's names for its accountant's ledgers. */
export const accountingLedgersKey = ['accounting-ledgers'] as const;
