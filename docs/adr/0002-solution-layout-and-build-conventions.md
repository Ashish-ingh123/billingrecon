# ADR 0002: Solution layout and build conventions

- **Status:** Accepted
- **Date:** 2026-09-26
- **Deciders:** project owner (specified the layout), Claude Code (implemented it)

## Context

CLAUDE.md fixes the architecture as a modular monolith with four layers and nine modules, and
requires compiler strictness (nullable, warnings as errors), a single target framework, reproducible
builds and architecture tests. Those rules need a concrete solution shape that the compiler and the
test suite can enforce, rather than conventions people have to remember. The product is now named
**BillingRecon**, which fixes the root namespace.

## Decision

We will use one solution, `BillingRecon.sln`, with the following layout and build conventions.

**Projects.** Four source projects under `src/`, one per layer, named `BillingRecon.<Layer>`:
`Domain`, `Application`, `Infrastructure`, `Api`. Four test projects under `tests/`:
`BillingRecon.Domain.Tests`, `BillingRecon.Application.Tests`, `BillingRecon.IntegrationTests`
(in-process API host today; Testcontainers.MsSql and Respawn join when the first `DbContext` lands)
and `BillingRecon.ArchitectureTests` (NetArchTest).

**Dependency rule in project references.** `Domain` references nothing. `Application` references
`Domain`. `Infrastructure` references `Application` and `Domain`. `Api` references `Application` and
`Infrastructure`. The compiler therefore rejects any reference that breaks the rule; the architecture
tests additionally check type-level dependencies and module isolation.

**Modules as folders and namespaces.** Every source project contains the nine module folders
(`Common`, `Payers`, `Billing`, `Payments`, `Receivables`, `Adjustments`, `Reporting`, `Identity`,
`Audit`). Namespaces follow `BillingRecon.<Layer>.<Module>` and `.editorconfig` enforces
namespace-matches-folder (IDE0130) at build time. Each folder starts with a `README.md` placeholder
that states what belongs there.

**Shared build settings.** `Directory.Build.props` at the root is the only place that sets
`TargetFramework`, and it turns on nullable reference types, implicit usings, warnings as errors,
XML documentation (so unused-using analysis runs in the build), `latest-recommended` .NET analyzers
with `EnforceCodeStyleInBuild`, deterministic builds with `ContinuousIntegrationBuild` on CI, and
NuGet vulnerability auditing across transitive packages. `tests/Directory.Build.props` imports it
and adds the shared test stack (xUnit, Shouldly, test SDK, runner) plus global usings.

**Central package management.** `Directory.Packages.props` holds every package version. Project
files reference packages without versions. ASP.NET Core package versions follow the target framework
through one MSBuild property so an upgrade is a single edit.

**Pinned SDK.** `global.json` pins the SDK to 8.0.405 with `rollForward: latestPatch`.

**API bootstrap.** Controllers only (no minimal APIs), built-in ProblemDetails with
`UseExceptionHandler` and `UseStatusCodePages`, health checks at `/health`, and built-in OpenAPI
plus the Scalar UI in the Development environment only. The OpenAPI and Scalar wiring is compiled
only for .NET 9 or later (see ADR 0001).

## Alternatives

- **One project with layer folders.** Simplest, but the dependency rule would rest on discipline
  alone; the compiler could not reject a Domain-to-Infrastructure reference.
- **One project per module per layer.** Strongest isolation, but 36 projects for a small team is
  heavy, slows the build and makes cross-module contracts awkward. Revisit if the namespace-based
  module rule proves too weak.
- **Package versions in each project file.** The default, but versions drift between projects and
  upgrades touch many files.
- **Warnings as suggestions, analyzers off.** Faster to start, but a financial system needs the
  compiler to catch what it can, and retrofitting strictness onto a grown codebase is expensive.

## Consequences

- Layer violations fail compilation; module violations fail the test suite. Both are visible in CI.
- Every new package needs a `Directory.Packages.props` entry, which makes the approval step in the
  working agreement a natural checkpoint.
- Strict analyzers and style enforcement mean some friction on every change; the trade is fewer
  latent defects. Rules can be tuned in `.editorconfig` with a comment explaining why.
- The nine module folders in four projects add 36 placeholder READMEs. They are deleted as real
  code arrives.
- The target framework, and with it the OpenAPI and Scalar wiring, is governed by ADR 0001.
