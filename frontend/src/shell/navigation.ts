import type { Role } from '../auth/roles';

/**
 * A section of the app and the roles that may open it: a copy of the permission matrix of docs/requirements.md
 * section 1 (ADR 0032). A role sees a section when its row of the matrix gives it anything there, reading included. The
 * API checks every request against the same matrix; hiding a link is a convenience, not a control.
 */
export interface Section {
  path: string;
  label: string;
  roles: readonly Role[];
  /** What the section is for. */
  purpose: string;
  /** The prompt of docs/build-prompts.md that builds its screens. */
  builtBy: string;
}

const everyone: readonly Role[] = ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Cashier', 'Viewer'];

export const sections: readonly Section[] = [
  {
    path: '/dashboard',
    label: 'Dashboard',
    roles: ['TenantAdmin', 'Supervisor', 'Viewer'],
    purpose: 'The figures of a period against the previous one, the ageing of what is owed, and what needs attention.',
    builtBy: '7.6',
  },
  {
    path: '/patients',
    label: 'Patients',
    roles: everyone,
    purpose: 'Register patients and correct their details.',
    builtBy: '7.2',
  },
  {
    path: '/payers',
    label: 'Payers',
    roles: everyone,
    purpose: "Who owes the bills: a patient's own payer, or a guarantor.",
    builtBy: '7.2',
  },
  {
    path: '/service-items',
    label: 'Service items',
    roles: everyone,
    purpose: 'The catalogue of services a bill is made of.',
    builtBy: '7.2',
  },
  {
    path: '/bills',
    label: 'Bills',
    roles: everyone,
    purpose: 'Draft, post and look up bills.',
    builtBy: '7.3',
  },
  {
    path: '/payments',
    label: 'Payments',
    roles: everyone,
    purpose: 'Record payments, allocate them to bills, and ask for reversals.',
    builtBy: '7.4',
  },
  {
    path: '/refunds',
    label: 'Refunds',
    roles: ['TenantAdmin', 'Supervisor', 'Cashier', 'Viewer'],
    purpose: "Refund a payment's unapplied cash or a credit note's available credit.",
    builtBy: '7.4',
  },
  {
    path: '/credit-notes',
    label: 'Credit notes',
    roles: everyone,
    purpose: 'Credit a posted bill, and apply the credit to other bills.',
    builtBy: '7.5',
  },
  {
    path: '/adjustments',
    label: 'Adjustments',
    roles: ['TenantAdmin', 'Supervisor', 'BillingClerk', 'Viewer'],
    purpose: 'Discounts, write-offs and rounding against a bill.',
    builtBy: '7.5',
  },
  {
    path: '/approvals',
    label: 'Approvals',
    roles: ['TenantAdmin', 'Supervisor'],
    purpose: 'The requests that wait for a second person to approve or reject them.',
    builtBy: '7.5',
  },
  {
    path: '/reports',
    label: 'Reports',
    roles: everyone,
    purpose: 'The registers, the daily collection, balances and the reconciliation.',
    builtBy: '7.5b',
  },
  {
    path: '/administration',
    label: 'Users and settings',
    roles: ['TenantAdmin'],
    purpose: "The hospital's users and their roles, its settings and its document prefixes.",
    builtBy: 'none yet',
  },
];

export function canOpen(section: Section, roles: readonly Role[]): boolean {
  return section.roles.some((role) => roles.includes(role));
}

/** The sections a user with these roles may open, in the order of the navigation. */
export function sectionsFor(roles: readonly Role[]): Section[] {
  return sections.filter((section) => canOpen(section, roles));
}

/**
 * Where a user lands after signing in: the dashboard for those who may see it, the payments for a cashier, the bills
 * for a billing clerk; a user with no known role lands on a page that says so.
 */
export function landingPath(roles: readonly Role[]): string {
  if (roles.includes('TenantAdmin') || roles.includes('Supervisor') || roles.includes('Viewer')) {
    return '/dashboard';
  }

  if (roles.includes('Cashier')) {
    return '/payments';
  }

  if (roles.includes('BillingClerk')) {
    return '/bills';
  }

  return '/no-access';
}
