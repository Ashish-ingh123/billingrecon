import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { answer, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { ComparedField } from '../components/conflict';
import { listQuery, type ListParams } from '../components/listParams';
import { formatDate } from '../format';

/** A patient as the API answers one (`docs/api.md` section 8.1). */
export type Patient = components['schemas']['PatientDto'];

export type Sex = components['schemas']['Sex'];

/** The fields of a patient a form edits, as text; a blank one is sent as null. */
export interface PatientValues {
  mrn: string;
  fullName: string;
  dateOfBirth: string;
  sex: '' | Sex;
  phone: string;
  email: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  state: string;
  pinCode: string;
}

export const patientFields = [
  'mrn',
  'fullName',
  'dateOfBirth',
  'sex',
  'phone',
  'email',
  'addressLine1',
  'addressLine2',
  'city',
  'state',
  'pinCode',
] as const satisfies readonly (keyof PatientValues)[];

export const emptyPatient: PatientValues = {
  mrn: '',
  fullName: '',
  dateOfBirth: '',
  sex: '',
  phone: '',
  email: '',
  addressLine1: '',
  addressLine2: '',
  city: '',
  state: '',
  pinCode: '',
};

export function patientValues(patient: Patient): PatientValues {
  return {
    mrn: patient.mrn,
    fullName: patient.fullName,
    dateOfBirth: patient.dateOfBirth ?? '',
    sex: patient.sex ?? '',
    phone: patient.phone ?? '',
    email: patient.email ?? '',
    addressLine1: patient.addressLine1 ?? '',
    addressLine2: patient.addressLine2 ?? '',
    city: patient.city ?? '',
    state: patient.state ?? '',
    pinCode: patient.pinCode ?? '',
  };
}

const blankAsNull = (value: string): string | null => (value.trim() === '' ? null : value);

/** The editable fields of a patient, as the API takes them (a correction: every field but the MRN). */
function correction(values: PatientValues) {
  return {
    fullName: values.fullName,
    dateOfBirth: blankAsNull(values.dateOfBirth),
    sex: values.sex === '' ? null : values.sex,
    phone: blankAsNull(values.phone),
    email: blankAsNull(values.email),
    addressLine1: blankAsNull(values.addressLine1),
    addressLine2: blankAsNull(values.addressLine2),
    city: blankAsNull(values.city),
    state: blankAsNull(values.state),
    pinCode: blankAsNull(values.pinCode),
  };
}

/** The fields a conflict compares (ADR 0039 point 7): what a user sees of a patient. */
export const comparedPatientFields: readonly ComparedField<Patient>[] = [
  { label: 'Full name', value: (patient) => patient.fullName },
  { label: 'Date of birth', value: (patient) => formatDate(patient.dateOfBirth) },
  { label: 'Sex', value: (patient) => patient.sex ?? '' },
  { label: 'Phone', value: (patient) => patient.phone ?? '' },
  { label: 'E-mail address', value: (patient) => patient.email ?? '' },
  { label: 'Address line 1', value: (patient) => patient.addressLine1 ?? '' },
  { label: 'Address line 2', value: (patient) => patient.addressLine2 ?? '' },
  { label: 'City', value: (patient) => patient.city ?? '' },
  { label: 'State', value: (patient) => patient.state ?? '' },
  { label: 'PIN code', value: (patient) => patient.pinCode ?? '' },
  { label: 'Status', value: (patient) => patient.status },
];

export const patientKeys = {
  all: ['patients'] as const,
  list: (params: ListParams) => ['patients', 'list', params] as const,
  one: (id: string) => ['patients', id] as const,
};

export function getPatient(client: ApiClient, id: string): Promise<Patient> {
  return answer(client.GET('/api/v1/patients/{id}', { params: { path: { id } } }));
}

export function registerPatient(client: ApiClient, values: PatientValues): Promise<Patient> {
  return answer(client.POST('/api/v1/patients', { body: { mrn: values.mrn, ...correction(values) } }));
}

export function correctPatient(
  client: ApiClient,
  id: string,
  version: string,
  values: PatientValues,
): Promise<Patient> {
  return answer(
    client.PATCH('/api/v1/patients/{id}', {
      params: { path: { id } },
      body: correction(values),
      headers: ifMatch(version),
    }),
  );
}

export function deactivatePatient(client: ApiClient, patient: Patient): Promise<Patient> {
  return answer(
    client.POST('/api/v1/patients/{id}/deactivate', {
      params: { path: { id: patient.id } },
      headers: ifMatch(patient.version),
    }),
  );
}

export function reactivatePatient(client: ApiClient, patient: Patient): Promise<Patient> {
  return answer(
    client.POST('/api/v1/patients/{id}/reactivate', {
      params: { path: { id: patient.id } },
      headers: ifMatch(patient.version),
    }),
  );
}

export function usePatients(params: ListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: patientKeys.list(params),
    queryFn: ({ signal }) => answer(client.GET('/api/v1/patients', { params: { query: listQuery(params) }, signal })),
    // The rows on screen stay while a search that is being typed is read (ADR 0039, amended 2026-11-25).
    placeholderData: keepPreviousData,
  });
}

export function usePatient(id: string) {
  const client = useApiClient();
  return useQuery({ queryKey: patientKeys.one(id), queryFn: () => getPatient(client, id) });
}
