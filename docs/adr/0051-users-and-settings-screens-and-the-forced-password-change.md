# ADR 0051: The users and settings screens, and the forced password change

- **Status:** Accepted. The owner approved the plan of Prompt 10.1 on 2026-11-24, the forced password change with it.
- **Date:** 2026-11-24
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0030 point 3 ("nothing forces a change at the next sign-in"). **Builds on:** ADR 0030 (users and
  roles), ADR 0031 (sign-in and tokens), ADR 0032 (permissions), ADR 0033 (settings and prefix changes), ADRs 0047 to
  0050 (the design of Phase 9).

## Context

R1 had the API for users, roles, the three tenant settings and prefix changes, but no screens (backlog B18): a hospital's
administrator could set up their staff only by calling the API. A password the administrator gives, at creation or by a
reset, was the user's password for good (ADR 0030 point 3), so the administrator knew it. And four C# records that carry
a password printed it in their generated `ToString()` (backlog B24).

## Decision

1. **The screens, for the TenantAdmin (policy `ManageTenant`), through the API that exists.** Under `/administration`, the
   "Users and settings" section of the shell: the users, a page at a time, found by e-mail address, status and role;
   a user's page with their roles, status, last sign-in and history (`AuditTrail`), disable and enable (asked once more,
   in place), and reset password; a new user with a first password and roles; a correction of the address, the name and
   the roles, sent as `PATCH` then `PUT .../roles` with the chained version, each only when it changed. Every role is a
   check box with what it may do. The settings: the threshold, the back-date window and the due days with `If-Match`; a
   prefix change as a request (`POST /api/v1/settings/prefix-changes`), which a second person decides in the inbox of
   ADR 0042. The API's own refusals (the last active administrator, oneself, a prefix used before) are shown by
   `ProblemAlert`; the screen hides disable and reset on one's own page but does not repeat the API's rules.
2. **A password change is due** after an administrator sets the password: when a user is created and after a reset. The
   flag is `Users.PasswordChangeDue` (`bit`, not null, default 0; migration `0021_PasswordChange`), set by `User.Create`
   from `CreateUserHandler` and by `User.ResetPassword`, cleared by `User.ChangePassword`, which raises
   `UserPasswordChanged` (audited, never with the hash). Existing users start with 0: nobody is locked out by the migration.
3. **What the token carries.** A sign-in of a user whose change is due issues a token with the claim `pwd_change` =
   `required` and a lifetime of 15 minutes (`JwtOptions.PasswordChangeLifetime`) in place of 60. The sign-in answers
   `credentialsChangeDue: true` (the word "password" stays out of every answer's body, as the tests of ADR 0031 hold).
   A user's record answers `credentialsChangeDue` too, so the administrator sees "Password to be changed".
4. **What stays open while a change is due.** One endpoint: `POST /api/v1/auth/password` (policy `SignedIn`, marked
   `[AllowWhilePasswordChangeDue]`), which takes the current password and the new one (12 to 128 characters, not the
   current one), answers 422 `password_current_wrong` for a wrong current password, and on success answers a new
   sign-in's body with a full token. Every other endpoint that needs a user answers 403 `password_change_required`
   (`PasswordChangeGateMiddleware`, after authorisation); the anonymous ones (the sign-in, the health checks) are not
   concerned. Any signed-in user may change their own password this way at any time; the shell links to it.
5. **The browser.** The session keeps `credentialsChangeDue`; `RequireAuth` sends every route to `/change-password`
   while it is set, a page outside the shell with the sign-in page's look, which signs the new token in and goes on to
   the user's landing page. Sign out stays offered.
6. **B24 closed.** `CreateUserCommand`, `ResetUserPasswordCommand`, `CreateUserRequest`, `ResetUserPasswordRequest` and
   the new `ChangeOwnPasswordCommand` and `ChangePasswordRequest` print `***` for every password, as `SignInCommand` and
   `LoginRequest` do.

## Consequences

- A new hospital's administrator sets up every member of staff from the browser; nobody keeps a password an
  administrator chose.
- The token of a user who must change is short-lived and good for one endpoint; a stolen first password still lets its
  holder choose the password, which is why the first password goes by a safe channel (the form says so).
- One more migration for the deployment step (`docs/runbook.md` section 3), and the `Database` tests of 10.1 owe their
  run on SQL Server 2022 (runbook section 8, row 10.1).
- The prefix change's request has no `Idempotency-Key` (ADR 0033), so the form sends it once and shows the outcome.

## Built note (2026-11-24)

Built as decided on the office machine: 646 Domain, 431 Application, 132 Architecture and 758 Integration tests outside
the `Database` collection; the `Database` collection's tests of this change (57) on LocalDB 2019 in a copy; 461 component
tests; `routes.spec.ts` with the six new routes (24 runs: both themes, desktop and phone, axe, no CSP refusal) and every
other stubbed browser suite in Edge against the built app. The accessible name of a prefix's "Request a change" button
is its `aria-label` ("Request a change of the prefix of bills"), which begins with the visible text.
