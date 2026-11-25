# ADR 0057: A sign-out ends the user's sessions on the server

- **Status:** Accepted. The owner approved the plan of Prompt 10.2c on 2026-11-25, with its three choices: a sign-out
  ends every session of the user, the user's row is read on every request, and Development's access without a token
  stays as it is.
- **Date:** 2026-11-25
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0020 (audit), ADR 0030 (users), ADR 0031 (sign-in and tokens), ADR 0038 point 9 (the session in
  the browser), ADR 0051 (the forced password change), ADR 0052 (the gate of a suspended hospital).
  **Amends:** ADR 0031 point 8 and ADR 0032 point 8 (a token no longer outlives a disabling or a change of roles), and
  rows S4 and E2 of the threat model (`docs/security.md` section 10).
- **Number:** 0053 to 0056 are kept for Prompts 10.3 to 10.6 (`docs/build-prompts.md`), so this one is 0057.

## Context

"Sign out" forgot the token in one browser tab and told the API nothing. The token stayed valid until its expiry, up to
60 minutes: copied, or used from another tab or another tool, it went on working, and so did the token of a user who had
been disabled or had lost a role. ADR 0031 point 8 accepted that for R1 and named the alternative, a check of the user
on every request. The owner reported it as a defect on 2026-11-25: a user who signed out could still work.

## Decision

1. **A number of sessions on the user.** `Users.SessionVersion` (`int`, 0 for every existing user; migration
   `0022_UserSessions`, scaffolded). A token carries the number it was issued with in the claim `ses`
   (`ITokenIssuer.Issue` takes it).
2. **What raises the number,** each of them ending every session the user had, on every tab and device:
   a sign-out (`User.EndSessions`, event `UserSignedOut`, audit action `SignedOut`); the user's own change of password,
   whose answer is a token of the new session; a reset of the password by an administrator; a disabling; a change of
   the roles that changes them, the administrator's own included. Enabling a user, a change of name or address and a
   sign-in raise nothing. The four that existed raise it in the methods of the aggregate that already raise their event.
3. **The check, on every request with a token.** `SessionGateMiddleware`, after authentication and before authorization,
   asks `IUserSessionReader.IsCurrentAsync(userId, sessionVersion)`: the user exists in the tenant of the token, is
   active, and has that number. Otherwise the answer is 401 `session_ended` ("This session has ended. Sign in again."),
   also for a token with no `ses` claim, which is refused without a read. `UserSessionReader` reads the row by its key
   through the context, so the tenant filter applies, and **keeps nothing**: a sign-out holds from the next request on,
   on every instance. The cost is one read by primary key per request; the load test of ADR 0046 measures it
   (runbook section 8, row 10.2c).
4. **The endpoint.** `POST /api/v1/auth/logout`, policy `SignedIn`, no body, no `Idempotency-Key` and no `If-Match`
   (as the change of one's own password, ADR 0030 point 7), 204 with `Cache-Control: no-store`. A user whose password
   change is due may call it. The handler answers `user_not_found` for a request without a user.
5. **Not concerned.** The anonymous endpoints (the sign-in, the health checks) authenticate nobody and are not checked.
   The development identity is no token and has no session: in Development a request without a token acts as the
   development user as before (ADR 0031 point 9), and never anywhere else.
6. **The browser.** The Sign out buttons call the endpoint with the token of the session that ends (`useSignOut`) and
   forget the session at once, whatever the API answers and whether it answers. A session ends once: the 401 of a
   request still on its way does not turn "You have signed out." into "Your session has ended.". A sign-out in one tab
   ends the session of the same user in the other tabs of the browser at once, by a `storage` event on a
   `localStorage` key that names the user by id and is removed in the same step; a tab of another user is left alone.
   Without that signal a tab learns it from the API at its next request.

## Consequences

- **Everyone signs in once more after the deployment:** a token issued before it has no `ses` claim.
- A user signed in on two devices is signed out of both by signing out of one. Ending one device only would need a
  table of revoked tokens and would not have closed E2; rejected.
- An administrator who changes their own roles signs in again.
- A token stolen and used before its owner signs out still works until then or its expiry: S4 is reduced, not closed.
- A test that sends requests as a user it then disables, resets or signs out needs a new sign-in for that user; the
  permission walk keeps the sign-out as its last row.

## Alternatives considered

- **A table of revoked tokens by `jti`.** One device at a time, but a write and a table to clean up for every sign-out,
  and nothing for a disabled user or changed roles.
- **A stamp of time ("tokens issued before are refused").** A token's times are whole seconds, so a token issued in
  the same second as the sign-out could not be told apart.
- **A cache of a few seconds,** as the suspended hospital has (30 seconds). A signed-out user would work on for that
  long, which is the defect reported.

## Built note (2026-11-25)

- Verified on the office machine: the build with warnings as errors, the format gate, every test outside the
  `Database` collection, the `Database` collection in a throwaway copy on LocalDB 2019, the frontend's gates and
  component tests. Not run on SQL Server 2022 or through nginx (runbook section 8, row 10.2c).
- Three existing tests changed because behaviour changed, none loosened: the token that `AuthenticationTests` crafts
  "without a flaw" now names its session; the shell's test of the notice after a sign-out answers the sign-out's own
  request; and `UsersApiTests`, which reached the guard of the last administrator with a token from before its user
  lost the role (ADR 0031, built note), now expects that token refused with 401 `session_ended`. That guard can no
  longer be reached through the API by one request after another, only by two administrators at the same moment; it
  stays in the handler and is held by `UserHandlerTests`.
- LocalDB keeps the probe database between runs, made from the model of the run before: drop `BillingRecon_Probe`
  before a run after a change of the model, with the two of CLAUDE.md's dev machine notes.
