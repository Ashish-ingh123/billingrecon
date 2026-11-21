# ADR 0031: Sign-in and tokens

- **Status:** Accepted. Written before the code, as Prompt 5.2 asks; the owner approved the plan on 2026-11-21 with the
  eight recommended answers, which are points 1, 2, 4, 5, 8, 9, 10 and 11 below; the other points follow from them.
  The built note at the end records what the build found. Built and tested on .NET 10 (SDK 10.0.404) on the office
  machine: build, format gate and the tests without a database in the repository, the database tests in a throwaway
  copy against SQL Server LocalDB 2019. SQL Server 2022 in Docker and the container's smoke test in CI are still to
  come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0009 (the tenant comes from the token), ADR 0015 section 6 (the development identity), ADR 0030
  (users and passwords; its point 7 is confirmed here).
- **Implements:** BR-002, BR-601 (a disabled user cannot sign in), BR-602; US-02, US-04.

## Context

Until this prompt the API acted for a fixed tenant and user in Development and for nobody elsewhere. Users with
passwords exist since Prompt 5.1. This prompt lets a user sign in, gives the client a token that carries the tenant,
the user and the roles, and makes that token the only source of the tenant of a request (BR-002). What each role may
do is Prompt 5.3.

## Decision

1. **The token is a JWT issued and validated by the API itself**, with `Microsoft.AspNetCore.Authentication.JwtBearer`
   10.0.12 (the package the owner approved), referenced by the Api project. The token is written with the
   `JsonWebTokenHandler` that comes with it. The issuer of tokens (`JwtTokenIssuer`, behind the Application port
   `ITokenIssuer`) and the identity of a request (`RequestPrincipalIdentity`, which is `ITenantContext` and
   `ICurrentUser`) live in the Api project, as `HttpCorrelationContext` does.
2. **Signing and lifetime.** HS256 with a key of at least 256 bits from configuration (`Jwt:SigningKey`, base64),
   checked when the API starts: a missing, malformed or short key stops it. `appsettings.Development.json` carries a key
   for Development only; outside Development the API refuses to start with that key. The issuer is `BillingRecon` and
   the audience `BillingRecon.Api`, both checked. A token lives 60 minutes, with one minute of tolerance for clocks;
   there is no refresh token: the client signs in again. The lifetime is checked against the application's clock
   (`IClock`), so the tests can move time.
3. **What the token carries:** `sub` (the user's id), `tenant_id` (the tenant's id), one `role` per role, `jti`, `iat`,
   `nbf` and `exp`. The handler is told not to rename the claims (`MapInboundClaims = false`), and a token without a
   `tenant_id` and a `sub` that are ids is refused.
4. **Sign-in:** `POST /api/v1/auth/login` with the tenant's code, the e-mail address and the password answers the token,
   its type (`Bearer`), when it expires and how many seconds that is, with `Cache-Control: no-store`. **Every failure
   answers the same 401 `invalid_credentials`:** an unknown or suspended tenant, an unknown address, a wrong password, a
   disabled user (BR-601). So the API never says which tenants or addresses exist. A sign-in for an address that does
   not exist still costs one password verification, against a hash nobody has, so it takes as long. The reason of a
   refusal goes to the log (a warning with the tenant's code and, when known, the user's id), never into the answer,
   and neither the address nor the password is ever logged.
5. **A sign-in is recorded:** `Users.LastSignInAtUtc` and an audit record `SignedIn`. To write that row, the sign-in
   request acts as the user whose password it has just verified: the use case enters, through the Application port
   `ISignInScope`, the very principal the token will carry, and from then on the request's tenant and user are that
   user's. **This is the one place where the tenant of a request comes from verified credentials instead of a token**,
   which is what signing in means; nothing the client sends besides the credentials is involved, and an architecture
   test keeps the port to the sign-in use case alone. Signing in changes the user's row version, so an administrator
   who read the user before gets 409 on a change and reads it again.
6. **The only use of `IgnoreQueryFilters`** is `LoginUserLookup`: a sign-in has no tenant yet, and the address is unique
   only within a tenant, so the user is found by the tenant the code names, with that tenant in the query by hand. The
   tenant itself is found by its code without switching anything off: a scope without a tenant sees every tenant row
   (ADR 0015). `BannedSymbolsTests` checks that exactly one file suppresses the ban, around one call, with the tenant's
   id in its predicate.
7. **Deny by default.** Every endpoint needs an authenticated user (a fallback authorisation policy). The anonymous ones
   are the sign-in, the two health endpoints, and the OpenAPI document and Scalar page in Development. A request without
   a valid token gets 401 with the code `unauthorized` that a bare 401 already had; a refused sign-in gets 401
   `invalid_credentials`, for which the error model gains the kind `Unauthorized`. An architecture test lists the
   anonymous actions, and a test sends a request without a token to every endpoint of every controller.
8. **A token stays valid until it expires.** A user disabled, or whose roles change, after signing in keeps a working
   token for at most 60 minutes. Accepted for R1; a check of the user on every request is the alternative if that window
   is ever too long.
9. **Development.** A request without an `Authorization` header acts as the development user of
   `DevelopmentIdentity` (a TenantAdmin), so the Scalar page and local tools keep working; a request with a bearer token
   is validated as everywhere else. Outside Development there is no such fallback. The development seeder writes its
   rows through a context of its own for the development tenant and user, since no request is running.
10. **The tests use real tokens.** A test client gets, for every request it sends, a token minted by the API's own
    issuer for the identity it acts as, so every request goes through the whole validation, and moving the test clock
    never leaves a client with an expired token. The header-based stand-in of the tests is removed. The sign-in itself,
    with real passwords, is tested end to end.
11. **No Idempotency-Key on the sign-in.** Its body holds a password, and the key store keeps a SHA-256 fingerprint of
    the body (ADR 0030 point 7, which the owner confirmed with this plan). `IdempotencyConventionTests` names its second
    category the non-financial controllers: reference data, users and sign-in.
12. **Brute force is not handled yet.** There is no rate limit and no lockout on sign-in in this prompt; the built-in
    rate limiter of ASP.NET Core can add one without a package. A follow-up.

## Alternatives

- **Hand-written token code.** Rejected: security code that a well-known library already does correctly.
- **Asymmetric keys (RS256).** Useful when another service has to validate the tokens; here the API issues and
  validates its own, and one secret is simpler to keep. A change of algorithm later is a configuration and code change in
  one place.
- **A distinct answer for a disabled user.** It would tell an attacker that the address exists and the password is right.
- **Log the sign-in only, without the row and the audit record.** Rejected with the plan: the audit trail is where the
  question "who was signed in" is answered.
- **Every test signing in for real.** Correct but slow (a verification costs about 0.3 seconds by design); the sign-in
  has its own tests.
- **The development user signing in with a password.** Rejected with the plan: Development keeps working without a
  sign-in step.

## Consequences

- `UnavailableIdentity` and the header-based test principal disappear; anonymous requests outside Development get 401
  instead of a fault.
- The container's smoke test in CI starts the API with a throwaway signing key.
- A request for an endpoint that does not exist gets 401 before it gets 404 when it carries no token.

## Built (2026-11-21)

Built as decided. What the build found or added, none of it a change of a decision:

- **The lifetime is checked by a validator of our own.** The time provider of `TokenValidationParameters` is internal
  in IdentityModel 8.19.2, the version JwtBearer 10.0.12 brings, so the library's check would read the system clock.
  `TokenLifetime` takes its place through `LifetimeValidator`, with the application's clock and the same bounds as the
  library (from `nbf` to `exp`, each widened by the minute of tolerance). A validator set there switches off the
  library's own check that a token has an expiry, so `TokenLifetime` refuses a token without one. The database tests
  prove it in passing: their clock is five weeks behind the machine's, so every one of their tokens would be expired
  by the system clock.
- **An anonymous endpoint authenticates nobody.** The bearer scheme does not read the token of a request to an
  endpoint marked anonymous, and the development scheme does not apply the development identity to it
  (`AnonymousEndpoints`). Without that, a sign-in in Development, or one that still carries a token of another tenant,
  would see only that tenant's row and fail as an unknown hospital. `HttpSignInScope` refuses to enter a user in a
  request that already acts for one, so the sign-in cannot lose this by accident.
- **A refused token gets a bare `WWW-Authenticate: Bearer`** (`IncludeErrorDetails = false`): why it was refused is not
  told, as for a refused sign-in.
- **Only HS256 is accepted** (`ValidAlgorithms`): a token signed with the right key and another algorithm is refused,
  as is an unsigned one. The times of a token are whole seconds, so the issuer cuts the instant to the second and the
  expiry the sign-in answers is the token's own.
- **The code and the address are measured without the spaces around them**, which the sign-in drops anyway: a code of
  the longest length typed with a space was refused with 400 before. A test whose random code happened to be that
  long found it.
- **Nothing secret is in the text of a sign-in.** The command, the request, the answer and the issued token are
  records, whose generated `ToString` would print the password, the address or the token; each prints its harmless
  members only.
- **The tests.** A test client sends, with every request, a token minted for it by the API's issuer (a delegating
  handler); a client that names no role holds TenantAdmin, the same as the user row behind it. Every test host outside
  Development has a random key of its own, long enough for HS512, so a test can sign with the right key and the wrong
  algorithm. The log enrichment no longer reads the long name-identifier claim, since the claims keep their names.
- **Not done here:** the user commands and requests of Prompt 5.1 that hold a password are records with the generated
  `ToString` too; nothing logs them, and making them print no password is a small follow-up.
