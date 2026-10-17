# ADR 0000: Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-09-26
- **Deciders:** project owner, Claude Code

## Context

This project is a financial system built incrementally over five releases by a small team working
with an AI coding assistant. Decisions about frameworks, patterns, schema conventions and security
controls are made continuously, often in chat, and are lost when a conversation ends. Later
contributors, human or AI, then either re-litigate the decision or unknowingly violate it.
`CLAUDE.md` holds the current rules, but it records *what* the rules are, not *why* they were
chosen or what was rejected.

## Decision

We record every architecturally significant decision as an Architecture Decision Record (ADR)
in `docs/adr/`.

- **One file per decision**, named `NNNN-short-kebab-title.md`. `NNNN` is a zero-padded
  sequence number, allocated in order and never reused.
- **Header** with Status, Date and Deciders. Status is one of `Proposed`, `Accepted`, `Rejected`,
  `Deprecated`, or `Superseded by ADR NNNN`.
- **Four body sections, always in this order:** Context, Decision, Alternatives, Consequences.
- **ADRs are immutable once accepted**, apart from updating the Status line and adding links.
  Changing a decision means writing a new ADR that supersedes the old one.
- An ADR is written **when the decision is made**, in the same change as the code that applies it,
  so it is reviewed alongside that code.
- Any ADR that changes a rule in `CLAUDE.md` is linked from `CLAUDE.md`.
- "Architecturally significant" means: hard to reverse, cross-cutting, affects the financial
  correctness rules, adds or removes a dependency, or a future reader would ask "why is it like this?"

### The format

**Context.** The forces at play: the problem, constraints, requirements, assumptions, and why
the decision is needed now. State facts, not preferences. A reader should be able to tell whether
the context still holds today.

**Decision.** What we will do, in the active voice ("We will ..."). Precise enough that someone
can check whether code complies with it.

**Alternatives.** The options that were seriously considered, each with a sentence on why it was
not chosen. This is what stops the same debate from being repeated.

**Consequences.** What becomes easier, what becomes harder, new risks and costs, follow-up work,
and the conditions under which the decision should be revisited. Include the negative consequences.

## Alternatives

- **No records; decisions live in chat history and people's heads.** Cheapest today, but the
  reasoning is unrecoverable once the conversation is gone, and an AI assistant starting a new
  session has no access to it.
- **A wiki or Confluence space.** Not versioned with the code, not reviewed in pull requests,
  and drifts from what is actually built.
- **One large design document.** Goes stale quickly and hides what changed, when and why.
- **A heavier template (full MADR, Y-statements).** More fields than a small team will fill in
  consistently. The four-section format can be extended later if needed.

## Consequences

- Decisions are versioned, reviewable in pull requests next to the code, and survive context loss.
- New contributors and new AI sessions can read the reasoning, not only the rules.
- Small overhead per decision, and discipline is needed to write the ADR at decision time rather
  than afterwards.
- The first decisions to record: **ADR 0001** target framework (.NET 8 LTS, installed, versus
  .NET 10 LTS, not installed, given .NET 8 support ends 10 Nov 2026), and **ADR 0002** solution
  and namespace layout under the confirmed product name.

## Template

Copy this into `docs/adr/NNNN-title.md`:

```markdown
# ADR NNNN: <Title>

- **Status:** Proposed
- **Date:** YYYY-MM-DD
- **Deciders:** <names>

## Context

<Forces, constraints, problem statement. Facts.>

## Decision

We will <decision>.

## Alternatives

- **<Option A>**: <why not>
- **<Option B>**: <why not>

## Consequences

- <easier>
- <harder / risk / cost>
- <follow-up work; when to revisit>
```
