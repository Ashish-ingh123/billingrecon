# ADR 0004: Money and rounding

- **Status:** Proposed (accept on review of the R1 design documents)
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

A hospital bill has many lines (consultation, tests, pharmacy, room charges), each with a quantity,
a unit price that may carry four decimals (per-unit drug prices, per-minute charges), a discount
and a tax rate. Patients see and pay paise. The sum printed on the bill must equal the sum of the
printed lines, and the ledger must agree with both, to the paisa, forever (BR-001, BR-404).

Three technical facts shape the decision. `double`/`float` cannot represent ₹0.10 exactly, so sums
drift. .NET's `Math.Round(decimal)` defaults to `MidpointRounding.ToEven` (banker's rounding), which
turns ₹49.975 into ₹49.98 but ₹49.985 into ₹49.98, a result customers and auditors do not expect.
SQL Server's `ROUND` rounds half away from zero, so rounding in two places gives two answers.
Rupees have exactly two decimals; there is no sub-paisa unit.

R1 is INR only (`requirements.md` §2). GST rounding of *tax payable* to the nearest rupee (section
170, CGST Act) is a return-filing concern that arrives with GST reporting, not a bill-line concern.

## Decision

We will represent money as a **`Money` value object over `decimal` with scale 2, INR only**, and
round **once, at line level, half away from zero**.

- `Money` is a `readonly record struct` wrapping a `decimal` with exactly two decimals.
  `Money.Of(decimal)` rejects a value with more than two decimals (that is a bug, not a rounding
  request). `Money.Round(decimal raw)` is the only way to produce money from an arithmetic result
  and applies `Math.Round(raw, 2, MidpointRounding.AwayFromZero)`.
- `Money` has `+`, `-`, comparison, `Zero`, `IsZero`, `IsNegative`. It has **no `*` or `/`**: code
  multiplies raw decimals (`quantity.Value * unitPrice.Value`) and then calls `Money.Round`, so the
  rounding point is visible at the call site.
- Line arithmetic (BR-202) is implemented once, in `LineCalculator`:
  `LineAmount = Money.Round(Quantity × UnitPrice)`, `TaxableAmount = LineAmount − Discount`,
  `TaxAmount = Money.Round(TaxableAmount × Rate / 100)`, `LineTotal = TaxableAmount + TaxAmount`.
- Document totals are **sums of already-rounded line values** (BR-203) and are never rounded
  themselves. Allocation, adjustment and refund amounts are entered as `Money` and need no rounding.
- Storage: `decimal(18,2)` for every amount, `decimal(19,4)` for unit prices (`UnitPrice`),
  `decimal(9,3)` for quantities (`Quantity`), `decimal(5,2)` for percentages. EF Core configures
  precision and scale explicitly on every property; a test fails the build if a decimal property is
  left at the provider default.
- The additive identities (`LineTotal = LineAmount − Discount + Tax`, `Total = SubTotal − DiscountTotal +
  TaxTotal`) are also CHECK constraints. The multiplicative ones are not, because SQL `ROUND` and
  .NET rounding are not guaranteed to agree at every midpoint; the domain is the single implementation.
- Domain tests cover the midpoints: `49.975 → 49.98`, `49.985 → 49.99`, `-0.005 → -0.01`, and a
  1 000-line bill whose total equals the sum of its lines.

## Alternatives

- **`double` or `float`.** Binary floating point cannot hold decimal fractions exactly; sums of many
  lines drift by paise. Disqualified for a ledger.
- **Integer paise (`long`).** Exact and fast, but unit prices with four decimals and percentages need
  a second representation, every EF mapping and report needs conversion, and the readability of
  `decimal` in SQL is lost. `decimal` gives exactness with none of that.
- **`decimal` with the default `ToEven` rounding.** Statistically unbiased, but it contradicts what
  a patient computes by hand and what SQL Server does; auditors query the database directly.
- **Round at document level (sum unrounded lines, then round the total).** The printed lines would
  not add up to the printed total, and credit notes against single lines could not be reconciled.
- **`Money` with a currency code now.** Correct in general, but R1–R5 are INR only; a currency
  column on every amount table and a currency dimension on every comparison buys nothing today. If
  multi-currency ever arrives, a new ADR adds `Currency` to `Money` and a migration adds the column.
- **Round in SQL (computed columns).** Splits the rounding rule between two engines with different
  midpoint behaviour, and computed columns cannot be unit-tested in the Domain.

## Consequences

- Money arithmetic is exact, reproducible in SQL (`SUM` of stored values equals the stored total),
  and testable without a database.
- A line total may differ from the unrounded product by at most ₹0.005; this is by design and
  matches how the bill is printed.
- Developers must call `Money.Round` explicitly after any multiplication. Forgetting it is a
  compile error (`decimal` is not `Money`), not a silent drift.
- Multi-currency or sub-paisa precision would require a new ADR, a `Money` change and a migration.
- GST-specific rounding (tax payable to the nearest rupee, CGST/SGST split) is deferred with GST
  reporting and will be implemented on top of, not instead of, line-level rounding.
