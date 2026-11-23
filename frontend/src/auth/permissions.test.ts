import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { can, policies } from './permissions';

/**
 * The frontend's copy of the API's policies (ADR 0039 point 3) is the API's: read from Policies.cs, the source of truth,
 * so that a policy added or a role changed there fails here until the copy follows.
 */
const policiesCs = readFileSync(
  join(process.cwd(), '..', 'src', 'BillingRecon.Api', 'Common', 'Authorization', 'Policies.cs'),
  'utf8',
);

function apiPolicies(): Record<string, string[]> {
  const table: Record<string, string[]> = {};
  for (const [, name = '', roles = ''] of policiesCs.matchAll(/\[(\w+)\]\s*=\s*\[([^\]]*)\]/g)) {
    table[name] = [...roles.matchAll(/Role\.(\w+)/g)].map(([, role]) => role ?? '');
  }

  return table;
}

describe("the frontend's copy of the API's policies", () => {
  it('reads a table from Policies.cs', () => {
    expect(Object.keys(apiPolicies()).length).toBeGreaterThan(10);
  });

  it('has every policy of the API, with the same roles in the same order, and no other', () => {
    expect(policies).toEqual(apiPolicies());
  });
});

describe('who a policy admits', () => {
  it('admits a user by any one of their roles', () => {
    expect(can('WriteReferenceData', ['BillingClerk'])).toBe(true);
    expect(can('WriteReferenceData', ['Viewer', 'Cashier'])).toBe(false);
    expect(can('WriteReferenceData', ['Viewer', 'Supervisor'])).toBe(true);
    expect(can('Decide', [])).toBe(false);
  });
});
