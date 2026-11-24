# ADR 0052: Onboarding and suspending a hospital by the operator's command

- **Status:** Accepted. The owner approved the plan of Prompt 10.2 on 2026-11-24: commands on the server, a suspension
  that also refuses tokens issued before it, and a `Demo` environment for the one-command run.
- **Date:** 2026-11-24
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0009 (tenancy), ADR 0020 (audit), ADR 0030, ADR 0033, ADR 0045 (the demo bootstrap), ADR 0051 (the
  forced password change). **Amends:** ADR 0045 point 1 (the demo now runs in the `Demo` environment only, besides
  Development).

## Context

R1 has no way to create a hospital but the demo's bootstrap and the development seeder, and none to suspend one but an
`UPDATE` by hand (runbook 16.1), after which the tokens already issued kept working for up to an hour. A live hospital
needs an onboarding that cannot be reached from the internet, leaves an audit trail, and gives its first administrator a
password nobody else keeps.

## Decision

1. **A command of the API's own binary, not an endpoint.** When its first argument is `tenant`, the API runs
   `tenant create`, `tenant suspend` or `tenant resume` with its own configuration and services, prints the outcome and
   exits (`TenantCommandLine`); with Compose, `docker compose run --rm api tenant ...`. It connects as the application's
   login. Exit codes: 0 done, 1 refused by a rule, 2 arguments not understood or the Demo environment.
2. **`tenant create`** makes, in one transaction: the tenant (`Tenant.Onboard`, event `TenantOnboarded`), its threshold,
   back-date window and due days (`TenantSettingsChanged`), its prefixes where they differ from the standard ones
   (`TenantPrefixChanged`; two types may swap theirs), and its first TenantAdmin (`UserCreated`) with a one-time password
   of 20 random characters from an alphabet without look-alikes, which is printed once and stored nowhere, and a password
   change due (ADR 0051). Everything is checked before anything is written (`tenant_invalid` with the option at fault);
   a code that exists is `tenant_code_taken`, also when another run makes it between the check and the save.
3. **Who it acts as.** `TenantOperations` makes a context for the one tenant it acts on with no user
   (`OperatorIdentity`), and commits through the unit of work, so every change is audited in the same transaction with
   no user and a correlation id of its own. Finding a tenant by its code is the one read across tenants, with
   `SystemIdentity`, which sees every tenant row and nothing tenant-scoped.
4. **Suspension.** `Tenant.Suspend` and `Tenant.Resume` change the existing `Status` (no migration), each with a
   required reason in its event (`TenantSuspended`, `TenantResumed`, `IHasReason`); a second suspension, or lifting an
   active hospital, is `tenant_status_unchanged`. Sign-in already refused a suspended hospital; now every request with a
   token is checked too (`SuspendedTenantGateMiddleware`, 403 `tenant_suspended`), from the tenant's own row through
   `ITenantStatusReader`, kept 30 seconds per instance (`TenantStatusCache`, in memory: no distributed cache). Nothing is
   deleted.
5. **The demo apart.** The one-command run's API runs in the environment `Demo`, Production in all but the name
   (`API_ENVIRONMENT`, default `Demo`). `Demo:Enabled` is refused at startup in any environment but `Demo` and
   `Development` (`DemoOptions.EnvironmentMessage`), and the tenant commands are refused in `Demo`.

## Consequences

- An operator onboards a hospital in one command and its administrator signs in; runbook section 17 is the procedure,
  and incident 16.1 suspends with the command instead of SQL and needs no change of the signing key.
- Every request with a token reads one row at most every 30 seconds per tenant and instance; a suspension takes up to 30
  seconds to reach a token issued before it.
- Changed expectations, called out: the demo's startup tests run in `Demo` (they ran in Production), and a Compose run now
  reports the environment `Demo`. The tests of hosts without a database take every tenant as active (`ActiveTenants`).
- There is no command for an administrator who loses the one-time password before using it with no other administrator
  to reset it; the runbook makes it an incident.

## Built note (2026-11-24)

Built on the office machine: 652 Domain, 431 Application, 132 Architecture and 775 Integration tests outside the
`Database` collection; the `Database` collection on LocalDB 2019 in a copy (runbook section 8, row 10.2, owes SQL Server
2022 and the commands through Compose).
