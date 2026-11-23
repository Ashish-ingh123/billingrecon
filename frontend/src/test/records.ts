import type { Bill, BillLine } from '../bills/api';
import type { Patient } from '../patients/api';
import type { Payer } from '../payers/api';
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

/** A page of a list, as the API answers one. */
export function page<T>(items: T[], totalCount = items.length, pageNumber = 1) {
  return { items, page: pageNumber, pageSize: 20, totalCount };
}

/** The ProblemDetails of a refusal. */
export function problem(status: number, errorCode: string, detail: string, errors?: Record<string, string[]>) {
  return { title: detail, status, detail, errorCode, correlationId: 'c0ffee00-0000-4000-8000-000000000000', errors };
}
