# ADR 0068: Gateway settlements and the GatewayCharges account

- **Status:** Accepted as design on 2026-11-28 (Prompt 13.0, approved "as recommended"). Built by Prompt 13.5, which
  adds its built note and the migration.
- **Date:** 2026-11-28
- **Deciders:** project owner, Claude Code
- **Rules:** BR-1019 to BR-1022
- **Amends:** ADR 0005 (a control account, one template, identities 9 and 15), ADR 0062 (a named `GatewayCharges`
  ledger)

## Context

A patient pays ₹1,000 by UPI through the hospital's gateway; the cashier records ₹1,000 (BR-301). The next day the
gateway transfers to the bank the day's payments less its fee and the GST on the fee, as one sum: say ₹48,312.40 for
fifty payments. No bank line equals any payment, and the difference is the gateway's charges, which are the
hospital's expense, not anything a payer owes.

## Decision

1. **The settlement report is read** like a statement (ADR 0065): dry run, import, never twice (a unique index on the
   gateway's settlement id per channel). Each item has its kind (`Payment`, `Refund`, `Chargeback`, `Adjustment`), the
   gateway's payment id, gross, fee, tax and net; a settlement whose items do not add up to the paisa is refused.
   Razorpay's settlement report first; another gateway is a new reader of the same model.
2. **Two levels of matching:** the items to BillingRecon's payments by the gateway's payment id (the payment's
   reference) and to its refunds, by the rules of ADR 0066; the settlement's net to the one bank line carrying its UTR,
   or its amount in the window.
3. **A new control account, `GatewayCharges`** (debit-normal, no subject). When the net is matched to its bank line, one
   gateway charges document (`GWC` series) posts `Dr GatewayCharges / Cr Cash` for the fees plus tax, dated the
   settlement's date. **No checker:** the figure is the gateway's own, and no payer's balance changes. While the
   charges stand, the settlement's match cannot be ended; R2 has no reversal of a charges document.
4. **Chargebacks, unknown refunds and adjustments are never posted by themselves.** They are exceptions until a user
   records the payment reversal or the refund through R1's maker-checker.
5. **Identities:** the trial balance (9) includes `GatewayCharges`; its balance equals the posted charges documents,
   each equal to its settlement's fees and tax (15). The export gains a named `GatewayCharges` ledger.

## Consequences

- The ledger's `Cash` ties to the bank: payments in, less refunds, less the gateway's charges.
- The accountant's package gets the charges as a voucher instead of a figure worked out by hand; the GST on the fee
  (input tax the hospital may claim) is inside the one ledger in R2, and its split is R5's GST work (B50).
- A settlement read in error cannot be undone in BillingRecon in R2; the training notes say the dry run is the check.

## Alternatives considered

- **Record the fees on the settlement only, post nothing:** the receivables ledger stays as it was, but its `Cash`
  never equals the bank and the accountant enters every fee by hand. Rejected by the owner's approval.
- **Post the fee as an `Adjustment` of each bill:** would reduce what patients owe by the gateway's fee, which is
  wrong. Rejected.
- **Post the charges at import, before the bank line is seen:** the bank might not have received the sum; matching
  first ties the posting to money that arrived. Rejected.
