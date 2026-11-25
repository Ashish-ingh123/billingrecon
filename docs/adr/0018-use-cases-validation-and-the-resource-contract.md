# ADR 0018: Use cases, validation, the unit of work, and the HTTP contract of a resource

- **Status:** Accepted (owner's approval of the Prompt 2.2 plan, 2026-10-17). Written on the office machine and
  pre-checked on .NET 9, with the database tests run against SQL Server LocalDB 2019; the first run on .NET 10
  and SQL Server 2022 is still to come (see "Verification" under Consequences).
- **Date:** 2026-10-17
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0003 (how modules register), ADR 0007 (the ETag and If-Match contract, built here), ADR 0011
  (where `validation_failed` comes from).

## Context

Prompt 2.2 builds the first use cases: patients and payers, end to end. Everything after it copies what it does,
so the following had to be decided once:

- the shape of a use case, without a mediator (MediatR is banned, CLAUDE.md section 2);
- where request validation runs, how its failure looks to a client, and what stays a business rule;
- how a use case saves, and what happens when it loses a race to another request: two registrations of one MRN,
  two clerks correcting one patient;
- how handlers and validators reach the container without a package for scanning;
- the HTTP contract of a resource: the bodies, partial updates, the version a client must send back, paging, enums,
  unknown fields, and what another tenant's id answers.

The blueprint's Prompt 2.2 asks for handlers behind `ICommandHandler`/`IQueryHandler` registered by convention,
FluentValidation for the request shape and the domain for rules, request and response DTOs with explicit mapping,
and 201, 400, 404 and 409 on a duplicate MRN. Its Part G calls ETag and If-Match optional in the MVP; CLAUDE.md
section 4, rule 6, requires them, and CLAUDE.md wins.

## Decision

### Use cases

- **Two contracts** in `Application/Common`: `ICommandHandler<TCommand, TResult>` and `IQueryHandler<TQuery, TResult>`,
  each with one method, `HandleAsync(message, ct)`, returning `Task<Result<TResult>>`. An expected failure is a failed
  result; an exception is a bug or a fault (ADR 0011).
- **One public sealed class per command or query**, named after it (`RegisterPatientHandler`), in the module's
  Application namespace, with its ports injected through the constructor. No pipeline, no behaviours, no mediator:
  the controller asks the container for `ICommandHandler<RegisterPatientCommand, PatientDto>` and calls it.
- **Messages and results are records**: `RegisterPatientCommand`, `ListPatientsQuery`, `PatientDto`. Mapping is
  explicit (`ToDto`, `ToDetails`, the request's `ToCommand`).
- **A command handler** validates the command, loads the aggregate through a repository (tracked), compares the
  version the client read with the one it loaded, calls the aggregate, and commits once. **A query handler** reads
  through a read service, untracked, and validates the query when it has a shape to check (paging, filters).
- **Ports per module**: a repository loads by id and adds, nothing else (no remove, BR-005); a read service returns
  DTOs and pages, and answers the existence checks a command makes before it writes (`MrnExistsAsync`).

### Validation

- **FluentValidation 12.2.0**, in the Application layer only. A command or query that has a shape gets one public
  sealed validator; rules several of them share are internal validators that each includes (patient demographics,
  payer details, paging).
- **A validator checks the shape**: presence, lengths after trimming (so that it agrees with what the aggregate
  stores), formats (phone, e-mail, PIN code), ranges, enum values, and dates against `IClock.TodayIst`. **Business
  rules stay in the domain or the handler**: that only self-pay payers exist is the aggregate's rule (BR-103, 422),
  that an MRN is free is the handler's (BR-101, 409).
- **The handler calls the validator itself**, as its first statement (`validator.GetErrorAsync(command, ct)`).
  `UseCaseConventionTests` fails when a handler does not take the validator of its message although one exists, or
  takes one that does not exist.
- **A failure is `validation_failed`** (`CommonErrors`), status 400, with messages per field, the field names written
  as the JSON names them (`fullName`, `pageSize`, `lines[0].unitPrice`). It is the code model binding already uses for
  a body it cannot read, so a client handles every 400 with field errors the same way.
- **The domain keeps its own guards** and throws when a value breaks a limit: the validator has refused such a value
  first, so reaching the aggregate with it is a bug.

### The unit of work

- **One `SaveChanges` per use case** (BR-007), through `IUnitOfWork.CommitAsync`, which returns a `Result`.
- **Two lost races are expected outcomes**: a row changed since it was read is `concurrency_conflict` (409, BR-804),
  and a row a unique index refused (SQL Server 2601 or 2627) is `duplicate_key` (409). Everything else throws.
- **Check, then let the index decide.** A handler checks a uniqueness rule before it writes (`MrnExistsAsync`), which
  answers the usual case; the unique index decides the race between two requests that both passed the check, and the
  handler replaces `duplicate_key` with the error of the rule (`patient_mrn_duplicate`, `patient_already_linked`).
  Both paths give the client the same answer.
- The message of SQL Server's duplicate-key error names the key, which can be an MRN; it is not passed on, and Entity
  Framework's own error events are logged at Debug (ADR 0015, built note of 2026-10-17).

### Registration

- **`AddApplication`** (`Api/Common`) scans the Application assembly: every public class that implements one of the two
  handler contracts is registered under it, every public validator under `IValidator<T>`, all scoped. No package is
  needed for that.
- **Each business module registers its own ports** (`AddPayers` in `Infrastructure/Payers`), called from `Program.cs`:
  `AddInfrastructure` lives in Common, which may not name a business module (ADR 0003).
- `UseCaseRegistrationTests` asks the API's container for every handler and validator in a request scope.

### The HTTP contract of a resource

Written out in `docs/api.md`. In short:

- **Routes** under `/api/v1/<resources>`; a state change is an action (`POST /{id}/deactivate`, `/reactivate`), never a
  status field in a body; there is no DELETE (BR-005).
- **Bodies.** A request is an Api record with explicit mapping into the command; a response is the Application DTO, so
  there is one contract per resource. A member the request record does not have is refused (400): a misspelt field
  cannot pass unnoticed, and a field that cannot be changed (the MRN, the status, a payer's kind) cannot be sent.
- **Enums travel by name**, both ways; a number is refused, and validators also refuse a name the enum does not define.
- **Partial updates** (owner's decision, 2026-10-17): `PATCH` sends every editable field, and an optional field left out
  is cleared. Merge semantics would need to tell an absent member from a null one.
- **Versions** (BR-804, ADR 0007): `GET`, the `201` of a create and the `200` of a change carry the row version as a
  strong ETag, the quoted base64 of the eight bytes, and the body carries the same value as `version`. Every change of
  an existing record needs `If-Match` with that ETag: without it 428 `precondition_required`, with anything but one
  strong ETag of eight bytes 400 `validation_failed` (field `If-Match`), with a stale one 409 `concurrency_conflict`.
  The handler compares the versions before it changes anything, and the UPDATE checks the row version again.
- **Paging**: `page` from 1 (at most 1,000,000), `pageSize` 1 to 100 (default 20), an envelope
  `{ items, page, pageSize, totalCount }`, and a stable order (by name, then by id).
- **Another tenant's record answers 404**, with the same code and message as an id nobody has. 403 is for a known user
  who lacks a permission in their own tenant (the Identity slice).

## Alternatives

- **MediatR, or pipeline behaviours of our own.** MediatR is banned. A decorator that validates every command is
  possible without it, but it hides the most important line of a handler; the explicit call plus a convention test
  gives the same guarantee and stays readable.
- **Validation in the API** (controllers or a filter). CLAUDE.md puts the validators in the Application layer, and the
  command, not the HTTP request, is the contract that later callers (imports, jobs) will use.
- **FluentValidation.DependencyInjectionExtensions or Scrutor** for registration. Each is a package to approve and keep
  current for what a twenty-line scan does.
- **JSON Merge Patch (RFC 7396) or JSON Patch (RFC 6902).** Merge Patch needs every optional field to tell "absent"
  from "null"; JSON Patch is an operation language clients get wrong. Sending the editable fields whole is explicit and
  easy to validate.
- **Optional If-Match**, as the blueprint allows in the MVP. A guard that clients may skip protects nothing (ADR 0007).
- **403 for another tenant's id.** It tells the caller that the record exists, which is information about another
  hospital (BR-002).
- **Catching `DbUpdateException` in each handler.** The Application layer does not know Entity Framework; the unit of
  work translates the two expected cases once.
- **A search that matches the beginning of an MRN.** The MRN is a value object stored through a converter, and Entity
  Framework does not translate a `LIKE` on a converted value; the search matches a whole MRN, or part of a name.

## Consequences

- A new use case is a message record, a validator when it has a shape, and a handler. Registration is automatic, and
  the convention tests catch a missing handler, a duplicate one, or a forgotten validator.
- Every endpoint that changes an existing record has three more outcomes (428, 400 for If-Match, 409), each tested.
- **Deviation from ADR 0007**: a 409 does not carry the current ETag. The client re-reads, as BR-804 says.
- **Creating a guarantor is not idempotent**: a client that retries `POST /payers` without a patient after a timeout can
  create a second guarantor. BR-803 covers financial documents only; the duplicate is deactivated by hand.
- Domain events are raised and not dispatched yet; the dispatch comes with their first subscriber, the audit writer.
- There are no role checks yet; they come with the Identity slice (CLAUDE.md section 9, review finding 2.3).
- **Verification.** Pre-checked on 2026-10-17 in the throwaway .NET 9 copy: the build, the format gate and every test
  outside the database collection pass, apart from the eleven that differ on .NET 9 by design; the database collection,
  pointed at LocalDB 2019 in the copy only, passes in full (89 tests), with a migration scaffolded there by EF Core 9.
  Still to do on the machine with .NET 10 and Docker: scaffold `0002_Payers` with EF Core 10, build, and run every test.

## Amended 2026-11-25 (Prompt 10.2b)

On the owner's request for a search that answers while it is typed, with the plan approved that day:

- **Every search matches a part of the text**, as `LIKE '%text%'`, without regard to case. Two did not: the MRN of a
  patient (the whole MRN only, the alternative rejected above) and the e-mail filter of the users (its beginning only).
  Both now match a part.
- **The MRN is compared through its converter by a cast**, as the service item code has been since Prompt 2.3:
  `EF.Functions.Like((string)(object)patient.Mrn, pattern, escape)`. The cast names the column, so SQL Server compares
  the stored text; no second column, no migration. The sentence above, that Entity Framework does not translate a
  `LIKE` on a converted value, holds for `Contains` and not for this form.
- **The search is data, never a pattern.** `LikePattern.Containing` (Infrastructure, `Common/Persistence`) puts the
  escape character before `\`, `%`, `_` and `[`; the patient, user and service item read services use it. The payer
  search and the outstanding report keep `Contains`, which Entity Framework escapes the same way.
- **Cost, accepted.** A pattern that begins with `%` cannot seek on an index: each search reads the tenant's rows of
  the table, once for the count and once for the page. For patients nothing changes in practice, because the name was
  already searched in part; for users the range seek on `UX_Users_TenantId_NormalizedEmail` becomes a scan of one
  hospital's users. Not measured. A hospital whose patient list makes this slow needs a full-text or trigram index,
  which is a later decision.
- **The API accepts a search of one character**, as before; the two-character minimum is the screen's (ADR 0039,
  amended the same day).
- **Tests** (`Database` collection): a part of an MRN and a part of an e-mail found; `%`, `_` and `[` taken as text in
  an MRN, `%` and `_` in an e-mail. The assertion "an MRN is found whole, not by a part" of `PatientsApiTests` is
  reversed with the rule.
