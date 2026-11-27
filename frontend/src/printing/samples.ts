import type { Bill } from '../bills/api';
import type { PaymentWithAllocations } from '../payments/api';
import type { Refund } from '../refunds/api';
import type { CashierShift } from '../shifts/api';

/**
 * A sample document for the preview of the print settings (ADR 0059): invented, and the same for every hospital. A
 * bill that carries tax, so that the preview shows the columns a tax invoice must have.
 */
export const sampleBill: Bill = {
  id: '00000000-0000-4000-8000-0000000000b1',
  patientId: '00000000-0000-4000-8000-0000000000a1',
  payerId: '00000000-0000-4000-8000-0000000000a2',
  billDate: '2026-04-06',
  dueDate: '2026-04-21',
  encounterReference: 'OPD-1024',
  notes: null,
  origin: 'Standard',
  legacyNumber: null,
  status: 'Posted',
  number: 'BIL/26-27/000042',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-04-06T05:30:00+00:00',
  subTotal: 2200,
  discountTotal: 0.5,
  taxTotal: 49.98,
  total: 2249.48,
  balanceDue: 2249.48,
  voidedAtUtc: null,
  voidReason: null,
  lines: [
    {
      id: '00000000-0000-4000-8000-0000000000c1',
      lineNo: 1,
      serviceItemId: '00000000-0000-4000-8000-0000000000d1',
      description: 'Complete blood count',
      quantity: 3,
      unitPrice: 333.3333,
      lineAmount: 1000,
      discountAmount: 0.5,
      taxRatePercent: 5,
      sacCode: '999311',
      taxAmount: 49.98,
      lineTotal: 1049.48,
    },
    {
      id: '00000000-0000-4000-8000-0000000000c2',
      lineNo: 2,
      serviceItemId: '00000000-0000-4000-8000-0000000000d2',
      description: 'Consultation, general medicine',
      quantity: 1,
      unitPrice: 1200,
      lineAmount: 1200,
      discountAmount: 0,
      taxRatePercent: 0,
      sacCode: '999312',
      taxAmount: 0,
      lineTotal: 1200,
    },
  ],
  createdAtUtc: '2026-04-06T05:00:00+00:00',
  updatedAtUtc: '2026-04-06T05:30:00+00:00',
  version: 'AAAAAAAAAAE=',
};

export const samplePatient = {
  fullName: 'Asha Verma',
  mrn: 'MRN-1001',
  phone: '+91 00000 00000',
  addressLine1: '4 Example Lane',
  addressLine2: null,
  city: 'Pune',
  state: 'Maharashtra',
  pinCode: '411001',
};

export const sampleReceipt: PaymentWithAllocations = {
  payment: {
    id: '00000000-0000-4000-8000-0000000000e1',
    number: 'PAY/26-27/000017',
    fiscalYear: '2026-27',
    payerId: sampleBill.payerId,
    paymentDate: '2026-04-06',
    method: 'Cash',
    reference: null,
    amount: 2500,
    unappliedAmount: 250.52,
    status: 'Posted',
    notes: null,
    origin: 'Standard',
    legacyNumber: null,
    reversedAtUtc: null,
    reversalReason: null,
    createdAtUtc: '2026-04-06T05:40:00+00:00',
    updatedAtUtc: '2026-04-06T05:45:00+00:00',
    version: 'AAAAAAAAAAI=',
  },
  allocations: [
    {
      id: '00000000-0000-4000-8000-0000000000f1',
      billId: sampleBill.id,
      amount: 2249.48,
      status: 'Active',
      allocatedAtUtc: '2026-04-06T05:45:00+00:00',
      allocatedByUserId: null,
      reversedAtUtc: null,
      reversalReason: null,
    },
  ],
};

export const sampleRefund: Refund = {
  id: '00000000-0000-4000-8000-0000000000a9',
  payerId: sampleBill.payerId,
  sourceKind: 'Payment',
  paymentId: sampleReceipt.payment.id,
  creditNoteId: null,
  refundDate: '2026-04-07',
  amount: 250.52,
  method: 'Cash',
  reference: null,
  reason: 'Advance not used',
  status: 'Posted',
  number: 'REF/26-27/000003',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-04-07T06:00:00+00:00',
  voidedAtUtc: null,
  voidReason: null,
  createdAtUtc: '2026-04-07T05:50:00+00:00',
  updatedAtUtc: '2026-04-07T06:00:00+00:00',
  version: 'AAAAAAAAAAM=',
};

export const sampleCashier = 'Meera Nair';

/** A shift counted five rupees short and approved, so that the preview shows a difference and its reason. */
export const sampleShift: CashierShift = {
  id: '00000000-0000-4000-8000-0000000000c1',
  cashierUserId: '00000000-0000-4000-8000-0000000000c2',
  cashierName: sampleCashier,
  businessDate: '2026-04-06',
  status: 'Closed',
  openingFloat: 1000,
  openedAtUtc: '2026-04-06T03:00:00+00:00',
  cashDocuments: 14,
  cashReceived: 8450.5,
  cashPaidOut: 250.5,
  expectedCash: 9200,
  count: [
    { denomination: 2000, pieces: 0 },
    { denomination: 500, pieces: 14 },
    { denomination: 200, pieces: 6 },
    { denomination: 100, pieces: 9 },
    { denomination: 50, pieces: 1 },
    { denomination: 20, pieces: 2 },
    { denomination: 10, pieces: 0 },
    { denomination: 5, pieces: 1 },
    { denomination: 2, pieces: 0 },
    { denomination: 1, pieces: 0 },
  ],
  looseChange: 0,
  countedCash: 9195,
  difference: -5,
  differenceReason: 'A five-rupee coin given in change by mistake',
  countedAtUtc: '2026-04-06T11:30:00+00:00',
  closedAtUtc: '2026-04-06T11:45:00+00:00',
  version: 'AAAAAAAAAAQ=',
};
