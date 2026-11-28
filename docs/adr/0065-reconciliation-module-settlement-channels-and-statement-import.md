# ADR 0065: The Reconciliation module, settlement channels and the import of a statement

- **Status:** Accepted as design on 2026-11-28 (Prompt 13.0; the owner approved the R2 contract "as recommended").
  Built by Prompt 13.1, which adds its built note.
- **Date:** 2026-11-28
- **Deciders:** project owner, Claude Code
- **Rules:** BR-1001 to BR-1005, BR-1024 (`docs/requirements.md` section 7)

## Context

R2 compares what reached the hospital's bank and gateway accounts with what BillingRecon recorded. The bank's side
comes as a file: every Indian bank's net banking exports a statement as CSV or Excel, each with its own columns, date
format and way of writing a debit. The gateways (Razorpay, PayU, Paytm, PhonePe) publish a settlement report. Banks
and gateways also offer APIs, but each needs an agreement, credentials and a secret on our server.

No existing module owns this. Payments owns the payments; Receivables owns the ledger; neither should know a bank's
columns.

## Decision

1. **A new module, `Reconciliation`**, a folder and namespace in every project (ADR 0003). It depends on `Common` and
   on the public Application contracts of Payments (payments, refunds and shifts, read; recording a payment) and
   Receivables (`IReceivablesLedger`). It never names another module's entities, so **no exception to the module
   rule** is needed. `ModuleDependencyTests` lists it with the business modules; Reporting reads its tables as it
   reads every module (ADR 0036).
2. **Settlement channels.** A channel is a bank account (`Bank`) or a gateway account (`Gateway`) of the hospital. It
   keeps a name and a label of at most the last four digits, never a full account number. A gateway channel names the
   bank channel it settles into. A TenantAdmin sets them up, under Users and settings.
3. **Files only, through a column mapping.** R2 reads any bank's CSV with a mapping saved per channel (the columns of
   date, value date, description, reference, a signed amount or a debit and a credit column, balance; the date format;
   the decimal style), not named banks' formats. An Excel file is saved as CSV first. No bank or gateway API: no outside
   service, no credential, no cost.
4. **Dry run, then import** (the pattern of ADR 0053): the dry run reads the file and stores nothing; the import reads it
   again, refuses it whole if it differs from the dry run, and stores the accepted lines in one transaction. The import
   is an idempotent POST (ADR 0006). The file itself is not kept, only its SHA-256 and the lines read from it.
5. **A line is never imported twice.** Its fingerprint is a SHA-256 of the channel, date, direction, amount, reference,
   balance and description; with it an occurrence number, so that two identical lines of one file are both kept and a
   later file adds an identical line only beyond the count already stored. A unique index on (TenantId, ChannelId,
   Fingerprint, Occurrence) makes the database refuse a duplicate.
6. **A line keeps what was read.** Its fields and the row's raw text never change and are never deleted; only its
   status follows its matches. The row has a row version for that status.
7. **Limits:** 5 MB and 20,000 lines a file; the import of the largest file under 30 seconds.

## Consequences

- Any bank works on the day a TenantAdmin writes its mapping; a bank that changes its columns needs a new mapping, not
  a release.
- A mistake in a mapping is caught by the dry run, not after the import; a wrongly imported line cannot be deleted, it
  can only be excluded (BR-1012). The dry run is therefore the step the training notes stress.
- The raw text may name payers. It is never logged, and it is kept like the financial documents (8 years).

## Alternatives considered

- **Named banks' formats** (HDFC, ICICI, SBI, Axis): fewer steps for those banks, a release for every other bank.
  Rejected for R2; a named format can later be a stored mapping shipped with the product.
- **Bank and gateway APIs:** no file to download, but an agreement and a secret per hospital and per bank. Left for
  later, each as its own decision.
- **Matching inside Payments:** no new module, but Payments would learn bank columns and settlement reports. Rejected.
