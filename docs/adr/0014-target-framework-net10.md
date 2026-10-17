# ADR 0014: Target framework .NET 10

- **Status:** Accepted (owner's instruction, 2026-10-03). Verified on .NET 10 on 2026-10-17: clean build and
  493 tests green on the personal machine, SDK feature band 10.0.400 (see Consequences).
- **Date:** 2026-10-03
- **Deciders:** project owner, Claude Code
- **Supersedes:** [ADR 0001](0001-target-framework.md)

## Context

ADR 0001 chose .NET 8 because it was the only supported LTS installed, and set a hard date: .NET 8
leaves support on 10 November 2026. .NET 9 leaves support on the same day. .NET 10 is the current LTS
(released November 2025, supported to November 2028).

The next step adds EF Core, the `dotnet-ef` tool and the first migration with its model snapshot. The
owner decided that these are set up once, on the target version, instead of on .NET 8 and again five
weeks later: "do the .NET 10 upgrade as its own step" before the DbContext step.

Two facts about the machines shape how this is done:

- The .NET 10 SDK is installed on the owner's personal machine, where the project is run. Its exact
  version is not known on the machine where the code is written.
- The office machine, where the code is written, has SDKs 8.0.405 and 9.0.102 only, and the owner
  decided not to add the .NET 10 SDK there. Nothing that targets .NET 10 can be built on it.

## Decision

We will target **net10.0** and keep every framework-bound package on the same major version.

- `Directory.Build.props`: `TargetFramework` is `net10.0`.
- `global.json`: SDK `10.0.100` with `rollForward: latestFeature`, so any installed .NET 10 SDK is
  accepted. Once the SDK version on the personal machine (and later on CI) is known, the pin is
  tightened to that feature band with `latestPatch`, as ADR 0001 had it.
- `Directory.Packages.props` holds literal versions; the conditional `AspNetCoreVersion` property is
  gone. On 2026-10-03 the current patch is 10.0.12:
  `Microsoft.AspNetCore.OpenApi` and `Microsoft.AspNetCore.Mvc.Testing` 10.0.12,
  `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.Design` 10.0.12 (used
  from the DbContext step on), `Serilog.AspNetCore` 10.0.0.
- `dotnet-ef` 10.0.12 is a **local tool** (`.config/dotnet-tools.json`), restored with
  `dotnet tool restore`, so the tool version is in git and matches the EF Core packages.
- The `#if NET9_0_OR_GREATER` blocks are removed: the OpenAPI document and the Scalar UI are always
  compiled and are mapped in Development only, and their test always runs.
- **`Microsoft.Data.SqlClient` stays at 7.1.1** after a re-check against EF Core 10:
  - `Microsoft.EntityFrameworkCore.SqlServer` 10.0.12 declares `Microsoft.Data.SqlClient >= 6.1.6`.
    EF Core's 10.0 servicing branch builds against 6.1.7; EF Core's main branch (version 11) builds
    against 7.1.0.
  - 7.1.1 satisfies the declared minimum. SqlClient 7.0 moved the Azure and Entra authentication
    code into an optional package, so the 7.x dependency graph has no Azure identity libraries; the
    6.1.x graph brings them in although this system signs in with a SQL login. The other breaking
    changes in 7.0 (SQL Server 2000 support removed, clean-up of old error-handling regions) do not
    touch anything EF Core or this code uses.
  - It is still one major version ahead of what the EF Core 10 line is tested with. The pair is
    therefore proven by the first runs against SQL Server (applying the migration, the readiness
    check, and the integration tests of the next step). **Fallback:** if a failure points at
    SqlClient, set the version to 6.1.7 in `Directory.Packages.props`; nothing else changes.
- Two things are deliberately left in place until the first build on .NET 10 confirms they can go:
  the explicit `public partial class Program` declaration (the .NET 10 SDK generates one and reports
  the explicit one as information, ASP0027), and the log filter that drops the framework's own copy
  of an unhandled exception (a no-op on .NET 10, see ADR 0011). Both were removed on 2026-10-17, after the
  first green build on .NET 10, and the owner's run the same day confirmed it.

## Alternatives

- **Stay on .NET 8 until November.** EF Core 8, `dotnet-ef` 8 and the first snapshot would be set up
  now and replaced within weeks. Rejected by the owner.
- **.NET 9.** Buildable on the office machine, but out of support on the same day as .NET 8.
- **Target both net8.0 (or net9.0) and net10.0** so the office machine can still build. EF Core 10
  targets net10.0 only, so the lower target would need a second set of EF packages, a second
  snapshot path and conditional code. That is the double set-up the owner wanted to avoid.
- **Pin the exact SDK (10.0.401, the latest on 2026-10-03) with `latestPatch`.** Reproducible, but
  the build would refuse to start if the personal machine has another feature band. Adopt it once
  the installed version is known.
- **`Microsoft.Data.SqlClient` 6.1.7**, the version the EF Core 10 line is built against. The most
  conservative pairing; it adds the Azure identity libraries to the graph, each of which is one
  more package the vulnerability audit can stop the build on. Kept as the fallback.

## Consequences

- EF Core, the tool, the SqlClient pairing and the first snapshot are set up once. The support
  deadline of 10 November 2026 no longer applies; the next one is November 2028.
- **The office machine cannot build or test the repository any more.** Code written there is
  pre-checked in a throwaway copy moved down to .NET 9 (with the EF Core 9 packages from the local
  cache). That copy proves that the code compiles and the tests pass on .NET 9. It cannot show: the
  C# 14 compiler, the .NET 10 analyzers (warnings are errors), or anything that behaves differently
  in ASP.NET Core 10 and EF Core 10. Those are only known after a build on the personal machine.
- **Done on 2026-10-17** on the personal machine: `dotnet tool restore`, `dotnet build`, `dotnet test`
  (493 tests green). The follow-ups were applied the same day: `global.json` pinned to the 10.0.400
  feature band with `latestPatch` (the band read from `SdkAnalysisLevel` in that machine's build
  outputs; the exact patch is still to be recorded in `CLAUDE.md`), the explicit `Program` declaration
  removed, the log filter removed; the owner's build and test run on .NET 10 the same day was green.
- The analyzer rule set changes with the SDK. New warnings on .NET 10 are fixed, or tuned in
  `.editorconfig` with a comment saying why.
- ASP.NET Core 10 writes the OpenAPI document as version 3.1 by default (3.0 on .NET 9). Nothing
  depends on the document version yet.
- Framework-bound packages are now edited by hand in one file when a new patch is released. That is
  one more place to touch than the old conditional property, and no place where a version can drift
  from the target framework unnoticed.
