# ADR 0026: Adjustments: no exemption from the second person, and the bill reached through Billing's contract

- **Status:** Accepted. The owner approved the plan of Prompt 4.4 on 2026-11-21 with the five recommended answers,
  which are points 1 to 5 below. Built and tested on .NET 10 (SDK 10.0.404, EF Core 10.0.12) in a throwaway copy on
  the office machine, with the database tests run against SQL Server LocalDB 2019; SQL Server 2022 in Docker is
  still to come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0003 (module boundaries), ADR 0022 (the order of a posting use case), ADR 0023 (maker-checker),
  ADR 0024 (the Billing contract and the one lock).
- **Implements:** BR-501 to BR-503, BR-604 for adjustments. **Changes a business rule:** BR-501 gains one sentence.
- **Resolves:** open question 1 of `docs/domain-model.md` section 10.

## Context

An adjustment (Prompt 4.4) lowers a posted bill by a discount, a write-off or a rounding difference, without money
and without a credit note. It is the first document of a module of its own that has to change a bill: credit notes
live in Billing, next to the bill, and payments only ever apply an amount. Open question 1 of the domain model asked
whether a rounding of less than one rupee should skip the second person, and the prompt said to raise it before any
code.

## Decision

1. **No exemption from maker-checker in R1.** Every adjustment waits for a checker, a rounding of a few paise
   included. CLAUDE.md section 4 rule 9 and BR-604 name the posting of an adjustment without an exception; an
   exemption would need a tenant setting, a change of BR-502 and BR-604, and an ADR of its own. Nothing built here
   stands in its way: a second path can be added beside the approval later.
2. **The cap against the balance due is asked twice** (BR-501 says "at posting"): when the maker submits, for an
   early 422 `adjustment_exceeds_balance_due`, and again when the checker approves, because the bill may have been
   paid or credited while the adjustment waited. At the approval the bill itself is the last word:
   `Bill.ApplyCredit` refuses more than is owed, and a bill changed by a request that committed in between fails the
   save or the ledger, 409 `concurrency_conflict`.
3. **The adjustment date is not before the bill date**, as BR-214 says for credit notes. BR-501 gains that sentence;
   BR-205 applies as before. 422 `adjustment_date_before_bill_date`.
4. **The payer is not asked whether it is active.** An adjustment takes no new money and lowers what is owed.
5. **A posted adjustment is not reversed.** No prompt asks for it and BR-503 says adjustments only reduce; a wrong
   one is corrected by raising a new bill. The table has no reversal column.
6. **Adjustments reaches the bill only through `IBillSettlement`**, the contract of Billing that payments use
   (ADR 0024), which gains one method: `FindPostedAsync(billId)` answers the payer, the bill date, the number and the
   balance due of a posted bill, in a record without any type of the Billing domain, or `bill_not_found` /
   `bill_not_posted`. `ApplyAsync` lowers the bill at the approval. ADR 0003 lists an exception
   `Application.Adjustments` to `Domain.Billing` for exactly this use case; **it is not used**, and can be taken out
   of the list when the owner agrees. The plan said the bill would be read through Billing's read service; that
   service answers a DTO that carries a Billing enum, which Adjustments may not name, hence the method here.
7. **The shape of ADR 0023 and ADR 0025:** `POST /adjustments/{id}/post` always answers 202 and opens the approval
   request; `approve` is the posting and `reject` returns it to draft. A draft is replaced as a whole with
   `PUT /adjustments/{id}`; it has one amount and no lines.
8. **The order of an approval** (the six steps of ADR 0022): the version; the adjustment waits and its date is
   still allowed; the checker is a user and not the maker; the cap against what the bill is owed now; the number, for
   the fiscal year of the adjustment date; the bill takes the amount; the posting Dr Adjustments / Cr Receivable(bill),
   which locks the payer's row of `PayerBalances`; one save for the adjustment, the bill and the request.
9. **The reconciliation gains three identities**: `AdjustmentWithoutPosting`, `PostingWithoutAdjustment` and
   `AdjustmentPayerMismatch`. What an adjustment took off a bill is already covered by the bill's balance against
   its receivable in the ledger.
10. **Migration `0014_Adjustments`** creates the one table; its foreign keys to bills, payers and ledger postings
    come from the typed-id convention (ADR 0021).

## Alternatives

- **Auto-approve a rounding under one rupee** (a tenant setting `AutoApproveAdjustmentsBelow`). Not in R1, by the
  owner's decision: point 1.
- **Use the listed exception and load the `Bill` in the Adjustments use case.** It would work and is allowed.
  Rejected: payments were given the contract instead of their exception (ADR 0024), and one way of lowering a bill
  from outside Billing is easier to reason about than two.
- **Check the cap only at posting**, as BR-501 reads literally. The maker would learn of a hopeless adjustment only
  from the checker.

## Consequences

- Settling a difference of forty paise takes two people. That is the cost of point 1, accepted.
- `IBillSettlement` is now the one way a module outside Billing reads or lowers a bill; refunds (4.6) do not touch
  bills and need nothing from it.
- An approval saves three aggregates in one commit and writes an audit record for each: `Posted` on the adjustment,
  with its reason, `CreditApplied` on the bill, `Approved` on the request.
- The README placeholders of the four Adjustments folders are gone.
- **Verification, for the first time on the real target.** On 2026-11-21 the office machine got the .NET 10 SDK
  (10.0.404). In a throwaway copy of the repository with **no version substitution**, the repository's own
  `dotnet-ef` 10.0.12 scaffolded `0003` to `0014` one by one, as `docs/runbook.md` section 8 B describes, and every
  migration held exactly what its row says (`0014` the `Adjustments` table alone). Strict build and format gate
  clean; Domain 560, Application 313, Architecture 113 and Integration 977 tests, **all green, none failing**: the
  eleven tests that failed in the .NET 9 copies pass here, as predicted. The only change in the copy was the database
  fixture, pointed at LocalDB 2019 because the machine has no Docker. The concurrency classes of runbook section 8 D
  (221 tests) ran six times: five clean, and once an older test failed, `BillApprovalApiTests.BR804_two_checkers_at_once...`
  of Prompt 3.6, which then passed 45 times in a row and whose message was not captured (CLAUDE.md section 9,
  item 23). Still to do: the same run on SQL Server 2022 in Docker, and the migrations brought into the repository.
