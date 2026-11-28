import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import {
  adjustment,
  creditNote,
  draft,
  letterhead,
  printSettings,
  open,
  pageOf,
  patient,
  payer,
  posted,
  screenshots,
  sessionOf,
  trail,
  userId,
  type Answers,
} from './stubbed';

// The finishing pass of Phase 9 in a real browser (Prompt 9.4b, ADR 0050 amended): every route of the shell, with the
// API's answers given by the test, under nginx's content security policy. For each: both themes at 1440 and 390 px with
// nothing refused, no console error, no request the test did not expect, no layout shift; no sideways scroll from 360 to
// 1,920 px and at 200% zoom (a 720 px window); and no serious or critical finding of axe's WCAG 2.2 A and AA rules. Every
// route's screenshot in both themes at both widths is kept as an artifact of the run (test-results), not in git. It needs
// the built app (`npm run build`, `npm run preview`, E2E_BASE_URL=http://localhost:4173).

const openingBatch = {
  id: 'fd000000-0000-4000-8000-000000000001',
  cutOverDate: '2027-04-01',
  status: 'PendingApproval',
  receivables: 300,
  receivableTotal: 6584094.91,
  advances: 55,
  advanceTotal: 540270.61,
  submittedAtUtc: '2027-04-02T05:00:00+00:00',
  submittedByUserId: 'd0000000-0000-4000-8000-000000000009',
  decidedAtUtc: null,
  version: 'AAAAAAAADgA=',
};

// Since Prompt 13.1c (ADR 0065): a bank channel, one import of its statement and one of its lines.
const bankChannel = {
  id: 'ec000000-0000-4000-8000-000000000001',
  kind: 'Bank',
  name: 'HDFC current',
  accountLabel: '4521',
  gateway: null,
  bankChannelId: null,
  dateWindowDays: 3,
  mapping: {
    headerRow: 1,
    dateColumn: 'Txn Date',
    dateFormat: 'dd/MM/yyyy',
    valueDateColumn: null,
    descriptionColumn: 'Narration',
    referenceColumn: 'Ref No',
    amountLayout: 'TwoColumns',
    amountColumn: null,
    debitColumn: 'Debit',
    creditColumn: 'Credit',
    balanceColumn: 'Balance',
    decimalStyle: 'Point',
  },
  status: 'Active',
  createdAtUtc: '2026-10-16T05:00:00+00:00',
  updatedAtUtc: '2026-10-16T05:00:00+00:00',
  version: 'AAAAAAAAB9E=',
};
const statementImport = {
  id: 'ed000000-0000-4000-8000-000000000001',
  channelId: bankChannel.id,
  fileName: 'hdfc-16-oct.csv',
  fileHash: 'A'.repeat(64),
  rowsRead: 5,
  linesAdded: 4,
  linesAlreadyHeld: 0,
  rowsRefused: 1,
  firstDate: '2026-10-16',
  lastDate: '2026-10-16',
  createdAtUtc: '2026-10-17T05:00:00+00:00',
  createdByUserId: 'd0000000-0000-4000-8000-000000000002',
};
const statementLine = {
  id: 'ee000000-0000-4000-8000-000000000001',
  channelId: bankChannel.id,
  importId: statementImport.id,
  rowNumber: 2,
  transactionDate: '2026-10-16',
  valueDate: null,
  direction: 'In',
  amount: 12000,
  reference: 'N3341',
  description: 'NEFT CR-HDFC0001-RAVI KUMAR-OPD BILL',
  balance: 152300.5,
  rawText: '16/10/2026,NEFT CR-HDFC0001-RAVI KUMAR-OPD BILL,N3341,,"12,000.00","1,52,300.50"',
  status: 'Matched',
  version: 'AAAAAAAAB9F=',
  matchReason: 'The reference N3341 and the amount ₹12,000.00 agree with PAY/26-27/000233 of 16 Oct 2026.',
  proposalReason: null,
  proposalScore: null,
  proposalCount: 0,
};
const everyRole = sessionOf(['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier']);

const serviceItem = {
  id: 'c3000000-0000-4000-8000-000000000003',
  code: 'LAB/CBC',
  name: 'Complete blood count',
  category: 'Laboratory',
  defaultUnitPrice: 450,
  taxRatePercent: 0,
  status: 'Active',
  createdAtUtc: '2026-10-01T04:32:00+00:00',
  updatedAtUtc: '2026-10-01T04:32:00+00:00',
  version: 'AAAAAAAAB9M=',
};
const payment = {
  id: 'f6000000-0000-4000-8000-000000000001',
  number: 'PAY/26-27/000001',
  fiscalYear: '2026-27',
  payerId: payer.id,
  paymentDate: '2026-11-21',
  method: 'Cash',
  reference: null,
  amount: 2000,
  unappliedAmount: 1500,
  status: 'Posted',
  origin: 'Standard',
  legacyNumber: null,
  notes: null,
  reversedAtUtc: null,
  reversalReason: null,
  createdAtUtc: '2026-11-21T05:00:00+00:00',
  updatedAtUtc: '2026-11-21T06:00:00+00:00',
  version: 'AAAAAAAACQA=',
};
const allocation = {
  id: 'f7000000-0000-4000-8000-000000000001',
  billId: posted.id,
  amount: 500,
  status: 'Active',
  allocatedAtUtc: '2026-11-21T06:00:00+00:00',
  allocatedByUserId: 'd0000000-0000-4000-8000-000000000002',
  reversedAtUtc: null,
  reversalReason: null,
};
const refund = {
  id: 'f8000000-0000-4000-8000-000000000001',
  payerId: payer.id,
  sourceKind: 'Payment',
  paymentId: payment.id,
  creditNoteId: null,
  refundDate: '2026-11-22',
  amount: 200,
  method: 'UPI',
  reference: 'asha@upi',
  reason: 'Paid twice at the counter',
  status: 'Draft',
  number: null,
  fiscalYear: null,
  postedAtUtc: null,
  voidedAtUtc: null,
  voidReason: null,
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T05:00:00+00:00',
  version: 'AAAAAAAACgA=',
};
const draftCreditNote = { ...creditNote, id: 'f9000000-0000-4000-8000-000000000002', status: 'Draft', number: null };
const draftAdjustment = { ...adjustment, id: 'fa000000-0000-4000-8000-000000000002', status: 'Draft' };
const period = { from: '2026-11-01', to: '2026-11-30' };
const report = (items: object[], totals: object) => ({ ...pageOf(items), totals, period });
const billRow = {
  billId: posted.id,
  number: posted.number,
  billDate: posted.billDate,
  dueDate: posted.dueDate,
  patientId: patient.id,
  patientName: patient.fullName,
  payerId: payer.id,
  payerName: payer.name,
  total: posted.total,
  balanceDue: posted.balanceDue,
  status: 'PartiallyPaid',
  postedAtUtc: posted.postedAtUtc,
};
const paymentRow = {
  paymentId: payment.id,
  number: payment.number,
  paymentDate: payment.paymentDate,
  payerId: payer.id,
  payerName: payer.name,
  method: 'Cash',
  reference: null,
  amount: 2000,
  unappliedAmount: 1500,
  status: 'Posted',
  reversedAtUtc: null,
};
const request = {
  approvalRequestId: 'fb000000-0000-4000-8000-000000000001',
  documentType: 'Adjustment',
  action: 'Post',
  documentId: adjustment.id,
  parentDocumentId: null,
  makerUserId: 'e1000000-0000-4000-8000-000000000009',
  makerComment: 'Staff discount',
  requestedAtUtc: '2026-11-22T05:00:00+00:00',
  waitingDays: 2,
  payerId: payer.id,
  payerName: payer.name,
  amount: 49.48,
};
const balance = {
  payerId: payer.id,
  payerName: payer.name,
  receivable: 949.48,
  unappliedCash: 1500,
  creditAvailable: 0,
  netOutstanding: -550.52,
};
const colleague = {
  id: 'a7000000-0000-4000-8000-000000000001',
  email: 'ravi.iyer@cityhospital.example',
  displayName: 'Ravi Iyer',
  status: 'Active',
  roles: ['Supervisor', 'Cashier'],
  credentialsChangeDue: true,
  lastSignInAtUtc: null,
  createdAtUtc: '2026-11-01T05:00:00+00:00',
  updatedAtUtc: '2026-11-20T05:00:00+00:00',
  version: 'AAAAAAAAB9E=',
};
const settings = {
  postingApprovalThreshold: 100000,
  maxBackdateDays: 7,
  defaultDueDays: 15,
  prefixes: { bill: 'BIL', creditNote: 'CRN', payment: 'PAY', adjustment: 'ADJ', refund: 'REF' },
  // Since Prompt 10.4 (ADR 0054): the letterhead of the printed documents, partly set.
  letterhead: {
    name: 'City Hospital',
    legalName: 'City Hospital Private Limited',
    addressLine1: '12 Example Road',
    addressLine2: null,
    city: 'Pune',
    state: 'Maharashtra',
    pinCode: '411001',
    phone: null,
    email: null,
    gstin: null,
    signatory: 'Authorised signatory',
    signatoryDesignation: null,
    logoSha256: null,
    signatureSha256: null,
  },
  version: 'AAAAAAAAC1E=',
};
const figure = (value: number, previous: number | null, changePercent: number | null) => ({
  value,
  previous,
  changePercent,
});

// Since Prompt 11.2b (ADR 0060): the signed-in user's open shift, a colleague's counted one, and their day.
const openShift = {
  id: 'fe000000-0000-4000-8000-000000000001',
  cashierUserId: userId,
  cashierName: 'Asha Rao',
  businessDate: '2026-11-23',
  status: 'Open',
  openingFloat: 1000,
  openedAtUtc: '2026-11-23T03:00:00+00:00',
  cashDocuments: 1,
  cashReceived: 2000,
  cashPaidOut: 0,
  expectedCash: 3000,
  count: null,
  looseChange: null,
  countedCash: null,
  difference: null,
  differenceReason: null,
  countedAtUtc: null,
  closedAtUtc: null,
  version: 'AAAAAAAAEAA=',
};
const countedShift = {
  ...openShift,
  id: 'fe000000-0000-4000-8000-000000000002',
  cashierUserId: 'd0000000-0000-4000-8000-000000000077',
  cashierName: 'Chetan Cashier',
  status: 'PendingApproval',
  count: [2000, 500, 200, 100, 50, 20, 10, 5, 2, 1].map((denomination) => ({
    denomination,
    pieces: denomination === 500 ? 5 : denomination === 200 ? 2 : 0,
  })),
  looseChange: 0,
  countedCash: 2900,
  difference: -100,
  differenceReason: 'A note of 100 is missing',
  countedAtUtc: '2026-11-23T11:30:00+00:00',
  version: 'AAAAAAAAEQA=',
};
const dayEnd = {
  date: '2026-11-23',
  shifts: [openShift, countedShift].map((shift) => ({
    shiftId: shift.id,
    cashierUserId: shift.cashierUserId,
    cashierName: shift.cashierName,
    status: shift.status,
    openedAtUtc: shift.openedAtUtc,
    closedAtUtc: null,
    openingFloat: shift.openingFloat,
    received: shift.cashReceived,
    paidOut: shift.cashPaidOut,
    net: shift.cashReceived - shift.cashPaidOut,
    expectedCash: shift.expectedCash,
    countedCash: shift.countedCash,
    difference: shift.difference,
  })),
  shiftsTotal: { received: 4000, paidOut: 0, net: 4000 },
  outsideShifts: { received: 0, paidOut: 0, net: 0 },
  cash: { received: 4000, paidOut: 0, net: 4000 },
  difference: -100,
  unclosedShifts: 2,
};

const answers: Answers = {
  // What the page of a forgotten password asks first: whether this product sends mail (ADR 0061, amended).
  'GET /api/v1/auth/options': () => ({ passwordResetByMail: true }),
  // Since Prompt 13.1c: the channels, the imports and the lines of R2 (ADR 0065).
  'GET /api/v1/settlement-channels': () => pageOf([bankChannel]),
  [`GET /api/v1/settlement-channels/${bankChannel.id}`]: () => bankChannel,
  'GET /api/v1/statement-imports': () => pageOf([statementImport]),
  'GET /api/v1/statement-lines': () => pageOf([statementLine]),
  'GET /api/v1/patients': () => pageOf([patient]),
  [`GET /api/v1/patients/${patient.id}`]: () => patient,
  'GET /api/v1/payers': () => pageOf([payer]),
  [`GET /api/v1/payers/${payer.id}`]: () => payer,
  'GET /api/v1/service-items': () => pageOf([serviceItem]),
  [`GET /api/v1/service-items/${serviceItem.id}`]: () => serviceItem,
  // A filter by status (the payer's open bills) answers the posted bill alone, as the API does.
  'GET /api/v1/bills': (url) => pageOf(url.searchParams.has('Status') ? [posted] : [draft, posted]),
  [`GET /api/v1/bills/${draft.id}`]: () => draft,
  [`GET /api/v1/bills/${posted.id}`]: () => posted,
  'GET /api/v1/payments': () => pageOf([payment]),
  [`GET /api/v1/payments/${payment.id}`]: () => payment,
  [`GET /api/v1/payments/${payment.id}/allocations`]: () => ({ payment, allocations: [allocation] }),
  'GET /api/v1/cashier-shifts': () => pageOf([openShift, countedShift]),
  [`GET /api/v1/cashier-shifts/${openShift.id}`]: () => openShift,
  [`GET /api/v1/cashier-shifts/${countedShift.id}`]: () => countedShift,
  'GET /api/v1/reports/day-end': () => dayEnd,
  // Since Prompt 11.4b: the accountant's export of a month, one ledger without a name, and the names (ADR 0062).
  'GET /api/v1/reports/accounting-export/summary': () => ({
    from: '2026-11-01',
    to: '2026-11-30',
    vouchers: 3,
    receipts: 1,
    payments: 0,
    journals: 2,
    controlTotal: 1049.48,
    ledgerTotal: 1049.48,
    ledgers: [
      { ledger: 'Receivable', name: 'Patient Receivables', debit: 500, credit: 549.48 },
      { ledger: 'Revenue', name: 'Hospital Income', debit: 0, credit: 450 },
      { ledger: 'TaxOutput', name: null, debit: 0, credit: 50 },
      { ledger: 'Cash', name: 'Cash', debit: 549.48, credit: 0 },
    ],
    missing: ['TaxOutput'],
  }),
  'GET /api/v1/settings/accounting-ledgers': () => ({
    receivable: 'Patient Receivables',
    unappliedCash: 'Advances from Patients',
    creditAvailable: null,
    revenue: 'Hospital Income',
    taxOutput: null,
    adjustments: 'Discounts & Write-offs',
    openingBalances: null,
    cash: 'Cash',
    card: 'HDFC Current A/c',
    upi: 'HDFC Current A/c',
    bankTransfer: null,
    cheque: null,
    version: 'AAAAAAAAD1E=',
  }),
  'GET /api/v1/refunds': () => pageOf([refund]),
  [`GET /api/v1/refunds/${refund.id}`]: () => refund,
  'GET /api/v1/credit-notes': () => pageOf([creditNote, draftCreditNote]),
  [`GET /api/v1/credit-notes/${creditNote.id}`]: () => creditNote,
  [`GET /api/v1/credit-notes/${draftCreditNote.id}`]: () => draftCreditNote,
  'GET /api/v1/adjustments': () => pageOf([adjustment, draftAdjustment]),
  [`GET /api/v1/adjustments/${adjustment.id}`]: () => adjustment,
  [`GET /api/v1/adjustments/${draftAdjustment.id}`]: () => draftAdjustment,
  'GET /api/v1/audit-records': (url) =>
    trail(url.searchParams.get('EntityId') ?? '', ['Drafted', 'LinesReplaced', 'Posted']),
  'GET /api/v1/reports/bill-register': () => report([billRow], { bills: 1, total: 2249.48, balanceDue: 949.48 }),
  'GET /api/v1/reports/payment-register': () =>
    report([paymentRow], { payments: 1, amount: 2000, unappliedAmount: 1500 }),
  'GET /api/v1/reports/daily-collection': () => ({
    date: '2026-11-23',
    methods: [{ method: 'Cash', payments: 1, received: 2000, reversed: 0, refunded: 0, net: 2000 }],
    totals: { payments: 1, received: 2000, reversed: 0, refunded: 0, net: 2000 },
  }),
  'GET /api/v1/reports/outstanding-by-payer': () =>
    report([balance], {
      payers: 1,
      receivable: 949.48,
      unappliedCash: 1500,
      creditAvailable: 0,
      netOutstanding: -550.52,
    }),
  'GET /api/v1/reports/unapplied-cash': () => report([paymentRow], { payments: 1, unappliedAmount: 1500 }),
  'GET /api/v1/reports/available-credit': () => report([], { creditNotes: 0, availableAmount: 0 }),
  'GET /api/v1/reports/pending-approvals': () =>
    report([request], { requests: 1, oldestRequestedAtUtc: request.requestedAtUtc }),
  'GET /api/v1/reports/reconciliation': () =>
    report([], { rows: 0, byIdentity: [], checkedAtUtc: '2026-11-23T06:00:00+00:00' }),
  [`GET /api/v1/reports/payers/${payer.id}/balance`]: () => balance,
  'GET /api/v1/users': () => pageOf([colleague]),
  [`GET /api/v1/users/${colleague.id}`]: () => colleague,
  'GET /api/v1/settings': () => settings,
  'GET /api/v1/settings/letterhead': () => letterhead,
  'GET /api/v1/settings/print': () => printSettings(),
  'GET /api/v1/opening-batches': () => pageOf([openingBatch]),
  [`GET /api/v1/opening-batches/${openingBatch.id}`]: () => openingBatch,
  'GET /api/v1/dashboard/summary': () => ({
    period,
    previousPeriod: { from: '2026-10-01', to: '2026-10-31' },
    totalBilled: figure(2249.48, 1000, 124.95),
    amountCollected: figure(2000, 1500, 33.33),
    outstandingDues: figure(949.48, 500, 89.9),
    unappliedCash: figure(1500, 0, null),
    pendingApprovals: figure(1, 0, null),
    reconciliationDrift: figure(0, null, null),
  }),
  'GET /api/v1/dashboard/trend': () => ({ period, interval: 'Day', points: [], daily: [] }),
  'GET /api/v1/dashboard/ageing': () => ({
    asOf: '2026-11-23',
    bills: 1,
    total: 949.48,
    buckets: [
      { bucket: '0-30', bills: 1, amount: 949.48, share: 100 },
      { bucket: '31-60', bills: 0, amount: 0, share: 0 },
      { bucket: '61-90', bills: 0, amount: 0, share: 0 },
      { bucket: '90+', bills: 0, amount: 0, share: 0 },
    ],
  }),
  'GET /api/v1/dashboard/top-payers': () =>
    report([balance], { payers: 1, receivable: 0, unappliedCash: 0, creditAvailable: 0, netOutstanding: 0 }),
  'GET /api/v1/dashboard/recent-payments': () =>
    report([paymentRow], { payments: 1, amount: 2000, unappliedAmount: 1500 }),
  'GET /api/v1/dashboard/pending-approvals': () =>
    report([request], { requests: 1, oldestRequestedAtUtc: request.requestedAtUtc }),
  'GET /api/v1/dashboard/drift': () =>
    report([], { rows: 0, byIdentity: [], checkedAtUtc: '2026-11-23T06:00:00+00:00' }),
};

/** Every route of the shell and the heading it shows once it has drawn. */
// A page is opened by a user with every role, unless it names the session it is for (the stages of a sign-in).
const routes: readonly (readonly [string, string | RegExp, ReturnType<typeof sessionOf>?])[] = [
  ['/dashboard', 'Dashboard'],
  ['/patients', 'Patients'],
  ['/patients/new', 'Register patient'],
  [`/patients/${patient.id}`, patient.fullName],
  [`/patients/${patient.id}/edit`, /^Edit/],
  ['/payers', 'Payers'],
  // Not "New payer": that heading shows only while the patient loads, and may be gone before it is looked for.
  [`/payers/new?patientId=${patient.id}`, `Payer for ${patient.fullName}`],
  [`/payers/${payer.id}`, /Asha Verma/],
  [`/payers/${payer.id}/edit`, /^Edit/],
  [`/payers/${payer.id}/statement`, 'Statement of Asha Verma'],
  ['/service-items', 'Service items'],
  ['/service-items/new', /^New/],
  [`/service-items/${serviceItem.id}`, /Complete blood count|LAB\/CBC/],
  [`/service-items/${serviceItem.id}/edit`, /^Edit/],
  ['/bills', 'Bills'],
  ['/bills/new', 'New bill'],
  [`/bills/${draft.id}`, 'Draft bill'],
  [`/bills/${posted.id}`, posted.number],
  // Since Prompt 10.4b (ADR 0054): the printed documents.
  [`/bills/${posted.id}/print`, /^Print/],
  [`/payments/${payment.id}/print`, /^Print/],
  [`/refunds/${refund.id}/print`, /^Print/],
  [`/bills/${draft.id}/edit`, 'Edit draft bill'],
  ['/payments', 'Payments'],
  [`/payments/new?payerId=${payer.id}`, /^Record/],
  [`/payments/${payment.id}`, payment.number],
  [`/payments/${payment.id}/allocate`, /^Allocate/],
  ['/refunds', 'Refunds'],
  [`/refunds/new?paymentId=${payment.id}`, 'Draft a refund'],
  [`/refunds/${refund.id}`, /refund/i],
  [`/refunds/${refund.id}/edit`, /refund/i],
  ['/credit-notes', 'Credit notes'],
  [`/credit-notes/new?billId=${posted.id}`, /credit note/i],
  [`/credit-notes/${creditNote.id}`, creditNote.number ?? 'Credit note'],
  [`/credit-notes/${draftCreditNote.id}/edit`, /credit note/i],
  [`/credit-notes/${creditNote.id}/apply`, /^Apply/],
  ['/adjustments', 'Adjustments'],
  // Not "New adjustment": that heading shows only while the bill loads, and may be gone before it is looked for.
  [`/adjustments/new?billId=${posted.id}`, `Adjust ${posted.number}`],
  [`/adjustments/${adjustment.id}`, /adjustment/i],
  [`/adjustments/${draftAdjustment.id}/edit`, /adjustment/i],
  ['/approvals', 'Approvals'],
  ['/reports', 'Reports'],
  ['/reports/bills', 'Bill register'],
  ['/reports/payments', 'Payment register'],
  ['/reports/collection', 'Daily collection'],
  // Since Prompt 11.2b: the shifts of the cash counter, the closing slip and the day-end report (ADR 0060).
  ['/shifts', 'Shifts'],
  ['/shifts/new', 'Open a shift'],
  [`/shifts/${openShift.id}`, /^Shift of/],
  [`/shifts/${openShift.id}/close`, 'Close the shift'],
  [`/shifts/${countedShift.id}`, /^Shift of/],
  [`/shifts/${countedShift.id}/print`, /^Print/],
  ['/reports/day-end', 'Day-end report'],
  // Since Prompt 11.4b: the accountant's export and the names of its ledgers (ADR 0062).
  ['/reports/accounting-export', "Accountant's export"],
  ['/administration/accounting', 'Users and settings'],
  // Since Prompt 13.1c: the settlement channels and the bank statements (ADR 0065).
  ['/administration/channels', 'Users and settings'],
  ['/administration/channels/new', 'New settlement channel'],
  [`/administration/channels/${bankChannel.id}`, bankChannel.name],
  ['/reconciliation', 'Bank reconciliation'],
  ['/reconciliation/import', 'Import a bank statement'],
  ['/reports/outstanding', 'Outstanding by payer'],
  ['/reports/unapplied-cash', 'Unapplied cash'],
  ['/reports/available-credit', 'Available credit'],
  ['/reports/pending-approvals', 'Pending approvals'],
  ['/reports/reconciliation', 'Reconciliation'],
  ['/administration', 'Users and settings'],
  ['/administration/users/new', 'New user'],
  [`/administration/users/${colleague.id}`, colleague.displayName],
  [`/administration/users/${colleague.id}/edit`, `Edit ${colleague.displayName}`],
  ['/administration/settings', 'Users and settings'],
  // Since Prompt 10.3b: bringing in a hospital's records and opening balances (ADR 0053).
  ['/administration/print', 'Users and settings'],
  ['/administration/import', 'Users and settings'],
  ['/administration/import/patients', 'Import patients and guarantors'],
  ['/administration/import/opening-balances', 'Import opening balances'],
  [`/administration/import/batches/${openingBatch.id}`, 'Opening batch'],
  ['/change-password', 'Change your password'],
  // Since Prompt 11.3b: the pages before the application (ADR 0061). The second step and its enrolment, each for a
  // session at its stage (backlog B43); a forgotten password, which needs no session at all.
  ['/second-step', 'Enter your code', { ...sessionOf(['Supervisor']), secondStep: 'Pending' }],
  ['/second-step/enrol', 'Set up your second step', { ...sessionOf(['Supervisor']), secondStep: 'Enrolment' }],
  ['/forgot-password', 'Forgot your password?'],
  ['/reset-password', 'Choose a new password'],
  ['/no-access', 'No access'],
  ['/no-such-page', 'Page not found'],
];

const sizes = { desktop: { width: 1440, height: 900 }, phone: { width: 390, height: 844 } } as const;

/** The training notes' screenshots in docs/ui, by route (docs/go-live.md); payment.png is cashier.spec.ts's. */
const trainingShots: Record<string, { file: string; fullPage: boolean } | undefined> = {
  '/patients/new': { file: 'patient-new', fullPage: false },
  [`/payments/new?payerId=${payer.id}`]: { file: 'payment-new', fullPage: false },
  '/approvals': { file: 'approvals', fullPage: false },
  '/shifts/new': { file: 'shift-open', fullPage: false },
  [`/shifts/${openShift.id}/close`]: { file: 'shift-close', fullPage: true },
  '/reports/day-end': { file: 'day-end', fullPage: false },
  '/reports/accounting-export': { file: 'accounting-export', fullPage: false },
  '/administration/accounting': { file: 'settings-accounts', fullPage: true },
  '/administration/channels/new': { file: 'channel-new', fullPage: true },
  '/administration/channels': { file: 'settings-channels', fullPage: false },
  '/reconciliation': { file: 'reconciliation', fullPage: false },
  '/administration': { file: 'users', fullPage: false },
  '/administration/users/new': { file: 'user-new', fullPage: false },
  '/second-step': { file: 'second-step', fullPage: false },
  '/second-step/enrol': { file: 'second-step-enrol', fullPage: false },
};

async function sidewaysAt(page: Page, width: number) {
  await page.setViewportSize({ width, height: 900 });
  return page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
}

for (const [path, heading, signedIn = everyRole] of routes) {
  for (const theme of ['light', 'dark'] as const) {
    for (const [label, size] of Object.entries(sizes)) {
      test(`${path} in the ${theme} theme on a ${label}`, async ({ page }, info) => {
        const { errors, unexpected, settle, probe } = await open(page, path, theme, size, answers, signedIn);

        await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
        await page.waitForLoadState('networkidle');
        await settle();
        const { violations, shift, moved } = await probe();
        expect(violations, 'refused by the content security policy').toEqual([]);
        expect(errors, 'errors in the console').toEqual([]);
        expect(unexpected, 'requests the test did not expect').toEqual([]);
        expect(shift, `layout shift after the first paint: ${moved.join(', ')}`).toBeLessThan(0.01);
        expect(await page.locator('h1').count(), 'one h1').toBe(1);
        await page.screenshot({ path: info.outputPath(`${label}-${theme}.png`), fullPage: true });
        // The chosen screenshots of 9.4b for docs/ui (E2E_SCREENSHOTS=1): the reports' index and one report.
        const chosen = { '/reports': 'reports', '/reports/bills': 'report-bills' }[path];
        if (screenshots && chosen !== undefined) {
          await page.screenshot({
            path: `../docs/ui/${chosen}${label === 'phone' ? '-phone' : ''}${theme === 'dark' ? '-dark' : ''}.png`,
          });
        }
        // The training notes' screenshots (docs/go-live.md, Prompt 10.7b), light and desktop only; taken by hand until
        // 2026-11-28 and written here since, so that a change to a screen can retake them. A tall form is the full page.
        const training = trainingShots[path];
        if (screenshots && training !== undefined && theme === 'light' && label === 'desktop') {
          await page.screenshot({ path: `../docs/ui/${training.file}.png`, fullPage: training.fullPage });
        }

        if (label === 'desktop') {
          const axe = await new AxeBuilder({ page })
            .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
            .analyze();
          const serious = axe.violations
            .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
            .map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(' | ')}`);
          expect(serious, 'axe: serious and critical findings').toEqual([]);

          // From 360 to 1,920 px, and at 200% zoom (a 1440 px screen's page at 720 px): nothing wider than the window.
          for (const width of [1920, 720, 360]) {
            expect(await sidewaysAt(page, width), `sideways scroll at ${String(width)} px`).toBeLessThanOrEqual(0);
          }
        }
      });
    }
  }
}
