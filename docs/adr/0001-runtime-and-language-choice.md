# ADR-0001: Runtime and language choice — TypeScript / Node.js on AWS Lambda

## Status

Accepted — 2026-09-09

## Context

`docs/06-COLLABORATION-PROTOCOL.md` §3 frames this as a choice between two options:

- **A) .NET 8 isolated Lambda** — the collaboration protocol's own default
  recommendation, on the reasoning that Osama can review and defend every line, and
  ownership of the code is explicitly graded.
- **B) TypeScript / Node.js** — lighter serverless ergonomics, faster cold start,
  the ecosystem most Lambda/SAM tooling and examples target first.

The kickoff conversation was cut short before Osama confirmed a preference; given the
instruction to move fast, TypeScript was picked as the default and is recorded here so
the tradeoff is visible and revisitable, not silently buried.

## Decision

TypeScript on Node.js (Lambda runtime `nodejs22.x` — the current LTS runtime at the
time this was written; `nodejs20.x` was already past its update-deprecation date),
deployed as standard (non-isolated) AWS Lambda functions, bundled per-function with
esbuild.

## Alternatives considered

- **.NET 8 isolated Lambda.** Rejected for now on cold-start weight and slower
  edit/build/test iteration during a time-constrained build — not because it is a
  worse choice for defensibility. If Osama is in fact stronger in .NET, this ADR
  should be revisited before submission; the layering rules (`docs/02-ENGINEERING-STANDARDS.md`
  §2) were followed language-agnostically, so a port would not require redesigning the
  architecture, only reimplementing it.
- **Python 3.12 on Lambda.** Not seriously considered — not in either option A/B, and
  would have introduced a third language into the kickoff decision without a stated
  reason to prefer it.

## Consequences

- Faster to iterate against the assessment's ~6–8 hour core-scope target.
- npm/TypeScript ecosystem for AWS SDK v3, esbuild, SAM's native `BuildMethod: esbuild`
  support, and Jest all fit together with minimal glue.
- Cold starts for a bundled, tree-shaken Node function are materially lighter than a
  .NET isolated worker, which matters directly for the 45-second budget's headroom.
- The risk this ADR exists to flag: if Osama cannot defend TypeScript/Node code as
  confidently as .NET in the interview, this decision actively hurts the score on the
  axis the client cares about most (ownership). This must be confirmed, not assumed —
  see the open item in `docs/PROGRESS.md`.
