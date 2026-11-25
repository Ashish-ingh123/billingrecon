# ADR 0058: Phase 10 is built against a reference hospital

- **Status:** Accepted. The owner said on 2026-11-25 that there is no real client and asked Claude Code to decide a
  hospital to build for, then approved the proposal.
- **Date:** 2026-11-25
- **Deciders:** project owner, Claude Code
- **Builds on:** `docs/build-prompts.md`, Phase 10 (the six inputs from the client, and the bar of Phase 10).
  **Amends:** those six inputs, and the first step of Prompts 10.3 and 10.4, which stopped without them.
- **Number:** 0053 to 0056 stay with Prompts 10.3 to 10.6.

## Context

Phase 10 was written to take BillingRecon to a live hospital, and six of its inputs were to come from that hospital: an
export of its old system, its document formats and its accountant's requirements, where production runs, that it can go
live with self-pay only, its staff, and its letterhead. Prompts 10.3 and 10.4 stop without them. There is no such
hospital. Waiting would leave the import of opening balances and the printed documents unbuilt, though any first
hospital needs both on its first day.

## Decision

1. **One fictional hospital stands in for the client:** `docs/reference-hospital.md`, a self-pay, multi-specialty
   hospital of about 120 beds, the kind R1 is complete for. It gives inputs 1, 2, 4, 5 and 6.
2. **Its export is synthetic and generated,** from a fixed seed, by a script in the repository; it includes the faults a
   real export has, so the import's refusals are exercised. No record is copied from anywhere.
3. **The import reads one documented CSV layout,** not another system's format. A real export is mapped to it.
4. **The tax document is built both ways** (a bill of supply, and a tax invoice when a line carries tax), and the choice
   is recorded as a chartered accountant's, open until a real hospital's accountant makes it.
5. **Input 3 stays the owner's:** hosting, the domain and who holds the secrets cost money and make accounts. Prompt
   10.5 may be written for a generic Docker host, with the deployment itself owed.
6. **What "done" means changes with it.** Prompts 10.3 and 10.4 are done against the reference hospital. Prompt 10.7
   becomes the readiness list for a first real hospital, and names everything this ADR stood in for as open.

## Consequences

- Prompts 10.3, 10.4 and the code of 10.6 can be built now.
- Nothing proves the import against a real system's data until a real export exists; the reference export only proves
  it against the faults we thought of.
- The legal and security steps of the bar (point 5) are untouched: nobody decides them here.
- Rule 6 of the bar (no real patient's data outside production) holds by construction.

## Alternatives considered

- **Wait for a client.** Leaves two first-day features unbuilt and untested for an unknown time.
- **Use a public sample of hospital data.** None is known to be free of real people's data and fit for this; a
  generated one is safer and reproducible.
