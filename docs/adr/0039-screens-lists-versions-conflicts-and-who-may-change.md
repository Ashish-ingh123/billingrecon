# ADR 0039: Screens: lists in the address, versions and conflicts, who may change, and decimals

- **Status:** Accepted. The owner approved the plan of Prompt 7.2 on 2026-11-23 "as recommended", with its two answers
  (points 6 and 7 below). The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0038 (the frontend), ADR 0007 (row versions, ETag and If-Match), ADR 0018 (the HTTP contract of a
  resource), ADR 0032 (one policy per capability).

## Context

Prompt 7.2 builds the first screens that read and change records: patients, payers and service items, each with a
list, a record, a form, and deactivate and reactivate. Prompts 7.3 to 7.6 build many more on the same contract
(`docs/api.md` sections 4 to 6): pages of `{ items, page, pageSize, totalCount }`, a `version` on every record, `If-Match`
on every change, 409 `concurrency_conflict` when someone changed the record first, and a policy per capability. How a
screen does each of these is decided once here, so that every screen does it the same way.

## Decision

1. **A list keeps its search, its filters and its page in the address** (`?search=&status=&page=`), so Back, a refresh and
   a link show the same list. A search is sent when the user submits it, not on each key. A page past the end says so and
   offers the first page.
2. **The version a change sends is the `version` of the record as the screen last read it**, written as the strong ETag
   the API expects (`"<version>"`, `docs/api.md` section 4), in `If-Match` on every `PATCH` and every `POST` action of a
   record. An edit form keeps the record it started from, and a refresh of the record in the background does not move it.
3. **Who may change is a copy of the API's policies** (`src/auth/permissions.ts`), one entry per policy of
   `src/BillingRecon.Api/Common/Authorization/Policies.cs`, with the same name and roles; a frontend test reads that file
   and fails when the copy differs. A screen shows an action only to the roles of its policy; the read-only roles see the
   same screens without it. The API still checks every request: a hidden button is a convenience, not a control.
4. **Deactivating asks once more, in place**: the button becomes "Confirm" and "Cancel". No dialog library.
5. **Decimals are typed as text and sent as JSON numbers.** The form checks the shape (digits, the decimals the field
   allows) and refuses a value with more than 15 significant digits, the most a JSON number keeps exactly in a browser;
   so the API receives what was typed, or nothing. The API reads a number written as a string too (ADR 0038, built
   note), but the frontend does not rely on it: the contract says numbers. Amounts are shown with Indian grouping.
6. **Payers have a section of their own** in the navigation (`/payers`), beside Patients and Service items. A payer is
   created from its patient's page (linked to that patient) or from the list of payers (a guarantor).
7. **A conflict (409 `concurrency_conflict`) is shown, explained and decided by the user.** The edit form says
   "Someone else changed this record while you were editing it. Your changes have not been saved.", reads the record
   again, lists the fields whose value is no longer the one the user started from, and offers two choices: **Load the
   latest version** (the user's changes are dropped) or **Keep my changes** (the form keeps them and now rests on the
   latest version, so saving replaces it knowingly). A deactivate or reactivate that meets a conflict says the same and
   shows the record as it is now. This is what `docs/api.md` section 4 asks of a client: read again, show what changed,
   decide; the API never merges.
8. **The API's messages for a field are shown at the field**; every other refusal in the one error component of ADR 0038
   (`ProblemAlert`).

## Alternatives

- **The list's state in memory** (React state): lost on Back and on a refresh, and a list cannot be linked.
- **A search on every key**, debounced: more requests, and a list that moves under the user's hand.
- **The `ETag` header instead of the body's `version`**: the same value; the body's is at hand in a list as well.
- **Permissions as a table in the frontend alone**: it would drift from the API unseen.
- **A modal dialog to confirm**: a dialog library or a hand-made focus trap, for a reversible action.
- **Decimals sent as strings**: exact for any length, but on a reading of the API that its contract does not promise.
- **One "Patients and payers" section with two tabs** (the alternative to point 6): one link fewer, two levels of
  navigation.
- **A conflict that only offers to reload** (the alternative to point 7): simpler, but the user loses their changes
  without seeing why.

## Consequences

- Every list and form of 7.3 to 7.6 follows points 1 to 8; a new policy of the API fails the frontend's test until the
  copy has it.
- The navigation test's copy of the matrix gains `/payers` for all five roles (the matrix row "Patients, payers, service
  items").
- A screen that changes a record must keep the version it read; a screen that only reads needs none.

## Built (2026-11-23)

Built as decided. What the build found:

- **The OpenAPI snapshot of 7.1 would have failed in CI.** The descriptions of the API's document come from the XML
  comments of the source, and the generator joins their lines with the machine's line break: `\r\n` on Windows, `\n` on
  the Linux runners. `OpenApiSnapshotTests` now writes and compares the document with every `\r\n` inside it as `\n`, so
  the snapshot is the same whichever machine wrote it (242 descriptions changed in the committed copy; the generated
  types did not).
- **The query parameters of a list are named as the API binds them** in the document (`Search`, `Status`, `Page`,
  `PageSize`), which the API reads without regard to case; `docs/api.md` writes them in lower case. The address of a
  list keeps the lower-case names of point 1.
- **`If-Match` from the body's `version` is what the API answers as its ETag**: seen against the API (`ETag:
  "AAAAAAAAB9U="`, `version: AAAAAAAAB9U=`).
- **The policies' copy is checked against `Policies.cs`** by `src/auth/permissions.test.ts`, which reads the C# file
  from the repository (CI checks out all of it); a change of a role of any of the 17 policies fails it.
- **After a change, a screen sets the record it got back** in the query cache and refreshes the lists only, not the
  record itself, which would be read twice for nothing.
- **Tests** (`docs/testing-strategy.md` section 5a): the screens are tested against `routedApi`, a fake that answers by
  method and path, since a record's page asks for more than one thing at once (a patient and their payer). 59 tests
  beyond 7.1's: the lists (search, status and page in the address and in the request, empty, paging), the records by
  role (actions shown to the writers only, deactivate with its confirmation and its version, a refusal, a conflict), the
  forms (required fields, the API's field messages at their fields, `If-Match`, both choices after a conflict), payers
  from a patient and as guarantors, the decimals as sent, the policies against the API's, the formats in Indian style.
  With the `If-Match` unquoted, a conflict that lists nothing, every role allowed to change, the status filter not sent
  and the digit limit dropped, one at a time, each is caught.
- **In the browser**, against the API in Development on a throwaway LocalDB database: a BillingClerk paged through 23
  patients, met the API's refusal of a short phone number at its field and of a taken MRN in the error component,
  registered a patient, created their payer from their page, met a real conflict (the record changed through the API
  behind the open form: Phone and City listed), kept their changes and saved over it, created a service item whose price
  of 1250000.1234 came back exactly, and deactivated it after the confirmation; a Viewer read the patient and their payer
  with no action offered and was refused the registration form by its address.

## Amended 2026-11-25 (Prompt 10.2b)

Point 1 said "a search is sent when the user submits it, not on each key", and "a search on every key" was a rejected
alternative. The owner reversed it: no Search or Find button is pressed before a list answers.

- **A search is sent while it is typed**: after a pause of 300 ms, from 2 characters; an emptied box shows the whole
  list again; Enter sends at once, whatever the length. One hook, `useLiveSearch` (`src/components`), serves the lists
  of patients, payers and service items (`ListFilters`), the users, and the outstanding report. The Search and Find
  buttons are gone.
- **What the rejected alternative feared is answered.** "More requests": one per pause, and a request that a later key
  made stale is cancelled through the query's abort signal; its answer, if it still comes, belongs to another query
  key and is never shown. "A list that moves under the user's hand": the rows on screen stay until the new ones arrive
  (`placeholderData: keepPreviousData`), and a browser test holds the layout shift under 0.01.
- **The address still carries the search** (`?search=`, `?email=`), but a search replaces the entry of the history
  instead of pushing one, so Back leaves the list and does not walk through every key. A status, a role and a page
  still push. A search returns to page 1.
- **The box keeps what is typed.** The address holds the search trimmed; the box is set from the address only when the
  address changed by something else (Back, a link), so a space typed between two words is not taken away.
- **The server** matches a part of the text everywhere (ADR 0018, amended the same day).
- **Not built:** a text search on the lists of bills, payments, credit notes, adjustments and refunds, which have none;
  it needs new parameters in the API and is a later prompt.
- **Tests:** `LiveSearch.test.tsx` (nothing before the pause and one request after it; one character sends nothing; an
  emptied box; Enter; the rows kept; a stale request cancelled and its answer dropped; the address replaced and Back;
  page 1; the users and the outstanding report) and `e2e/search.spec.ts` in a browser, both themes.
