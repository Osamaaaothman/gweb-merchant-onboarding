# GWEB Merchant Onboarding & Underwriting Intake Layer

> **Status: Phase 1 — skeleton + cross-cutting primitives.** This README grows with
> every phase (see `docs/08-IMPLEMENTATION-PLAN.md`). Sections marked `(TBD)` are not
> built yet — that is an honest gap, not a hidden one.

## What this is

An **intake and decision-support prototype** for merchant onboarding: it collects
applicant and business information, accepts supporting documents, classifies the
merchant's business type using Merchant Category Codes (MCC), and produces an
explainable underwriting-assistance result.

**What this is not:** it does not perform authoritative identity verification, credit
decisions, sanctions screening, or final merchant approval. Every external provider
(AI evaluation, etc.) is mockable, and mock output is always labelled `"provider": "mock"`.
The most positive terminal state the system can produce is `READY_FOR_MANUAL_REVIEW` —
there is no `APPROVED` state anywhere in this codebase.

Full requirements: [`docs/00-PRODUCT-BRIEF.md`](docs/00-PRODUCT-BRIEF.md) and the
original brief at [`docs/assessment/`](docs/assessment/).

## Architecture overview

Backend: **AWS Lambda only** (no EC2/ECS/EKS/Fargate/App Runner — see `infra/template.yaml`,
which contains no such resource type). API Gateway (HTTP API) in front, DynamoDB for
metadata/workflow state, S3 for document bytes, all defined as Infrastructure as Code
(AWS SAM). Every synchronous request is bound by a **45-second hard deadline** (internal
target ≤ 35s, see `shared/deadline/DeadlineBudget.ts`).

Full architecture document with diagram: `docs/ARCHITECTURE.md` **(TBD — Phase 13)**.
ADRs so far: [`docs/adr/0001-runtime-and-language-choice.md`](docs/adr/0001-runtime-and-language-choice.md),
[`docs/adr/0002-iac-tool-choice.md`](docs/adr/0002-iac-tool-choice.md).

## Tech stack

| Layer | Choice |
|---|---|
| Backend runtime | TypeScript, Node.js (Lambda `nodejs22.x`) |
| IaC | AWS SAM (`infra/template.yaml`) |
| API | Amazon API Gateway — HTTP API |
| State | Amazon DynamoDB |
| Documents | Amazon S3 (pre-signed uploads) |
| AI evaluation | Mock adapter by default; real provider pluggable behind config |
| Frontend | React + TypeScript + Vite **(TBD — Phase 11)** |
| Testing | Jest, ts-jest |

## Prerequisites and local setup

- Node.js ≥ 20 (Lambda runtime is `nodejs22.x`; any local Node ≥ 20 works for
  `tsc`/`eslint`/`jest` — only `sam build`/`sam local` care about the target runtime)
- Python 3.9+ and `pip` (AWS SAM CLI is a Python package: `pip install aws-sam-cli`)
- **Docker** — required by `sam local start-api` / `sam local invoke`, which run each
  function inside a container matching the real Lambda runtime. `sam build` and
  `sam validate` do **not** need Docker.

```bash
npm install
npm run build                                     # compiles + type-checks everything
PATH="$(pwd)/node_modules/.bin:$PATH" sam validate -t infra/template.yaml --lint
PATH="$(pwd)/node_modules/.bin:$PATH" sam build -t infra/template.yaml -b .aws-sam/build
sam local start-api -t infra/template.yaml        # serves http://127.0.0.1:3000
curl http://127.0.0.1:3000/v1/health
```

The `PATH=...` prefix puts the project's local `esbuild` on PATH for SAM's build step
(SAM's esbuild builder looks for an `esbuild` binary on PATH; a global `npm install -g
esbuild` works too, if you'd rather not repeat the prefix).

**Verified in this environment:** `sam validate --lint` and `sam build` both succeed
against `infra/template.yaml` (real esbuild bundle of `handlers/health.ts` produced
under `.aws-sam/build/`). **Not verified here:** `sam local start-api` — this sandbox
has no Docker daemon, so the HTTP-level local run has not actually been executed by
me. The handler logic it would serve is verified a different way instead:
`tests/handlers/health.test.ts` calls the compiled handler directly against
API-Gateway-shaped event/context objects. Please run `sam local start-api` yourself
once you have Docker available, and tell me if anything doesn't match what the unit
tests predict — that's a real gap until someone actually watches curl hit port 3000.

## Running tests

```bash
npm install
npm run lint
npm run build
npm test              # or: npm run test:coverage
```

## Seeding the MCC catalog **(TBD — Phase 5)**

## Deployment instructions **(TBD — Phase 13)**

## Cleanup / teardown **(TBD — Phase 13)**

## Environment variables

See [`.env.example`](.env.example) for the full list with descriptions. Copy it to
`.env` for local development; `.env` is git-ignored and must never be committed.

## API examples **(TBD — Phase 13, OpenAPI spec)**

## Assumptions

- No authentication/authorization is implemented in this prototype. Full detail and the
  seam left for a real authorizer will be documented here once the API exists (Phase 2+).

## Tradeoffs

Recorded as they are made, with ADRs in `docs/adr/` for anything significant.

## Known gaps

Tracked here honestly as phases land. Nothing is marked done until it is actually done
and verified — see `docs/07-DELIVERY-CHECKLIST.md`.

- **`sam local start-api` not actually executed.** Verified up through `sam build`;
  the HTTP-level local run needs Docker, which this build environment doesn't have.
  See "Prerequisites and local setup" above.
- **Runtime/language choice (TypeScript vs .NET 8) was not confirmed with Osama**
  before implementation started — see `docs/adr/0001-runtime-and-language-choice.md`.
  If Osama is meaningfully stronger in .NET, this should be revisited: the layering
  is language-agnostic, so a port would not require redesigning anything, only
  reimplementing it.
- **S3 CORS `AllowedOrigins` is `["*"]`.** Fine for local development against no fixed
  frontend origin yet; must be tightened to the real web app origin once one exists
  (Phase 11).
- **DynamoDB table is a Phase-1 skeleton** (bare `pk`/`sk`, no GSIs). The real
  access-pattern design and single- vs multi-table justification land in ADR-0003 in
  Phase 2, before any application data is modeled against it.
- **No authentication/authorization** — see Assumptions above.
- **No AWS deployment executed.** Local-first per `docs/06-COLLABORATION-PROTOCOL.md`
  §3; a real `sam deploy` (and its teardown script) is scoped for Phase 13, contingent
  on Osama providing AWS account details.

## What is real vs. mocked

- **Mocked by default:** the AI evaluation/classification provider (labelled
  `"provider": "mock"` in every response it touches).
- **Real:** everything else — validation, persistence, document storage/lifecycle,
  MCC catalog, risk policy, deadline/timeout handling.

## Test coverage

Measured by running `npm run test:coverage` (last run: 55 tests, 7 suites, all passing):

| | Statements | Branches | Functions | Lines |
|---|---|---|---|---|
| All files (`shared/`, `config/`, `handlers/`) | 99.37% | 94.64% | 100% | 99.37% |

This covers only what exists so far — `shared/` primitives, the config loader, and the
health handler. Numbers will be re-measured and reported per-phase as `domain/`,
`services/`, `policy/`, and `adapters/` are added; a stale global percentage from
Phase 1 will not be left standing in for what a later phase actually covers.
