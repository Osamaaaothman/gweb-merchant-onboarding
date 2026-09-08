# GWEB Merchant Onboarding & Underwriting Intake Layer

> **Status: Phase 0 — repository foundation.** This README is a skeleton and grows with
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

Backend: **AWS Lambda only** (no EC2/ECS/EKS/Fargate/App Runner). API Gateway in front,
DynamoDB for metadata/workflow state, S3 for document bytes, all defined as
Infrastructure as Code (AWS SAM). Every synchronous request is bound by a **45-second
hard deadline** (internal target ≤ 35s).

Full architecture document with diagram: `docs/ARCHITECTURE.md` **(TBD — Phase 13)**.

## Tech stack

| Layer | Choice |
|---|---|
| Backend runtime | TypeScript, Node.js 20, AWS Lambda |
| IaC | AWS SAM |
| API | Amazon API Gateway (REST) |
| State | Amazon DynamoDB |
| Documents | Amazon S3 (pre-signed uploads) |
| AI evaluation | Mock adapter by default; real provider pluggable behind config |
| Frontend | React + TypeScript + Vite **(TBD — Phase 11)** |
| Testing | Jest, ts-jest |

## Prerequisites and local setup **(TBD — Phase 1)**

Will document: Node version, AWS SAM CLI, local simulation approach (`sam local`),
and the one-command local run path.

## Running tests

```bash
npm install
npm run lint
npm run build
npm test              # or: npm run test:coverage
```

Currently only a pipeline-smoke test exists (`tests/scaffold.test.ts`); real domain,
handler, and integration tests land starting Phase 1.

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

## What is real vs. mocked

- **Mocked by default:** the AI evaluation/classification provider (labelled
  `"provider": "mock"` in every response it touches).
- **Real:** everything else — validation, persistence, document storage/lifecycle,
  MCC catalog, risk policy, deadline/timeout handling.

## Test coverage

Not yet measured — will be reported here once real tests exist (Phase 1+). No coverage
number will be claimed without having actually run `npm run test:coverage`.
