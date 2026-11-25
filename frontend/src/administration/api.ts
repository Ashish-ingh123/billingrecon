import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import { answer, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import { isRole, type Role } from '../auth/roles';
import { pageSize } from '../components/listParams';

/** A user as the API answers one (`docs/api.md` section 8.11; ADR 0030, ADR 0051). */
export type User = components['schemas']['UserDto'];
export type UserStatus = components['schemas']['UserStatus'];
export type TenantSettings = components['schemas']['TenantSettingsDto'];
export type PrefixChange = components['schemas']['PrefixChangeDto'];
export type DocumentType = components['schemas']['DocumentType'];

/** What each role may do, as the permission matrix says it (`docs/requirements.md` section 1), for the role choosers. */
export const roleDuties: Record<Role, string> = {
  TenantAdmin: 'Manages the hospital: its users, their roles, its settings and prefixes; reads everything.',
  Supervisor: 'Approves and rejects what waits for a second person; drafts and posts like a clerk; reads everything.',
  BillingClerk: 'Registers patients and payers, drafts and posts bills, drafts credit notes and adjustments.',
  Cashier: 'Records payments, allocates them, drafts refunds; asks for reversals.',
  Viewer: 'Reads bills, payments, reports and the dashboard; changes nothing.',
};

/** The documents that are numbered, in the order the settings show them, with the key of their prefix. */
export const documentTypes: readonly { type: DocumentType; label: string; key: keyof TenantSettings['prefixes'] }[] = [
  { type: 'Bill', label: 'Bills', key: 'bill' },
  { type: 'CreditNote', label: 'Credit notes', key: 'creditNote' },
  { type: 'Payment', label: 'Payments', key: 'payment' },
  { type: 'Adjustment', label: 'Adjustments', key: 'adjustment' },
  { type: 'Refund', label: 'Refunds', key: 'refund' },
];

/** The filters of the list of users, kept in the address: `?email=&status=&role=&page=`. */
export interface UserListParams {
  email: string;
  status: UserStatus | undefined;
  role: Role | undefined;
  page: number;
}

export function useUserListParams(): UserListParams & {
  change: (next: Partial<UserListParams>, options?: { replace?: boolean }) => void;
} {
  const [query, setQuery] = useSearchParams();
  const status = query.get('status');
  const role = query.get('role');
  const page = Number.parseInt(query.get('page') ?? '1', 10);
  const params: UserListParams = {
    email: query.get('email') ?? '',
    status: status === 'Active' || status === 'Disabled' ? status : undefined,
    role: isRole(role) ? role : undefined,
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };

  const change = (next: Partial<UserListParams>, options?: { replace?: boolean }) => {
    const merged = { ...params, ...next, page: next.page ?? 1 };
    const search = new URLSearchParams();
    if (merged.email.trim() !== '') search.set('email', merged.email.trim());
    if (merged.status !== undefined) search.set('status', merged.status);
    if (merged.role !== undefined) search.set('role', merged.role);
    if (merged.page > 1) search.set('page', String(merged.page));
    setQuery(search, { replace: options?.replace === true });
  };

  return { ...params, change };
}

export const userKeys = {
  all: ['users'] as const,
  list: (params: UserListParams) => ['users', 'list', params] as const,
  one: (id: string) => ['users', id] as const,
  settings: ['settings'] as const,
};

export function getUser(client: ApiClient, id: string): Promise<User> {
  return answer(client.GET('/api/v1/users/{id}', { params: { path: { id } } }));
}

export function useUsers(params: UserListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: userKeys.list(params),
    queryFn: ({ signal }) =>
      answer(
        client.GET('/api/v1/users', {
          signal,
          params: {
            query: {
              ...(params.email === '' ? {} : { Email: params.email }),
              ...(params.status === undefined ? {} : { Status: params.status }),
              ...(params.role === undefined ? {} : { Role: params.role }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
    // The users on screen stay while a search that is being typed is read (ADR 0039, amended 2026-11-25).
    placeholderData: keepPreviousData,
  });
}

export function useUser(id: string) {
  const client = useApiClient();
  return useQuery({ queryKey: userKeys.one(id), queryFn: () => getUser(client, id) });
}

export function createUser(
  client: ApiClient,
  values: { email: string; displayName: string; password: string; roles: Role[] },
): Promise<User> {
  return answer(client.POST('/api/v1/users', { body: values }));
}

export function updateUser(
  client: ApiClient,
  id: string,
  version: string,
  values: { email: string; displayName: string },
): Promise<User> {
  return answer(
    client.PATCH('/api/v1/users/{id}', { params: { path: { id } }, body: values, headers: ifMatch(version) }),
  );
}

export function replaceRoles(client: ApiClient, id: string, version: string, roles: Role[]): Promise<User> {
  return answer(
    client.PUT('/api/v1/users/{id}/roles', { params: { path: { id } }, body: { roles }, headers: ifMatch(version) }),
  );
}

export function disableUser(client: ApiClient, user: User): Promise<User> {
  return answer(
    client.POST('/api/v1/users/{id}/disable', { params: { path: { id: user.id } }, headers: ifMatch(user.version) }),
  );
}

export function enableUser(client: ApiClient, user: User): Promise<User> {
  return answer(
    client.POST('/api/v1/users/{id}/enable', { params: { path: { id: user.id } }, headers: ifMatch(user.version) }),
  );
}

export function resetPassword(client: ApiClient, user: User, password: string): Promise<User> {
  return answer(
    client.POST('/api/v1/users/{id}/password', {
      params: { path: { id: user.id } },
      body: { password },
      headers: ifMatch(user.version),
    }),
  );
}

export function useSettings() {
  const client = useApiClient();
  return useQuery({
    queryKey: userKeys.settings,
    queryFn: () => answer(client.GET('/api/v1/settings')),
  });
}

/** The three settings a TenantAdmin changes (ADR 0033), as the form holds them: text, sent as numbers. */
export interface SettingsValues {
  postingApprovalThreshold: string;
  maxBackdateDays: string;
  defaultDueDays: string;
}

export function settingsValues(settings: TenantSettings): SettingsValues {
  return {
    postingApprovalThreshold:
      settings.postingApprovalThreshold === null ? '' : String(settings.postingApprovalThreshold),
    maxBackdateDays: String(settings.maxBackdateDays),
    defaultDueDays: String(settings.defaultDueDays),
  };
}

export function updateSettings(client: ApiClient, version: string, values: SettingsValues): Promise<TenantSettings> {
  return answer(
    client.PATCH('/api/v1/settings', {
      body: {
        postingApprovalThreshold:
          values.postingApprovalThreshold.trim() === '' ? null : Number(values.postingApprovalThreshold),
        maxBackdateDays: Number(values.maxBackdateDays),
        defaultDueDays: Number(values.defaultDueDays),
      },
      headers: ifMatch(version),
    }),
  );
}

/** The letterhead as a form edits it (ADR 0054): every part as text, an empty one for none. */
export interface LetterheadValues {
  legalName: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  state: string;
  pinCode: string;
  phone: string;
  email: string;
  gstin: string;
  signatory: string;
}

export const letterheadFields = [
  'legalName',
  'addressLine1',
  'addressLine2',
  'city',
  'state',
  'pinCode',
  'phone',
  'email',
  'gstin',
  'signatory',
] as const satisfies readonly (keyof LetterheadValues)[];

export function letterheadValues(settings: TenantSettings): LetterheadValues {
  const { letterhead } = settings;
  return {
    legalName: letterhead.legalName ?? '',
    addressLine1: letterhead.addressLine1 ?? '',
    addressLine2: letterhead.addressLine2 ?? '',
    city: letterhead.city ?? '',
    state: letterhead.state ?? '',
    pinCode: letterhead.pinCode ?? '',
    phone: letterhead.phone ?? '',
    email: letterhead.email ?? '',
    gstin: letterhead.gstin ?? '',
    signatory: letterhead.signatory ?? '',
  };
}

/** Every part is sent, an empty one as null, which clears it; the version is the settings'. */
export function updateLetterhead(
  client: ApiClient,
  version: string,
  values: LetterheadValues,
): Promise<TenantSettings> {
  const part = (value: string) => (value.trim() === '' ? null : value.trim());
  return answer(
    client.PUT('/api/v1/settings/letterhead', {
      body: {
        legalName: part(values.legalName),
        addressLine1: part(values.addressLine1),
        addressLine2: part(values.addressLine2),
        city: part(values.city),
        state: part(values.state),
        pinCode: part(values.pinCode),
        phone: part(values.phone),
        email: part(values.email),
        gstin: part(values.gstin)?.toUpperCase() ?? null,
        signatory: part(values.signatory),
      },
      headers: ifMatch(version),
    }),
  );
}

export function requestPrefixChange(
  client: ApiClient,
  documentType: DocumentType,
  prefix: string,
  comment: string,
): Promise<PrefixChange> {
  return answer(
    client.POST('/api/v1/settings/prefix-changes', {
      body: { documentType, prefix: prefix.trim(), comment: comment.trim() === '' ? null : comment.trim() },
    }),
  );
}
