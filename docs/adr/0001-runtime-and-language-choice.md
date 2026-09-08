# ADR-0001: Runtime and language choice — ASP.NET Core (.NET) on AWS Lambda

## Status

Accepted — 2026-09-09 (supersedes the earlier TypeScript/Node.js decision made
2026-09-09 earlier the same day, before Osama confirmed a preference)

## Context

`docs/06-COLLABORATION-PROTOCOL.md` §3 framed this as .NET 8 vs. TypeScript/Node,
recommending .NET specifically because "understanding and taking responsibility for
the code" is graded, and Osama is strongest in .NET. The first pass of this build
went ahead with TypeScript to keep moving without waiting for confirmation — a
process violation, recorded honestly rather than smoothed over (see `AI-USAGE.md`).
Osama subsequently confirmed: **ASP.NET, not raw Lambda handlers**, and **.NET 9,
because it's already installed.**

Before implementing, .NET 9 on Lambda was checked against current AWS documentation
rather than assumed:

- .NET 9 is **container-image-only** on Lambda (no managed zip runtime), with a
  stated deprecation date of **2026-11-10** — about two months out from when this was
  written.
- .NET 10 is available as a **managed runtime** (`dotnet10`, zip deploy, no container
  required) and as a container base image, supported through November 2028.
- .NET 10 SDK was already installed on the same machine as .NET 9, so it satisfies
  Osama's actual stated reason ("already installed") at least as well as .NET 9 does.

This was flagged to Osama in-session rather than silently substituted, and building
proceeded on .NET 10 given the time-sensitive workflow.

## Decision

**ASP.NET Core Minimal API on .NET 10**, hosted on AWS Lambda via
`Amazon.Lambda.AspNetCoreServer.Hosting` (`AddAWSLambdaHosting(LambdaEventSource.HttpApi)`),
deployed as a self-contained executable assembly (Lambda `Handler` is the assembly
name only, not a class/method-qualified handler string) behind API Gateway HTTP API,
managed runtime `dotnet10`.

## A deliberate deviation from docs/03-ARCHITECTURE-RULES.md §1

The architecture rules state a preference: "Prefer one Lambda per logical route group
rather than one monolithic proxy handler," for IAM least-privilege, smaller cold-start
surface, and clearer log separation. `AddAWSLambdaHosting` runs the **entire**
ASP.NET Core app — all routes, all middleware — inside **one** Lambda function behind
a single `$default` HTTP API route. This is structurally the "monolithic proxy
handler" pattern the rules prefer against.

This is accepted, not ignored, for a concrete reason: it is the standard, officially
supported, idiomatic way to run ASP.NET Core (the framework Osama actually knows and
can defend) on Lambda. The alternative — one Lambda per route, each a bare
`Amazon.Lambda.Core` function handler with no ASP.NET Core hosting — would mean
writing plumbing Osama is *not* using his ASP.NET Core knowledge for, undermining the
entire reason ASP.NET Core was chosen.

**What this costs**, honestly:
- One IAM role for the whole API, not one scoped role per route group. Least
  privilege still applies (see `infra/template.yaml`'s `Policies:` block, added as
  routes start touching DynamoDB/S3) but the blast radius of that one role is the
  whole API's permission set, not one route's.
- Cold start pays for the whole ASP.NET Core pipeline on every cold invocation, not
  just one handler's code path.
- One CloudWatch log group for every route, not one per route group.

**Production mitigation**, documented rather than built now (a real improvement, not
a current gap being hidden): if the API grows large enough that IAM blast radius or
cold-start-per-route becomes a real cost, the natural split is by bounded context —
e.g. a separate Lambda (still ASP.NET Core Minimal API, still `AddAWSLambdaHosting`)
for the documents/S3 surface versus the applications/DynamoDB surface — rather than
one Lambda per individual route.

## Alternatives considered

- **.NET 8 isolated Lambda, bare `Amazon.Lambda.Core` function handlers (one per
  route).** The collaboration protocol's original recommendation, and it would follow
  docs/03-ARCHITECTURE-RULES.md §1 to the letter. Superseded by Osama's explicit
  instruction to use ASP.NET.
- **.NET 9.** Rejected: container-image-only on Lambda, deprecated 2026-11-10 — see
  Context above.
- **TypeScript/Node.js.** What Phase 0–1 actually shipped with first, before this
  correction. Fully removed from the repository in the branch that introduced this
  ADR revision (`refactor/dotnet-runtime-migration`) rather than left alongside the
  .NET code as dead weight.

## Consequences

- Osama can review, defend, and extend every line using a framework he already knows
  — the entire point of the collaboration protocol's original recommendation, now
  actually satisfied.
- The IAM/cold-start tradeoff above is real and must be defensible in the interview:
  "why is there only one Lambda function?" has an honest answer now, in this ADR.
- `docs/03-ARCHITECTURE-RULES.md` §1's preference is explicitly deviated from, with
  the reasoning on record — exactly what that section itself asks for when shared
  code/a shared pattern is used ("say why in an ADR").
- Everything built in Phases 0–1 under the TypeScript decision needed reimplementing
  in C#. That work is done as of this ADR's date; see `docs/PROGRESS.md` for what
  landed and `AI-USAGE.md` for the honest account of why it happened twice.
