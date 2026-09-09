# ADR-0008: Submission gate and normalized review payload

## Status

Accepted — 2026-09-09

## Context

Brief acceptance criterion: "Submission blocks on missing required items; produces
normalized review payload when complete." `docs/08-IMPLEMENTATION-PLAN.md` Phase 9:
completeness gate with a precise missing-items list, a normalized internal review
payload for downstream processor mapping, version/lock the submitted application, and
"terminal state is READY_FOR_MANUAL_REVIEW — never APPROVED."

`CompletenessChecker` (Phase 3) already covers Applicant/Business field completeness.
It does not, and by design should not, know about documents -- Phase 9 needs to add
the document half of "missing required items" without touching that already-shipped,
fully-tested code.

## Decision

### A real "list documents for an application" query, finally built

`docs/adr/0003-dynamodb-table-strategy.md`'s access-pattern table planned
`Query(pk=APP#{id}, sk begins_with "DOC#")` as access pattern #3 since Phase 2 --
labelled "Phase built: 4" in that table, but no caller actually needed it until now
(Phase 4/7/8 endpoints all take a specific `documentId` the client already knows, per
their own ADRs' deliberate scope decisions). The submission gate is the first caller
that needs to discover *which* document types exist without already knowing their
IDs, so `IDocumentRepository.ListByApplicationIdAsync` was added -- a real DynamoDB
`Query`, not a new access pattern invented under time pressure; the design was already
there, just unbuilt. `infra/template.yaml`'s IAM policy gains `dynamodb:Query`,
scoped to the same one table ARN as every other action -- its own comment had
literally anticipated this exact addition ("extend this list ... when a later phase
adds Query").

### `SubmissionChecker`: reuses `CompletenessChecker`, adds document requirements

`Gweb.Domain.Submission.SubmissionChecker.Check(Applicant?, Business?, IReadOnlyList<Document>)`
calls `CompletenessChecker.Check` for the applicant/business half (so what blocks
submission can never drift from what `GET /v1/applications/{id}`'s own completeness
field already reports) and adds required-document logic on top: per brief's document
table, Government ID, Business Registration, and Bank Evidence are "Required" --
Business License and Additional Evidence are "Conditional" (their necessity depends on
jurisdiction/business-type rules this system has no rule engine for) and Processing
Statement is explicitly "Optional." Only the three unconditionally-required types are
checked. A document type counts as satisfied only once it reaches `Received` or
`Accepted` -- `Uploading`/`Requested`/`Rejected` don't count, the same bar `Document`'s
own state machine already uses to mean "usable."

### `IApplicationRepository` gains `UpdateAsync`, deliberately not a unified `SaveAsync`

Every other entity in this codebase (Applicant, Business, Document, McClassification,
Evaluation) has one `SaveAsync(entity, expectedVersion, ...)` method where
`expectedVersion=0` means create. `Application` already had a *different*, earlier
convention: a dedicated `CreateAsync` (always-conditional-create, no expectedVersion
parameter at all) plus `GetByIdAsync` -- and no update method existed at all until
`Submit()` needed one. Rather than retrofitting `Application` onto the
`SaveAsync`/`expectedVersion=0` convention (which would give `expectedVersion` two
different meanings depending on which method reads it), `UpdateAsync` was added as a
third, update-only method, matching the split-methods shape `Application`'s repository
already had. `Application.Version` starts at 1 (not 0, unlike every other entity), so
this distinction is real, not just stylistic.

### The normalized review payload is the submit response, not a separate GET

The brief's user journey lists "Review & submit" (step 7, human-facing) and "Internal
review payload" (step 8, a "normalized record for downstream mapping") as related but
distinct outputs. This system serves them differently on purpose:

- **Review** (step 7 -- "show all data, missing items, warnings, document status,
  proposed MCC, evaluation summary") is served by the granular endpoints that already
  exist: `GET /v1/applications/{id}` (applicant/business/completeness), `GET
  .../documents/{id}` (per-document status), `GET .../classify` (proposed MCC), `GET
  .../evaluation` (rate/risk summary). A real frontend (Phase 11) would call these
  directly; no new aggregate "review screen" endpoint was added for a UI that doesn't
  exist yet, avoiding building an untested response shape ahead of its only consumer.
- **The normalized review payload** (step 8) is exactly what `POST
  /v1/applications/{id}/submit` returns on success: `SubmitResponse`, composed from
  the exact same masked `ApplicantResponse`/`BusinessResponse`/`DocumentResponse`
  types every other endpoint already returns (so masking can't drift between "normal"
  responses and the submission payload), plus the current `McClassification` and
  `Evaluation`, if either exists. This satisfies the brief's literal API surface (only
  `POST /submit` is listed, no separate review route) while still producing the
  distinct "normalized record for downstream mapping" the brief names.

### Blocking is a `400`, not a `409`

An incomplete submission returns `400 VALIDATION_FAILED` with the full
`SubmissionReadiness` (missing applicant fields, missing business fields, missing
document types) as the error's `details` -- consistent with how every other
malformed/incomplete-input case in this codebase is reported (e.g. `PatchApplicantAsync`'s
field errors), and distinct from `409 CONFLICT`, which is reserved for real
optimistic-concurrency races (a second `submit` call on an already-submitted
application correctly gets `409`, from `Application.Submit()`'s own state-machine
guard -- no special-casing needed in `SubmissionService`, that guard already existed).

### Terminal state, restated

No new `ApplicationStatus` value was added. `Submitted` (existing since Phase 2)
*is* the "ready for manual review" terminal state the brief describes --
`NoAutoApprovalPathTests` already enforces that no domain enum anywhere in this
codebase ever grows an `Approved` value, and README has stated since Phase 1 that
`Submitted` is the most positive terminal state this system produces. Renaming the
enum value at Phase 9 to literally spell "ReadyForManualReview" would touch every
prior phase's already-shipped, already-tested references to `Submitted` for a
cosmetic gain; the semantic guarantee the brief actually cares about (no
auto-approval) is already real and already tested.

## What's verified

- `SubmissionCheckerTests`: every missing-item category (applicant, business,
  each required document type), that `Uploading`/`Rejected` documents don't count as
  satisfied, and a fully-complete case.
- `InMemoryApplicationRepositoryTests`/`DynamoDbApplicationRepositoryTests`: the new
  `UpdateAsync`, including its own conflict/stale-version cases.
- `InMemoryDocumentRepositoryTests`/`DynamoDbDocumentRepositoryTests`: the new
  `ListByApplicationIdAsync`, including that it's correctly scoped to one application
  and the real `KeyConditionExpression`/`begins_with` shape sent to DynamoDB.
- `SubmissionServiceTests`: not-found, blocked-with-readiness-details, the full
  successful path, and a double-submit correctly producing `409` from `Application.Submit()`'s
  existing guard.
- `SubmitEndpointTests`: the full real HTTP journey -- create → PATCH applicant → PATCH
  business → real presign/complete for three required documents → submit -- exercised
  end-to-end through the actual endpoints, not shortcut. Confirms blocked submission
  reports the precise missing items over HTTP, a successful submission's payload never
  contains an unmasked government ID or bank account number, and a second submit
  attempt correctly returns `409`.

## Consequences

- "Conditional" document requirements (Business License, Additional Evidence) are not
  enforced at all yet -- doing so needs a jurisdiction/business-type rule engine this
  system doesn't have. Documented as a Known Gap, not silently ignored.
- No aggregate "review screen" endpoint exists ahead of Phase 11's frontend; if a
  future consumer needs one call instead of four, this is the natural seam to add it
  (a thin composition over already-existing service calls, no new domain logic).
