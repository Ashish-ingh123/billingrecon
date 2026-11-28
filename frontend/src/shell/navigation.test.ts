import { describe, expect, it } from 'vitest';
import type { Role } from '../auth/roles';
import { landingPath, sections, sectionsFor } from './navigation';

// The permission matrix of docs/requirements.md section 1, read as "what may this role open", written out by hand so
// that a change of the navigation's copy has to agree with it: a role opens a section when its row gives it anything,
// reading included (a Cashier reads credit notes, ADR 0032).
const matrix: Record<Role, string[]> = {
  TenantAdmin: [
    '/dashboard',
    '/patients',
    '/payers',
    '/service-items',
    '/bills',
    '/payments',
    '/refunds',
    '/shifts',
    '/credit-notes',
    '/adjustments',
    '/approvals',
    '/reports',
    '/reconciliation',
    '/administration',
  ],
  Supervisor: [
    '/dashboard',
    '/patients',
    '/payers',
    '/service-items',
    '/bills',
    '/payments',
    '/refunds',
    '/shifts',
    '/credit-notes',
    '/adjustments',
    '/approvals',
    '/reports',
    '/reconciliation',
  ],
  BillingClerk: [
    '/patients',
    '/payers',
    '/service-items',
    '/bills',
    '/payments',
    '/shifts',
    '/credit-notes',
    '/adjustments',
    '/reports',
  ],
  Cashier: [
    '/patients',
    '/payers',
    '/service-items',
    '/bills',
    '/payments',
    '/refunds',
    '/shifts',
    '/credit-notes',
    '/reports',
  ],
  Viewer: [
    '/dashboard',
    '/patients',
    '/payers',
    '/service-items',
    '/bills',
    '/payments',
    '/refunds',
    '/shifts',
    '/credit-notes',
    '/adjustments',
    '/reports',
    '/reconciliation',
  ],
  // The Accountant (ADR 0069) opens what a Viewer opens; and Reconciliation, its own work (13.1c).
  Accountant: [
    '/dashboard',
    '/patients',
    '/payers',
    '/service-items',
    '/bills',
    '/payments',
    '/refunds',
    '/shifts',
    '/credit-notes',
    '/adjustments',
    '/reports',
    '/reconciliation',
  ],
};

describe('the navigation follows the permission matrix', () => {
  it.each(Object.entries(matrix))('%s sees exactly its sections', (role, paths) => {
    expect(sectionsFor([role as Role]).map((section) => section.path)).toEqual(paths);
  });

  it('gives a user with several roles what any of them may open', () => {
    expect(sectionsFor(['BillingClerk', 'Cashier']).map((section) => section.path)).toEqual([
      '/patients',
      '/payers',
      '/service-items',
      '/bills',
      '/payments',
      '/refunds',
      '/shifts',
      '/credit-notes',
      '/adjustments',
      '/reports',
    ]);
  });

  it('shows nothing to a user without a known role', () => {
    expect(sectionsFor([])).toEqual([]);
  });

  it('names every section once', () => {
    expect(new Set(sections.map((section) => section.path)).size).toBe(sections.length);
  });
});

describe('where a user lands', () => {
  it.each([
    [['TenantAdmin'], '/dashboard'],
    [['Supervisor'], '/dashboard'],
    [['Viewer'], '/dashboard'],
    [['Cashier'], '/payments'],
    [['BillingClerk'], '/bills'],
    [['BillingClerk', 'Cashier'], '/payments'],
    [['Cashier', 'Viewer'], '/dashboard'],
    [[], '/no-access'],
  ] as [Role[], string][])('%j lands on %s', (roles, path) => {
    expect(landingPath(roles)).toBe(path);
  });
});
