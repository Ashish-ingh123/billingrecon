# ADR 0033: Tenant settings and prefix changes

- **Status:** Accepted. The owner approved the plan of Prompt 5.3 on 2026-11-21, whose questions 7 and 8 are points 4
  and 6 below, and the plan of Prompt 5.3b the same day with the four recommended answers, which are points 3, 5, 6 and
  9. The built note at the end records what the build found.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0008 (a series' prefix changes), ADR 0023 (the document type of an approval request), ADR 0032
  point 6 (the trails only a TenantAdmin reads).
- **Implements:** BR-607, BR-802, BR-604 (changing a number-series prefix), BR-701; US-03.

## Context

The tenant row holds the settings since Prompt 1.3 (the approval threshold, the back-date window, the default due days
and one default prefix per document type), and nothing could change them. BR-607 gives the settings to a TenantAdmin,
audited; BR-802 and BR-604 make a change of prefix a maker-checker action that affects only future numbers.

## Decision

1. **The three settings** are read with `GET /api/v1/settings` and changed with `PATCH /api/v1/settings` (If-Match, the
   tenant's version), by a TenantAdmin only. The threshold is empty (no bill needs approval) or above zero; the
   back-date window and the default due days are 0 to 365 days. As every `PATCH` of the API, the body is the new state:
   a threshold left out or null means none. The change is audited (`SettingsChanged`) and applies from the next request,
   since every use case reads the settings when it runs; a bill already waiting for approval keeps waiting.
2. **The prefixes** are shown by the same `GET` and are never changed by the `PATCH`: a prefix changes only through a
   prefix change.
3. **A prefix change is a document of its own** (`PrefixChange`, table `PrefixChanges`): a TenantAdmin asks for a new
   prefix of one document type (`POST /api/v1/settings/prefix-changes`), and the request waits for a checker. A
   Supervisor or TenantAdmin who is not the maker approves or rejects it (`.../{id}/approve`, `.../{id}/reject`,
   If-Match). The checkers read the requests (`GET`), so a TenantAdmin and a Supervisor may; the inbox of approval
   requests lists them. There is no withdrawal: a request made in error is rejected, as for every other document. One
   request waits at a time per document type.
4. **The approval changes the prefix where future numbers come from:** the tenant's default for the document type, and
   the series of that type for the current fiscal year (by today's date in India) if it exists. So the next number
   already carries the new prefix, and numbers already issued keep theirs. The series of earlier years keep their
   prefix; series of later years are created from the new default.
5. **The approval request's document type is `PrefixChange`**, the document it is about: the value `NumberSeries`,
   which no row ever used, is renamed, and its check constraint changes with migration `0019_PrefixChanges`.
6. **A prefix belongs to one document type for good.** A new prefix may not be the default of another type, nor any
   prefix a series of another type ever had, nor a prefix another type's change came from, went to, or waits to go to.
   So two documents of different kinds can never carry the same number text. The type's own earlier prefixes may come
   back. Asked when the change is requested and again when it is approved; a refusal is 409 `prefix_in_use`. A request
   for the prefix the type already has is 422 `prefix_unchanged`, and a second request while one waits is 409
   `prefix_change_pending`. A prefix is one to three characters `[A-Z0-9]`, trimmed and upper-cased first.
7. **No new raw SQL.** The series row is changed through the model and its row version. A number taken in the same
   instant as an approval changes that row too, so the approval answers 409 `concurrency_conflict` and the checker
   tries again.
8. **Everything is audited:** the settings change on the tenant, the request, its approval or rejection, the new
   default on the tenant (`PrefixChanged`) and the new prefix of the series (`PrefixChanged` on `NumberSeries`, which
   becomes an aggregate for it).
9. **The trails of the tenant, of its prefix changes and of its number series are the TenantAdmin's alone**, joining
   the trail of a user (ADR 0032 point 6).
10. **The database guards the rest:** a check that the tenant's five defaults differ, a check that the threshold is
    empty or above zero, the format checks of every prefix column, and a filtered unique index that lets one change
    wait per document type.

## Alternatives

- **Change only an existing series (BR-802 read literally), the defaults as plain settings.** Rejected with the plan:
  two ways to change one prefix, one of them without a second person.
- **Change only the defaults.** Rejected: the change would show only in the next fiscal year.
- **Prefixes distinct only from the other four current defaults.** Rejected with the plan: a credit note could take
  `BIL` in a year whose bills already used it.
- **Lock the series row with raw SQL during an approval.** Rejected: raw SQL is allowed in four named statements only
  (ADR 0021), and a rare 409 is an honest answer.

## Consequences

- Migration `0019_PrefixChanges`: the table, the renamed value in `CK_ApprovalRequests_DocumentType`, and two new
  checks on `Tenants`.
- The matrix test gains the six endpoints; the audit trail refuses the three new kinds to anyone but a TenantAdmin.
