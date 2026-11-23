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

/** A page of a list, as the API answers one. */
export function page<T>(items: T[], totalCount = items.length, pageNumber = 1) {
  return { items, page: pageNumber, pageSize: 20, totalCount };
}

/** The ProblemDetails of a refusal. */
export function problem(status: number, errorCode: string, detail: string, errors?: Record<string, string[]>) {
  return { title: detail, status, detail, errorCode, correlationId: 'c0ffee00-0000-4000-8000-000000000000', errors };
}
