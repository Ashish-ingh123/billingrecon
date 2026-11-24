import { readFileSync } from 'node:fs';
import { expect, type Page } from '@playwright/test';

// What the browser tests of the screens share (Prompts 9.3 and 9.3b, ADR 0049): a session put in the tab, the API's answers
// given by the test (a request it did not expect is counted), the page's response carrying nginx's content security
// policy read from frontend/deploy/nginx.conf, and a probe of refusals and layout shifts. The records are as the API
// answers them.

export const policy = /set \$csp "([^"]+)";/.exec(
  readFileSync(new URL('../deploy/nginx.conf', import.meta.url), 'utf8'),
)?.[1];
export const screenshots = process.env.E2E_SCREENSHOTS === '1';

const base64 = (value: object) => Buffer.from(JSON.stringify(value)).toString('base64url');
export const userId = 'd0000000-0000-4000-8000-000000000002';

/** A session of the given roles, as the sign-in keeps it in the tab (the token is not checked: there is no API). */
export function sessionOf(roles: readonly string[]) {
  const token = `${base64({ alg: 'HS256', typ: 'JWT' })}.${base64({ sub: userId, tenant_id: 't1', role: roles, exp: Math.floor(Date.now() / 1000) + 3600 })}.signature`;
  return {
    accessToken: token,
    expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
    userId,
    tenantId: 'd0000000-0000-4000-8000-000000000001',
    credentialsChangeDue: false,
    roles,
    email: 'asha@cityhospital.org',
    hospital: 'city-hospital',
  };
}

export const session = sessionOf(['BillingClerk', 'Supervisor']);

export const patient = {
  id: 'a1000000-0000-4000-8000-000000000001',
  mrn: 'MRN-1001',
  fullName: 'Asha Verma',
  dateOfBirth: '1984-03-12',
  sex: 'Female',
  phone: null,
  email: null,
  addressLine1: null,
  addressLine2: null,
  city: 'Bengaluru',
  state: 'Karnataka',
  pinCode: null,
  status: 'Active',
  createdAtUtc: '2026-10-01T04:30:00+00:00',
  updatedAtUtc: '2026-10-01T04:30:00+00:00',
  version: 'AAAAAAAAB9E=',
};
export const payer = {
  id: 'b2000000-0000-4000-8000-000000000002',
  kind: 'SelfPay',
  name: 'Asha Verma',
  patientId: patient.id,
  phone: null,
  email: null,
  status: 'Active',
  createdAtUtc: '2026-10-01T04:31:00+00:00',
  updatedAtUtc: '2026-10-01T04:31:00+00:00',
  version: 'AAAAAAAAB9I=',
};
export const lines = [
  {
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
  },
  {
    id: 'd4000000-0000-4000-8000-000000000002',
    lineNo: 2,
    serviceItemId: null,
    description: 'Consultation, general medicine',
    quantity: 1,
    unitPrice: 1200,
    lineAmount: 1200,
    discountAmount: 0,
    taxRatePercent: 0,
    taxAmount: 0,
    lineTotal: 1200,
  },
];
export const draft = {
  id: 'e5000000-0000-4000-8000-000000000001',
  patientId: patient.id,
  payerId: payer.id,
  billDate: '2026-11-20',
  dueDate: '2026-12-05',
  encounterReference: 'OPD-7781',
  notes: null,
  status: 'Draft',
  number: null,
  fiscalYear: null,
  postedAtUtc: null,
  subTotal: 2200,
  discountTotal: 0.5,
  taxTotal: 49.98,
  total: 2249.48,
  balanceDue: 2249.48,
  voidedAtUtc: null,
  voidReason: null,
  lines,
  createdAtUtc: '2026-11-20T05:00:00+00:00',
  updatedAtUtc: '2026-11-20T05:00:00+00:00',
  version: 'AAAAAAAACAA=',
};
export const posted = {
  ...draft,
  id: 'e5000000-0000-4000-8000-000000000002',
  status: 'PartiallyPaid',
  number: 'BIL/26-27/000123',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-11-20T06:00:00+00:00',
  balanceDue: 949.48,
};
export const creditNote = {
  id: 'f9000000-0000-4000-8000-000000000001',
  billId: posted.id,
  payerId: payer.id,
  patientId: patient.id,
  creditNoteDate: '2026-11-22',
  reason: 'Test not done',
  status: 'Posted',
  number: 'CRN/26-27/000004',
  fiscalYear: '2026-27',
  postedAtUtc: '2026-11-22T06:00:00+00:00',
  subTotal: 300,
  discountTotal: 0,
  taxTotal: 0,
  total: 300,
  availableAmount: 0,
  voidedAtUtc: null,
  voidReason: null,
  lines: [{ ...lines[0], billLineId: lines[0]?.id, quantity: 1, lineAmount: 300, lineTotal: 300, taxAmount: 0 }],
  applications: [
    {
      id: 'f9100000-0000-4000-8000-000000000001',
      billId: posted.id,
      amount: 300,
      status: 'Active',
      allocatedAtUtc: '2026-11-22T06:00:00+00:00',
      allocatedByUserId: null,
      appliedAtPosting: true,
      reversedAtUtc: null,
      reversalReason: null,
    },
  ],
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T06:00:00+00:00',
  version: 'AAAAAAAACwA=',
};
export const adjustment = {
  id: 'fa000000-0000-4000-8000-000000000001',
  billId: posted.id,
  payerId: payer.id,
  adjustmentDate: '2026-11-22',
  kind: 'Discount',
  amount: 49.48,
  reason: 'Staff discount',
  status: 'PendingApproval',
  number: null,
  fiscalYear: null,
  postedAtUtc: null,
  voidedAtUtc: null,
  voidReason: null,
  createdAtUtc: '2026-11-22T05:00:00+00:00',
  updatedAtUtc: '2026-11-22T05:00:00+00:00',
  version: 'AAAAAAAADAA=',
};
export const trail = (entityId: string, actions: string[]) => ({
  items: actions.map((action, index) => ({
    id: index + 1,
    occurredAtUtc: `2026-11-2${String(index)}T05:00:00+00:00`,
    userId: index % 2 === 0 ? userId : 'e1000000-0000-4000-8000-000000000009',
    correlationId: `c0000000-0000-4000-8000-00000000000${String(index)}`,
    entityType: 'Bill',
    entityId,
    documentNumber: action === 'Posted' ? 'BIL/26-27/000123' : null,
    action,
    before: null,
    after: { Status: action },
    reason: action === 'Voided' ? 'Entered twice' : null,
  })),
  page: 1,
  pageSize: 20,
  totalCount: actions.length,
});
export const pageOf = (items: object[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length });

/** The API's answers, by method and path; anything else is a request the screens did not send before. */
export type Answers = Record<string, (url: URL) => object>;

type Probe = { violations: string[]; shift: number; moved: string[] };

export async function open(
  page: Page,
  path: string,
  theme: 'light' | 'dark',
  size: { width: number; height: number },
  answers: Answers,
  signedIn: ReturnType<typeof sessionOf> = session,
) {
  expect(policy, 'the policy of frontend/deploy/nginx.conf').toBeTruthy();
  const unexpected: string[] = [];
  await page.setViewportSize(size);
  await page.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url());
    const answer = answers[`${route.request().method()} ${url.pathname}`];
    if (answer === undefined) {
      unexpected.push(`${route.request().method()} ${url.pathname}`);
      await route.fulfill({ status: 503, json: { title: 'Not in the test', status: 503 } });
      return;
    }

    await route.fulfill({ json: answer(url), headers: { etag: '"AAAAAAAACAA="' } });
  });
  await page.route('**/*', async (route) => {
    if (route.request().resourceType() !== 'document') {
      await route.fallback();
      return;
    }

    const response = await route.fetch();
    await route.fulfill({ response, headers: { ...response.headers(), 'content-security-policy': policy ?? '' } });
  });
  await page.addInitScript(
    ([kept, chosen]) => {
      window.sessionStorage.setItem('billingrecon.session', kept);
      window.localStorage.setItem('billingrecon.theme', chosen);
      const probe: Probe = { violations: [], shift: 0, moved: [] };
      (window as unknown as { probe: Probe }).probe = probe;
      document.addEventListener('securitypolicyviolation', (event) => {
        probe.violations.push(`${event.violatedDirective}: ${event.blockedURI}`);
      });
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          const shift = entry as unknown as {
            value: number;
            startTime: number;
            sources?: { node?: Node; previousRect: DOMRectReadOnly; currentRect: DOMRectReadOnly }[];
          };
          probe.shift += shift.value;
          for (const source of shift.sources ?? []) {
            const node = source.node as Element | undefined;
            // What moved, by how much, and when: the failure names the cause.
            probe.moved.push(
              `${node?.nodeName ?? '?'}.${String(node?.className ?? '')} ${shift.value.toFixed(4)} at ${String(Math.round(shift.startTime))} ms, y ${String(Math.round(source.previousRect.y))}->${String(Math.round(source.currentRect.y))}, h ${String(Math.round(source.previousRect.height))}->${String(Math.round(source.currentRect.height))}`,
            );
          }
        }
      }).observe({ type: 'layout-shift', buffered: true });
    },
    [JSON.stringify(signedIn), theme] as const,
  );

  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(message.text());
  });
  page.on('pageerror', (error) => errors.push(error.message));
  await page.goto(path);

  return {
    errors,
    unexpected,
    settle: () =>
      page.waitForFunction(() =>
        document
          .getAnimations()
          .every(
            (animation) => animation.playState === 'finished' || animation.effect?.getTiming().iterations === Infinity,
          ),
      ),
    probe: () => page.evaluate(() => (window as unknown as { probe: Probe }).probe),
  };
}
