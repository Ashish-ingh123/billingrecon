import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { inTurn, json, renderApp, routedApi, sessionFor } from '../test/app';
import {
  adjustment,
  allocation,
  bill,
  creditApplication,
  creditNote,
  pendingApproval,
  payment,
  problem,
  refund,
  reportPage,
} from '../test/records';
import type { PendingApproval } from './api';

const supervisor = sessionFor(['Supervisor']);
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const inbox = 'GET /api/v1/reports/pending-approvals';
const totals = (requests: PendingApproval[]) => ({
  requests: requests.length,
  oldestRequestedAtUtc: requests[0]?.requestedAtUtc ?? null,
});
const waiting =
  (...requests: PendingApproval[]) =>
  () =>
    json(200, reportPage(requests, totals(requests)));

const prefixChange = {
  id: 'fc000000-0000-4000-8000-000000000001',
  documentType: 'Bill',
  fromPrefix: 'BIL',
  toPrefix: 'INV',
  status: 'Pending',
  requestedAtUtc: '2026-11-22T05:00:00+00:00',
  requestedByUserId: 'd0000000-0000-4000-8000-000000000009',
  decidedAtUtc: null,
  version: 'AAAAAAAADQA=',
};
const later = creditApplication({
  id: 'f9100000-0000-4000-8000-000000000002',
  appliedAtPosting: false,
  status: 'PendingReversal',
});

/**
 * Each kind of request: what the inbox reads to decide it, where the approval goes, with which version, and whether it
 * carries a key (a prefix change does not, ADR 0033).
 */
const kinds: {
  kind: PendingApproval['documentType'];
  request: PendingApproval;
  read: [string, unknown];
  approve: string;
  version: string;
  keyed: boolean;
}[] = [
  {
    kind: 'Bill',
    request: pendingApproval(),
    read: [`GET /api/v1/bills/${bill().id}`, bill({ status: 'PendingApproval' })],
    approve: `/api/v1/bills/${bill().id}/approve`,
    version: bill().version,
    keyed: true,
  },
  {
    kind: 'CreditNote',
    request: pendingApproval({ documentType: 'CreditNote', documentId: creditNote().id }),
    read: [`GET /api/v1/credit-notes/${creditNote().id}`, creditNote({ status: 'PendingApproval' })],
    approve: `/api/v1/credit-notes/${creditNote().id}/approve`,
    version: creditNote().version,
    keyed: true,
  },
  {
    kind: 'Adjustment',
    request: pendingApproval({ documentType: 'Adjustment', documentId: adjustment().id, amount: 49.48 }),
    read: [`GET /api/v1/adjustments/${adjustment().id}`, adjustment({ status: 'PendingApproval' })],
    approve: `/api/v1/adjustments/${adjustment().id}/approve`,
    version: adjustment().version,
    keyed: true,
  },
  {
    kind: 'Refund',
    request: pendingApproval({ documentType: 'Refund', documentId: refund().id, amount: 200 }),
    read: [`GET /api/v1/refunds/${refund().id}`, refund({ status: 'PendingApproval' })],
    approve: `/api/v1/refunds/${refund().id}/approve`,
    version: refund().version,
    keyed: true,
  },
  {
    kind: 'Payment',
    request: pendingApproval({
      documentType: 'Payment',
      action: 'Reverse',
      documentId: payment().id,
      makerComment: 'Cheque dishonoured',
    }),
    read: [
      `GET /api/v1/payments/${payment().id}/allocations`,
      { payment: payment({ status: 'PendingReversal' }), allocations: [] },
    ],
    approve: `/api/v1/payments/${payment().id}/approve-reversal`,
    version: payment().version,
    keyed: true,
  },
  {
    kind: 'PaymentAllocation',
    request: pendingApproval({
      documentType: 'PaymentAllocation',
      action: 'Reverse',
      documentId: allocation().id,
      parentDocumentId: payment().id,
      makerComment: 'Wrong bill',
    }),
    read: [
      `GET /api/v1/payments/${payment().id}/allocations`,
      { payment: payment(), allocations: [allocation({ status: 'PendingReversal' })] },
    ],
    approve: `/api/v1/payments/${payment().id}/allocations/${allocation().id}/approve-reversal`,
    version: payment().version,
    keyed: true,
  },
  {
    kind: 'CreditNoteAllocation',
    request: pendingApproval({
      documentType: 'CreditNoteAllocation',
      action: 'Reverse',
      documentId: later.id,
      parentDocumentId: creditNote().id,
      makerComment: 'Applied to the wrong bill',
    }),
    read: [
      `GET /api/v1/credit-notes/${creditNote().id}`,
      creditNote({ status: 'Posted', number: 'CN/26-27/000001', applications: [creditApplication(), later] }),
    ],
    approve: `/api/v1/credit-notes/${creditNote().id}/applications/${later.id}/approve-reversal`,
    version: creditNote().version,
    keyed: true,
  },
  {
    kind: 'PrefixChange',
    request: pendingApproval({
      documentType: 'PrefixChange',
      action: 'ChangePrefix',
      documentId: prefixChange.id,
      payerId: null,
      payerName: null,
      amount: null,
      makerComment: 'New letterhead',
    }),
    read: [`GET /api/v1/settings/prefix-changes/${prefixChange.id}`, prefixChange],
    approve: `/api/v1/settings/prefix-changes/${prefixChange.id}/approve`,
    version: prefixChange.version,
    keyed: false,
  },
];

describe("the checker's inbox (BR-603 to BR-606)", () => {
  it("lists what waits with its payer, amount, maker and reason, and offers no review of the user's own request", async () => {
    const theirs = pendingApproval({
      documentType: 'Payment',
      action: 'Reverse',
      documentId: payment().id,
      makerComment: 'Cheque dishonoured',
    });
    const mine = pendingApproval({
      approvalRequestId: 'fb000000-0000-4000-8000-000000000002',
      makerUserId: supervisor.userId,
      waitingDays: 0,
    });
    const api = routedApi({ [inbox]: waiting(theirs, mine) });
    renderApp('/approvals', { session: supervisor, fetch: api.fetch });

    const rows = await screen.findAllByRole('row');
    const [, first, second] = rows;
    expect(within(first!).getByText(/^Reverse a payment/)).toBeInTheDocument();
    expect(within(first!).getByRole('link', { name: 'The payment' })).toHaveAttribute(
      'href',
      `/payments/${payment().id}`,
    );
    expect(within(first!).getByRole('link', { name: 'Asha Verma' })).toBeInTheDocument();
    expect(within(first!).getByText('1,049.48')).toBeInTheDocument();
    expect(within(first!).getByText('Cheque dishonoured')).toBeInTheDocument();
    expect(within(first!).getByText('d0000000')).toBeInTheDocument();
    expect(within(first!).getByRole('button', { name: 'Review' })).toBeInTheDocument();

    expect(within(second!).getByText('You')).toBeInTheDocument();
    expect(within(second!).getByText('Yours: waits for another checker')).toBeInTheDocument();
    expect(within(second!).queryByRole('button', { name: 'Review' })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Review' })).toHaveLength(1);
  });

  it.each(kinds)(
    'approves a $kind with the version it read and the key of its operation',
    async ({ request, read, approve, version, keyed }) => {
      const api = routedApi({
        [inbox]: inTurn(waiting(request), waiting()),
        [read[0]]: () => json(200, read[1]),
        [`POST ${approve}`]: () => json(200, read[1]),
      });
      const { user } = renderApp('/approvals', { session: supervisor, fetch: api.fetch });

      await user.click(await screen.findByRole('button', { name: 'Review' }));
      expect(await screen.findByText('State')).toBeInTheDocument();
      await user.click(screen.getByRole('button', { name: 'Approve' }));
      await user.click(screen.getByRole('button', { name: 'Confirm: approve' }));

      expect(await screen.findByText(/^Approved: /)).toBeInTheDocument();
      expect(await screen.findByText('Nothing waits for a decision.')).toBeInTheDocument();
      const sent = api.sent('POST', approve)[0];
      expect(sent?.headers.get('If-Match')).toBe(`"${version}"`);
      if (keyed) {
        expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
      } else {
        expect(sent?.headers.get('Idempotency-Key')).toBeNull();
      }

      expect(await sent?.json()).toEqual({ comment: null });
    },
  );

  it('rejects with a comment, which it asks for, and sends it', async () => {
    const api = routedApi({
      [inbox]: inTurn(waiting(pendingApproval()), waiting()),
      [`GET /api/v1/bills/${bill().id}`]: () => json(200, bill({ status: 'PendingApproval' })),
      [`POST /api/v1/bills/${bill().id}/reject`]: () => json(200, bill()),
    });
    const { user } = renderApp('/approvals', { session: supervisor, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Review' }));
    await user.click(await screen.findByRole('button', { name: 'Reject' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: reject' }));
    expect(screen.getByText('Give the reason.')).toBeInTheDocument();
    expect(api.sent('POST', `/api/v1/bills/${bill().id}/reject`)).toHaveLength(0);

    await user.type(screen.getByLabelText(/Why is it rejected/), 'Wrong payer');
    await user.click(screen.getByRole('button', { name: 'Confirm: reject' }));

    expect(await screen.findByText(/^Rejected: post a bill of Asha Verma/)).toBeInTheDocument();
    const sent = api.sent('POST', `/api/v1/bills/${bill().id}/reject`)[0];
    expect(await sent?.json()).toEqual({ comment: 'Wrong payer' });
    expect(sent?.headers.get('Idempotency-Key')).toMatch(uuid);
  });

  it('sends a comment with an approval when one is written', async () => {
    const api = routedApi({
      [inbox]: inTurn(waiting(pendingApproval()), waiting()),
      [`GET /api/v1/bills/${bill().id}`]: () => json(200, bill({ status: 'PendingApproval' })),
      [`POST /api/v1/bills/${bill().id}/approve`]: () => json(200, bill()),
    });
    const { user } = renderApp('/approvals', { session: supervisor, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Review' }));
    await user.click(await screen.findByRole('button', { name: 'Approve' }));
    await user.type(screen.getByLabelText('Comment (optional)'), '  Checked with the ward  ');
    await user.click(screen.getByRole('button', { name: 'Confirm: approve' }));

    await screen.findByText(/^Approved: /);
    expect(await api.sent('POST', `/api/v1/bills/${bill().id}/approve`)[0]?.json()).toEqual({
      comment: 'Checked with the ward',
    });
  });

  it('says so when someone else decided first, and shows the document as it is now', async () => {
    const api = routedApi({
      [inbox]: waiting(pendingApproval()),
      [`GET /api/v1/bills/${bill().id}`]: inTurn(
        () => json(200, bill({ status: 'PendingApproval' })),
        () => json(200, bill({ status: 'Posted', number: 'BIL/26-27/000001', version: 'AAAAAAAACAE=' })),
      ),
      [`POST /api/v1/bills/${bill().id}/approve`]: () =>
        json(409, problem(409, 'concurrency_conflict', 'The bill was changed by someone else.')),
    });
    const { user } = renderApp('/approvals', { session: supervisor, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Review' }));
    await user.click(await screen.findByRole('button', { name: 'Approve' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: approve' }));

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Someone else changed this before you could approve it.');
    expect(notice).toHaveTextContent('State: was Waiting for approval, now Posted');
    expect(screen.queryByText(/^Approved: /)).not.toBeInTheDocument();
  });

  it("shows the API's refusal of a decision by the request's maker", async () => {
    const api = routedApi({
      [inbox]: waiting(pendingApproval()),
      [`GET /api/v1/bills/${bill().id}`]: () => json(200, bill({ status: 'PendingApproval' })),
      [`POST /api/v1/bills/${bill().id}/approve`]: () =>
        json(403, problem(403, 'approval_checker_is_maker', 'The maker of a request cannot decide it.')),
    });
    const { user } = renderApp('/approvals', { session: supervisor, fetch: api.fetch });

    await user.click(await screen.findByRole('button', { name: 'Review' }));
    await user.click(await screen.findByRole('button', { name: 'Approve' }));
    await user.click(screen.getByRole('button', { name: 'Confirm: approve' }));

    expect(await screen.findByText('The maker of a request cannot decide it.')).toBeInTheDocument();
  });

  it('filters by what waits, and keeps the filter in the address', async () => {
    const api = routedApi({ [inbox]: waiting() });
    const { user, router } = renderApp('/approvals', { session: supervisor, fetch: api.fetch });

    await user.selectOptions(await screen.findByLabelText('Waiting'), 'Refunds');

    await waitFor(() => expect(router.state.location.search).toBe('?documentType=Refund'));
    await waitFor(() =>
      expect(api.sent('GET', '/api/v1/reports/pending-approvals').map((sent) => new URL(sent.url).search)).toContain(
        '?DocumentType=Refund&Page=1&PageSize=20',
      ),
    );
  });

  it('is open to the checkers only', async () => {
    const api = routedApi({});
    renderApp('/approvals', { session: sessionFor(['BillingClerk', 'Cashier']), fetch: api.fetch });

    expect(await screen.findByRole('heading', { name: 'No access' })).toBeInTheDocument();
    expect(api.requests).toHaveLength(0);
  });
});
