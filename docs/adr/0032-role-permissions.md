# ADR 0032: Role permissions

- **Status:** Accepted. The owner approved the plan of Prompt 5.3 on 2026-11-21 with the recommended answers; questions 1
  to 6 of that plan are points 2 to 7 below, and questions 7 and 8, on prefixes, belong to Prompt 5.3b. The built note
  at the end records what the build found.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0031 point 7 (deny by default), ADR 0023 (the checker).
- **Implements:** the permission matrix of `docs/requirements.md` section 1, BR-603; US-43 (a Viewer reads the audit
  trail).

## Context

Since Prompt 5.2 every endpoint but the sign-in and the health checks needs a signed-in user, and the token carries
the user's roles, but no endpoint asks for one. The matrix says which role may do what; BR-603 adds that a checker
holds Supervisor or TenantAdmin, "enforced server-side and cannot be bypassed by role". Prompt 5.3 was split with the
plan: this ADR is the permissions, Prompt 5.3b the tenant settings.

## Decision

1. **Named policies, one per capability of the matrix**, each admitting the roles below. A user with several roles may
   do what any of them may.

   | Policy | Roles | Endpoints |
   |---|---|---|
   | `ManageTenant` | TenantAdmin | the users |
   | `ReadReferenceData` | all five | read patients, payers, service items |
   | `WriteReferenceData` | TenantAdmin, Supervisor, BillingClerk | change them |
   | `ReadBills` | all five | read bills |
   | `WriteBills` | TenantAdmin, Supervisor, BillingClerk | draft, edit, void, post |
   | `ReadCreditNotes` | all five | read credit notes |
   | `ReadAdjustments` | TenantAdmin, Supervisor, BillingClerk, Viewer | read adjustments |
   | `DraftCreditNotesAndAdjustments` | TenantAdmin, Supervisor, BillingClerk | draft, edit, void, submit; apply a credit note's credit |
   | `ReadPayments` | all five | read payments and their allocations |
   | `RecordPayments` | TenantAdmin, Supervisor, BillingClerk, Cashier | record, allocate |
   | `ReadRefunds` | TenantAdmin, Supervisor, Cashier, Viewer | read refunds |
   | `DraftRefunds` | TenantAdmin, Supervisor, Cashier | draft, edit, void, submit |
   | `RequestReversals` | TenantAdmin, Supervisor, BillingClerk, Cashier | ask for the reversal of a payment, an allocation, an application of credit |
   | `Decide` | TenantAdmin, Supervisor | approve, reject, and the checker's inbox |
   | `ReadAudit` | all five | the audit trail |
   | `SignedIn` | any signed-in user | the service information |

2. **Every action names exactly one policy**, on the action itself and never on a controller: two attributes would
   combine as "both", and a reader would have to look in two places. The sign-in stays the one anonymous action. A
   request the policy refuses gets 403 with the code `forbidden` that a bare 403 already had. An architecture test
   checks that every action of the API carries one policy of the catalogue.
3. **A blank cell of the matrix is no access, not even reading**, with one exception: a Cashier reads credit notes,
   because a Cashier drafts refunds from a credit note's available credit (BR-307) and has to see it. So a Cashier
   neither reads nor drafts adjustments, a BillingClerk neither reads nor drafts refunds, and a Viewer asks for no
   reversal.
4. **Applying a credit note's credit to another bill** is in the credit notes' row: TenantAdmin, Supervisor and
   BillingClerk.
5. **The checker's inbox** (`GET /api/v1/approval-requests`) is for checkers: TenantAdmin and Supervisor (BR-605,
   "listed for checkers"). The pending-approvals report of BR-901 is a report, for everyone, when it is built.
6. **The audit trail is read by every role (US-43), except the trail of a user**, which only a TenantAdmin reads, since
   the users are the TenantAdmin's alone in the matrix: anyone else gets 403 `forbidden` for `entityType=User`, in any
   case and with any spaces around it. Prompt 5.3b adds the trail of the tenant's settings to the same rule.
7. **The checker's role is checked twice.** The `Decide` policy guards every approve and reject endpoint, and the
   decision itself checks it again: `ApprovalRequest.Approve` and `Reject` take the checker's roles and refuse a checker
   without Supervisor or TenantAdmin with 403 `approval_checker_role`, after the check that the checker is not the
   maker. So no use case can decide without asking, whatever an endpoint says. `ICurrentUser` gains `Roles`, read from
   the token like the user.
8. **The roles are those of the token**, which are the user's roles when they signed in; a change of roles applies at
   the next sign-in, within the 60 minutes ADR 0031 point 8 accepts.
9. **One table-driven test walks every endpoint with every role**: against the database, a client holding one role
   sends each request, and the test expects 403 `forbidden` exactly where its table says no. The table is the test's
   own copy of the matrix, written from `docs/requirements.md` and these points, not the catalogue of point 1, so a
   policy that admits the wrong role fails it; and it has to name every endpoint the API maps, so a new endpoint fails
   it until someone decides.
10. **Reports** have no endpoint yet. The matrix's row (everyone) applies when they arrive (Phase 6).

## Alternatives

- **The role checks in every use case.** Rejected: the matrix is about who may call what, which is the API's concern;
  the use cases keep the one rule about roles that is business, BR-603.
- **`RequireRole` written on each endpoint.** Rejected: a named capability makes the matrix readable in the code and
  changes in one place.
- **Every role reads everything; the blank cells restrict changes only.** Rejected with the plan: the matrix marks
  reading with R where it allows it.

## Consequences

- A test client that names no role is a TenantAdmin, who may do everything; a test that acts as another role now gets
  what that role may do, and a test whose role the matrix refuses changes its role (each named in the commit).
- The development user is a TenantAdmin, so Development is unchanged.
- A new endpoint needs a policy and a row in the matrix test before the build is green.

## Built (2026-11-21)

Built as decided, on .NET 10 (SDK 10.0.404); the database tests ran in a throwaway copy against SQL Server LocalDB
2019, as for every prompt since 4.4. What the build added, none of it a change of a decision:

- **`AddPermissions`** registers the policies and takes over the fallback policy from the authentication
  (`AddRequestIdentity`), so everything about what a user may do is in one place, `Api/Common/Authorization`.
- **A refusal is a bare 403**, which the status-code pages write as a ProblemDetails with the code `forbidden`, as they
  write the bare 401 `unauthorized`.
- **`ApprovalRequest` (Common) now names `Role` (Identity).** Both are cross-cutting modules, which the module rule lets
  depend on each other.
- **The trail of a user is recognised as the database compares the kind**: without regard to case or to the spaces
  around it, since SQL Server's comparison would find the records for `user` or `User ` too.
- **The matrix test also requires** that no admitted request answers 401 or a server error.
- **Existing tests:** a client that sets up what it then acts on names both roles (`"Cashier", "BillingClerk"`); a
  Viewer client that registered what it read became a BillingClerk; the tests that a maker cannot decide their own
  request act as the maker holding Supervisor, so they still reach BR-603's first rule, and assert the refusal of the
  maker's own role beside it; the users' test of the last administrator acts with a token from before the second
  user lost the role, which is the one way that guard can still be reached.

## Amended 2026-11-25 (Prompt 10.2c, ADR 0057)

Point 8: the roles are still those of the token, but a change of a user's roles now ends the user's sessions, so the
token with the old roles is refused at once and the next sign-in carries the new ones.

## Amended 2026-11-26 (Prompt 10.8, review finding R7): a trail is read by who may read its record

Point 6 let every role read the audit trail, with four kinds held back for the TenantAdmin. The independent review of
2026-11 (`docs/review-2026-11.md`, R7) showed what that gave away: a trail holds the record before and after each
change, whole, so a Cashier, whose role reads no adjustment, read any adjustment's amount, kind and reason through its
trail; a BillingClerk every refund; a Viewer the opening batches. The owner approved the fix on 2026-11-26.

1. **A trail is read by whoever may read its record.** `AuditTrailPolicies` (Api, Audit) names, for each kind of
   audited record, the read policy of its document; the one action of `AuditRecordsController` keeps `ReadAudit` as
   its policy (every role may ask) and then authorises against the kind's own, as it did for the four.
2. **What a checker decides is read by who may decide:** the trails of `ApprovalRequest` and `OpeningBatch` need
   `Decide`. The maker of a request no longer reads its trail; the state of the request and the checker's comment are
   on the document. The owner chose this.
3. **A kind that is not named has no reader** (403), where it had an empty trail (200). An architecture test holds the
   names to the aggregates of the domain, so a new audited record fails the build until its reader is decided.
4. `PrintSettings` joins the TenantAdmin's kinds.

Tests: `AuditTrailPolicyTests` (the map complete, its policies of the catalogue) and
`US43_the_trail_of_a_record_is_read_by_who_may_read_the_record_and_by_no_one_else` (26 pairs of a role and a kind, the
finding's three among them; an unknown kind; no kind). **One existing test changed with the behaviour:**
`BillApprovalApiTests.US22...` read an approval request's trail as its maker; the checker reads it now, and the maker
is asserted to be refused. No screen changed: each shows the trail of the record its reader may read.

Verified on the office machine: Architecture 141, the `Database` collection, 541, on LocalDB 2019 in a copy. SQL
Server 2022 is owed with the rest.

## Amended 2026-11-26 (Prompt 10.8, review finding R8): a waiting request is shown to who may read its document

The inbox of the checker (`GET /api/v1/approval-requests`) needs `Decide`. The pending-approvals report needed only
`Reports`, which all five roles hold, and named for each request its document, payer and amount: a Cashier listed the
waiting adjustments, a BillingClerk the waiting refunds, both blank cells of the matrix (`docs/review-2026-11.md`, R8).

1. **The report stays open to every role, and its rows are filtered.** A maker sees what waits of the documents they
   may read; closing the report to the deciders would have taken that from them. `PendingApprovalPolicies` (Api,
   Reporting) names the read policy of each kind of document a request can be for; who may decide is shown every kind.
2. **The count and the oldest are of what is shown:** a count of rows that are not shown would say that they exist.
3. A prefix change and an opening batch are shown to who may decide.
4. The panel of the dashboard takes the same filter. Its three roles (TenantAdmin, Supervisor, Viewer) read every kind
   today, so nothing changes there; it is filtered so that a role added to the dashboard later is not a new hole.
5. **Not changed:** the figure "pending approvals" of the dashboard summary and trend is a count of all requests, with
   no document named, for the same three roles.

Tests: `PendingApprovalPolicyTests` (every kind named, with a read policy or `Decide`), and one database test each in
`AdjustmentsApiTests` and `RefundsApiTests` (`US42_a_waiting_..._is_in_the_pending_approvals_of_who_may_read_it_and_of_no_other_role`).
The OpenAPI snapshot changed in two descriptions only. Verified on the office machine: the `Database` collection, 549,
on LocalDB 2019 in a copy; Architecture 143. SQL Server 2022 is owed with the rest.
