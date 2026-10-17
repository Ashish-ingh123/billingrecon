# ADR 0011: Error model: Result for business failures, exceptions for faults, ProblemDetails on the wire

- **Status:** Accepted
- **Date:** 2026-10-03
- **Deciders:** project owner (approved the plan for the infrastructure step), Claude Code

## Context

CLAUDE.md §5 already states the rule: expected failures return `Result` / `Result<T>` with an
`Error { Code, Message, Kind }`, exceptions are for bugs and infrastructure faults, and API errors are
RFC 9457 ProblemDetails with an `errorCode` and a `traceId`. Until this step none of it existed in
code, and three things were unsettled:

- **Where `Result` lives.** CLAUDE.md §3 listed `Result<T>` under Application, but the domain model
  has aggregates returning `Result` (`docs/domain-model.md` §4) and Domain may reference nothing.
- **What an error response looks like when no use case produced it.** Unknown routes, model binding
  failures, wrong HTTP methods and unhandled exceptions are answered by the framework. Left alone
  they had no `errorCode`, and on .NET 8 the routing-level 404 had no `traceId` either.
- **Media type.** `[Produces("application/json")]` on `ApiControllerBase` forced every
  controller-produced ProblemDetails to `application/json` instead of `application/problem+json`.

A billing system needs the distinction to be sharp, because the two kinds of failure are handled by
different people. "This bill is already posted" is a fact the cashier's screen must show and the
client code must branch on. "The database connection dropped while posting" is nobody's business
rule: the client can only retry, and an operator needs the stack trace.

## Decision

We will keep the two channels strictly apart and give every error response one shape.

**Business failures are values.**

- `Error`, `ErrorKind`, `Result` and `Result<T>` live in `BillingRecon.Domain.Common`, so aggregates,
  use cases and controllers all use the same types. This supersedes the placement in CLAUDE.md §3.
- An `Error` has a stable snake_case `Code` (validated on construction; it is API contract), a
  human-readable `Message`, an `ErrorKind` and, for validation failures, messages per field.
- `ErrorKind` decides the HTTP status and nothing else does: `Validation` 400 (with `errors`),
  `NotFound` 404, `Forbidden` 403, `Conflict` 409, `BusinessRule` 422.
- A business failure is not logged as an error and is never thrown. Reading `Result.Value` on a
  failure or `Result.Error` on a success is a bug and throws `InvalidOperationException`.

**Faults are exceptions.**

- Bugs (a null that cannot be null, unbalanced ledger lines, an invalid error code) and
  infrastructure faults (SQL Server unreachable, timeout) throw. Nothing catches them to turn them
  into a `Result`.
- One `IExceptionHandler` (`GlobalExceptionHandler`) answers 500 with `errorCode = internal_error`
  and logs the exception exactly once, with the correlation id. The response carries no exception
  type, message or stack trace outside the Development environment; in Development an `exception`
  extension carries them.
- "Exactly once" needed one adjustment on .NET 8 and 9: the framework's exception-handler middleware
  logged the exception itself before it called the handler, so that single framework event was
  filtered out of the log pipeline. From .NET 10 the framework no longer logs an exception a handler
  took; the filter was removed on 2026-10-17 and the owner's test run on .NET 10 confirmed it. A test
  asserts one event.
- A request the client abandoned never reaches the handler: the framework answers 499 for it and
  nothing is logged as an error (also covered by a test).
- A request the server refuses to read (`BadHttpRequestException`: body too large, malformed,
  too slow) is the client's fault. The handler answers with the status the exception carries
  (400, 408 or 413) and logs it at Information, not as a 500 and not as an error.

**One response shape.**

- Every error is `application/problem+json` with `type`, `title`, `status`, and three extensions:
  `errorCode`, `traceId` and `correlationId` (ADR 0012). A single `CustomizeProblemDetails` callback
  adds them, so controllers, model binding, status-code pages and the exception handler all agree.
  Where the framework has no `type` and `title` for a status (413, 428, 429, ...), the callback
  supplies `about:blank` and the HTTP status phrase, as RFC 9457 prescribes.
- `ErrorProblemDetailsMapper` turns an `Error` into ProblemDetails (`detail` = message, `errorCode`
  = code, and for a validation error every field message, however many). Controllers call
  `Problem(error)` on `ApiControllerBase`.
- Errors the framework produces get a default code from the status: `validation_failed` or
  `bad_request` (400), `unauthorized`, `forbidden`, `not_found`, `method_not_allowed`,
  `not_acceptable`, `request_timeout`, `conflict`, `payload_too_large`, `unsupported_media_type`,
  `unprocessable_entity`, `precondition_required`, `too_many_requests`, `internal_error`,
  `service_unavailable`, otherwise `http_<status>`. The whole table is pinned by a test.
- A request body that cannot be read as JSON is answered with a generic message per JSON path
  (`AllowInputFormatterExceptionMessages` is off). The serializer's own messages name CLR types and
  line numbers, which are internals.
- The API answers with ProblemDetails even when the request's `Accept` header rules out JSON
  (`FallbackProblemDetailsWriter`); an error with an empty body is worse than an unrequested
  media type.
- `[Produces("application/json")]` is removed from `ApiControllerBase`. Response types are declared
  per action.

**Example from billing.** Posting bill `BIL/26-27/000123` a second time returns
`Result.Failure(Error.BusinessRule("bill_already_posted", "The bill is already posted."))`. The API
answers 422 with that code, nothing is logged as an error, and the client shows the message. If
instead SQL Server drops the connection while the posting transaction is open, the exception
propagates, the transaction rolls back (no number consumed, no ledger rows: BR-801, BR-401), the
handler answers 500 `internal_error` with the `correlationId`, and the log holds the stack trace
under that id. The client may retry with the same `Idempotency-Key` (ADR 0006).

## Alternatives

- **Exceptions for business failures (`BillAlreadyPostedException`).** Familiar, but it makes
  expected outcomes invisible in method signatures, turns control flow into `catch` blocks, floods
  the error log with non-errors and makes "500 means a defect" untrue.
- **`Result` in Application, with Domain methods throwing or returning bool.** Keeps Domain free of
  one more type, but aggregates then cannot say *why* an operation was refused without exceptions,
  and every handler would translate.
- **A library (`FluentResults`, `ErrorOr`, `OneOf`).** More features than needed and a dependency in
  the one project that has none. Four small types cover the rule.
- **One exception filter per controller, or `try`/`catch` in actions.** Duplicated and easy to forget;
  the built-in `IExceptionHandler` runs for the whole pipeline.
- **Honouring `Accept` strictly and returning 406 or an empty body for errors.** Correct by the
  letter of content negotiation, but it hides the reason for the failure from the caller.

## Consequences

- A method's signature now says whether it can fail for a business reason. Handlers and controllers
  have one way to report failure, and tests assert on codes instead of on exception types.
- A 500 always means a defect or an outage, so it can be alerted on. Each one is logged once and is
  findable by the `correlationId` in the response.
- Error codes are contract: renaming one is a breaking change. The catalogue of business codes will
  be listed in `docs/api.md` as use cases arrive; the framework-level defaults are listed above.
- Domain gains four types and stays free of references. `Error` clashes with a Visual Basic keyword,
  so analyzer rule CA1716 is switched off in `.editorconfig` (the solution is C# only).
- Callers must check `IsSuccess` before `Value`; forgetting is a runtime exception, not a compile
  error. If that bites, add `Match`/`Map` helpers in a later ADR.
- Exception messages reach the log unmasked (ADR 0012), so they must never contain patient data.
