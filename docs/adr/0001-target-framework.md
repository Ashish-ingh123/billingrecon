# ADR 0001: Target framework

- **Status:** Superseded by [ADR 0014](0014-target-framework-net10.md) (2026-10-03)
- **Date:** 2026-09-26
- **Deciders:** project owner (rule: current LTS, check the installed SDK), Claude Code

## Context

CLAUDE.md requires the current .NET LTS. On 2026-09-26 two LTS releases are in support:
.NET 8 (support ends 10 November 2026) and .NET 10 (released November 2025, supported to
November 2028). The development machine has SDKs 5.0.207, 8.0.405 and 9.0.102 installed and no
.NET 10 SDK. Installing an SDK is a change to the machine that the owner has to make.

The API is specified with built-in OpenAPI and the Scalar UI. Built-in OpenAPI document generation
(`Microsoft.AspNetCore.OpenApi` with `AddOpenApi` and `MapOpenApi`) exists from .NET 9 only, because
it depends on the JSON schema exporter added to System.Text.Json in .NET 9. On .NET 8 a document can
only come from a third-party generator (Swashbuckle, NSwag), and the working agreement requires
approval before any dependency is added.

## Decision

We will target **net8.0** now, because it is the only in-support LTS installed, and **upgrade to
.NET 10 before 10 November 2026**, preferably before R1 ships.

- `Directory.Build.props` holds the single `TargetFramework`; `global.json` pins SDK 8.0.405 with
  `rollForward: latestPatch`.
- ASP.NET Core package versions follow the target framework through the `AspNetCoreVersion`
  property in `Directory.Packages.props` (8.0.31, 9.0.20, 10.0.12), so the upgrade does not touch
  project files.
- The OpenAPI and Scalar wiring in `Program.cs`, the `Microsoft.AspNetCore.OpenApi` package reference
  and the integration test for `/openapi/v1.json` and `/scalar` are compiled only when
  `NET9_0_OR_GREATER`. On net8.0 the API logs a warning at startup in Development and serves no
  OpenAPI document. No Swashbuckle is added.
- The upgrade procedure: install the .NET 10 SDK, set `TargetFramework` to `net10.0`, set the
  `global.json` version to the installed 10.0.x SDK, run `dotnet build` and `dotnet test`, then mark
  this ADR superseded by a short ADR that records the new version.
- The upgrade path was exercised on 2026-09-26 by switching to `net9.0` with the installed 9.0.102
  SDK: the solution built with zero warnings and all tests passed, including the OpenAPI and Scalar
  endpoint test that is compiled out on net8.0. The repository was then reverted to net8.0.

## Alternatives

- **.NET 10 now.** The intended end state and the actual current LTS, but the SDK is not installed
  and installing it is the owner's call. Becomes the decision as soon as the SDK is present.
- **.NET 9.** Installed and has built-in OpenAPI, but it is an STS release whose support ended in
  May 2026. Unacceptable for a financial system.
- **.NET 8 with Swashbuckle for the OpenAPI document.** Gives a working Scalar UI today, but adds a
  dependency that was not asked for, contradicts the "built-in" requirement, and would be removed
  again at the upgrade. Rejected for now; a new ADR can adopt it if the .NET 10 upgrade has to wait.

## Consequences

- Until the upgrade there is no OpenAPI document and no Scalar UI locally; the `scalar` launch URL
  in `launchSettings.json` returns 404 on net8.0 and the startup warning explains why.
- The upgrade is two one-line edits plus an SDK install. Package versions and the guarded code follow
  automatically, and the test suite proves the OpenAPI endpoints as soon as the target moves.
- Hard deadline: .NET 8 leaves support on 10 November 2026. Shipping R1 on .NET 8 after that date is
  not acceptable.
- The analyzer rule set changes with the SDK. The net9.0 check produced no new warnings today; a
  later SDK may, and those are fixed or tuned in `.editorconfig` at upgrade time.
