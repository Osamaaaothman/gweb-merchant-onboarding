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
target ≤ 35s, see `src/Gweb.Shared/Deadline/DeadlineBudget.cs`).

One ASP.NET Core Minimal API app, hosted as **one Lambda function** behind a single
`$default` HTTP API route (`Amazon.Lambda.AspNetCoreServer.Hosting`) — a deliberate
deviation from the architecture rules' one-Lambda-per-route preference, justified in
[`docs/adr/0001-runtime-and-language-choice.md`](docs/adr/0001-runtime-and-language-choice.md).

Full architecture document with diagram: `docs/ARCHITECTURE.md` **(TBD — Phase 13)**.
ADRs so far: [`docs/adr/0001-runtime-and-language-choice.md`](docs/adr/0001-runtime-and-language-choice.md),
[`docs/adr/0002-iac-tool-choice.md`](docs/adr/0002-iac-tool-choice.md).

## Tech stack

| Layer | Choice |
|---|---|
| Backend runtime | **ASP.NET Core Minimal API, .NET 10** (Lambda `dotnet10` managed runtime) |
| Lambda hosting | `Amazon.Lambda.AspNetCoreServer.Hosting` (`AddAWSLambdaHosting`) |
| IaC | AWS SAM (`infra/template.yaml`) |
| API | Amazon API Gateway — HTTP API |
| State | Amazon DynamoDB |
| Documents | Amazon S3 (pre-signed uploads) |
| AI evaluation | Mock adapter by default; real provider pluggable behind config |
| Frontend | React + TypeScript + Vite **(TBD — Phase 11)** |
| Testing | xUnit, coverlet |

**Why .NET 10 and not .NET 9:** .NET 9 on Lambda is container-image-only and AWS
deprecates it 2026-11-10; .NET 10 is a fully managed (zip-deploy) runtime supported
through November 2028. Both were installed locally; .NET 10 was chosen on that basis
after checking current AWS documentation rather than assuming. Full reasoning in
ADR-0001.

## Prerequisites and local setup

- **.NET 10 SDK**
- **Python 3.9+ and `pip`** (AWS SAM CLI is a Python package: `pip install aws-sam-cli`)
- **Docker** — required by `sam local start-api` / `sam local invoke`, which run the
  function inside a container matching the real Lambda runtime. `sam build` and
  `sam validate` do **not** need Docker. On Windows, put Docker Desktop's
  `resources\bin` directory on `PATH` (needed for the `docker-credential-desktop`
  helper SAM's Docker SDK looks for).

```bash
dotnet restore
dotnet build                                        # TreatWarningsAsErrors + analyzers on
sam validate -t infra/template.yaml --lint
sam build -t infra/template.yaml -b .aws-sam/build
sam local start-api -t .aws-sam/build/template.yaml # serves http://127.0.0.1:3000
curl http://127.0.0.1:3000/v1/health
```

**Important:** point `sam local start-api` at `.aws-sam/build/template.yaml` (the
*built* template), not `infra/template.yaml` (source) — a compiled runtime like .NET
has nothing to mount at `/var/task` from the source template, and the request fails
with a `502` ("executable assembly ... not found"). This is a real mistake made and
caught during this same session; documenting it here so it isn't repeated.

**Verified in this environment, for real, with Docker actually running:**
`sam validate --lint`, `sam build` (real `dotnet publish` producing a self-contained
Linux executable), and `sam local start-api`. Actual output from
`curl -i http://127.0.0.1:3000/v1/health -H "x-correlation-id: real-verification-test"`:

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"status":"ok","correlationId":"real-verification-test","remainingBudgetMs":34998}
```

And the structured log lines the container actually wrote to stdout for that request
(captured from `sam local start-api`'s own output, not hand-written):

```
START RequestId: f6cec536-c563-4c81-bff8-d6f9e86e7da4 Version: $LATEST
{"level":"INFO","timestamp":"2026-09-08T22:11:43.9898679+00:00","correlationId":"real-verification-test","applicationId":null,"handler":"health","event":"request_received"}
{"level":"INFO","timestamp":"2026-09-08T22:11:43.9910097+00:00","correlationId":"real-verification-test","applicationId":null,"handler":"health","event":"request_completed","durationMs":2}
END RequestId: f6cec536-c563-4c81-bff8-d6f9e86e7da4
REPORT RequestId: f6cec536-c563-4c81-bff8-d6f9e86e7da4	Duration: 1016.76 ms	Billed Duration: 1017 ms	Memory Size: 512 MB	Max Memory Used: 512 MB
```

`remainingBudgetMs: 34998` came from the **real** `ILambdaContext.RemainingTime` inside
the container (not the local-dev fallback constant in `Program.cs`), confirming the
`AbstractAspNetCoreFunction.LAMBDA_CONTEXT` wiring is correct against the actual Lambda
runtime emulator, not just against the WebApplicationFactory-based integration test.

## Running tests

```bash
dotnet test                                                                     # fast
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings \
  --results-directory ./coverage                                               # with coverage
```

`coverlet.runsettings` excludes compiler/source-generator-emitted code (the
`[GeneratedRegex]` state machines in `Redactor.cs`) from coverage — that code wasn't
hand-written and shouldn't be judged as if it were.

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

Recorded as they are made, with ADRs in `docs/adr/` for anything significant. The one
worth reading first: [`docs/adr/0001-runtime-and-language-choice.md`](docs/adr/0001-runtime-and-language-choice.md)
— it documents a real deviation from `docs/03-ARCHITECTURE-RULES.md` §1 (one Lambda
for the whole API, not one per route), what that costs, and the production mitigation
if it becomes a real problem.

## Known gaps

Tracked here honestly as phases land. Nothing is marked done until it is actually done
and verified — see `docs/07-DELIVERY-CHECKLIST.md`.

- **This repository was built in TypeScript first, then rewritten in ASP.NET Core /
  .NET 10** after Osama confirmed his actual runtime preference mid-session. The
  TypeScript code was fully removed rather than left alongside the .NET code. See
  ADR-0001 and `AI-USAGE.md` for the honest account of why this happened.
- **One Lambda hosts the entire API** rather than one per route — a deliberate,
  documented deviation from `docs/03-ARCHITECTURE-RULES.md` §1. See ADR-0001 for the
  IAM/cold-start cost this carries and the production mitigation if it matters later.
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

Measured by running `dotnet test --collect:"XPlat Code Coverage" --settings
coverlet.runsettings` (last run: 53 tests, all passing; generated-code excluded per
`coverlet.runsettings`):

| Assembly | Line coverage | Branch coverage |
|---|---|---|
| `Gweb.Api` | 100% | — |
| `Gweb.Config` | 100% | — |
| `Gweb.Shared` | 99.3% | 96.7% |
| **Overall** | **99.6%** | **96.7%** |

This covers only what exists so far — `Gweb.Shared` primitives, the config loader, and
the health endpoint. Numbers will be re-measured and reported per-phase as domain,
service, policy, and adapter code is added; a stale global percentage from Phase 1
will not be left standing in for what a later phase actually covers.
