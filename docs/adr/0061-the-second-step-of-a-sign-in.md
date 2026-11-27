# ADR 0061: The second step of a sign-in

- **Status:** Accepted. On 2026-11-27 the owner approved the proposal of Prompt 11.3 "as recommended", with its five
  decisions: a QR encoder of our own with the key always shown as text; the BCL's SMTP client behind a port (11.3b);
  a hold of 24 hours on a user's decisions after an administrator's reset; forced enrolment at the next sign-in with no
  grace period; and the split into 11.3a (this record) and 11.3b (the forgotten password, which will amend it).
- **Date:** 2026-11-27
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0031 (what a token carries and how long it lives; what a sign-in answers), ADR 0051 (the order of what
  a sign-in owes first), ADR 0057 (what ends a user's sessions), ADR 0032 (what a decision needs), ADR 0030 point 8 (what
  no audit image holds), ADR 0055 (a third secret file), ADR 0045 (the demo's switch).
- **Narrows:** row R3 of the threat model (review finding R5, accepted by the owner on 2026-11-26).

## Context

A sign-in was a password. Whoever learns the password of a Supervisor approves refunds and write-offs as that
Supervisor, and whoever administers a hospital can set a Supervisor's password and do the same (review finding R5).
A hospital's cash counter (Prompt 11.2) raises what a stolen approval is worth. The prompt asks for a second step by
time-based code (TOTP, RFC 6238) on the BCL with no package, required for TenantAdmin and Supervisor and offered to
the rest, and for the review finding to be weighed.

## Decision

1. **The code is TOTP as authenticator apps implement it:** HMAC-SHA-1, six digits, steps of 30 seconds (RFC 6238 over
   RFC 4226), computed in the Domain (`Totp`) from a secret and an instant, with the BCL alone. SHA-1 is what the RFC
   specifies and the only algorithm every common app reads; its weakness against collisions does not touch an HMAC.
   The analyzer's warning (CA5350) is suppressed at that one call with this reason.
2. **A code is accepted for its own step and the one before and after** (a phone's clock that drifts), **and once:**
   the user's row keeps the last step a code was accepted for, and a code of that step or an earlier one is refused.
   Every candidate is compared in constant time.
3. **The secret is 20 random bytes, encrypted at rest** with AES-256-GCM (BCL) under `TwoStep:EncryptionKey`, with a
   fresh nonce and the ids of the tenant and the user as associated data, so a value copied to another user's row does
   not open. The stored text is `v1.nonce.ciphertext.tag`; the version is there so a later key or algorithm can be
   told apart. **The key is a secret of the host of its own** (a third secret file in production, ADR 0055;
   `TWO_STEP_ENCRYPTION_KEY` in the local run), exactly 256 bits, checked at startup; the key of
   `appsettings.Development.json` is refused outside Development. It is never the signing key of the tokens: that one
   is replaced to sign everybody out (runbook section 9), and replacing this one loses every enrolment.
4. **Enrolment in three moments.** `POST /api/v1/auth/second-step/enrolment` makes a secret, stores it protected as the
   pending one and answers it once, as base32 text and as the `otpauth://` address a QR code holds;
   `.../enrolment/confirm` takes the first code of the app, makes the pending secret the user's second step, makes
   **ten recovery codes** (80 random bits each, shown this once, stored as SHA-256 hashes: a fast hash is enough for a
   value that was never a password), ends every session the user had and answers the sign-in of a new one. A second
   step is enrolled once: with one in place, beginning another is refused, so a stolen full session cannot replace it.
5. **Who must have one:** a TenantAdmin and a Supervisor. Everyone else may set one up from the shell, and is asked
   for it from then on. **No grace period:** at the first sign-in after this release an administrator or a supervisor
   without a second step is given a token that only enrols.
6. **The token between the two steps.** The claim `step` is `pending` on a token the password of a user with a second
   step gave (5 minutes, good for `POST /api/v1/auth/second-step` and the sign-out), `enrol` on a token of a user who
   must enrol (15 minutes, good for the two enrolment actions and the sign-out), `done` on a full token whose sign-in
   gave the second step, and absent on a full token of a user who has none. `SecondStepGateMiddleware` answers 403
   `second_step_required` or `second_step_enrolment_required` for anything else, as the gate of ADR 0051 does for a
   password that is due; a password that is due is changed first, and the token of the change is then one of these.
   **A token that waits for a code cannot begin an enrolment:** the password alone never replaces the second step.
   One place decides which token a user is given (`SignInTokens`), for the sign-in, the changed password and the
   second step alike. The sign-in's answer says the stage (`secondStep`), so the browser knows where to go.
7. **A decision needs the second step.** Every POST whose policy is `Decide` (every approve and reject there is or
   will be) passes `DecisionGateMiddleware`: a token without `step` = `done` is 403 `second_step_required` wherever the
   requirement is in force. With point 5 no Supervisor has such a token; the gate is what holds if one ever does.
8. **Wrong codes.** Each is saved on the user (`SecondStepFailures`) and audited (`SecondStepFailed`); the fifth in a
   row ends the user's sessions, the one between the two steps with them (401 `session_ended`), and the user signs in
   again with the password, where the limits of ADR 0035 apply. A right code or a recovery code starts the count
   again. No counter lives in memory: the limit holds across instances and restarts.
9. **An administrator's reset** (`POST /api/v1/users/{id}/second-step/reset`, `If-Match`, policy `ManageTenant`) is
   the answer to a lost phone with no recovery code: the secret and the recovery codes are gone, the user's sessions
   end, and the user enrols again at the next sign-in. Audited as `SecondStepReset` with the administrator as its user.
10. **Review finding R5: a hold of 24 hours.** After an administrator resets a user's password or second step, that
    user approves and rejects nothing until 24 hours have passed (`Users.ApprovalHoldUntilUtc`; 403 `approval_on_hold`
    from the same gate as point 7, read from the row on each decision). So an administrator who resets a Supervisor to
    sign in as them cannot approve at once; the reset is in the audit trail, the Supervisor is signed out and finds
    their second step gone, and a day passes in which that is noticed. **What stays accepted:** an administrator who
    waits a day, or who creates a new Supervisor and enrols as them (a new user has no hold; row R3 says so). The
    control is delay and evidence, not prevention: who administers a hospital's users can in the end be any of them.
11. **Nothing of the second step is in an audit image or an answer.** `SecondStepSecret`, `SecondStepPendingSecret`
    and `RecoveryCodeHashes` carry the annotation of ADR 0030 point 8; the commands, the requests and the two answers
    that hold a code, the secret or the recovery codes print `***` for them.
12. **Development and Demo.** `TwoStep:Required` may be false in those two environments only (the API refuses to
    start otherwise), so the demo's users, the load test and the browser journey sign in with a password alone. The
    Development identity (a request without a token) is no sign-in and passes the gate of point 7.
13. **The browser.** The session keeps the stage. `RequireAuth` sends a session that waits for its code to
    `/second-step` and one that must enrol to `/second-step/enrol`, both outside the shell with the sign-in page's
    look. The enrolment draws the QR code itself (`auth/qr.ts`: byte mode, level M, versions 1 to 12, the mask chosen
    by the standard's penalty rules, as an SVG path, so nginx's policy is unchanged) and always shows the key as text
    beside it. The recovery codes are shown once, after the new session is already kept. An administrator's page of a
    user shows whether a second step is set up, the hold, and the reset, asked once more.

## Consequences

- **A new secret.** Every deployment needs `TwoStep:EncryptionKey`: `.env` of the local run, the secret file in
  production, the smoke run of CI. A `.env` made before this release stops `docker compose` with the message that names
  the variable. The key belongs in the operator's vault with the backups' key: a database restored without it has
  users who must all enrol again (an administrator's reset each, or `tenant` commands for the first administrator).
- **Administrators and supervisors are stopped at their next sign-in** until they enrol. A hospital whose only
  administrator loses both phone and recovery codes needs the operator (runbook section 17) or a second administrator;
  `docs/go-live.md` asks for two administrators for that reason.
- **Tokens issued before the release** carry no `step` claim. For a Cashier nothing changes; a Supervisor's old token
  reaches everything but a decision for its remaining minutes, and then they enrol.
- **A user who changes their own password is asked for the code again:** the answer of the change is a token that
  waits for it.
- **The hold catches honest resets too.** A Supervisor who forgot their password and had it reset approves nothing
  for a day; another Supervisor or an administrator decides meanwhile. Said in the administrator's screen.
- Existing tests whose user signs in for real as a TenantAdmin or Supervisor enrol through the API as a user would
  (`Support/SecondStepApi`), or hold a role that needs no second step where the test is about something else; test
  clients are given tokens whose second step is given.

## Alternatives considered

- **A package for TOTP or for the QR code.** The prompt excludes the first; the owner chose our own encoder for the
  second. The cost is about 350 lines and a correctness risk a camera has to settle; the key as text is the answer
  when a phone will not read the code.
- **HMAC-SHA-256 or eight digits.** Stronger on paper, unread by several common apps.
- **The key of the second step derived from the signing key of the tokens.** One secret fewer, and a signing key
  replaced after an incident would destroy every enrolment.
- **A counter of wrong codes in memory**, as the sign-in limits of ADR 0035 are. A restart or a second instance would
  forget it; the row is there.
- **Preventing R5 outright:** a reset of a Supervisor as a maker-checker request of its own. It moves the problem to
  the second administrator and blocks a hospital with one. Kept for a client who asks.
- **Own removal of a second step, or new recovery codes on request.** Not built: an administrator's reset covers a
  lost phone, and fewer ways to weaken a sign-in is the point. In the backlog.

## Built note (2026-11-27, Prompt 11.3a)

- Migration `0031_SecondStep`: seven columns of `Users` (`SecondStepSecret`, `SecondStepPendingSecret`,
  `SecondStepLastStep`, `SecondStepFailures`, `SecondStepEnrolledAtUtc`, `RecoveryCodeHashes`,
  `ApprovalHoldUntilUtc`) and `CK_Users_SecondStepFailures`. Existing users start with no second step and no hold.
- The recovery codes are one column of hashes, not rows: using one rewrites the column, and nothing is deleted.
- The wrong codes of point 8 are not also fed into the account limit of ADR 0035, as the proposal said they would be:
  that limit is keyed by the hospital's code and the typed address, which the second step does not have, and the
  count on the row does the same work.
- The QR encoder is held to the error correction of the standard's worked example, to the format and version words of
  its tables, to the fixed patterns of a symbol, and to a reading of drawn symbols back into their text by a reader
  written in the test. **No camera has read one here:** the owner scans one on the personal machine (runbook section
  8, row 11.3a).
- Verified on .NET 10 with LocalDB 2019 in a throwaway copy, and in the browser by component tests only.
