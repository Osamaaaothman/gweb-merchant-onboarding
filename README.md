# GWEB Merchant Onboarding & Underwriting Intake Layer

> **Status: Phase 13 — documentation & IAM hardening.** This README grows with
> every phase (see `docs/08-IMPLEMENTATION-PLAN.md`). Sections marked `(TBD)` are not
> built yet — that is an honest gap, not a hidden one.

## Deliverables, per the brief's §12

| Deliverable | Where |
|---|---|
| Source repository | This repo -- `src/` (backend), `frontend/` (SPA), `tests/`, `infra/` (IaC) |
| Runnable demo | [`docs/DEMO.md`](docs/DEMO.md) -- local reproduction; see "Deployment instructions" below for the (unexecuted, see Known Gaps) real-AWS path |
| Architecture document + diagram | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) |
| API documentation | [`docs/openapi.yaml`](docs/openapi.yaml) (OpenAPI 3.0) |
| MCC implementation | "Seeding / refreshing the MCC catalog" below, [`docs/adr/0004-mcc-catalog-storage.md`](docs/adr/0004-mcc-catalog-storage.md), [`docs/adr/0005-risk-policy-representation.md`](docs/adr/0005-risk-policy-representation.md) |
| Test evidence | [`docs/TEST-EVIDENCE.md`](docs/TEST-EVIDENCE.md) |
| Security note | [`docs/SECURITY.md`](docs/SECURITY.md) |
| Demo walkthrough | [`docs/DEMO.md`](docs/DEMO.md) |
| AI usage report | [`AI-USAGE.md`](AI-USAGE.md) |

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

Application state lives in a **single DynamoDB table** (`gweb-applications-{stage}`),
one item type per aggregate member under a shared `APP#{id}` partition key — see
[`docs/adr/0003-dynamodb-table-strategy.md`](docs/adr/0003-dynamodb-table-strategy.md)
for the full access-pattern table. So far: `Application` (create + resume, Phase 2),
`Applicant` and `Business` (full field set from the brief §3.1/§3.2, PATCH semantics,
Phase 3), `Document` (presign/complete lifecycle, Phase 4, `DOC#{documentId}` sort key
under the same `APP#{id}` partition). Evaluation items land in a later phase using the
same table, no new IaC resource per entity.

Documents follow the brief's exact lifecycle (`REQUESTED → UPLOADING → RECEIVED →
PROCESSING → ACCEPTED | NEEDS_REVIEW | REJECTED`, `src/Gweb.Domain/Documents/Document.cs`).
The client uploads straight to S3 via a pre-signed **POST** (not PUT — only a presigned
POST can enforce a content-length-range condition, `src/Gweb.Adapters.Storage/S3DocumentStorage.cs`);
Lambda never receives a document body. Content-Type, size range, and the client-declared
SHA-256 checksum are all pinned as S3 policy conditions, so a mismatched upload is
rejected by S3 itself before this system ever sees it. `complete()` re-verifies anyway
(checksum via `ChecksumMode.ENABLED`, plus a 16-byte ranged read for a file-signature/magic-byte
check) as defense in depth, and is idempotent — a repeat call after the document has
left `Uploading` just returns the current record.

Government ID numbers, EIN/UBI, and bank account numbers are **masked at capture**:
the domain layer extracts only the last 4 characters the moment a PATCH request
arrives and discards the rest immediately (`GovernmentIdentification.FromFullNumber`,
`RegistrationIdentifier.FromFullValue`, `SettlementBankAccount.FromFullAccountNumber`
in `src/Gweb.Domain/Applications/`) — the full value is never stored, never logged,
and never returned in a response, because it never exists anywhere past that one
conversion call.

The MCC catalog (`GET /v1/mcc?query=...`) is **packaged static data**, not DynamoDB —
~300 read-only reference rows with no per-application state and no need for a
database's guarantees. See [`docs/adr/0004-mcc-catalog-storage.md`](docs/adr/0004-mcc-catalog-storage.md)
for the full reasoning, including an honest note on where the data came from (the
brief's named source, the Visa Merchant Data Standards Manual, is a paid/licensed
document this session has no access to — the catalog was compiled from the
well-established public MCC taxonomy instead, flagged as a deliberate substitution,
not a silent one).

Risk policy (`IRiskPolicy` / `StaticRiskPolicy`) is structurally independent of the MCC
catalog — it takes a bare MCC code string, never an `MccCode`, so the two can change
without touching each other (brief's core rule for this area). Risk level
(`Standard`/`EnhancedReview`/`Restricted`) is computed from packaged, hand-authored
configuration data with per-acquirer overrides, never hardcoded per MCC in C#. See
[`docs/adr/0005-risk-policy-representation.md`](docs/adr/0005-risk-policy-representation.md).
No endpoint yet — it's consumed internally once Phase 7 (classify) and Phase 8
(evaluate) exist. **No auto-approval path exists anywhere in this codebase** — enforced
structurally by `NoAutoApprovalPathTests`, which fails immediately if any domain enum
ever grows an "Approved" value, not just documented as a promise.

MCC classification (`POST /v1/applications/{id}/classify`) goes through `IEvaluationProvider`
-- `MockEvaluationProvider` by default (deterministic, offline, reuses real catalog
hints) or `GeminiEvaluationProvider` when `AI_PROVIDER=gemini` and a real key is
configured. Both are grounded against `IMccCatalog` (never free-form) and every
candidate is re-validated against the real catalog before being persisted -- a
hallucinated code is dropped even if the model ignores its instructions.
`ClassificationService` falls back to the mock provider (clearly labelled
`"provider": "mock"`) if the real provider fails for any reason, so a caller always
gets a usable, honest result. Both the applicant's confirmed/corrected MCC and the
system's proposed one are persisted (`McClassification`), so a mismatch is visible to
a reviewer. See [`docs/adr/0006-ai-evaluation-provider.md`](docs/adr/0006-ai-evaluation-provider.md)
for the full design, including a real empirical finding (this model's latency runs
close to the system's 35s internal target) and how the code defends against it.
When the business description has no genuine keyword match in the catalog at all
(found via live testing, Phase 11: `ClassificationService` used to silently substitute
the catalog's browsing default and let a provider propose one of those arbitrary codes
at full confidence), every candidate's confidence is capped at 35% and its explanation
replaced with an honest "no confident match" message -- an unrelated business
description never again gets shown as a 90%-confidence answer. The frontend also lets
the applicant search the real catalog directly (`GET /v1/mcc?query=...`) and pick a
code by hand instead of relying on either provider at all.

Rate evaluation (`POST /v1/applications/{id}/evaluate`) extends the same provider with
real **multimodal** statement extraction -- `GeminiEvaluationProvider` sends the actual
document bytes to Gemini as an inline part (verified against the live API: a realistic
synthetic statement was correctly parsed into every normalized field plus a one-sentence
commentary, in ~6 seconds). `EffectiveRateCalculator` is pure, deterministic arithmetic
that never reads anything but the five numeric fields a provider reported -- no AI
output can reach the math through any other path. `RiskSignalDetector` is equally
deterministic: every signal (contradictions, missing evidence, unusually high ticket,
MCC mismatch, incomplete ownership, regulated-activity mentions) is computed from data
already in the system, and every one cites a `sourceField` (brief "no unexplained
flags"). The response separates `extracted` / `calculated` / `commentary` as distinct
top-level fields. If the deadline budget is too low to attempt evaluation at all, the
endpoint returns `202` with the record marked `Processing`, pollable via `GET`. See
[`docs/adr/0007-rate-evaluation-and-risk-signals.md`](docs/adr/0007-rate-evaluation-and-risk-signals.md).

Submission (`POST /v1/applications/{id}/submit`) is a real gate, not a rubber stamp:
blocked with the precise missing items (`400`, listing missing applicant fields,
missing business fields, and missing required document types) until the applicant,
business, and all three required documents (Government ID, Business Registration,
Bank Evidence) are genuinely complete -- reusing the same `CompletenessChecker` the
aggregate `GET` endpoint already uses, so what blocks submission can never drift from
what a review screen would show as missing. A successful submission returns the
**normalized review payload** the brief asks for (masked applicant/business, every
document's status, the current MCC classification, the current evaluation) and locks
the application at `Submitted` -- still no `Approved` state anywhere in this codebase,
enforced structurally by `NoAutoApprovalPathTests`. See
[`docs/adr/0008-submission-gate.md`](docs/adr/0008-submission-gate.md).

Deadline hardening (Phase 10): every DynamoDB/S3/Gemini call already routed through a
budget-derived timeout in earlier phases -- this phase audited that end-to-end (no gaps
found) and added `Gweb.Shared.Resilience.BoundedRetry`, a budget-aware bounded retry
with exponential backoff and full jitter, wired into the Gemini adapter only. DynamoDB
and S3 deliberately do **not** get a second app-level retry layer -- the AWS SDK already
retries transient failures on those internally, and stacking an uncoordinated retry loop
on top would risk retry amplification rather than add safety. Gemini has no SDK and no
built-in retry, and is the one adapter that has hit a real transient failure in this
project (a live HTTP 503 during Phase 8's manual testing). See
[`docs/adr/0009-deadline-hardening-and-retry.md`](docs/adr/0009-deadline-hardening-and-retry.md).

The frontend (Phase 11, `frontend/`) is a Vite + React + TypeScript SPA -- Tailwind v4 +
hand-authored shadcn/ui-style components, TanStack Query for server state, Zustand for
UI-only state, react-hook-form + zod mirroring the backend's own field validation,
`motion` for restrained, deliberate animation. Talks to the real backend over the exact
API surface documented below -- no separate "frontend API," no duplicated business
logic. Building it surfaced and closed a real backend gap (a client-facing
"list documents for an application" route never existed) and, for the first time in
this project, exercised a genuinely real S3-compatible document upload end-to-end
(MinIO, not just the in-memory adapter) -- see "Frontend" and the S3 section below, and
[`docs/adr/0010-frontend-architecture.md`](docs/adr/0010-frontend-architecture.md) for
the full design rationale.

Phase 12 adds the brief's specifically-named mandatory single integration test --
create → update applicant/business → pre-sign → mock upload completion → classify →
confirm → evaluate → submit, chained in one continuous run through the real HTTP
pipeline. See "Running tests" below and
[`docs/adr/0011-end-to-end-journey-test.md`](docs/adr/0011-end-to-end-journey-test.md).

Full architecture document with diagram: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
ADRs so far: [`docs/adr/0001-runtime-and-language-choice.md`](docs/adr/0001-runtime-and-language-choice.md),
[`docs/adr/0002-iac-tool-choice.md`](docs/adr/0002-iac-tool-choice.md),
[`docs/adr/0003-dynamodb-table-strategy.md`](docs/adr/0003-dynamodb-table-strategy.md),
[`docs/adr/0004-mcc-catalog-storage.md`](docs/adr/0004-mcc-catalog-storage.md),
[`docs/adr/0005-risk-policy-representation.md`](docs/adr/0005-risk-policy-representation.md),
[`docs/adr/0006-ai-evaluation-provider.md`](docs/adr/0006-ai-evaluation-provider.md),
[`docs/adr/0007-rate-evaluation-and-risk-signals.md`](docs/adr/0007-rate-evaluation-and-risk-signals.md),
[`docs/adr/0008-submission-gate.md`](docs/adr/0008-submission-gate.md),
[`docs/adr/0009-deadline-hardening-and-retry.md`](docs/adr/0009-deadline-hardening-and-retry.md),
[`docs/adr/0010-frontend-architecture.md`](docs/adr/0010-frontend-architecture.md),
[`docs/adr/0011-end-to-end-journey-test.md`](docs/adr/0011-end-to-end-journey-test.md).

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
| Frontend | React 19 + TypeScript + Vite, Tailwind v4 + shadcn/ui, TanStack Query, Zustand, react-hook-form + zod, motion (`frontend/`) — see `docs/adr/0010-frontend-architecture.md` |
| Testing | xUnit, coverlet, Moq |
| Persistence | Amazon.DynamoDBv2 SDK, single-table (`docs/adr/0003-dynamodb-table-strategy.md`) |

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

### Testing the `/v1/applications` endpoints against a real DynamoDB

Two options, both real (neither is the `InMemoryApplicationRepository` mock):

**A) Against DynamoDB Local**, without needing an AWS account at all:

```bash
docker run -d --name dynamodb-local -p 8000:8000 amazon/dynamodb-local:latest \
  -jar DynamoDBLocal.jar -inMemory -sharedDb
# create the table once (see docs/adr/0003-dynamodb-table-strategy.md for the schema:
# pk/sk, both String, both required)

PERSISTENCE_PROVIDER=dynamodb \
APPLICATIONS_TABLE_NAME=gweb-applications-local \
DYNAMODB_SERVICE_URL=http://localhost:8000 \
AWS_ACCESS_KEY_ID=local AWS_SECRET_ACCESS_KEY=local AWS_REGION=us-east-1 \
dotnet run --project src/Gweb.Api

curl -i -X POST http://localhost:5243/v1/applications
curl -i http://localhost:5243/v1/applications/<id-from-the-response-above>
```

`DYNAMODB_SERVICE_URL` is read only in `Program.cs`'s DynamoDB branch and is unset in
every deployed environment — it exists purely so the real repository code can be
pointed at DynamoDB Local instead of AWS, with no code change.

**Actually run against a real (local) DynamoDB — full journey, re-verified 2026-09-09
once Docker was available again:** `POST /v1/applications` → `201`; `GET` on that ID →
`200` with the same application (resume); `PATCH .../applicant` → `200`, government ID
masked to last4 in the response, persisted for real; `PATCH .../business` → `200`,
persisted; `GET /v1/applications/{id}` afterward returns the full aggregate with both,
completeness correctly listing only the fields still missing; `POST .../documents/presign`
→ `201`, a real `Document` row written to the same live table, plus a genuinely
SigV4-signed presigned-POST policy (local signing, no network call, so it succeeds even
without a real bucket); `POST .../complete` against that same (non-existent-in-this-run)
S3 bucket correctly returns `503 DEPENDENCY_UNAVAILABLE` rather than silently
succeeding — proving the error-mapping path, not just the happy path. `GET` on a random
well-formed UUID → `404 NOT_FOUND`; `GET /v1/applications/not-a-guid` → `400
VALIDATION_FAILED`. This closes the gap noted in earlier phases ("Phase 3's
`Applicant`/`Business` repositories were not re-verified against a live DynamoDB
Local") — they now have been, along with Phase 4's `Document` repository, which hadn't
been live-verified even once before.

**Updated 2026-09-09 (Phase 11): a real S3-compatible upload has now actually
happened.** `S3_SERVICE_URL` mirrors `DYNAMODB_SERVICE_URL` (same
override-with-no-code-change mechanism) and points the real `AmazonS3Client` at a local
MinIO container instead of AWS:

```bash
docker run -d --name minio -p 9000:9000 -p 9001:9001 \
  -e MINIO_ROOT_USER=minioadmin -e MINIO_ROOT_PASSWORD=minioadmin \
  minio/minio server /data --console-address ":9001"
docker exec minio mc alias set local http://localhost:9000 minioadmin minioadmin
docker exec minio mc mb local/gweb-documents-local

PERSISTENCE_PROVIDER=dynamodb \
APPLICATIONS_TABLE_NAME=gweb-applications-local \
DYNAMODB_SERVICE_URL=http://localhost:8000 \
DOCUMENTS_BUCKET_NAME=gweb-documents-local \
S3_SERVICE_URL=http://localhost:9000 \
AWS_ACCESS_KEY_ID=minioadmin AWS_SECRET_ACCESS_KEY=minioadmin AWS_REGION=us-east-1 \
dotnet run --project src/Gweb.Api
```

`POST .../documents/presign` → a real MinIO-signed presigned POST (same SigV4 shape
S3 itself uses); POSTing an actual file to that URL with every `uploadFields` entry as
a form field ahead of `file` → a genuine `204` from MinIO, the object actually stored;
`POST .../complete` immediately afterward → `200`, `"status":"Received"`, real
`actualSizeBytes`/checksum verification against the bytes MinIO actually has, not a
declared value taken on faith. Exercised for all three required document types this
way, then through the full frontend journey end-to-end (see "Frontend" below) — this
closes what was, until this phase, the project's single longest-standing Known Gap
("no real S3 bucket has ever received an actual uploaded byte"). One real limitation
hit along the way: this MinIO version's bucket-CORS API rejected every configuration
tried, so a real browser's cross-origin upload to `localhost:9000` needed a
local-dev-only Vite proxy workaround — see `docs/adr/0010-frontend-architecture.md` and
`frontend/vite.config.ts`'s comment. This does not apply to a real deployed frontend
talking to real S3.

**B) `sam local start-api`** — also re-verified for real this session, including a
genuine cold `Building image...` pull of the `dotnet10` Lambda runtime image (several
minutes the first time; instant afterward). `GET /v1/health` and `GET /v1/mcc?query=...`
both returned real `200`s from inside the actual Lambda runtime emulator container, not
just `WebApplicationFactory`. **Still open, not re-tested this session:** the earlier
finding that `--env-vars` (for forcing `PERSISTENCE_PROVIDER=inmemory` inside the
container) is parsed by the SAM CLI but does not change which branch `Program.cs` takes
at cold start. Option A above remains the verified path for the DynamoDB-backed flow.

## Frontend

```bash
cd frontend
npm install
npm run dev      # http://localhost:5173, proxies /v1 to http://localhost:5243
```

Run the backend separately first (`dotnet run --project src/Gweb.Api`, any of the
options above — `PERSISTENCE_PROVIDER=inmemory` is enough for the full click-through
journey except a real file landing in S3; see the MinIO section above for that).
`frontend/vite.config.ts`'s dev-server `proxy` sends every `/v1/*` request to
`http://localhost:5243`, so the frontend never needs CORS configured on the backend for
local development, and every API call in the browser is a same-origin request.

`npm run build` produces a static bundle in `frontend/dist/` — deployable to any static
host (S3+CloudFront, in a real AWS deployment) once one exists; not deployed anywhere
in this session (see Known Gaps). `VITE_API_BASE_URL` (unset by default, meaning
"same origin") is the only environment-specific value a real deployment would set, to
point the built bundle at the real API Gateway URL instead of a dev-server proxy.

Full design rationale — framework choice, state-management split, the shadcn/ui CLI
bugs worked around, the MinIO CORS workaround — in
[`docs/adr/0010-frontend-architecture.md`](docs/adr/0010-frontend-architecture.md).

## Running tests

```bash
dotnet test                                                                     # fast
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings \
  --results-directory ./coverage                                               # with coverage
```

`coverlet.runsettings` excludes compiler/source-generator-emitted code (the
`[GeneratedRegex]` state machines in `Redactor.cs`) from coverage — that code wasn't
hand-written and shouldn't be judged as if it were.

### The mandatory end-to-end journey test

```bash
dotnet test --filter FullyQualifiedName~EndToEndJourneyTests
```

[`tests/Gweb.Tests/EndToEnd/EndToEndJourneyTests.cs`](tests/Gweb.Tests/EndToEnd/EndToEndJourneyTests.cs)
is the brief's specifically-named integration test: create application → update
applicant → update business → pre-sign upload → mock upload completion (for all three
required document types) → classify → confirm → evaluate → submit, in one continuous
run through the real ASP.NET Core pipeline (`WebApplicationFactory`, never a
service-layer shortcut). Runs green from a clean checkout with no external
dependencies — `TestEnvironment.cs` forces `PERSISTENCE_PROVIDER=inmemory` and
`AI_PROVIDER=mock` for the whole automated suite, so this test needs no Docker, no
DynamoDB Local, no MinIO, and no network access, unlike the live MinIO/DynamoDB Local
verification documented above (which is real but manual, not part of this command).
Also asserts along the way: submission is correctly blocked (`400`, precise missing
document list) before any documents exist; masked values (government ID, EIN, bank
account) never appear unmasked anywhere, including in the final submit payload; a
second submit on an already-submitted application correctly returns `409`, not a
silent no-op or a second `200`.

## Deployment instructions

**Not executed against a real AWS account this session** — no AWS account was
available (see Known Gaps). `sam validate --lint` and `sam build` (a real
`dotnet publish` producing a self-contained Linux executable) have both been run for
real; `sam deploy` has not. These are the commands a real deployment would use:

```bash
sam build -t infra/template.yaml -b .aws-sam/build

# First deploy: --guided walks through stack name, region, and parameter values
# interactively (Stage, DeadlineTargetMs, PresignTtlSeconds, AiProvider, GeminiModel,
# AiApiKey -- leave AiApiKey empty to stay on the mock provider, no credentials
# needed) and saves the answers to samconfig.toml for future non-interactive deploys.
sam deploy --guided -t .aws-sam/build/template.yaml

# Subsequent deploys, once samconfig.toml exists:
sam deploy -t .aws-sam/build/template.yaml

# The stack's HttpApiUrl output is the base URL for every API example below.
aws cloudformation describe-stacks --stack-name <stack-name> \
  --query "Stacks[0].Outputs[?OutputKey=='HttpApiUrl'].OutputValue" --output text
```

If `AiProvider=Gemini`, source `AiApiKey` from SSM Parameter Store instead of passing
it as a plain CloudFormation parameter (even `NoEcho`'d) — see
`docs/06-COLLABORATION-PROTOCOL.md` for the exact `aws ssm put-parameter` shape, and
`docs/SECURITY.md` for why this matters. `infra/template.yaml`'s `AiApiKey` parameter
is a documented simplification for a project that has never executed a real deploy,
not the recommended production shape.

## Cleanup / teardown

```bash
# CloudFormation cannot delete a non-empty S3 bucket -- empty it first (this also
# removes every version, since the bucket has versioning enabled).
aws s3api list-object-versions --bucket gweb-documents-<stage>-<account-id> \
  --output json --query '{Objects: Versions[].{Key:Key,VersionId:VersionId}}' > /tmp/versions.json
aws s3api delete-objects --bucket gweb-documents-<stage>-<account-id> --delete file:///tmp/versions.json
aws s3api list-object-versions --bucket gweb-documents-<stage>-<account-id> \
  --output json --query '{Objects: DeleteMarkers[].{Key:Key,VersionId:VersionId}}' > /tmp/markers.json
aws s3api delete-objects --bucket gweb-documents-<stage>-<account-id> --delete file:///tmp/markers.json

# Then delete the stack -- removes the DynamoDB table, the now-empty S3 bucket, the
# HTTP API, and the Lambda function together.
sam delete --stack-name <stack-name>
```

DynamoDB's `PointInTimeRecoverySpecification` and the table itself are deleted with
the stack (no `DeletionPolicy: Retain` is set — a deliberate choice for a disposable
dev/assessment stack; a production stack would likely retain the table and rely on the
data-retention/deletion design in `docs/SECURITY.md` instead of stack deletion).

Local teardown (Docker containers used for live verification this session):

```bash
docker rm -f gweb-dynamodb-local gweb-minio 2>/dev/null
```

## Environment variables

See [`.env.example`](.env.example) for the full list with descriptions. Copy it to
`.env` for local development; `.env` is git-ignored and must never be committed.

## API examples

Full OpenAPI 3.0 spec: [`docs/openapi.yaml`](docs/openapi.yaml) (every endpoint,
request/response schema, and error shape -- importable into Swagger UI, Postman, or
any OpenAPI-aware tool). These are real curl examples against the same endpoints (see
also "Prerequisites and local setup" above for how to run them locally, and
[`docs/DEMO.md`](docs/DEMO.md) for a concise start-to-submit walkthrough).

```bash
# Start a new application
curl -i -X POST http://localhost:5243/v1/applications
# -> 201, Location: /v1/applications/<id>, body: {"id":"...","status":"InProgress","version":1,...}

# Resume it later
curl -i http://localhost:5243/v1/applications/<id>
# -> 200, same shape

# Unknown (but well-formed) id
curl -i http://localhost:5243/v1/applications/00000000-0000-0000-0000-000000000000
# -> 404, {"error":{"code":"NOT_FOUND",...}}

# Malformed id -- rejected at the boundary, never reaches DynamoDB
curl -i http://localhost:5243/v1/applications/not-a-guid
# -> 400, {"error":{"code":"VALIDATION_FAILED",...}}

# Fill in applicant fields -- partial, PATCH semantics, call as many times as needed
curl -i -X PATCH http://localhost:5243/v1/applications/<id>/applicant \
  -H "Content-Type: application/json" \
  -d '{"legalFirstName":"Jane","legalLastName":"Testerson","email":"jane@example.invalid","governmentId":{"type":"Passport","number":"X1234567"}}'
# -> 200, body has governmentId.last4 = "4567" -- the full number never appears anywhere

# Fill in business fields, including beneficial owners
curl -i -X PATCH http://localhost:5243/v1/applications/<id>/business \
  -H "Content-Type: application/json" \
  -d '{"legalBusinessName":"Testerson Trading LLC","entityType":"Llc","beneficialOwners":[{"name":"John Doe","roleTitle":"Co-owner","ownershipPercentage":40}]}'
# -> 200; combined ownership (this + the applicant's own OwnershipPercentage, if set)
#    over 100% -> 400 VALIDATION_FAILED instead

# Full aggregate view, including completeness -- what's still missing to submit
curl http://localhost:5243/v1/applications/<id>
# -> 200, { id, status, ..., applicant: {...}, business: {...},
#           completeness: { isComplete, missingApplicantFields: [...], missingBusinessFields: [...] } }

# Request a pre-signed upload for a document -- Lambda never sees the bytes
curl -i -X POST http://localhost:5243/v1/applications/<id>/documents/presign \
  -H "Content-Type: application/json" \
  -d '{"type":"BankEvidence","originalFilename":"voided-check.pdf","contentType":"application/pdf","declaredSizeBytes":48213,"declaredChecksumSha256Base64":"<base64-sha256-of-the-file>"}'
# -> 201, { document: { id, status: "Uploading", ... }, uploadUrl, uploadFields: { key, Content-Type, x-amz-checksum-sha256, ... } }
# The client then POSTs the actual file straight to `uploadUrl` as multipart/form-data,
# with every entry in `uploadFields` as a form field alongside the file -- never through
# this API.

# After the client's direct-to-S3 upload finishes, confirm it landed and verify it
curl -i -X POST http://localhost:5243/v1/applications/<id>/documents/<documentId>/complete
# -> 200, { status: "Received", actualSizeBytes, uploadedAt, ... } on a verified match
#    or   { status: "Rejected", rejectionReason: "..." } if checksum/size/signature disagree
#    -- calling this again after either outcome is a no-op, safe to retry

# Fetch a document's current lifecycle state
curl http://localhost:5243/v1/applications/<id>/documents/<documentId>
# -> 200, { id, applicationId, type, status, originalFilename, contentType, ... }

# Search the MCC catalog -- by code, code prefix, or description substring
curl http://localhost:5243/v1/mcc?query=grocery
# -> 200, { query: "grocery", results: [ { code: "5411", description: "Grocery Stores, Supermarkets", category: "Retail" } ] }

curl http://localhost:5243/v1/mcc?query=60
# -> 200, code-prefix matches (6010, 6011, 6012, ...) ranked ahead of description-only matches

curl http://localhost:5243/v1/mcc
# -> 200, first 25 codes ordered by code -- a browsing default when no query is given

# Classify a business's MCC -- requires the business description to be filled in first
curl -i -X POST http://localhost:5243/v1/applications/<id>/classify
# -> 200, { candidates: [{ mccCode, confidence, explanation }, ...],
#           proposedMccCode, proposedProvider: "mock" | "gemini", classifiedAt,
#           selfSelectedMccCode: null, selfSelectedAt: null, hasMismatch: false, version }
# Real example against a live Gemini call (AI_PROVIDER=gemini), verified during this
# phase's build for a grocery-store description:
#   proposedMccCode: "5411", proposedProvider: "gemini", candidates[0].confidence: 0.98

# Confirm the proposal, or correct it to a different real MCC code
curl -i -X POST http://localhost:5243/v1/applications/<id>/classify/confirm \
  -H "Content-Type: application/json" -d '{"mccCode":"5411"}'
# -> 200, same shape; selfSelectedMccCode now set; hasMismatch true if it disagrees
#    with proposedMccCode
# -> 400 VALIDATION_FAILED if mccCode isn't a real code in the catalog

# Current classification state, without re-running classification
curl http://localhost:5243/v1/applications/<id>/classify
# -> 200, same shape as above; 404 NOT_FOUND if classify has never been called

# Evaluate without a processing statement -- still runs risk-signal detection
curl -i -X POST http://localhost:5243/v1/applications/<id>/evaluate \
  -H "Content-Type: application/json" -d '{}'
# -> 200, { status: "Completed", extracted: null, calculated: null, commentary: null,
#           riskSignals: [{ code: "MISSING_PROCESSING_STATEMENT", message, sourceField: "processingStatementDocumentId" }, ...],
#           processingStatementDocumentId: null, evaluatedAt, version }

# Evaluate with a completed ProcessingStatement document (documentId from its own
# presign/complete response -- see the document examples above)
curl -i -X POST http://localhost:5243/v1/applications/<id>/evaluate \
  -H "Content-Type: application/json" -d '{"processingStatementDocumentId":"<documentId>"}'
# -> 200, { status: "Completed",
#           extracted: { processor, monthlyVolume, discountRatePercent, perTransactionFee, monthlyFee, chargebackFeeTotal, statementPeriod, provider },
#           calculated: { totalMonthlyCostAmount, effectiveRatePercent, discountFeeAmount, transactionFeeAmount, monthlyFeeAmount, chargebackFeeAmount },
#           commentary: "...", riskSignals: [...], processingStatementDocumentId, evaluatedAt, version }
# -> 202 Accepted, { status: "Processing", ... } if the deadline budget was too low to
#    attempt evaluation at all -- poll the GET endpoint below
# Real example against a live Gemini call, verified during this phase's build for a
# realistic synthetic statement: extracted.provider: "gemini", monthlyVolume: 48732.15,
# discountRatePercent: 2.65, matching the statement exactly

# Current evaluation state, without re-running evaluation
curl http://localhost:5243/v1/applications/<id>/evaluation
# -> 200, same shape as above; 404 NOT_FOUND if evaluate has never been called

# Submit -- blocked while anything required is missing. 400, with the precise gap list
curl -i -X POST http://localhost:5243/v1/applications/<id>/submit
# -> 400 VALIDATION_FAILED, { error: { details: {
#      missingApplicantFields: ["legalFirstName", ...],
#      missingBusinessFields: [...],
#      missingRequiredDocuments: ["GovernmentId", "BusinessRegistration", "BankEvidence"]
#    } } }
# Same 400 shape (with a shorter or empty list per category) as each requirement is
# filled in -- applicant, business, and all three required documents (Government ID,
# Business Registration, Bank Evidence; presign+complete each first, see above) all
# have to be genuinely complete before this returns 200.

# Submit again once applicant, business, and all three required documents are complete
curl -i -X POST http://localhost:5243/v1/applications/<id>/submit
# -> 200, the normalized review payload: { id, status: "Submitted",
#      applicant: {...masked...}, business: {...masked...},
#      documents: [ {...}, {...}, {...} ],
#      classification: {...} | null, evaluation: {...} | null }
# Application is now locked at Submitted -- calling submit again returns 409 CONFLICT,
# not a second 200 or a silent no-op.
```

## Seeding / refreshing the MCC catalog

The catalog is packaged static data, not a database seed step — see
[`docs/adr/0004-mcc-catalog-storage.md`](docs/adr/0004-mcc-catalog-storage.md) for why.
To update it:

```bash
# 1. Edit the source-of-truth file
#    tools/McCatalogImport/source/mcc-codes-source.psv (pipe-delimited: code|description|category)

# 2. Regenerate the embedded resource the app actually loads
dotnet run --project tools/McCatalogImport
# -> validates every row (4-digit code, non-empty fields, no duplicates -- fails
#    loudly with the exact line number on any violation) and writes
#    src/Gweb.Adapters.Mcc/Resources/mcc-codes.json

# 3. Review the diff, then
dotnet test
```

No code change needed for a routine catalog update — only the source file and a
re-run of the tool.

## Assumptions

- No authentication/authorization is implemented in this prototype. `POST /v1/applications`
  reads an optional `x-actor` header purely for the audit-field value (`createdBy`) --
  it is not verified against anything, and any client can claim any actor name. The
  seam for a real authorizer: replace that header read in
  `src/Gweb.Api/Applications/ApplicationEndpoints.cs` with the identity API
  Gateway/Cognito attaches to the request once one exists.

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
- **Max upload size (25 MB, `Gweb.Domain.Documents.DocumentUploadLimits.MaxSizeBytes`)
  is a deliberate, documented default, not a number from the brief** — the brief says
  to validate size but never states a ceiling. Generous enough for a scanned PDF or
  phone photo, bounded so a single upload stays well clear of Lambda's own memory/payload
  limits. Trivial to change; it is one named constant, read from nowhere else.
- **Resolved 2026-09-09 (Phase 11), previously the project's longest-standing gap: no
  S3-compatible bucket had ever received an actual uploaded byte.** Now verified for
  real against MinIO (local S3-compatible storage) — a genuine SigV4-signed presigned
  POST, an actual multipart/form-data upload landing in the bucket, and `complete()`'s
  checksum/size/file-signature verification succeeding against the real stored bytes —
  exercised for all three required document types, both via raw `curl` and through the
  full rendered frontend. See the "Frontend" section above for the exact setup. Still
  genuinely unverified: the identical flow against **real AWS S3** rather than a
  MinIO stand-in, and a real `sam local start-api`/deployed-Lambda run performing the
  upload (this session's real-upload verification ran the backend via `dotnet run`,
  not through the Lambda runtime emulator) — both need either a real AWS account or
  more session time than remained after building the frontend that needed this
  verified.
- **IAM policies are unverified against real AWS.** `sam validate --lint` confirms the
  template is well-formed, and Phase 13's audit (`docs/SECURITY.md` "IAM approach")
  confirmed every granted action is justified by an actual code path and nothing
  broader (`UpdateItem`/`DeleteItem`/`Scan`/`s3:DeleteObject`/`ListBucket` are all
  absent because nothing needs them) -- but neither of those is a live test that
  `ApiFunction`'s policy grants exactly the right actions end-to-end against real AWS.
  This session caught one real reasoning error before it shipped (the S3 policy
  initially omitted `s3:PutObject`, on the wrong assumption that a presigned POST
  needs no grant on the signer's own role — see `AI-USAGE.md` §5) purely by
  re-reasoning about SigV4, not by a test. A real `sam deploy` + actual
  presigned-upload round trip is the only way to be fully sure the policy is both
  sufficient and not over-broad.
- **No authentication/authorization** — see Assumptions above.
- **No AWS deployment executed.** Local-first per `docs/06-COLLABORATION-PROTOCOL.md`
  §3; a real `sam deploy` (and its teardown script) is scoped for Phase 13, contingent
  on Osama providing AWS account details.
- **`sam local start-api --env-vars` does not actually override `Program.cs`'s
  cold-start persistence-provider choice** in this environment — see "Testing the
  `/v1/applications` endpoints" above for the real finding and the verified
  workaround (`dotnet run` directly against DynamoDB Local, re-confirmed working
  2026-09-09). Root cause not determined; a minor SAM CLI/managed-runtime interaction,
  not a bug in this repo's code, but worth understanding before relying on that flag
  for anything else.
- **Conditional document types (Business License, Additional Evidence) are not
  enforced by the submission gate.** The brief marks these "Conditional" on
  jurisdiction/business-type, and this system has no rule engine to evaluate that --
  only the three unconditionally-required types (Government ID, Business Registration,
  Bank Evidence) block `POST /submit`. See ADR-0008.
- **No aggregate "review screen" endpoint exists** — `ReviewStep.tsx` calls the
  granular endpoints (`GET /v1/applications/{id}`, `.../documents`, `.../classify`,
  `.../evaluation`) directly and composes the review screen client-side, confirming the
  ADR-0008 bet that these were sufficient without a new aggregate route was correct in
  practice, not just in theory. `POST /submit`'s own response is still the one place
  that returns everything pre-composed server-side (the normalized review payload).
- **The frontend has no automated test suite of its own** (no Vitest/Playwright) —
  verified this phase via real manual/scripted end-to-end testing (the actual rendered
  UI driven through a real browser, plus raw `curl` reproducing exactly what the
  browser's upload code sends), not repeatable automated coverage. The brief's testing
  requirements are explicitly scoped to the backend (`docs/05-TESTING-RULES.md`); a
  production frontend would still want its own component/integration tests.
- **The frontend's beneficial-owners UI doesn't exist yet** — `Business.beneficialOwners`
  is a real field the backend accepts and persists (see the next bullet), but
  `BusinessStep.tsx` doesn't currently expose an add/edit UI for the array; only the
  primary applicant's own fields are collected through the form. Documented as a gap
  now that a real UI exists to have this gap in, not a silent omission.
- **Beneficial owners beyond the primary applicant are lightweight records** (name,
  role, ownership percentage only) embedded in the `Business` payload, not full
  Applicant-grade KYC profiles with their own DOB/address/government ID. The brief
  places "ownership / beneficial-owner structure" as a `Business` field, and the API
  surface names only one PATCH route for individual/control-person data — see
  `src/Gweb.Domain/Applications/BeneficialOwner.cs`'s doc comment for the full
  reasoning. A production system would likely give each beneficial owner their own
  full KYC record and document (this matters for real KYB compliance); documented
  here as a deliberate scope cut given the assessment's literal API surface, not an
  oversight.
- **No consent/attestation *enforcement* yet** — `ConsentVersion`/`ConsentTimestamp`
  are captured and required for `CompletenessChecker` to report the applicant
  complete, but nothing currently blocks any other action on their absence (that
  blocking is Phase 9's submission gate).
- **The MCC catalog's data source is a well-established public MCC reference, not the
  brief's named source (the paid/licensed Visa Merchant Data Standards Manual)** — this
  session has no access to that manual. Every code the assessment's acceptance
  criteria actually depend on (6012, 6051, 6211 for Phase 6) is present and
  test-verified by code; the remaining ~270 entries have not been cross-checked
  word-for-word against an authoritative paid source. See ADR-0004 for the full
  reasoning and what a real refresh against the licensed manual would look like.
- **Risk policy is redeploy-to-change, not hot-reloadable, and has no version/audit
  trail of its own.** "Changeable without touching the catalog" (the brief's literal
  requirement) is satisfied; "changeable without a deploy, with a compliance-grade
  audit log of who changed what and when" is not. See ADR-0005's Consequences section
  for what a production version of this would need (a config service, not a packaged
  JSON file) — a deliberate scope decision at this assessment's size, not an oversight.
- **AI evaluation provider is Gemini, not Anthropic/OpenAI/Bedrock (the brief's named
  examples).** No vendor is mandated — "an adapter with a mock implementation" is the
  actual requirement — and this was a real key Osama supplied mid-session, used per his
  explicit instruction ("use only the free models"). See ADR-0006.
- **Free-tier Gemini rate limits/overload are real** — hit once during manual
  verification (a genuine HTTP 503 from the live API, back when `gemini-3.6-flash` was
  the default). The system's fallback-to-mock design means this degrades classification
  *quality* for that one request, not request *success* — the caller still gets a
  labelled, usable result. Since 2026-09-09 the default model is `gemini-3.5-flash-lite`
  (500 free requests/day vs. `gemini-3.6-flash`'s 20/day, confirmed working against the
  live API before switching — see ADR-0006's addendum) specifically so grading/demoing
  this system is unlikely to exhaust the quota; Phase 10 additionally added a real
  bounded retry with backoff/jitter (`Gweb.Shared.Resilience.BoundedRetry`) so a
  transient 503 like the one hit during Phase 7 no longer needs to fall all the way back
  to the mock provider on the first failure — see ADR-0009.
- **This model's real-world latency runs close to the system's 35s internal deadline
  target** — measured against `gemini-3.6-flash`: a single classify call took as long as
  ~30 seconds during manual testing (that model spends a large share of its output
  budget on hidden "thinking" tokens). Defended against with a hardcoded 20-second cap
  on that one call (`GeminiCallExecutor`) independent of remaining request budget, so a
  slow model response can never consume the whole request — verified by a real test
  using a hanging HTTP handler, not just asserted. This cap has not been separately
  re-measured against `gemini-3.5-flash-lite` (the current default, switched for quota
  headroom, not for speed) — a "Lite" model is generally the faster variant, so 20s
  should remain a comfortable, conservative ceiling rather than a tight one, but that is
  reasoning, not a fresh live measurement. The cap is a deliberate, revisitable value,
  not a permanent architectural limit.
- **`AI_API_KEY` in `infra/template.yaml` is a `NoEcho` CloudFormation parameter, not
  an SSM SecureString reference** — simpler for a project that has never executed a
  real `sam deploy`. A production setup should source it from SSM Parameter Store
  instead (the shape is already documented in `docs/06-COLLABORATION-PROTOCOL.md`), so
  the key never passes through a CloudFormation parameter, `NoEcho` or not.
- **`POST .../evaluate` still requires the caller to name the processing-statement
  document by ID** (from their own presign/complete response) rather than
  auto-discovering it by type, even though `IDocumentRepository.ListByApplicationIdAsync`
  now exists (built for the Phase 9 submission gate — see ADR-0008). Not wired into
  `EvaluationService` because that flow already has the ID from its own caller; revisit
  if a future caller doesn't. See ADR-0007.
- **"Regulated-license claims without evidence" only checks for a keyword in the
  business description, not an actual missing license document** — the stronger
  version needs the same list-documents query named above. Still a genuinely useful
  signal, just not the strongest possible version. See ADR-0007.
- **The async-fallback (`202` + `Processing` + poll) contract has no real background
  worker behind it.** The response shape, state machine, and poll endpoint are real and
  tested; nothing currently advances a stuck `Processing` record except the client
  calling `evaluate` again with more budget. A production system needs an actual async
  completion path (SQS/Step Functions) — explicitly out of scope until Phase 10. See
  ADR-0007.
- **Statement extraction sends real document bytes to Gemini as a multimodal inline
  part** (not just text) when `AI_PROVIDER=gemini` — verified against the live API, but
  only by running `GeminiEvaluationProvider.ExtractStatementAsync` directly against a
  realistic synthetic statement, not through the full HTTP → S3 → evaluate pipeline
  (this session has no AWS account to actually upload a real file to S3, and
  `InMemoryDocumentStorage` has no HTTP-reachable way to seed real bytes outside a test
  process). The orchestration around it (`EvaluationService`, the HTTP endpoints) is
  fully tested end-to-end against a simulated upload with a fake provider — only the
  combination of "real HTTP upload + real Gemini multimodal call in one live run" is
  unverified. See ADR-0007 "What's verified" for exactly what was and wasn't run.

## What is real vs. mocked

- **Mocked by default:** the AI evaluation/classification provider (labelled
  `"provider": "mock"` in every response it touches) -- `AI_PROVIDER=mock` needs no
  credentials, per brief "must work through an adapter with a mock implementation when
  no credentials exist."
- **Real when configured:** `AI_PROVIDER=gemini` calls the real Gemini API for real.
  Verified against the live API multiple times during Phase 7's build, including one
  full real request through the entire stack that correctly classified a grocery-store
  description as MCC 5411 (confidence 0.98) -- see
  [`docs/adr/0006-ai-evaluation-provider.md`](docs/adr/0006-ai-evaluation-provider.md)
  "What's verified." If the real provider fails, the system falls back to the mock
  provider automatically and labels the response accordingly, rather than surfacing an
  error for what should be a graceful degradation.
- **Real, unconditionally:** everything else — validation, persistence, document
  storage/lifecycle, MCC catalog, risk policy, deadline/timeout handling.

## Test coverage

Measured by running `dotnet test --collect:"XPlat Code Coverage" --settings
coverlet.runsettings` (last run: 399 tests, all passing; generated-code excluded per
`coverlet.runsettings`):

| Assembly | Line coverage | Branch coverage |
|---|---|---|
| `Gweb.Config` | 100% | 100% |
| `Gweb.Adapters.Storage` | 100% | 100% |
| `Gweb.Adapters.Mcc` | 100% | 88.9% |
| `Gweb.Adapters.RiskPolicy` | 100% | 83.3% |
| `Gweb.Shared` | 98.75% | 93.75% |
| `Gweb.Adapters.Persistence` | 98.91% | 88.39% |
| `Gweb.Services` | 96.05% | 92.85% |
| `Gweb.Api` | 92.93% | 84.28% |
| `Gweb.Domain` | 91.95% | 88.32% |
| `Gweb.Adapters.Evaluation` | 91.21% | 55.76% |
| **Overall** | **94.56%** | **86.13%** |

Up this phase (94.56%/86.13% vs. Phase 11's 94.49%/84.36%), mainly from two real-bug
regression tests plus the new end-to-end journey test exercising the full HTTP pipeline
in sequence. `Gweb.Adapters.Persistence` branch coverage jumped the most (80.35% →
88.39%) from `SaveRoundTripsCorrectlyWhenEntityTypeHasNeverBeenSet` -- the
never-set-EntityType branch this test exists for was genuinely untested before the bug
it caught. `Gweb.Services` branch coverage rose similarly (85.71% → 92.85%) from the
two classification confidence-cap tests. `Gweb.Adapters.Evaluation` stays the lowest of
any assembly for the same reason as every prior phase: more independent failure-mode
branches than the suite exercises every pairwise combination of, each path tested
individually rather than combinatorially. Numbers re-measured and reported per-phase; a
stale percentage from an earlier phase is never left standing in for what a later phase
actually covers.
