# ADR 0069: The Accountant role

- **Status:** Accepted as design on 2026-11-28 (Prompt 13.0, approved "as recommended"). Built by Prompt 13.1, which
  adds the role, its seeding and the matrix tests, and its built note.
- **Date:** 2026-11-28
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0030 (roles), ADR 0032 (how the matrix is applied); `docs/requirements.md` section 1

## Context

Reconciliation is done by the hospital's accountant, often from an outside firm. R1's roles do not fit: a Supervisor
approves, posts bills and decides every maker-checker request; a Viewer changes nothing; a Cashier records payments at
the counter. Giving the accountant Supervisor would let them approve their own suspense requests' neighbours and post
bills.

## Decision

1. **A sixth role, `Accountant`.** It imports statements and settlements, runs and makes matches, excludes lines,
   records forgotten payments and suspense from statement lines, and asks for suspense to be applied or returned
   (maker). It reads every document, report, the dashboard and the day-end report, and makes the accountant's export
   (BR-904).
2. **It approves nothing** (BR-603 still names Supervisor and TenantAdmin), posts no bill, credit note, adjustment or
   refund, opens no cashier shift, and sets up nothing (channels are a TenantAdmin's).
3. **No second step required** (BR-608 lists TenantAdmin and Supervisor): an Accountant may set one up and is then
   asked for it. Recorded as a question for the security review of R5.
4. The matrix of `docs/requirements.md` gains an Accountant column; `PermissionMatrixApiTests` gains its rows; the
   seeding of roles and the users screen offer it.

## Consequences

- A hospital can give its outside accountant exactly the work of the morning and the export.
- Every endpoint's policy has to say whether the Accountant is in it; the matrix test fails for any endpoint that is
  not listed, as today.

## Alternatives considered

- **No new role; Supervisors and TenantAdmins reconcile:** nothing to build, but the accountant gets the power to post
  bills and to approve. Rejected.
- **A permission per action instead of roles:** flexible, a large change to Identity for one role. Rejected for R2.
