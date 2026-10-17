# ADR 0013: API versioning by route prefix

- **Status:** Accepted
- **Date:** 2026-10-03
- **Deciders:** project owner (chose this option), Claude Code

## Context

`docs/requirements.md` §5 says the API is versioned under `/api/v1` and that a breaking change needs
a new version. The first controller was routed at `api/system`, without a version, and as the only
example it was the template every module would copy. There is one version today and no consumer
other than the project's own front end.

## Decision

We will version the API with a plain **route prefix** and no versioning package.

- `ApiRoutes.V1` (`"api/v1"`) is the single definition of the prefix. Every controller route starts
  with it: `[Route(ApiRoutes.V1 + "/system")]`.
- An integration test enumerates the endpoints and fails if any controller route is not under
  `api/v1/`.
- Operational endpoints are not part of the versioned API and stay outside the prefix:
  `/health/live`, `/health/ready`, and in Development the OpenAPI document and Scalar UI.
- Within `v1`, changes are additive only. A breaking change is introduced as `api/v2` next to `v1`.

## Alternatives

- **`Asp.Versioning.Mvc`.** Gives version negotiation, `api-supported-versions` response headers,
  deprecation metadata and per-version OpenAPI documents. None of that is needed while one version
  exists, and it is a dependency to approve and keep current. **This is the upgrade path:** adopt it
  (with a new ADR) when a `v2` is actually needed.
- **Header or query-string versioning.** Keeps URLs stable but hides the version from logs, links
  and caches, and contradicts the requirement's `/api/v1`.
- **A routing convention that prepends the prefix to every controller.** Removes the constant from
  each `[Route]`, but makes the real URL invisible at the controller and is more machinery than a
  constant plus a test.
- **No versioning until needed.** Retrofitting a prefix later breaks every client at once.

## Consequences

- The version is visible in every URL and in every request log line.
- A controller that forgets the prefix fails the test suite.
- There is no machine-readable list of supported versions and no deprecation signalling. That is
  acceptable with one version and one client; it is what `Asp.Versioning.Mvc` would add later.
- Moving the existing endpoint changed its URL: `/api/system/info` is now `/api/v1/system/info`.
