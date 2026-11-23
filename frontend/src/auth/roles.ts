/** The five roles of docs/requirements.md section 1, as the API names them in the token's `role` claim. */
export const roles = ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'] as const;

export type Role = (typeof roles)[number];

export function isRole(value: unknown): value is Role {
  return typeof value === 'string' && (roles as readonly string[]).includes(value);
}

/** How a role is written for people. */
export const roleLabels: Record<Role, string> = {
  TenantAdmin: 'Administrator',
  Supervisor: 'Supervisor',
  BillingClerk: 'Billing clerk',
  Cashier: 'Cashier',
  Viewer: 'Viewer',
};
