# Architecture Decision Records

One file per significant decision, numbered sequentially: `NNNN-short-title.md`.

Template (see `docs/02-ENGINEERING-STANDARDS.md` §12):

```markdown
# ADR-NNNN: <title>

## Status
Accepted — <date>

## Context
<the access patterns and constraints that forced a decision>

## Decision
<what we chose>

## Alternatives considered
<option B, option C — and specifically why they were rejected>

## Consequences
<what gets easier, what gets harder, what we will regret at scale>
```

Minimum expected ADRs: runtime/language choice · IaC tool · DynamoDB table strategy ·
deadline/timeout strategy · AI adapter contract and failure mode · MCC catalog storage
and refresh strategy · risk policy representation · auth assumptions.
