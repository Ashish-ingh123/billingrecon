import type { Adjustment } from '../adjustments/api';
import type { PendingApproval } from '../approvals/api';
import type { Bill, BillLine, BillSummary } from '../bills/api';
import type { CreditApplication, CreditNote, CreditNoteLine } from '../creditNotes/api';
import type { Patient } from '../patients/api';
import type { Payer } from '../payers/api';
import type { Allocation, Payment } from '../payments/api';
import type { Refund } from '../refunds/api';
import type { ServiceItem } from '../serviceItems/api';

/** Records as the API answers them, for the tests of the screens; each test changes what it is about. */

export function patient(overrides: Partial<Patient> = {}): Patient {
  return {
    id: 'a1000000-0000-4000-8000-000000000001',
    mrn: 'MRN-1001',
    fullName: 'Asha Verma',
    dateOfBirth: '1984-03-12',
    sex: 'Female',
    phone: '+91 98450 12345',
    email: 'asha@example.org',
    addressLine1: '12 MG Road',
    addressLine2: null,
    city: 'Bengaluru',
    state: 'Karnataka',
    pinCode: '560001',
    status: 'Active',
    createdAtUtc: '2026-10-01T04:30:00+00:00',
    updatedAtUtc: '2026-10-01T04:30:00+00:00',
    version: 'AAAAAAAAB9E=',
    ...overrides,
  };
}

export function payer(overrides: Partial<Payer> = {}): Payer {
  return {
    id: 'b2000000-0000-4000-8000-000000000002',
    kind: 'SelfPay',
    name: 'Asha Verma',
    patientId: 'a1000000-0000-4000-8000-000000000001',
    phone: null,
    email: null,
    status: 'Active',
    createdAtUtc: '2026-10-01T04:31:00+00:00',
    updatedAtUtc: '2026-10-01T04:31:00+00:00',
    version: 'AAAAAAAAB9I=',
    ...overrides,
  };
}

export function serviceItem(overrides: Partial<ServiceItem> = {}): ServiceItem {
  return {
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
    ...overrides,
  };
}

export function billLine(overrides: Partial<BillLine> = {}): BillLine {
  return {
    id: 'd4000000-0000-4000-8000-000000000001',
    lineNo: 1,
    serviceItemId: 'c3000000-0000-4000-8000-000000000003',
    description: 'Complete blood count',
    quantity: 3,
    unitPrice: 333.3333,
    lineAmount: 1000,
    discountAmount: 0.5,
    taxRatePercent: 5,
    taxAmount: 49.98,
    lineTotal: 1049.48,
    ...overrides,
  };
}

/** A draft of one line (the example of docs/api.md), for Asha Verma, owed by her own payer. */
export function bill(overrides: Partial<Bill> = {}): Bill {
  return {
    id: 'e5000000-0000-4000-8000-000000000001',
    patientId: 'a1000000-0000-4000-8000-000000000001',
    payerId: 'b2000000-0000-4000-8000-000000000002',
    billDate: '2026-11-20',
    dueDate: '2026-12-05',
    encounterReference: 'OPD-7781',
    notes: null,
    status: 'Draft',
    number: null,
    fiscalYear: null,
    postedAtUtc: null,
    subTotal: 1000,
    discountTotal: 0.5,
    taxTotal: 49.98,
    total: 1049.48,
    balanceDue: 1049.48,
    voidedAtUtc: null,
    voidReason: null,
    lines: [billLine()],
    createdAtUtc: '2026-11-20T05:00:00+00:00',
    updatedAtUtc: '2026-11-20T05:00:00+00:00',
    version: 'AAAAAAAACAA=',
    ...overrides,
  };
}

/** A bill as a list answers it: posted, owing what it totals unless said otherwise. */
export function billSummary(overrides: Partial<BillSummary> = {}): BillSummary {
  return {
    id: 'e5000000-0000-4000-8000-000000000011',
    patientId: 'a1000000-0000-4000-8000-000000000001',
    payerId: 'b2000000-0000-4000-8000-000000000002',
    billDate: '2026-11-10',
    dueDate: '2026-11-25',
    encounterReference: null,
    status: 'Posted',
    number: 'BIL/26-27/000011',
    total: 1049.48,
    balanceDue: 1049.48,
    createdAtUtc: '2026-11-10T05:00:00+00:00',
    updatedAtUtc: '2026-11-10T05:00:00+00:00',
    version: 'AAAAAAAACBA=',
    ...overrides,
  };
}

/** A cash payment of 1,500.00 from Asha's payer, nothing of it applied yet. */
export function payment(overrides: Partial<Payment> = {}): Payment {
  return {
    id: 'f6000000-0000-4000-8000-000000000001',
    number: 'PAY/26-27/000001',
    fiscalYear: '2026-27',
    payerId: 'b2000000-0000-4000-8000-000000000002',
    paymentDate: '2026-11-21',
    method: 'Cash',
    reference: null,
    amount: 1500,
    unappliedAmount: 1500,
    status: 'Posted',
    notes: null,
    reversedAtUtc: null,
    reversalReason: null,
    createdAtUtc: '2026-11-21T05:00:00+00:00',
    updatedAtUtc: '2026-11-21T05:00:00+00:00',
    version: 'AAAAAAAACQA=',
    ...overrides,
  };
}

export function allocation(overrides: Partial<Allocation> = {}): Allocation {
  return {
    id: 'f7000000-0000-4000-8000-000000000001',
    billId: 'e5000000-0000-4000-8000-000000000011',
    amount: 500,
    status: 'Active',
    allocatedAtUtc: '2026-11-21T06:00:00+00:00',
    allocatedByUserId: 'd0000000-0000-4000-8000-000000000002',
    reversedAtUtc: null,
    reversalReason: null,
    ...overrides,
  };
}

/** A draft refund of 200.00 from the payment above. */
export function refund(overrides: Partial<Refund> = {}): Refund {
  return {
    id: 'f8000000-0000-4000-8000-000000000001',
    payerId: 'b2000000-0000-4000-8000-000000000002',
    sourceKind: 'Payment',
    paymentId: 'f6000000-0000-4000-8000-000000000001',
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
    ...overrides,
  };
}

/** The whole first line of the bill above, credited at the bill's price and tax. */
export function creditNoteLine(overrides: Partial<CreditNoteLine> = {}): CreditNoteLine {
  return {
    lineNo: 1,
    billLineId: 'd4000000-0000-4000-8000-000000000001',
    description: 'Complete blood count',
    quantity: 3,
    unitPrice: 333.3333,
    lineAmount: 1000,
    discountAmount: 0.5,
    taxRatePercent: 5,
    taxAmount: 49.98,
    lineTotal: 1049.48,
    ...overrides,
  };
}

export function creditApplication(overrides: Partial<CreditApplication> = {}): CreditApplication {
  return {
    id: 'f9100000-0000-4000-8000-000000000001',
    billId: 'e5000000-0000-4000-8000-000000000001',
    amount: 300,
    status: 'Active',
    allocatedAtUtc: '2026-11-22T06:00:00+00:00',
    allocatedByUserId: null,
    appliedAtPosting: true,
    reversedAtUtc: null,
    reversalReason: null,
    ...overrides,
  };
}

/** A draft credit note of the bill above, crediting its whole line. */
export function creditNote(overrides: Partial<CreditNote> = {}): CreditNote {
  return {
    id: 'f9000000-0000-4000-8000-000000000001',
    billId: 'e5000000-0000-4000-8000-000000000001',
    payerId: 'b2000000-0000-4000-8000-000000000002',
    patientId: 'a1000000-0000-4000-8000-000000000001',
    creditNoteDate: '2026-11-22',
    reason: 'Test not done',
    status: 'Draft',
    number: null,
    fiscalYear: null,
    postedAtUtc: null,
    subTotal: 1000,
    discountTotal: 0.5,
    taxTotal: 49.98,
    total: 1049.48,
    availableAmount: 0,
    voidedAtUtc: null,
    voidReason: null,
    lines: [creditNoteLine()],
    applications: [],
    createdAtUtc: '2026-11-22T05:00:00+00:00',
    updatedAtUtc: '2026-11-22T05:00:00+00:00',
    version: 'AAAAAAAACwA=',
    ...overrides,
  };
}

/** A draft discount of 49.48 on the bill above. */
export function adjustment(overrides: Partial<Adjustment> = {}): Adjustment {
  return {
    id: 'fa000000-0000-4000-8000-000000000001',
    billId: 'e5000000-0000-4000-8000-000000000001',
    payerId: 'b2000000-0000-4000-8000-000000000002',
    adjustmentDate: '2026-11-22',
    kind: 'Discount',
    amount: 49.48,
    reason: 'Staff discount',
    status: 'Draft',
    number: null,
    fiscalYear: null,
    postedAtUtc: null,
    voidedAtUtc: null,
    voidReason: null,
    createdAtUtc: '2026-11-22T05:00:00+00:00',
    updatedAtUtc: '2026-11-22T05:00:00+00:00',
    version: 'AAAAAAAADAA=',
    ...overrides,
  };
}

/** A request that waits, as the pending approvals report answers it: by another user, a day ago. */
export function pendingApproval(overrides: Partial<PendingApproval> = {}): PendingApproval {
  return {
    approvalRequestId: 'fb000000-0000-4000-8000-000000000001',
    documentType: 'Bill',
    action: 'Post',
    documentId: 'e5000000-0000-4000-8000-000000000001',
    parentDocumentId: null,
    makerUserId: 'd0000000-0000-4000-8000-000000000009',
    makerComment: null,
    requestedAtUtc: '2026-11-22T05:00:00+00:00',
    waitingDays: 1,
    payerId: 'b2000000-0000-4000-8000-000000000002',
    payerName: 'Asha Verma',
    amount: 1049.48,
    ...overrides,
  };
}

/** A page of a report, with its totals, as the API answers one. */
export function reportPage<T, U>(items: T[], totals: U, totalCount = items.length) {
  return { period: null, items, page: 1, pageSize: 20, totalCount, totals };
}

/** A page of a list, as the API answers one. */
export function page<T>(items: T[], totalCount = items.length, pageNumber = 1) {
  return { items, page: pageNumber, pageSize: 20, totalCount };
}

/** The ProblemDetails of a refusal. */
export function problem(status: number, errorCode: string, detail: string, errors?: Record<string, string[]>) {
  return { title: detail, status, detail, errorCode, correlationId: 'c0ffee00-0000-4000-8000-000000000000', errors };
}
