# 02 — Engineering Standards

"Code quality, structure, readability, and maintainability" is the first thing the
client listed. This file is the bar.

---

## 1. The maintainability test

Before you commit any file, ask: **could another engineer who has never seen this
repo modify this file correctly in 10 minutes?** If not, it is not done.

Concretely:
- A reader should be able to find where a behavior lives from the folder name alone.
- A handler should read like a description of the use case, not like plumbing.
- Nothing important should be discoverable only by reading the implementation.

---

## 2. Layering — non-negotiable

The brief requires that "storage, external providers, AI, and business policy are
replaceable adapters/modules rather than embedded in one large handler."

```
handlers/        Lambda entry points. Thin. HTTP-aware. No business logic.
                 Responsibilities: parse → validate → build deadline budget →
                 call service → map result to HTTP response.

services/        Use-case orchestration. Pure business flow. Knows nothing about
                 API Gateway events, nothing about AWS SDK types.

domain/          Entities, value objects, state machines, invariants.
                 Zero infrastructure imports. Fully unit-testable with no mocks.

policy/          Risk policy engine. Configurable, data-driven, provider overrides.
                 Separate from the MCC catalog.

adapters/
  storage/       S3 implementation behind an interface (IDocumentStorage)
  persistence/   DynamoDB implementation behind an interface (IApplicationRepository, ...)
  ai/            AI provider behind an interface (IEvaluationProvider) + Mock impl
  clock/         Time source behind an interface (testability of deadlines/expiry)

shared/          Deadline budget, correlation context, structured logger, errors,
                 redaction utilities, result types.

config/          Environment-based configuration loading + validation at cold start.

web/             Frontend application.
infra/           IaC (SAM/CDK/Terraform templates).
tests/           Mirrors the source structure.
scripts/         MCC import/seed, local setup, cleanup.
```

### Dependency direction
`handlers → services → domain`
`services → adapter *interfaces*` (never concrete AWS classes)
`domain → nothing`

If `domain/` ever imports an AWS SDK type, that is a bug — fix it before committing.

### Adapter rule
Every external dependency gets an interface + at least two implementations:
the real one and a test/mock one. This is what makes the AI layer mockable and the
timeout tests possible.

---

## 3. Handler rules

A Lambda handler must:
1. Extract and generate a **correlation ID** (from header or new) and put it in the logging context
2. Establish the **deadline budget** from the remaining Lambda time (see §5)
3. **Validate the request** against a schema before anything else
4. Delegate to a service
5. Map domain results / errors to HTTP responses via one shared mapper
6. Never contain business rules, never touch the AWS SDK directly

A handler longer than ~60 lines is a smell. Say so and refactor.

---

## 4. Validation and typed models

- **Every API boundary validates its input.** No exceptions, including internal endpoints.
- Use schema validation appropriate to the runtime (e.g. Zod / FluentValidation /
  pydantic). Reject unknown fields explicitly rather than ignoring them.
- Validate: types, required-ness, string lengths, enum membership, numeric ranges,
  date sanity (DOB in the past, business start date not in the future), body size,
  content type, path parameter format.
- Parse into typed domain models at the boundary. **Do not pass raw dictionaries into
  services.**
- Validation failures return `400` with a machine-readable list of field errors, and
  **never echo back a sensitive submitted value**.

---

## 5. Deadline budget (cross-cutting — build this first)

There must be **one** deadline primitive used everywhere. Not ad-hoc timeouts scattered
in call sites.

Required behavior:
- Created at handler entry from Lambda's remaining execution time
- Internal target ≤ **35 s**, hard ceiling **45 s**
- Passed explicitly down through service → adapter → SDK client
- Every outbound call receives `min(remaining_budget - reserve, per_call_max)`
- When the budget is exhausted, the request returns a **structured timeout response**
  with the workflow state preserved for safe retry — never a raw crash, never a 502
- Retries are bounded, use exponential backoff **with jitter**, and only fire if the
  remaining budget can accommodate another attempt plus the reserve

Every adapter method that performs I/O takes the budget as a parameter. If a method
does I/O without a budget, that is a bug.

---

## 6. Error handling

- Define a small **domain error taxonomy**: `ValidationError`, `NotFoundError`,
  `ConflictError` (optimistic concurrency), `DependencyTimeoutError`,
  `DependencyUnavailableError`, `PolicyViolationError`.
- One mapper translates domain errors → HTTP status + response body. Handlers do not
  build error responses ad hoc.
- **Never swallow an exception.** Either handle it meaningfully or let it propagate to
  the boundary mapper.
- Error responses have a stable shape:
  ```json
  { "error": { "code": "VALIDATION_FAILED", "message": "...", "details": [...] },
    "correlationId": "..." }
  ```
- **Error messages returned to clients must not leak** stack traces, table names,
  bucket names, ARNs, or internal paths. Those go to CloudWatch, keyed by correlation ID.
- Distinguish retryable from non-retryable. Tell the client which it is.

---

## 7. Idempotency and concurrency

- Mutating endpoints accept a client request/idempotency ID and use DynamoDB
  **conditional writes** so a replay does not double-apply.
- Every record carries a `version` attribute; updates use optimistic concurrency and
  return `409` on conflict rather than silently overwriting.
- `documents/{id}/complete` must be safely callable twice with the same checksum.
- State transitions go through an explicit state machine that rejects illegal
  transitions (e.g. `REJECTED → UPLOADING`), rather than blind field assignment.

---

## 8. Naming and readability

- Names say what a thing **is** or **does**, in domain language:
  `ProposedMerchantCategoryCode`, not `mccObj`; `enhanced_review`, not `flag2`.
- No abbreviations except universally understood ones (`id`, `url`, `mcc`, `kyb`).
- Boolean names read as assertions: `isSubmittable`, `hasBankEvidence`.
- Functions do one thing. If the name needs "and", split it.
- Magic numbers become named constants: `DEADLINE_TARGET_MS`, `MAX_UPLOAD_BYTES`,
  `PRESIGN_TTL_SECONDS`.
- **Comments explain why, never what.** Delete any comment that restates the code.
- No commented-out code in a commit. Git remembers it.

---

## 9. Configuration

- Zero hard-coded bucket names, table names, regions, account IDs, endpoints, or keys.
- Config is loaded and **validated once at cold start**; the process fails fast with a
  clear message if a required variable is missing — not on the first request that needs it.
- `.env.example` lists every variable with a description and a safe placeholder.
- `README.md` documents each variable, where it comes from, and whether it is required.

---

## 10. Observability

- **Structured JSON logs only.** No `console.log("here")`.
- Every log line carries: `correlationId`, `applicationId` (when known), `handler`,
  `level`, `event`, `durationMs`.
- Log at boundaries: request received, external call started/finished, state
  transition, error. Not on every line.
- Emit at least: request duration, remaining-budget-at-completion, dependency call
  duration, timeout occurrences, validation failure counts.
- **See `docs/04-SECURITY-RULES.md` for what must never be logged.** That list is
  enforced by a redaction utility, not by developer memory.

---

## 11. Definition of Done (a phase is not done until all of these are true)

```
[ ] Code compiles / lints clean with zero warnings
[ ] Unit tests written and passing for new logic
[ ] Integration path still passes end-to-end
[ ] No secrets, no real PII, no hard-coded infrastructure identifiers
[ ] Errors handled and mapped; no swallowed exceptions
[ ] Deadline budget threaded through any new I/O
[ ] Sensitive fields redacted in logs and masked in responses
[ ] IaC updated if resources or permissions changed
[ ] README / OpenAPI updated if the API surface changed
[ ] ADR written if a significant decision was made
[ ] AI-USAGE.md updated
[ ] Osama passed the comprehension check for this phase
```

---

## 12. Architecture Decision Records

For every significant choice, write `docs/adr/NNNN-short-title.md`:

```markdown
# ADR-0003: Single-table DynamoDB design

## Status
Accepted — 2026-09-08

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

These ADRs are also the raw material for the required Architecture Document deliverable.
