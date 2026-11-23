import { Link } from 'react-router';
import { documentLink, requestLabels } from '../approvals/api';
import { billStatusLabels } from '../bills/api';
import { formatDecimal } from '../format';
import { methodLabels, paymentStatusLabels } from '../payments/api';
import type {
  AvailableCreditNote,
  AvailableCreditTotals,
  BillRegisterRow,
  BillRegisterTotals,
  CollectionByMethod,
  DailyCollection,
  DriftRow,
  OutstandingTotals,
  PayerOutstanding,
  PaymentRegisterRow,
  PaymentRegisterTotals,
  PendingApprovalRow,
  PendingApprovalsTotals,
  ReconciliationTotals,
  UnappliedCashTotals,
  UnappliedPayment,
} from './api';
import { comesTo } from './exportReport';
import type { ReportColumn, ReportTotal } from './view';

/**
 * The columns and totals of each report, one definition for the screen and the CSV (ADR 0043 point 1), and the check of an
 * export: its rows are as many as the report counts and come to its totals, to the paisa.
 */

// BillRegister

export const billRegisterColumns: readonly ReportColumn<BillRegisterRow>[] = [
  {
    header: 'Number',
    kind: 'text',
    value: (row) => row.number,
    show: (row) => <Link to={`/bills/${row.billId}`}>{row.number}</Link>,
  },
  { header: 'Bill date', kind: 'date', value: (row) => row.billDate },
  { header: 'Due date', kind: 'date', value: (row) => row.dueDate },
  { header: 'Patient', kind: 'text', value: (row) => row.patientName },
  { header: 'Payer', kind: 'text', value: (row) => row.payerName },
  { header: 'Status', kind: 'text', value: (row) => billStatusLabels[row.status] },
  { header: 'Total (₹)', kind: 'amount', value: (row) => row.total },
  { header: 'Balance due (₹)', kind: 'amount', value: (row) => row.balanceDue },
];

export function billRegisterTotals(totals: BillRegisterTotals): ReportTotal[] {
  return [
    { label: 'Bills', kind: 'count', value: totals.bills },
    { label: 'Total (₹)', kind: 'amount', value: totals.total, under: 'Total (₹)' },
    { label: 'Balance due (₹)', kind: 'amount', value: totals.balanceDue, under: 'Balance due (₹)' },
  ];
}

export const billRegisterIsWhole = (rows: readonly BillRegisterRow[], totals: BillRegisterTotals) =>
  rows.length === Number(totals.bills) &&
  comesTo(rows, (row) => row.total, totals.total) &&
  comesTo(rows, (row) => row.balanceDue, totals.balanceDue);

// PaymentRegister

export const paymentRegisterColumns: readonly ReportColumn<PaymentRegisterRow>[] = [
  {
    header: 'Number',
    kind: 'text',
    value: (row) => row.number,
    show: (row) => <Link to={`/payments/${row.paymentId}`}>{row.number}</Link>,
  },
  { header: 'Payment date', kind: 'date', value: (row) => row.paymentDate },
  { header: 'Payer', kind: 'text', value: (row) => row.payerName },
  { header: 'Method', kind: 'text', value: (row) => methodLabels[row.method] },
  { header: 'Reference', kind: 'text', value: (row) => row.reference },
  { header: 'Status', kind: 'text', value: (row) => paymentStatusLabels[row.status].label },
  { header: 'Amount (₹)', kind: 'amount', value: (row) => row.amount },
  { header: 'Unapplied (₹)', kind: 'amount', value: (row) => row.unappliedAmount },
];

export function paymentRegisterTotals(totals: PaymentRegisterTotals): ReportTotal[] {
  return [
    { label: 'Payments', kind: 'count', value: totals.payments },
    { label: 'Amount (₹)', kind: 'amount', value: totals.amount, under: 'Amount (₹)' },
    { label: 'Unapplied (₹)', kind: 'amount', value: totals.unappliedAmount, under: 'Unapplied (₹)' },
  ];
}

export const paymentRegisterIsWhole = (rows: readonly PaymentRegisterRow[], totals: PaymentRegisterTotals) =>
  rows.length === Number(totals.payments) &&
  comesTo(rows, (row) => row.amount, totals.amount) &&
  comesTo(rows, (row) => row.unappliedAmount, totals.unappliedAmount);

// DailyCollection

export const collectionColumns: readonly ReportColumn<CollectionByMethod>[] = [
  { header: 'Method', kind: 'text', value: (row) => methodLabels[row.method] },
  { header: 'Payments', kind: 'count', value: (row) => row.payments },
  { header: 'Received (₹)', kind: 'amount', value: (row) => row.received },
  { header: 'Reversed (₹)', kind: 'amount', value: (row) => row.reversed },
  { header: 'Refunded (₹)', kind: 'amount', value: (row) => row.refunded },
  { header: 'Net (₹)', kind: 'amount', value: (row) => row.net },
];

export function collectionTotals(collection: DailyCollection): ReportTotal[] {
  const { totals } = collection;
  return [
    { label: 'Payments', kind: 'count', value: totals.payments, under: 'Payments' },
    { label: 'Received (₹)', kind: 'amount', value: totals.received, under: 'Received (₹)' },
    { label: 'Reversed (₹)', kind: 'amount', value: totals.reversed, under: 'Reversed (₹)' },
    { label: 'Refunded (₹)', kind: 'amount', value: totals.refunded, under: 'Refunded (₹)' },
    { label: 'Net (₹)', kind: 'amount', value: totals.net, under: 'Net (₹)' },
  ];
}

/** The methods come to the day's totals, to the paisa: the check of an export of one read. */
export const collectionIsWhole = (collection: DailyCollection) =>
  comesTo(collection.methods, (row) => row.received, collection.totals.received) &&
  comesTo(collection.methods, (row) => row.reversed, collection.totals.reversed) &&
  comesTo(collection.methods, (row) => row.refunded, collection.totals.refunded) &&
  comesTo(collection.methods, (row) => row.net, collection.totals.net);

// Outstanding

export const outstandingColumns: readonly ReportColumn<PayerOutstanding>[] = [
  {
    header: 'Payer',
    kind: 'text',
    value: (row) => row.payerName,
    show: (row) => <Link to={`/payers/${row.payerId}/statement`}>{row.payerName}</Link>,
  },
  { header: 'Owed (₹)', kind: 'amount', value: (row) => row.receivable },
  { header: 'Unapplied cash (₹)', kind: 'amount', value: (row) => row.unappliedCash },
  { header: 'Available credit (₹)', kind: 'amount', value: (row) => row.creditAvailable },
  { header: 'Net outstanding (₹)', kind: 'amount', value: (row) => row.netOutstanding },
];

export function outstandingTotals(totals: OutstandingTotals): ReportTotal[] {
  return [
    { label: 'Payers', kind: 'count', value: totals.payers },
    { label: 'Owed (₹)', kind: 'amount', value: totals.receivable, under: 'Owed (₹)' },
    { label: 'Unapplied cash (₹)', kind: 'amount', value: totals.unappliedCash, under: 'Unapplied cash (₹)' },
    { label: 'Available credit (₹)', kind: 'amount', value: totals.creditAvailable, under: 'Available credit (₹)' },
    { label: 'Net outstanding (₹)', kind: 'amount', value: totals.netOutstanding, under: 'Net outstanding (₹)' },
  ];
}

export const outstandingIsWhole = (rows: readonly PayerOutstanding[], totals: OutstandingTotals) =>
  rows.length === Number(totals.payers) &&
  comesTo(rows, (row) => row.receivable, totals.receivable) &&
  comesTo(rows, (row) => row.unappliedCash, totals.unappliedCash) &&
  comesTo(rows, (row) => row.creditAvailable, totals.creditAvailable) &&
  comesTo(rows, (row) => row.netOutstanding, totals.netOutstanding);

// UnappliedCash

export const unappliedColumns: readonly ReportColumn<UnappliedPayment>[] = [
  {
    header: 'Payment',
    kind: 'text',
    value: (row) => row.number,
    show: (row) => <Link to={`/payments/${row.paymentId}`}>{row.number}</Link>,
  },
  { header: 'Payment date', kind: 'date', value: (row) => row.paymentDate },
  { header: 'Payer', kind: 'text', value: (row) => row.payerName },
  { header: 'Method', kind: 'text', value: (row) => methodLabels[row.method] },
  { header: 'Status', kind: 'text', value: (row) => paymentStatusLabels[row.status].label },
  { header: 'Amount (₹)', kind: 'amount', value: (row) => row.amount },
  { header: 'Unapplied (₹)', kind: 'amount', value: (row) => row.unappliedAmount },
];

export function unappliedTotals(totals: UnappliedCashTotals): ReportTotal[] {
  return [
    { label: 'Payments', kind: 'count', value: totals.payments },
    { label: 'Unapplied (₹)', kind: 'amount', value: totals.unappliedAmount, under: 'Unapplied (₹)' },
  ];
}

export const unappliedIsWhole = (rows: readonly UnappliedPayment[], totals: UnappliedCashTotals) =>
  rows.length === Number(totals.payments) && comesTo(rows, (row) => row.unappliedAmount, totals.unappliedAmount);

// AvailableCredit

export const availableCreditColumns: readonly ReportColumn<AvailableCreditNote>[] = [
  {
    header: 'Credit note',
    kind: 'text',
    value: (row) => row.number,
    show: (row) => <Link to={`/credit-notes/${row.creditNoteId}`}>{row.number}</Link>,
  },
  { header: 'Date', kind: 'date', value: (row) => row.creditNoteDate },
  {
    header: 'Bill',
    kind: 'text',
    value: (row) => row.billNumber,
    show: (row) => <Link to={`/bills/${row.billId}`}>{row.billNumber}</Link>,
  },
  { header: 'Payer', kind: 'text', value: (row) => row.payerName },
  { header: 'Total (₹)', kind: 'amount', value: (row) => row.total },
  { header: 'Available (₹)', kind: 'amount', value: (row) => row.availableAmount },
];

export function availableCreditTotals(totals: AvailableCreditTotals): ReportTotal[] {
  return [
    { label: 'Credit notes', kind: 'count', value: totals.creditNotes },
    { label: 'Available (₹)', kind: 'amount', value: totals.availableAmount, under: 'Available (₹)' },
  ];
}

export const availableCreditIsWhole = (rows: readonly AvailableCreditNote[], totals: AvailableCreditTotals) =>
  rows.length === Number(totals.creditNotes) && comesTo(rows, (row) => row.availableAmount, totals.availableAmount);

// PendingApprovalsReport

export const pendingColumns: readonly ReportColumn<PendingApprovalRow>[] = [
  { header: 'Asked', kind: 'instant', value: (row) => row.requestedAtUtc },
  {
    header: 'Request',
    kind: 'text',
    value: (row) => requestLabels[row.documentType],
    show: (row) => {
      const link = documentLink(row);
      return (
        <>
          {requestLabels[row.documentType]}
          {link !== null && (
            <>
              {' · '}
              <Link to={link.to}>{link.label}</Link>
            </>
          )}
        </>
      );
    },
  },
  { header: 'Payer', kind: 'text', value: (row) => row.payerName },
  { header: 'Amount (₹)', kind: 'amount', value: (row) => row.amount },
  {
    header: 'By',
    kind: 'text',
    value: (row) => row.makerUserId,
    show: (row) => <span title={row.makerUserId}>{row.makerUserId.slice(0, 8)}</span>,
  },
  { header: 'Reason given', kind: 'text', value: (row) => row.makerComment },
  { header: 'Days waiting', kind: 'count', value: (row) => row.waitingDays },
];

export function pendingTotals(totals: PendingApprovalsTotals): ReportTotal[] {
  return [
    { label: 'Requests', kind: 'count', value: totals.requests },
    { label: 'Oldest request', kind: 'instant', value: totals.oldestRequestedAtUtc },
  ];
}

export const pendingIsWhole = (rows: readonly PendingApprovalRow[], totals: PendingApprovalsTotals) =>
  rows.length === Number(totals.requests);

// Reconciliation

/** A series' figures are counts of documents and numbers, not money. */
const figure = (row: DriftRow, value: number | string) =>
  row.identity === 'NumberSeriesGap' ? String(value) : formatDecimal(value);

export const driftColumns: readonly ReportColumn<DriftRow>[] = [
  { header: 'Identity', kind: 'text', value: (row) => row.identity },
  { header: 'Reference', kind: 'text', value: (row) => row.reference },
  { header: 'Subject', kind: 'text', value: (row) => row.subjectId },
  { header: 'Cached', kind: 'amount', value: (row) => row.cached, show: (row) => figure(row, row.cached) },
  { header: 'Ledger', kind: 'amount', value: (row) => row.ledger, show: (row) => figure(row, row.ledger) },
];

export function driftTotals(totals: ReconciliationTotals): ReportTotal[] {
  return [
    { label: 'Drift rows', kind: 'count', value: totals.rows },
    ...totals.byIdentity.map((identity) => ({
      label: `Rows of ${identity.identity}`,
      kind: 'count' as const,
      value: identity.rows,
    })),
    { label: 'Checked at', kind: 'instant', value: totals.checkedAtUtc },
  ];
}

export const driftIsWhole = (rows: readonly DriftRow[], totals: ReconciliationTotals) =>
  rows.length === Number(totals.rows);

/** Each reading checks again, so only what it found is compared, never the moment it found it. */
export const sameFindings = (first: ReconciliationTotals, last: ReconciliationTotals) =>
  Number(first.rows) === Number(last.rows) && JSON.stringify(first.byIdentity) === JSON.stringify(last.byIdentity);
