# ADR 0012: Structured logging, correlation id and PII masking

- **Status:** Accepted
- **Date:** 2026-10-03
- **Deciders:** project owner (approved the plan for the infrastructure step), Claude Code

## Context

The requirements ask for structured logs with TenantId, UserId and CorrelationId on every event
(`docs/requirements.md` §5, Observability), for a correlation id that is accepted from
`X-Correlation-Id` or generated, returned in the response and present in every log line and audit
record (BR-703), and for "no tenant data in logs beyond ids" (§5, Security). CLAUDE.md §2 names
Serilog. Patients and payers carry phone numbers and e-mail addresses (`docs/database.md` §3), and
sooner or later one of them is passed to a log call, inside an object or inside free text.

The audit tables store the correlation id as `uniqueidentifier`, so whatever arrives in the header
has to be a GUID before it can be used.

## Decision

We will log through **Serilog** with one pipeline that every event passes through.

**Sinks.**

- Console, always on, as compact JSON (one CLEF document per line), so container logs are
  machine-readable.
- Seq, when `Seq:ServerUrl` is set (`SeqOptions`, validated at startup). Empty turns the sink off.
  Local development points it at the Seq container in `docker-compose.yml`.
- The logger belongs to the host (`AddSerilog(..., preserveStaticLogger: true)`) and is flushed when
  the host stops. The static `Log.Logger` is not used, so several hosts in one process cannot close
  each other's logger. Because of that, the request-logging middleware is given the host's logger
  explicitly.
- Minimum levels come from the `Serilog` configuration section. That section is read in full, so
  configuration can also add sinks or enrichers. It cannot remove the console sink, the Seq sink or
  the mask, which are set in code, and whatever it adds still receives masked events because the
  mask runs before every sink.

**Correlation id.**

- `CorrelationIdMiddleware` is the first middleware. It accepts `X-Correlation-Id` only when it is
  exactly one canonical GUID (8-4-4-4-12) that is not all zeros; anything else is discarded and a
  new GUID is generated. Malformed input is never echoed or logged.
- The id is returned in the `X-Correlation-Id` response header (in canonical lower case, also on
  error responses), added to every ProblemDetails as `correlationId` (ADR 0011), and pushed to the
  log context so every event of the request carries `CorrelationId`.

**Request log.** One event per request (`UseRequestLogging`) with method, path, status and
duration. The query string is never logged. A successful `/health/*` probe is not logged; a failing
one is.

**Tenant and user.** `TenantId` and `UserId` are added to the events of an authenticated request,
read from the `tenant_id` and `sub` claims. Anonymous requests get neither. When the Identity slice
adds `ITenantContext` and `ICurrentUser`, the enrichment reads from them instead.

**PII-masking policy for phone numbers and e-mail addresses.** `PiiMaskingEnricher` runs last, in
front of every sink, and rewrites event properties at any depth (scalars, destructured objects,
sequences, dictionaries):

- An e-mail address inside any logged string keeps its first character and its domain:
  `priya.sharma@example.com` becomes `p***@example.com`.
- An Indian mobile number (ten digits starting 6–9, optionally split 5+5, optionally prefixed
  `+91`, `91`, `0091` or `0`) keeps its last four digits: `+91 98765 43210` becomes `******3210`.
- A property whose name contains `email`, `phone` or `mobile` is masked whatever it holds: a
  recognisable address or number keeps the partial shape above, anything else (a landline, a
  number logged as a number) becomes `***`. An object logged under such a name is treated the same
  way throughout: every text or number inside it is masked.
- Dictionary keys are logged text too, so an address or number used as a key is masked like any
  other string; a suffix keeps two keys apart when they mask to the same text.
- A GUID is never touched, on its own or inside a longer string such as a request path, so
  correlation, tenant, user, trace and entity ids stay searchable.

The policy does **not** cover two things, which are therefore rules for the code:

1. **Exception messages and stack traces.** They are rendered from the exception object, not from
   properties. Exceptions must never be given patient data.
2. **Text baked into a message template.** Always pass values as template arguments
   (`"Reminder sent to {Recipient}"`), never by string interpolation.

## Alternatives

- **Microsoft.Extensions.Logging console JSON only.** No package, but no Seq sink, no enrichment
  pipeline to hang the mask on, and scopes instead of first-class properties.
- **OpenTelemetry logs with an OTLP collector.** The direction for R5 (metrics and tracing), but it
  needs a collector and a backend the team does not run yet. Serilog can export OTLP later.
- **Trust the header and accept any string as correlation id.** Lets callers inject arbitrary text
  into every log line and would not fit the `uniqueidentifier` audit columns.
- **Mask by property name only.** Misses addresses inside free text and notes. **Mask by pattern
  only.** Misses landlines and anything oddly formatted under a field called `Phone`. Both together
  cover each other's gaps.
- **A generic "any long digit run" phone pattern.** Would also mask timestamps, amounts rendered as
  text and document numbers. The Indian mobile pattern plus the name rule is narrower and safer.
- **A third-party masking package (for example `Serilog.Enrichers.Sensitive`).** A dependency for
  about a hundred lines of code whose exact behaviour we need to own and test.
- **Never log objects, only ids.** The right habit, and still the guidance, but a policy that
  depends on every developer remembering it is not a control.

## Consequences

- Every log line of a request can be found by the id the client received, in the console and in
  Seq, and the same id will be written to audit records.
- Phone numbers and e-mail addresses are masked before they leave the process, including in data
  that enrichers add. The rules are unit-tested, including the cases that must *not* be masked.
- Masking costs a scan of every string property of every event. It uses source-generated regular
  expressions with a 250 ms timeout; a value that hits the timeout is logged as `***`.
- False negatives remain: landlines and foreign numbers in free text, names, addresses, MRNs
  without ten digits, and UPI ids of the form `name@bank` (no dot, so not an e-mail address; one
  that starts with a mobile number, `9876543210@ybl`, does have the number masked). The policy is a
  safety net for the two fields the requirement names, not a licence to log patient data.
- False positives remain too: any logged *string* that looks like a mobile number is masked, for
  example an MRN containing a ten-digit run or a payment reference of twelve digits starting `91`.
  Only the log line is affected; the database keeps the real value. Numbers logged as numbers
  (amounts) are never touched.
- A client-supplied correlation id is normalised to lower case, so the echoed header can differ in
  case from what was sent.
- Seq is a development and operations tool, not a system of record; audit lives in SQL Server.
- `Serilog.AspNetCore` follows the framework's major version: 8.0.x on .NET 8, 10.0.x with the
  .NET 10 upgrade.
