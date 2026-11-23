# ADR 0033: Tenant settings and prefix changes

- **Status:** Accepted. The owner approved the plan of Prompt 5.3 on 2026-11-21, whose questions 7 and 8 are points 4
  and 6 below, and the plan of Prompt 5.3b the same day with the four recommended answers, which are points 3, 5, 6 and
  9. The built note at the end records what the build found. Its point 5 was decided by the owner on 2026-11-23 (option
  B, "approve as recommended"), which amends point 7 and the last alternative: see "Amended 2026-11-23" at the end.
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
   tries again. *Amended 2026-11-23:* the approval now holds the series first, under the lock a number takes, with
   one more statement in the allocator's file; a number in flight finishes first and the approval then changes the
   series without a 409 (see the end).
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
- **Lock the series row with raw SQL during an approval.** Rejected at first: raw SQL is allowed in four named
  statements only (ADR 0021), and a rare 409 is an honest answer. Adopted on 2026-11-23, once the build had shown that
  the 409 does not cover a series that does not exist yet (built note point 5, "Amended 2026-11-23").

## Consequences

- Migration `0019_PrefixChanges`: the table, the renamed value in `CK_ApprovalRequests_DocumentType`, and two new
  checks on `Tenants`.
- The matrix test gains the seven endpoints; the audit trail refuses the three new kinds to anyone but a TenantAdmin.

## Built 2026-11-21 (Prompt 5.3b)

Built as decided above. What the build settled beyond the text, and one finding:

1. **The posts of a prefix change take no `Idempotency-Key`.** Not in the plan. A prefix change is not a financial
   document (ADR 0006), so `PrefixChangesController` joins the controllers that the convention test declares
   non-financial, as the users did: a repeated request finds one waiting (409 `prefix_change_pending`, from the check
   or, in a race, from the filtered unique index), and a repeated decision sends a stale If-Match or finds the change
   decided.
2. **The request answers 201** with the change, its `Location` and its version, because it creates a document; the
   approval and the rejection answer 200 with the decided change. The approval request it opens has the action
   `ChangePrefix`.
3. **The change records the decision; the approval request makes it.** The methods of `PrefixChange` are
   `RecordApproval` and `RecordRejection`, called after `ApprovalRequest.Approve` or `Reject` with the checker's roles
   (ADR 0032). The convention test that finds every decision of an approval request (sixteen now) counts each once.
4. **The list is the newest first, then by id**, so two changes asked for in the same instant keep one order.
5. **Finding, open for the owner: point 7 holds for a series that exists, not for one that does not exist yet.** The
   approval reads the series of the current fiscal year and writes the tenant's new default only when it commits, a few
   milliseconds later (`ApprovePrefixChangeHandler`). When the first number of that type in that year is taken in
   between, the allocator creates the series from the default it still reads as the old one; the approval finds no
   series to change, commits the new default and answers 200. That year's series then keeps the old prefix, without a
   409 or an error, and the next year's starts from the new default. Numbers stay unique and gapless, and the old
   prefix still belongs to that type alone. It needs the first number of a type in a year and an approval in the same
   milliseconds. `docs/database.md` section 4.4 has the query that finds it; one more change of that type, or two to
   come back to the prefix asked for, repairs it. Closing it needs the approval to lock the range of the series (a
   locked read in the allocator's file), which is new raw SQL, the alternative rejected above; so it is not built, and
   the owner decides. *Closed on 2026-11-23, see below.*

**Verification.** On .NET 10 (SDK 10.0.404): in the repository, the Release build and `dotnet format
--verify-no-changes` are clean, and Domain 625, Application 392, Architecture 121 and the 658 integration tests without
a database pass; in a throwaway copy, with migration `0019` scaffolded there by the repository's `dotnet-ef` and copied
back, the whole integration project passes on LocalDB 2019 (1114 tests), and the settings, matrix, bill approval,
migration and number series tests ran five more times without a failure. Still owed: the same on SQL Server 2022 in
Docker (`docs/runbook.md` section 8). The finding of point 5 was found by reading the code, not by a test.

## Amended 2026-11-23: the approval holds the series (built note point 5 closed)

The owner chose, of three options put in the plan (B: the approval locks the series range with the allocator's own locked
read; A: the approval creates the series and the unique key settles the race; C: leave it, with the query of
`docs/database.md` section 4.4), **option B**. Option A was not chosen: without also reordering the allocator's
statement, the approval (tenant row, then the series' place in the index) and a first number (the series' place, then
the tenant row) take their locks in opposite orders and can deadlock; and it leaves series with no number taken.

1. **`INumberSeries.HoldAsync(type, fiscalYear)`** runs the locked read of the allocator alone, `SELECT … FROM
   NumberSeries WITH (UPDLOCK, HOLDLOCK)` for the current tenant's series, in the transaction of the use case, which it
   opens if none is open. It holds the row when the series exists, and the gap in the unique index where it will go
   when it does not, until the commit or the rollback. It takes no number and writes nothing.
2. **`ApprovePrefixChangeHandler` holds this year's series of the type after every check** (the version, the pending
   state, the checker, the decision of the approval request, the prefix still free) and **before it reads the tenant
   and the series**. So an approval and a number take the series first and the tenant row second, in the same order,
   and neither waits for the other in a circle:
   - a number in flight (the series locked, the default read, the row not written yet) finishes first; the approval
     then finds the series it created and changes its prefix, and answers 200;
   - a first number asked for while the approval holds the series waits, and is then created from the new default.
   In neither case does that year's series keep the old prefix. A number taken in the same instant as the approval no
   longer makes it answer 409: it waits for the approval or the approval waits for it. The row version still guards
   the change.
3. **One more statement in `NumberSeriesAllocator.cs`**, the file that already may send SQL: `HoldSeries`, a constant
   with the same three parameters and the tenant in its WHERE clause, under its own suppression of RS0030. No new
   file is sanctioned. `BannedSymbolsTests` now pins two statements in that file, both constants, both locking the
   series of the current tenant by the same key (a changed expectation, called out). This amends ADR 0021 point 3
   ("each one constant statement") for that file, and ADR 0008 built note point 3.
4. **Like a first number, a hold of a series that does not exist yet locks a gap**, and may wait for a neighbouring
   series whose posting is in flight, or make that neighbour's first number wait (ADR 0008, built note): once per
   approval, for milliseconds.

**Tests.** Application: the hold comes after the check of the prefix and before the reads of the tenant and the
series, and an approval that is refused holds nothing. Database: a hold of a series that does not exist yet keeps a
first number waiting until the holder ends, which then takes the default of that moment; a hold of an existing series
keeps a taker waiting and takes no number; a hold never waits for another tenant's existing series and opens nothing
without a tenant or with bad arguments; an approval in the instant of the first number of the year, stopped by the
test between its read of the default and its insert, waits for it and then changes the series it created; and ten
rounds of a first bill and an approval at the same instant, each round on a fresh tenant, all end with both answers 200
and the series of the year carrying the new prefix. With the hold removed in a throwaway copy, the last two tests
fail: the first because the approval does not wait, the second because a series kept the old prefix, which shows the
finding of point 5 by a test as well.

**Verification.** On .NET 10 (SDK 10.0.404): in the repository, the Release build and `dotnet format
--verify-no-changes` are clean, and Domain 625, Application 394, Architecture 121 and the 658 integration tests
without a database pass; in a throwaway copy the whole integration project passes on LocalDB 2019 (1120 tests), and
the settings, number series, bill approval and bill posting tests (81) ran five more times without a failure. Still
owed: SQL Server 2022 in Docker.
