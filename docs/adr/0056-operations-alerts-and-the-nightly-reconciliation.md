# ADR 0056: Operations: the operator's alerts and the nightly reconciliation

- **Status:** Accepted. On 2026-11-27 the owner approved the plan of Prompt 10.6 "as recommended", with its six
  points: alerts by mail through the mail server of ADR 0061 (amended); an external uptime monitor the owner sets up,
  for which nothing is built; one small pinned `alpine` image for the watchdog (10.6b); Seq's logs kept 30 days and the
  audit trail never deleted by the application and kept at least eight years; the limits of a single instance recorded
  as accepted risks; and the split into 10.6a (this record: the alerts inside the application) and 10.6b (the watchdog
  on the server, Seq's retention and the on-call page, which will amend it), on the branch `phase-10-operations`.
- **Date:** 2026-11-27
- **Deciders:** project owner, Claude Code
- **Number:** kept for Prompt 10.6 since 2026-11-25 (ADR 0057 and later say so).
- **Amends:** ADR 0006 (a failed sweep is an alert), ADR 0005 (the reconciliation also runs on a schedule), ADR 0055
  (one more setting of the production compose file).
- **Builds on:** ADR 0061 amended (the mail server and its queue), ADR 0012 (the PII mask of the logs), ADR 0052
  (suspending a hospital, the first step of an incident).

## Context

The product's promise is that every cached balance equals the ledger. The reconciliation that proves it ran only when
someone opened its report and after every database test. A drift on a Tuesday night would be found when a user noticed
a wrong balance, by which time more postings sit on top of it. Nothing told an operator about a job that failed: the
sweep of idempotency keys logged an error into Seq, which nobody reads at night. Prompt 10.6 asks for the
reconciliation of every tenant every night with an alert on any drift, for alerting on health, errors, a full disk or
database and an expiring certificate, for the retention of the logs and the audit, and for the limits of one instance
to be recorded.

## Decision

1. **One way to tell the operator: `IOperationsAlerts`.** An alert is always an error in the log (Seq), and a mail to
   `Alerts:MailTo` through the mail server of a forgotten password (`Mail:*`) when both are set. The mail goes through
   the same in-memory queue and sender (`MailOutbox`, `MailDispatcher`). The API says at startup when its alerts will
   only be logged. An alert's text names hospitals by their code and documents by their number, never a patient, a
   payer or a sum that identifies one.
2. **The reconciliation of every hospital at 02:00 IST** (`NightlyReconciliation`, run by `NightlyReconciler`): the
   query of the report (`ReconciliationQuery`, ADR 0005), once for each hospital, suspended ones included, each in a
   context of its own as that hospital's operator, as the sweep does. A night with drift, or with a hospital that could
   not be reconciled, sends **one** alert naming each such hospital with up to 20 of its rows and the first step of the
   runbook (section 16.1); a clean night sends nothing and writes one line of information. A run that fails as a
   whole is an alert of its own. 02:00 is after the day's work and before the 01:30 UTC full backup.
3. **A failed sweep of idempotency keys is an alert** (ADR 0006 amended), with the manual procedure of the runbook.
4. **Retention.** Seq keeps the logs 30 days (set by 10.6b). The audit trail is never deleted or changed by the
   application (the login cannot, ADR 0034) and is kept at least eight years, the period of the books of account
   under the Companies Act, 2013, section 128: my reading, to be confirmed by the legal review (`docs/go-live.md`,
   G6). Backups keep 35 days (ADR 0055).
5. **One instance.** The sign-in limits, the mail queue and the two jobs live in the process. A second instance would
   halve the limits' effect, keep its own queue, and run each job twice (twice the reads and two mails; nothing
   changes twice: the sweep's deletes and the reconciliation's reads are safe to repeat). Recorded as accepted risks in
   `docs/security.md`; lifting them (a lease row in the database for the jobs, the limits in the database) is for the
   day a second instance is needed.
6. **What the application cannot see is watched from outside it** (10.6b): `/health/ready` through nginx, the error
   rate in Seq, the disk, the database files and the certificate, by a script on the server; and the server itself
   being down by an external monitor the owner sets up.

## Consequences

- Drift is known the morning after it happened, with the hospitals named, instead of when a user notices it.
- Alerts depend on the mail server. Without one they are in Seq only, which the deployment files say is not acceptable
  for production.
- The mail of an alert waits in memory: a restart in the second between the alert and its sending loses the mail; the
  error stays in the log.
- A night's reconciliation reads every ledger once. Its cost grows with the ledgers; at the size of the reference
  hospital it is seconds.

## Alternatives considered

- **Seq's own alerts** with a notification app installed in Seq: no code, but an app inside Seq to install and keep,
  and the alert of a drift would depend on a query of log text.
- **A chat webhook** (Slack, Teams): an account and a URL the owner does not have.
- **A table of reconciliation runs** to prove the schedule: the log line of each night proves it, and Seq keeps it 30
  days; no migration.
- **Reconciling active hospitals only:** a suspended hospital's ledger can still drift, and suspension is the first
  step of an incident; all are reconciled.

## Built note (2026-11-27, Prompt 10.6a)

- `IOperationsAlerts`, `OperationsAlerts` and `AlertOptions` in Infrastructure, Common/Operations; the nightly
  reconciliation in Infrastructure, Receivables, registered by `AddReceivables` (the module rule keeps Common from
  Receivables). `ALERT_MAIL_TO` in both compose files and both examples of the environment. No migration, no package,
  no endpoint.
- The expired idempotency key removed and the fresh one kept was already proven by `IdempotencyApiTests` (review fix
  R3); its schedule is not under test.
- Verified on .NET 10 with LocalDB 2019 in a throwaway copy, with the mail server replaced. **Not verified:** an
  alert reaching a real mailbox, and the job running at 02:00 on a server (runbook section 8, row 10.6a).

## Amended (2026-11-27, Prompt 10.6b): the watchdog, Seq's retention and on-call

- **The watchdog** is the service `watch` of `docker-compose.prod.yml`: `deploy/prod/watch.sh` in `alpine:3.20.3`,
  which adds `bash`, `curl` and `openssl` with `apk` at start. Every 5 minutes six checks: `/health/ready` through
  nginx (the frontend's fixed address, so the certificate's name is checked as a browser would), the errors in Seq of
  the last 15 minutes (20 or more), the disks of the database and its backups (85%), the transaction log (80%), the
  last verified full backup (26 hours), and the certificate (14 days). One mail a day per check, by `curl` to the same
  mail server; each round is a line per check in its log.
- **Two deviations from the plan, both narrower:**
  - The database's fullness is read by the backup loop, the one container with `sqlcmd`, which writes the use of the
    transaction log and the instant of the last verified full backup into a shared volume (`ops-state`); the
    watchdog reads them. The data files grow with the disk, so the disk is their limit; the log is what fills when log
    backups fail. The age of the last backup was not in the prompt and costs one line: it is the alert a failed backup
    needed (runbook 19.8 said so).
  - **Seq's retention is a step of the runbook** (section 20.1), not of `prepare.sql`, which is SQL and cannot set Seq.
- **A Seq API key with the Read permission** is a ninth secret file, `seq_api_key`; an empty file turns the count of
  errors off.
- **On-call** is runbook section 20.3: who is called, how fast, and the first steps of each alert. The external
  uptime monitor is section 20.2: the owner's account, nothing in the repository.
- Verified by `deploy/prod/watch.test.sh` (run by CI, and under Git Bash here): every check failing mails six alerts
  once, a second round mails none, a round where every check passes mails nothing, and without a mail server nothing
  is mailed. **Not verified:** the watchdog on a server, the Seq API's answer to the query of errors, and the backup
  loop's two files (runbook section 8, row 10.6b).
