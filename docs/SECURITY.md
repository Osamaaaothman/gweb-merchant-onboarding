# Security note

Required deliverable per the assessment brief §12/§14 and `docs/04-SECURITY-RULES.md`.
Written honestly: what is real and implemented, what is a documented gap, and what a
production version would need — never a claim this build cannot back.

## Threat model

| Threat | Mitigation in this build | Production improvement |
|---|---|---|
| Unauthorized access to another applicant's data | **No authentication exists at all** (see "What is not mitigated" below) — the only barrier today is that an application ID is a random `Guid` (122 bits of entropy), not sequential or guessable. Every DynamoDB access pattern is scoped by `pk = APP#{applicationId}`, so even an internal bug in one handler cannot cross-read another application's items without that exact ID. | Real authentication (Cognito/API Gateway JWT authorizer or similar) plus an authorization check that the caller's identity is actually associated with the application ID in the path — not just "any caller who knows any valid ID." |
| Pre-signed URL abuse / reuse / oversized upload | The presigned POST pins `Content-Type`, a `content-length-range`, and the client-declared SHA-256 checksum as **S3 policy conditions** — S3 itself rejects a mismatched upload before this system ever sees it. TTL is time-boxed (`PRESIGN_TTL_SECONDS`, default 300s). `complete()` re-verifies checksum/size/file-signature against what actually landed, independent of what the presign step declared, and is idempotent (a repeat call is a no-op, not a re-verification that could be raced). | Bind the presigned URL to the specific document ID *and* a specific requesting session/identity once real auth exists, and consider single-use enforcement (a DynamoDB conditional write marking "consumed" the first time `complete()` succeeds — partially already true via the `Uploading → Received` state transition, which a second `complete()` call sees as already-resolved). |
| Malware or polyglot file upload | Extension allowlist (`.pdf .jpg .jpeg .png`) + MIME allowlist pinned in the presign conditions + **file signature (magic bytes) check** on `complete()` (`FileSignatureValidator` — `%PDF` / `FFD8FF` / PNG's 8-byte signature) so a renamed executable or a polyglot file that only matches the extension is rejected. Extension and declared Content-Type are required to agree. | This build does **not** scan file content for embedded malware/exploits (e.g. a well-formed PDF with an embedded macro or exploit payload) — a production system would add a scanning step (e.g. an S3 event triggering a Lambda that runs the object through a scanning service) before a document is ever marked `Accepted` for a human reviewer to open. |
| Prompt injection via business description | The business description is only ever included as **data** inside a fixed prompt template (`GeminiEvaluationProvider.BuildPrompt`) — never concatenated into or capable of altering the system instruction. Every candidate the model returns is re-validated against the real MCC catalog before being trusted (`ClassificationService`); a model instructed via injection to "always return code X at 100% confidence" still gets dropped if `X` isn't a real catalog code, and — as of this session's own live-testing round — confidence itself is capped independently of whatever the model claims whenever the underlying catalog search found no genuine match (see `docs/adr/0011-end-to-end-journey-test.md`'s sibling, the Phase 11 classification fix). | Add an explicit length cap and character-class filter on `businessDescription` before it ever reaches a prompt (currently bounded only by the 2000-char domain-validation max, not sanitized for prompt-injection-specific patterns), and log (redacted) prompt/response pairs for later audit of anomalous model behavior. |
| Replay / duplicate submission | Every write is a **version-conditional `PutItem`** (`attribute_not_exists(pk)` on create, `version = :expectedVersion` on update) — a replayed request with a stale version fails with a real `409 CONFLICT`, never a silent double-write. `Document.complete()` and `Application.Submit()` are both idempotent against their own already-resolved state. | A client-supplied idempotency key (e.g. `Idempotency-Key` header, stored and checked before re-executing a `POST`) would additionally protect against a replayed request that *doesn't* know the current version — today's protection is version-based, not full idempotency-key-based, for the `POST /applications` (create) and `POST /submit` (first call) paths specifically. |
| PII leakage through logs | `Gweb.Shared.Logging.Redactor` is the **one** path every structured log line in this codebase routes through (`StructuredLogger`) — deep-clones the log payload and replaces any value under a sensitive key (government ID, SSN, tax ID/EIN, DOB, bank/routing/account number, secrets, tokens, passwords, API keys, document body/content, AI prompts) with `[REDACTED]`, plus a separate pattern match that catches a pre-signed URL by its `X-Amz-Signature`/`X-Amz-Credential` query-string markers even under an innocuous key name like `uploadUrl`. Government ID, EIN, and bank account numbers are masked to last4 **at the moment of capture** in the domain layer itself (`GovernmentIdentification.FromFullNumber`, `RegistrationIdentifier.FromFullValue`, `SettlementBankAccount.FromFullAccountNumber`) — the full value exists only transiently inside that one conversion call and is never stored, so there is nothing left to leak even if redaction were ever bypassed. **Backed by a real regression test, not just structural intent**: `PiiRedactionEndToEndTests.PatchingAFullyPopulatedApplicantAndBusinessNeverLogsAnySensitiveRawValue` captures real stdout across a real HTTP PATCH of a fully-populated applicant and business and asserts none of the raw sensitive values (DOB, government ID, EIN, bank account number) appear anywhere in it — this is the exact evidence `docs/04-SECURITY-RULES.md` §2 asks for, exercised against the real endpoint flow, not the `Redactor` unit in isolation. | Extend the same test's coverage to document-related log lines (e.g. a rejected upload's rejection reason, which could theoretically echo file content in a future code change) and to the AI-provider request/response log lines specifically, for the same "asserted, not just believed" guarantee in that path too. |
| Enumeration of application IDs | Application IDs are `Guid.NewGuid()` (122 bits of randomness, not sequential) — brute-force enumeration is computationally infeasible. `GET /v1/applications/{id}` on a well-formed-but-unknown UUID returns a generic `404 NOT_FOUND`, never a different response shape that would let an attacker distinguish "doesn't exist" from "exists but I can't see it" (moot today since there's no access control to distinguish from, but the response shape is already the safe one for when auth is added). | Rate-limit `GET /v1/applications/{id}` per source IP/identity at API Gateway to slow even infeasible brute-force attempts to a crawl, and add real authorization so a valid-but-not-yours ID also returns 404 (not 403, which would confirm existence). |
| Denial of wallet (cost abuse via AI calls) | `GeminiCallExecutor` caps a single Gemini call at 20 seconds regardless of remaining budget, and `BoundedRetry` bounds retries to a small fixed count with backoff — a single request can never spawn unbounded AI spend. Response size is capped (`MaxResponseBodyChars`) before being parsed. | No per-application or per-IP rate limit on `POST /classify` or `POST /evaluate` exists yet — a caller could repeatedly re-trigger real Gemini calls (each one billed) with no cap beyond the 45-second-per-request ceiling. A production system needs a request-count or cost-based rate limit per application (e.g. "at most N classify calls per application per hour") independent of the per-request timeout, which only bounds *duration*, not *frequency*. |
| Supply-chain risk in dependencies | Backend: only well-known, first-party AWS SDK packages (`AWSSDK.DynamoDBv2`, `AWSSDK.S3`) plus `Amazon.Lambda.AspNetCoreServer.Hosting` — no obscure or unmaintained third-party packages. Frontend: dependencies are all top-tier, widely-used packages (React, TanStack Query, Zustand, react-hook-form, zod, Radix UI primitives, `motion`) pinned via `package-lock.json`. Neither project vendors or dynamically loads code from an untrusted source. | No automated dependency vulnerability scanning (e.g. `dotnet list package --vulnerable`, `npm audit`, or a CI-integrated Dependabot/Snyk) has been run this session — a real production pipeline would gate merges on this. |

## IAM approach

Least privilege, enforced literally, not just described: `ApiFunction`'s role
(`infra/template.yaml`) is scoped to exactly `dynamodb:PutItem`/`GetItem`/`Query` on
the one table ARN and `s3:PutObject`/`GetObject`/`GetObjectAttributes` on the one
bucket's objects — never `dynamodb:*`, never `s3:*`, never `Resource: "*"`. Every
action is justified by an actual code path:

- `dynamodb:PutItem` — every entity's create/update (all writes are conditional `PutItem`, never `UpdateItem`).
- `dynamodb:GetItem` — every entity's single-item read.
- `dynamodb:Query` — `IDocumentRepository.ListByApplicationIdAsync` (added Phase 9, exposed over HTTP in Phase 11).
- `s3:PutObject` — granted even though the Lambda itself never uploads a byte: a pre-signed POST is signed with this role's own credentials, so S3 authorizes the *client's* eventual upload against this role's permissions, not against an unauthenticated allowance. Without it, every presigned upload would `403` at S3 despite a validly-signed request — a real reasoning error this session initially made and caught before it shipped (see `AI-USAGE.md` §5).
- `s3:GetObject` / `s3:GetObjectAttributes` — `complete()`'s verification read (a 16-byte ranged read for the file-signature check, plus metadata for size/checksum).

**Audited this phase (Phase 13) for anything that cannot be justified out loud:**
nothing found to remove. There is no `dynamodb:UpdateItem`, `DeleteItem`, `Scan`,
`BatchWriteItem`, or `s3:DeleteObject`/`ListBucket` in the policy because no code path
in this repository calls any of them — "audited" here means confirmed the policy is
already the minimum the real code needs, not that something was found and trimmed.
This has not been verified by an actual `sam deploy` against real AWS (see Known
Gaps) — `sam validate --lint` confirms the template is well-formed, which is a
syntactic check, not a live IAM-boundary test.

**One role per Lambda function:** trivially true today since there is exactly one
Lambda function (`ApiFunction`) hosting the whole API — see
`docs/adr/0001-runtime-and-language-choice.md` for why one Lambda was chosen over
one-per-route, and the blast-radius tradeoff that decision costs (a single role now
has to be broad enough for every route's needs, rather than each route's own
minimal role). If this were split into multiple functions later, each would get its
own narrower role by construction, not a shared "app role."

## Logging and redaction decisions

Covered in the "PII leakage through logs" threat-model row above. Two additional,
deliberate decisions:

- **Masking happens at the domain layer, at capture time — not at the API response
  layer.** `GovernmentIdentification`, `RegistrationIdentifier`, and
  `SettlementBankAccount` only ever hold the masked/last4 form once constructed; there
  is no unmasked value anywhere in the domain model past the one `FromFull*()`
  conversion call to accidentally return, log, or persist. This is stronger than
  "remember to mask the response" — there is nothing left to forget to mask.
- **`DomainException.Details` is serialized directly into the client-facing HTTP
  response** by `HttpErrorMapper`. Every place that constructs a `Details` payload was
  checked to ensure it never carries a raw SDK exception message (which can contain
  table names, bucket names, or ARNs) — `DynamoDbCallExecutor`/`S3CallExecutor`/`GeminiCallExecutor`
  deliberately swallow the underlying exception's message and substitute a fixed,
  generic string. One real instance of this convention being violated was caught and
  fixed during this session's own build, before it ever shipped (`AI-USAGE.md` §5).

## Secrets management

`AI_API_KEY` (the only real secret this system has) is read from an environment
variable at cold start (`AppConfigLoader.LoadBaseConfig`), never logged, never
returned in a response, never written to DynamoDB. Locally, it lives only in a
git-ignored `.env` file (`.gitignore` covers `.env`/`.env.*`, keeping only
`.env.example` with a placeholder). In `infra/template.yaml` it is a `NoEcho`
CloudFormation parameter — this keeps it out of the CloudFormation console/CLI output
and change-set diffs, but is a **documented simplification** for a project that has
never executed a real `sam deploy`: a production setup should source it from SSM
Parameter Store as a `SecureString` instead (the exact `aws ssm put-parameter` shape
is already documented in `docs/06-COLLABORATION-PROTOCOL.md`), so the key never passes
through a CloudFormation parameter at all, real or `NoEcho`'d. Before every push this
session, the outgoing diff was grepped for the literal key substring to confirm it
never leaked into git history — see `AI-USAGE.md`'s ownership record.

## Encryption at rest, and the KMS question

DynamoDB: `SSESpecification.SSEEnabled: true` — AWS-owned key (the default), not a
customer-managed KMS key (CMK). S3: `SSEAlgorithm: AES256` — S3-managed keys (SSE-S3),
not SSE-KMS. Both are real encryption at rest, just not under a key this account
controls the lifecycle of. **In production, this should move to a customer-managed KMS
key for both** — the reasons: (1) a CMK gives an audit trail of every decrypt via
CloudTrail, which an AWS-owned key does not; (2) a CMK can be scoped so only
`ApiFunction`'s role (and no other principal in the account) can decrypt applicant PII
and document bytes, an additional access-control layer independent of the
DynamoDB/S3 IAM policy itself; (3) a CMK can be rotated and, if ever compromised,
disabled account-wide without re-encrypting existing data (KMS re-wraps the data key,
not the object). Not implemented in this build because it adds real deployment
complexity (key policy, cross-service grants) for a project that has never executed a
real deployment at all — a deliberate scope cut, not an oversight, consistent with
every other "documented but not implemented pending a real AWS account" item in Known
Gaps.

## Data retention and deletion

Not implemented — documented here per `docs/04-SECURITY-RULES.md` §6, since the design
does not need to be automated to be written down honestly:

- **Identifying an abandoned application:** an application whose `status` is still
  `InProgress` and whose `updatedAt` is older than a configurable threshold (e.g. 90
  days) is a reasonable definition of "abandoned." `updatedAt` already exists on every
  entity today; no new field is needed to compute this.
- **Removing DynamoDB items:** a TTL attribute (e.g. `expiresAt`, computed at write
  time as `updatedAt + retentionWindow`) is DynamoDB's native fit for this — enabling
  TTL on the table and setting that attribute on write would let expired items be
  removed automatically, at no additional write cost, without a scheduled job.
  Currently no TTL attribute is written by any repository.
- **Removing S3 objects:** an S3 lifecycle rule keyed on the `applications/{id}/`
  prefix (expiring objects after the same retention window) is the natural
  no-code-required fit, mirroring the DynamoDB TTL approach. Alternative: a scheduled
  cleanup Lambda that queries for abandoned applications and explicitly deletes their
  objects and items together, if TTL's eventual-consistency deletion timing (up to 48
  hours after expiry, per DynamoDB's own documentation) isn't tight enough for a real
  compliance requirement.
- **What is retained for audit, and for how long:** every entity's `createdAt`,
  `updatedAt`, `createdBy`/actor, and `correlationId` already exist and would need to
  survive independently of the application's own data if a real audit-retention
  requirement (e.g. "keep a record that an application existed and was abandoned, even
  after its PII is purged") applied — this build does not currently separate "audit
  metadata" from "PII payload" in a way that would let one be purged while the other
  is retained; that separation is a real design gap for a production compliance
  requirement, not just an unimplemented feature.
- **Data-subject deletion request:** today, deleting an application's DynamoDB items
  (`DeleteItem` for each `sk` under its `pk`) and S3 objects (`DeleteObject` for each
  key under its prefix) would fully remove it — no code implements this yet (no
  `DeleteItem`/`DeleteObject` IAM permission is granted, consistent with least
  privilege: nothing requests it because nothing needs it today). A production system
  would add a dedicated, audited "delete this application" operation, gated by real
  authorization, rather than exposing raw delete permissions broadly.

## Audit trail

Every entity in this codebase (`Application`, `Applicant`, `Business`, `Document`,
`McClassification`, `Evaluation`) carries `CreatedAt`, `UpdatedAt`, `CreatedBy`
(actor), and `CorrelationId`. `StructuredLogger` emits a structured log line for every
request's start and completion, keyed by the same correlation ID, so a CloudWatch
query can reconstruct the full sequence of what happened for one request even though
raw sensitive values never appear in it. What is **not** currently implemented: a
separate, explicit "material status change" event log (who changed what, from which
state to which state, and why) distinct from just the entity's own `UpdatedAt`/`Version`
bump — today, the *fact* that something changed is fully auditable (the version
number and timestamp prove it), but a human-readable "why" beyond the actor/correlation
ID is not captured as its own record. A production system handling real underwriting
decisions would want this as a first-class event stream, not just inferred from entity
version history.

## What is not mitigated (honest summary)

- **No authentication or authorization exists anywhere in this system.** Every
  endpoint is reachable by anyone who can reach the API. This is the single largest
  gap in this build, named explicitly and repeatedly rather than glossed over — see
  README "Known gaps" and "Assumptions." A real merchant-onboarding system handling
  the PII this one collects would be unacceptable to deploy without it; it is out of
  scope for this assessment's literal API surface (which specifies no auth mechanism)
  but is the first thing to add before any real usage.
- **No rate limiting** exists at any layer (API Gateway throttling is not configured
  in `infra/template.yaml`, and no application-level limit exists either) — see the
  "Denial of wallet" and "Enumeration" rows above for the specific consequences.
- **No malware/content scanning** of uploaded document bytes beyond the file-signature
  (magic-byte) check — see the "Malware or polyglot file upload" row above.
- **No dependency vulnerability scanning** has been run this session — see the
  "Supply-chain risk" row above.
