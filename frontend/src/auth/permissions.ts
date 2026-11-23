import { useSession } from './AuthContext';
import type { Role } from './roles';

/**
 * The API's policies, one per capability of the permission matrix, with the roles each admits: a copy of
 * `src/BillingRecon.Api/Common/Authorization/Policies.cs` (ADR 0032), which a test holds equal to it (ADR 0039 point 3).
 * A screen shows an action to the roles of its policy only; the API checks every request whatever the screen shows.
 */
export const policies = {
  ManageTenant: ['TenantAdmin'],
  ReadReferenceData: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'],
  WriteReferenceData: ['TenantAdmin', 'Supervisor', 'BillingClerk'],
  ReadBills: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'],
  WriteBills: ['TenantAdmin', 'Supervisor', 'BillingClerk'],
  ReadCreditNotes: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'],
  ReadAdjustments: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Viewer'],
  DraftCreditNotesAndAdjustments: ['TenantAdmin', 'Supervisor', 'BillingClerk'],
  ReadPayments: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'],
  RecordPayments: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier'],
  ReadRefunds: ['TenantAdmin', 'Supervisor', 'Cashier', 'Viewer'],
  DraftRefunds: ['TenantAdmin', 'Supervisor', 'Cashier'],
  RequestReversals: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier'],
  Decide: ['TenantAdmin', 'Supervisor'],
  ReadAudit: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'],
  Reports: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'],
  Dashboard: ['TenantAdmin', 'Supervisor', 'Viewer'],
} as const satisfies Record<string, readonly Role[]>;

export type Policy = keyof typeof policies;

/** Whether a user with these roles is admitted by a policy: by any one of their roles. */
export function can(policy: Policy, roles: readonly Role[]): boolean {
  const admitted: readonly Role[] = policies[policy];
  return roles.some((role) => admitted.includes(role));
}

/** Whether the signed-in user is admitted by a policy. */
export function useCan(policy: Policy): boolean {
  return can(policy, useSession().roles);
}
