# ADR 0023: Maker-checker: the approval request, where its decisions live, and what a waiting bill answers

- **Status:** Accepted. The owner decided the five open points on 2026-11-20 with the plan of Prompt 3.6. Written on
  the office machine and pre-checked on .NET 9, with the database tests run against SQL Server LocalDB 2019; the
  first run on .NET 10 and SQL Server 2022 is still to come.
- **Date:** 2026-11-20
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0003 (approval handlers live with their documents), ADR 0022 (the order of a posting; the
  temporary refusal of BR-207, which this ADR removes), ADR 0006 (which POSTs carry a key), ADR 0020 (two aggregates
  audited in one commit).
- **Implements:** CLAUDE.md section 4, rule 9, for its first case; BR-207, BR-603 to BR-606; US-22.

## Context

A bill whose total is at or above `Tenant.PostingApprovalThreshold` takes financial effect only when a second,
different user approves it (BR-207, BR-603). Since Prompt 3.5 such a bill could not be posted at all. The design
documents gave the aggregate (`ApprovalRequest`, Common) and the rule that the approval is the posting (BR-606), and
left open how the pieces meet over HTTP and what each party is told.

## Decision

1. **The maker submits by posting.** `POST /api/v1/bills/{id}/post` is the one request a clerk sends. Below the
   threshold, or when the tenant has none, it posts and answers 200, as before. At or above it, it answers
   **202 Accepted** with the bill in `PendingApproval`: no number, no ledger entry, no change of the payer's
   balance, and an `ApprovalRequest` with the current user as its maker, opened in the same commit. A client does
   not need to know the threshold; it reads the status code and the status of the bill.
2. **The checker decides through the document:** `POST /api/v1/bills/{id}/approve` and `.../reject`, both with
   `If-Match`. The handlers are `ApproveBillPostingHandler` and `RejectBillPostingHandler`, in Billing. Common, where
   the request lives, knows documents only by kind and id and must not call a business module (ADR 0003), so there
   is no `POST /approval-requests/{id}/approve`. The inbox is read-only: `GET /api/v1/approval-requests`, pending
   by default.
3. **The approval is the posting** (BR-606), in the six steps of ADR 0022: the version; every check, now including
   the request (pending, and the checker is not the maker); the number; the bill takes it; the ledger; one save for
   the bill and the request together. The checks of a posting are made again at that moment: the bill date against
   today (BR-205) and the payer (BR-105). A bill that waited too long cannot be approved; the checker rejects it and
   the maker corrects the date. The checker is recorded as the one who posted.
4. **The checker is never the maker**, for an approval and for a rejection: 403 `approval_checker_is_maker`, in the
   aggregate and again as a CHECK constraint (`CK_ApprovalRequests_CheckerIsNotMaker`). **That the checker holds
   Supervisor or TenantAdmin is not checked yet**: there are no users and no roles until Phase 5, and Prompt 5.3
   adds that check with the permission matrix. Until then any second user of the tenant can decide.
5. **A rejection needs a comment and makes the bill a draft again** (BR-605). The request is closed as `Rejected`
   and stays as history; the bill can be corrected, voided or posted once more, which opens a new request. At most
   one request of a document is pending, by a filtered unique index.
6. **What each state answers:**

   | The bill is | Edit, void, post | Approve, reject |
   |---|---|---|
   | a draft | allowed | 422 `bill_not_pending_approval` |
   | waiting for approval | 409 `bill_pending_approval` | allowed, for another user |
   | posted | 409 `bill_already_posted` | 409 `bill_already_posted` |
   | a voided draft | 422 `bill_not_draft` | 422 `bill_not_draft` |

   Two checkers at the same moment: one decision stands, the other gets 409 `concurrency_conflict` from the row
   version of the bill, and a number it may have taken is rolled back.
7. **A maker and a checker are people.** A scope without a user (system work) can neither submit a bill that needs
   approval nor decide one: 403 `approval_needs_user`.
8. **`approve` and `reject` both carry `[Idempotent]`.** The plan said only `approve` would. The convention test of
   ADR 0006 asks every POST of a financial controller for a key, and making an exception for one action of the bills
   controller would have meant weakening that test. A key on `reject` costs the client nothing.
9. **The events of the request are `ApprovalRequestOpened`, `ApprovalRequestApproved` and
   `ApprovalRequestRejected`**, not `ApprovalRequested`, `ApprovalGranted` and `ApprovalRejected` as
   `docs/domain-model.md` first had them: the audit trail takes its action from the name of the event without the
   name of the aggregate (ADR 0020), and the first names would have given `ed` and failed the naming test. The bill
   raises `BillSubmittedForApproval` and `BillReturnedToDraft`.
10. **The temporary refusal is gone.** `bill_requires_approval`, its check and its tests are removed, as ADR 0022
    said they would be.
11. **Migration `0010_Approvals`** creates `ApprovalRequests` and adds the filtered index of the bills that wait
    (`IX_Bills_TenantId_Status WHERE Status = 'PendingApproval'`). The index is configured in a class of its own,
    `BillApprovalConfiguration`, held back while the earlier migrations are scaffolded, like the columns of posting
    (ADR 0022). `CK_Bills_Status` lists `PendingApproval` from `0004_Bills` on, because it is generated from the
    enum.

## Alternatives

- **A separate `POST .../submit`.** The client would have to know the threshold, or ask first; and a bill below the
  threshold that was "submitted" would need a rule of its own.
- **Decide on the request** (`POST /approval-requests/{id}/approve`), with a dispatcher from document kind to the
  handler of its module. One endpoint for every document, at the price of Common calling business modules through a
  registry. Rejected with ADR 0003; a client that has the request also has the id and the kind of its document.
- **200 with the status `PendingApproval`** instead of 202. Rejected: the request did not do what its name says,
  and 202 lets a client tell without parsing the body.
- **Approve without re-checking the date and the payer.** The checker would post a bill that the same system would
  refuse to post directly a moment later.

## Consequences

- Credit notes, adjustments, refunds and reversals (Phase 4) follow the same shape: the maker's request to the
  document answers 202 and opens the request; `approve` and `reject` are actions of the document; the request
  itself is only ever read.
- An approval saves two aggregates in one commit and writes an audit record for each: the bill's `Posted` and the
  request's `Approved`, with the same instant, user and correlation id.
- Until Phase 5 the second person is any other user id. The API tests act as two users of one tenant through the
  test identity; a real supervisor arrives with roles.
- A changed expectation in existing tests, called out as the working agreement asks: the two tests of the threshold
  (`PostBillHandlerTests`, `BillPostingApiTests`) expected 422 `bill_requires_approval` and now expect the bill to
  wait (`PendingApproval`, 202).
- **Verification.** Pre-checked on 2026-11-20 in the throwaway .NET 9 copy: build, format gate, every unit and
  architecture test apart from the eleven that differ on .NET 9 by design, and the whole integration suite on
  LocalDB 2019, with `0003` to `0010` scaffolded there separately by EF Core 9: `0010` held `ApprovalRequests` and
  the index of waiting bills, and neither `0004` nor `0009` held that index. The approval and posting tests, with
  two checkers deciding at once, ran eight times in a row without a failure. Still to do on the machine with
  .NET 10 and Docker: everything in `docs/runbook.md` section 8.
