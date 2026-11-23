# ADR 0035: HTTP hardening: TLS at the proxy, allowed hosts, sign-in limits, CORS and the headers of an answer

- **Status:** Accepted. The owner approved the plan of Prompt 5.4b on 2026-11-23 "as recommended", with its seven
  answers, which are points 1 to 7 below. The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0031 (point 12, brute force, is answered here), ADR 0011 (three codes of refusals the API makes on
  its own), ADR 0017 (the smoke run of the image), ADR 0012 (what a refused sign-in writes to the log).
- **Implements:** the security row of `docs/requirements.md` section 5 ("TLS only"); review finding 2.4 of
  CLAUDE.md section 9 item 6 (TLS, allowed hosts, server header); BR-601 (a sign-in that guesses is limited).
- **Changes rules of CLAUDE.md:** section 2 (the Web API row: how a request reaches the API) and section 5 (one
  options class, `AllowedHostsOptions`, reads a key at the root of the configuration instead of a section).

## Context

Until this prompt the API listened on plain HTTP (8080 in its container, 5080 in Development), answered for any host
name, said `Server: Kestrel`, accepted any number of sign-ins, and had no answer for a browser on another origin. The
NFR says "TLS only"; review finding 2.4 left TLS, allowed hosts and the server header open; ADR 0031 point 12 left
guessing passwords open. The frontend (Phase 7) will call the API from a browser, and Prompt 8.1 puts nginx in front of
it to serve the frontend.

## Decision

1. **TLS ends at a reverse proxy in front of the API** (nginx of Prompt 8.1, an ingress, a load balancer), not in
   Kestrel: the container keeps plain HTTP on 8080 and never holds a certificate. Consequences:
   - **Forwarded headers.** `UseForwardedHeaders` is the first middleware. It reads `X-Forwarded-For` and
     `X-Forwarded-Proto` (one proxy deep, `ForwardLimit` 1) only from the loopback addresses, which the framework
     trusts by default, and from `ReverseProxy:KnownProxies` (addresses or CIDR ranges, checked at startup). From any
     other address the headers are ignored: the request keeps its own scheme and address. `X-Forwarded-Host` is not
     read; the proxy passes the original `Host` on. The framework's switch `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, which
     would add a second reader that believes every address, stops the API at startup.
   - **No HTTPS redirection.** Outside Development a request that did not arrive over HTTPS (directly, or as a known
     proxy says) is answered 400 `https_required` before anything of it is read. A redirect would come after the token
     or the password had crossed the network in clear; a client sending plain HTTP is to be fixed, not sent on. The
     proxy may redirect browsers that ask for the frontend over HTTP; that is its business.
   - **HSTS.** Outside Development an answer over HTTPS carries `Strict-Transport-Security: max-age=31536000`: one
     year, this host only (no `includeSubDomains`), not on the browsers' preload list. The framework leaves out
     `localhost`, `127.0.0.1` and `[::1]`.
   - **The two health endpoints** (`/health/live`, `/health/ready`) are answered over plain HTTP and for any host:
     probes come from inside the network, by whatever name reaches the container (the image's HEALTHCHECK, the CI smoke
     run, an orchestrator). They tell nothing but "Healthy" or "Unhealthy".
   - **Development** is unchanged: plain HTTP on `localhost:5080` is answered, no HSTS.
2. **Allowed hosts.** The standard key `AllowedHosts` (entries separated by semicolons; `*.hospital.example` for the
   subdomains of a name; an IP address, IPv6 in brackets), read into `AllowedHostsOptions` and checked by the API's
   own `TransportGuardMiddleware`, inside the error handling, so a refusal is 400 `host_not_allowed` as a
   ProblemDetails with its correlation id. The framework's host filter, which reads the same key, runs before
   everything, answers with an HTML page and would refuse the probes, is told to let every host through. **At
   startup:** outside Development the key is required and `*` is refused; every entry must be one the check can match
   (no scheme, port or path). In Development, unset or `*` means any host. `appsettings.json` no longer says
   `"AllowedHosts": "*"`, so every environment but Development must name its hosts.
3. **Two limits on the sign-in**, with the rate limiter of ASP.NET Core (`Microsoft.AspNetCore.RateLimiting` and
   `System.Threading.RateLimiting`, both in the shared framework; no package):
   - **Per client address**, the one the proxy that ended TLS saw: 30 sign-ins a minute, a fixed window with no queue,
     whatever they name and however they end. The policy `sign-in`, on the sign-in alone. Against one source trying
     a common password on many accounts.
   - **Per account**, the hospital's code and the address as typed (trimmed, any case, compared as the sign-in
     compares them): after 10 refused sign-ins in 15 minutes, every sign-in for that account is refused until the
     window ends, the right password included; a sign-in that succeeds counts nothing. Against many sources guessing
     one account. Built with the same library, in an action filter (`SignInThrottleFilter`), because it needs the
     bound body, which the rate limiter's partition cannot wait for. Keyed by what was typed, so an address nobody has
     is held exactly like a real one, and the limit tells nothing about which accounts exist.
   - **The answer:** 429 `too_many_sign_ins` as a ProblemDetails, the same for both limits so it does not say which
     was reached, with `Retry-After` in whole seconds (the limiter's own: 60 or 900) and `Cache-Control: no-store`. A
     warning in the log names the client address (per address) or the hospital's code (per account), never the e-mail
     address.
   - **Fixed in code**, like the lifetime of a token, and kept in memory by each instance of the API: a restart
     forgets them, and two instances would each allow as much (R1 runs one; accepted, `docs/security.md`).
   - **The trade-off, accepted with the plan:** someone who knows a hospital's code and a user's address can keep that
     user from signing in, fifteen minutes at a time, by guessing ten times. The alternative, a limit per address only,
     leaves guessing one account from many addresses unlimited.
4. **CORS from configuration.** `Cors:Origins` lists the frontend's origins, as a browser sends them; none by default,
   which is right when the proxy serves the frontend and the API from one origin (Prompt 8.1). Development ships
   `http://localhost:5173`, the Vite dev server. **At startup:** an entry must be an origin (a scheme, a host, a port
   only when it is not the default; no `*`, no path, no trailing slash), and outside Development `https`. The policy:
   those origins; the methods `GET`, `POST`, `PUT`, `PATCH`; the request headers `Authorization`, `Content-Type`,
   `If-Match`, `Idempotency-Key`, `X-Correlation-Id`; the exposed headers `ETag`, `Location`, `Retry-After`,
   `X-Correlation-Id`, `Idempotent-Replayed`; no credentials (the token travels in a header, never a cookie); a
   preflight kept 10 minutes. `UseCors` comes before authentication, so a preflight, which carries no token, is
   answered 204.
5. **No `Server` header:** Kestrel's `AddServerHeader` is false.
6. **The headers of an answer** (`SecurityHeadersMiddleware`, written when the response starts, so an error carries
   them too): `X-Content-Type-Options: nosniff` on every response; on the answers of `/api`,
   `Cache-Control: no-store` unless the action set its own (patient and financial data is kept by no browser and no
   cache) and `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` (an answer loads nothing and is
   shown in no frame). The OpenAPI document and the Scalar page of Development are outside `/api`.
7. **The risks the threat model accepts for R1** are the rows marked "Accepted" in `docs/security.md` section 10, among
   them: the application's login can read every tenant's rows through raw SQL (no row-level security, ADR 0009); a
   TenantAdmin can create a second user and approve their own request with it, which the audit trail shows and nothing
   prevents; plain HTTP between the proxy and the API inside the private network; no rate limit but the sign-in's and
   Kestrel's default body limit; audit images keep patient data with no retention rule; the limits forgotten at a
   restart and not shared between instances; no package lock files. Two rows belong to later prompts, whose wording in
   `docs/build-prompts.md` gains a sentence each: where the browser keeps the token (7.1), and the frontend's own
   headers with TLS at the nginx of the local demo (8.1).

### The order of the pipeline (`Program.cs`)

Forwarded headers; correlation id; the headers of an answer; the request log; the exception handler and the status
code pages; the transport guard (hosts, then HTTPS); HSTS outside Development; CORS; the rate limiter; authentication;
authorisation. Each refusal of the guard, the limiter and the filter is a ProblemDetails built by the same factory as
every other error (`ApiProblems`), with `errorCode`, `traceId` and `correlationId`.

## Alternatives

- **TLS in Kestrel**, a certificate in the container: the application would load, renew and protect certificates,
  and the non-root container would need them mounted. Rejected for R1; nothing in this ADR prevents it later, since a
  request that reaches Kestrel over HTTPS is HTTPS to the guard as well.
- **The proxy alone** owning redirection and HSTS, the API trusting whatever arrives: "TLS only" could then not be
  tested in the API, and a misconfigured proxy would go unnoticed. Rejected.
- **HTTPS redirection in the API** (`UseHttpsRedirection`): rejected for an API that receives tokens and passwords (see
  point 1).
- **The framework's host filter** in place of the API's check: rejected for the HTML answer before the correlation
  id and for refusing the probes.
- **A limit per client address only**: rejected with the plan (point 3).
- **A lockout stored on the user's row**: it would survive restarts and be shared by instances, but it changes the
  row (and its version) at every refusal, and would tell which addresses exist unless it also stored rows for addresses
  nobody has. Rejected for R1.
- **Configurable limits**: the numbers are policy, like the lifetime of a token; a change is a code change with this
  ADR amended.

## Consequences

- Every environment but Development must set `AllowedHosts`; a proxy that is not on the API's own machine must be in
  `ReverseProxy:KnownProxies`, or every API request is refused 400 `https_required`, which shows the mistake at once
  (`docs/runbook.md` section 11).
- The local demo of Prompt 8.1 needs TLS at its nginx (a local certificate) or every API request is refused; its
  prompt says so.
- The CI smoke run of the image sets `AllowedHosts=localhost` and checks that no `Server` header comes back and that an
  API request over plain HTTP gets 400 `https_required`.
- Every integration test host calls the API at `https://localhost` and names `localhost` in `AllowedHosts`, as a
  client of a deployment would; no test was loosened for it.

## Built (2026-11-23)

Built as decided. What the build found, none of it a change of a decision:

- **The framework's host filter is still in the pipeline**, first of all (a startup filter of the web host's defaults,
  `HostFilteringStartupFilter`); it reads `AllowedHosts` too. A post-configuration sets its list to `*`, so it lets every
  host through to the guard; a test checks it.
- **The test server has no client address**, and the forwarded headers middleware believes the first entry of a
  request that has none (servers that cannot tell are let through). The tests give a request its address with a
  test-only startup filter (`RemoteAddressStartupFilter`, header `X-Test-Remote-Address`) and prove there that a
  stranger's forwarded headers are ignored. On Kestrel the address is real: `WebApplicationFactory.UseKestrel`, new in
  ASP.NET Core 10, hosts the API on a port of the loopback address for the test of the `Server` header and of the
  scheme a local proxy forwards.
- **`System.Net.IPNetwork.TryParse` accepts a range written with host bits** (`10.0.0.5/24`) and matches it as its
  network; the startup check accepts it too. A range with a prefix that does not exist (`/33`) or none (`10.0.0.0/`) is
  refused.
- **CORS sends no `Vary: Origin`** while one origin is configured (the answer is the same for every request); the
  framework adds it once there are several. Every answer of `/api` is `no-store` anyway.
- **A preflight for a method the API never takes** is still answered with the allowed origin and the four methods it
  does take, so the browser stops; the test checks the list, not the absence of the origin.
- **HSTS is not on an answer of 500.** `UseHsts` sets the header before the rest of the pipeline runs, and the exception
  handler clears the response before it writes the 500. A browser keeps the policy from any earlier answer, so this is
  left as the framework does it.
- **The limiter's clock** is the framework's own monotonic timer, not `IClock`: the windows are durations, never
  stored instants (rule 8 of CLAUDE.md is about instants and business dates). So no test waits for a window to end;
  the tests check the limit, the answer and `Retry-After`.
