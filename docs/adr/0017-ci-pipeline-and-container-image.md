# ADR 0017: The CI pipeline, the API container image, and one line ending everywhere

- **Status:** Accepted (owner's approval of the Prompt 1.5 plan, 2026-10-17). Written on the office machine;
  the first run on GitHub is still to come (see "Verification" under Consequences).
- **Date:** 2026-10-17
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0002 (build conventions: adds the line-ending rule), ADR 0016 (where the database tests run).

## Context

The blueprint's Prompt 1.5 asks for a GitHub Actions workflow that restores, checks formatting, builds
with warnings as errors, runs every test including the Testcontainers ones, reports coverage, fails on
vulnerable packages, produces an idempotent migration script and builds the API image; a multi-stage
Dockerfile with a non-root user, a minimal runtime image and a health check; and Dependabot.

What shapes the decisions: the database tests need Docker (ADR 0016), which GitHub's hosted Ubuntu
runners provide and the office machine does not; the repository already fails any restore on any NuGet
advisory (`NuGetAudit`, level `low`, in `Directory.Build.props`); the API looks up `Asia/Kolkata` at
startup and refuses to start without it (ADR 0010); and `dotnet format` gave two different answers on
the same code during the pre-check, one on Windows and one on Linux, because `.editorconfig` set no
`end_of_line` and the formatter then writes the operating system's newline wherever it touches
whitespace.

## Decision

### The workflow (`.github/workflows/ci.yml`)

On every push to `main` and every pull request, two jobs on `ubuntu-latest`:

1. **Format, build, test, audit, migration script**, in that order, one job so that one restore and one
   build serve every gate: `dotnet tool restore` and `dotnet restore` (the audit runs here),
   `dotnet format --verify-no-changes`, `dotnet build` in Release (warnings are errors, and
   `GITHUB_ACTIONS` switches the deterministic CI build on), `dotnet test` for all four projects with
   coverlet coverage, then the artifacts: test results and the cobertura coverage report, a readable
   `dotnet list package --vulnerable --include-transitive` report whose High or Critical lines fail the
   job, and the idempotent migration script from `dotnet ef migrations script`.
2. **The API container image**, in parallel: build the Dockerfile with Buildx and the GitHub cache,
   then start the container with a connection string nothing listens on and wait for 200 from
   `/health/live`. That one request proves the image starts, as the non-root user, with the time zone
   data; readiness would answer 503, which is right for a database that is not there.

Coverage is a report, not a gate: the blueprint calls the figure a guide. The vulnerability report is
a second, visible gate behind the restore audit, which is stricter (any severity) and earlier.
Actions are pinned by major tag and Dependabot keeps them current. Nothing is pushed to a registry and
no secret is needed; the release workflow is Prompt 9.1.

### The image (`src/BillingRecon.Api/Dockerfile`, context the repository root)

Build stage on `mcr.microsoft.com/dotnet/sdk:10.0`, with `global.json`, the props and the project files
copied first so that a code change keeps the package layer. Runtime stage on
`mcr.microsoft.com/dotnet/aspnet:10.0`, the Debian image, which ships `tzdata`; curl is installed for
the `HEALTHCHECK` on `/health/live`, the only addition. The container runs as the image's own `app`
user (uid 1654) and listens on 8080, the image's default. `.dockerignore` keeps build output, tests,
docs, `.env` and the git history out of the context. The tags follow the major version; `global.json`
must accept the SDK the tag carries, and a build argument takes the exact tag if they drift apart.

### Dependabot (`.github/dependabot.yml`)

Weekly for NuGet (minor and patch updates grouped into one pull request; major versions of the
framework-bound packages ignored, since they follow the target framework by hand, ADR 0014) and for
GitHub Actions. The npm entry is written and commented out until `frontend/` exists (Prompt 8.1):
Dependabot reports an error for a directory that is not there.

### Coverage

`coverlet.collector` is referenced from `tests/Directory.Build.props`, so any test project can report,
and `dotnet test --collect:"XPlat Code Coverage"` writes cobertura files into the results directory.
CI collects it for the Domain and Application unit tests only, which is where the blueprint measures
it, and runs the integration and architecture tests uninstrumented: instrumentation slows the
regex scan of the PII masker's timeout test past its 250 ms budget (seen in the pre-check), and a
gate that flakes teaches people to ignore it.

### One line ending everywhere

`.gitattributes` says `* text=auto eol=lf` and `.editorconfig` says `end_of_line = lf`, with CRLF
kept for `.sln`, `.ps1`, `.cmd` and `.bat` in both. Every clone, on every operating system, gets LF
files, and `dotnet format --verify-no-changes` gives one answer everywhere. Visual Studio and Rider
handle LF files without complaint.

## Alternatives

- **A Windows runner.** It cannot run the Linux SQL Server container the database tests need.
- **One job per gate.** Clearer in the Actions UI, but each job restores and builds again; a single
  job keeps the run to one restore and one build, and the step names say which gate failed.
- **Coverage as a gate** (80 per cent on Domain and Application). The blueprint calls it a guide, not a
  goal, and a threshold on a codebase this young would be noise. The report is there to read.
- **`dotnet list package --vulnerable` as the only vulnerability gate.** The restore audit is earlier,
  covers the same graph and fails on any severity; the list is kept for its readable output.
- **A chiseled or Alpine runtime image.** Smaller, but without `tzdata` the API does not start, without
  curl there is no health check, and Alpine's musl and ICU add their own surprises. The Debian image is
  the one the runbook already assumed.
- **Pushing the image on every push to `main`.** The registry, its credentials and the tagging scheme
  are release decisions (Prompt 9.1); building and smoke-running the image proves what this step can.
- **Pinning actions to commit SHAs.** Safer against a moved tag, noisier to maintain; Dependabot keeps
  the tags current. Revisit in the R5 hardening.
- **Native line endings** (`text=auto` without `eol`). The default, and exactly what made the format
  gate machine-dependent.

## Consequences

- A run takes a few minutes: the restore, the build, and the SQL Server image pull for the database
  tests dominate.
- The day an advisory appears for any package in the graph, the restore fails everywhere, in CI and
  locally, until the patched version is pinned in `Directory.Packages.props`. That is the intended
  behaviour of the audit; `docs/runbook.md` section 5 says what to do.
- Working trees copied between machines (not cloned) may still hold CRLF files from before this
  decision; a clone after the first push is clean.
- The two acceptance criteria of the step, a green run on GitHub and a deliberately broken test turning
  it red, need the repository on GitHub and a first push, which the repository does not have yet
  (CLAUDE.md section 9).
- **Verification.** The workflow is written, not run: the office machine has neither Docker nor the
  .NET 10 SDK. In the .NET 9 pre-check copy the format gate passes with the line-ending rule (and fails
  without it), the coverage collection writes cobertura files for the two unit test projects, the
  vulnerable-package report runs (and its gate fires there, on the 9.x graph's advisory, as it
  should), and the migration script command produces the idempotent script with the EF Core 9 tool.
  The Dockerfile is proven by the first CI run, or by `docker build` on the owner's machine.

## Amended 2026-11-23 (Prompt 5.4b, ADR 0035)

The image now needs `AllowedHosts` outside Development and answers an API request only over HTTPS, since TLS ends at a
proxy in front of it. The smoke run passes `AllowedHosts=localhost` and gains a step that checks the image over plain
HTTP: `/health/live` answers (the HEALTHCHECK relies on it), no answer has a `Server` header, and
`/api/v1/system/info` gets 400 `https_required`. The Dockerfile is unchanged. Not run in CI yet (the repository is not on
GitHub); the same checks passed on the office machine against the published API on Kestrel with the smoke run's
settings.
