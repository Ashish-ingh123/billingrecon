# ADR 0010: Time handling

- **Status:** Accepted on 2026-11-24 with the release of R1, as built (the owner's decision with the Prompt 8.3 plan).
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

A payment recorded at 23:45 IST on 31 March belongs to fiscal year 2025-26 and to that day's
collection report. In UTC that instant is 18:15 on 31 March; a server in a container runs in UTC,
a developer's Windows laptop runs in IST, and a naive `DateTime.Now` gives different business dates
on each. The business date decides the fiscal year, the number series (ADR 0008), the due date and
later the ageing bucket (R3), so an off-by-one day is a numbering and reporting error, not a
cosmetic one.

India has a single time zone (UTC+05:30) with no daylight saving, which removes ambiguity but not
the need to be explicit. CLAUDE.md rule 8 bans `DateTime.Now`, `DateTime.UtcNow`,
`DateTimeOffset.Now` and `DateTime.Today` in production code and requires an `IClock`. Tests need to
freeze time to exercise year boundaries.

## Decision

We will separate **instants (UTC)** from **business dates (IST calendar dates)** and obtain both
only from an injected **`IClock`**.

- **`IClock`** (Application, `Common`):
  `DateTimeOffset UtcNow { get; }`, `DateOnly TodayIst { get; }` and
  `DateOnly ToIstDate(DateTimeOffset instant)`. `SystemClock` (Infrastructure) wraps
  `TimeProvider.System` and `TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")` with the Windows
  id `"India Standard Time"` as fallback. Tests use `FakeClock` with a settable instant.
- **One read per use case.** A handler reads `clock.UtcNow` once and uses that value for every
  stamp it writes (`PostedAtUtc`, `AllocatedAtUtc`, audit `OccurredAtUtc`), so all rows of one
  transaction carry the same instant.
- **Instants** are `DateTimeOffset` in code, always with offset zero, stored as `datetimeoffset(3)`
  in columns suffixed `…AtUtc` (BR-003). They are never converted to local time on the server; the
  front end converts for display.
- **Business dates** are `DateOnly` in code, stored as `date` in columns suffixed `…Date`. They are
  entered by users (bill date, payment date) or defaulted from `clock.TodayIst`, validated against
  `TodayIst` and `Tenant.MaxBackdateDays` (BR-205), and never derived from an instant except through
  `ToIstDate`. `FiscalYear.FromDate(DateOnly)` (BR-004) takes a business date, never an instant.
- **API representation.** Instants as ISO-8601 with `Z` (`2026-09-29T14:03:00.123Z`); business dates
  as `yyyy-MM-dd`. `System.Text.Json` handles `DateOnly` natively on .NET 8.
- **Enforcement.** An architecture test scans the `src/` source tree for the banned members and
  for `TimeZoneInfo.Local`; only `SystemClock` may use `TimeProvider.System`. (A
  `BannedApiAnalyzers` package would do this at compile time; adopting it needs the dependency
  approval in the working agreement and is left as an option.)
- **Server time zone is irrelevant.** Docker images and hosts may run in any zone; nothing reads it.

## Alternatives

- **Store local IST `datetime` everywhere.** Readable for Indian users, but the offset is implicit;
  the moment a UTC container or a non-Indian operator appears the data is ambiguous, and instants
  cannot be compared with external systems (payment gateways report UTC).
- **`datetime2` in UTC by convention.** Works, but the column type does not say it is UTC; a
  `datetimeoffset` value carries its offset and a CHECK could enforce zero. The 2 extra bytes are
  irrelevant.
- **Static `DateTime.UtcNow` calls.** Untestable (year-boundary tests would have to run on 31 March),
  and several reads within one transaction produce several instants.
- **`TimeProvider` injected directly, no `IClock`.** `TimeProvider` gives instants and timers but has
  no notion of a business date; every caller would repeat the IST conversion. `IClock` wraps it and
  adds `TodayIst`.
- **Business dates as `DateTime` at midnight.** The classic off-by-one: midnight IST becomes 18:30
  the previous day in UTC and the date shifts. `DateOnly` cannot be shifted.
- **Tenant-configurable time zone.** All tenants are Indian and India has one zone; a setting would
  be untested configuration. If a non-Indian tenant ever appears, `IClock` grows a zone parameter
  and this ADR is superseded.

## Consequences

- Every use case, validator and report receives `IClock`; it is the only time dependency, which
  makes fiscal-year and back-dating rules unit-testable with a fixed clock.
- Instants and dates cannot be mixed by accident: `DateTimeOffset` and `DateOnly` are different
  types, and the storage types differ.
- Daily reports (collection by method) are keyed by the business date on the document, not by the
  instant, so a payment entered after midnight UTC but before midnight IST lands on the right day.
- If India ever changes its time-zone rules, only `SystemClock`'s zone lookup changes; stored data
  is unaffected because instants are UTC and dates are already calendar dates.
