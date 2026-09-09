# ADR-0007: Rate evaluation, statement extraction, and risk signals

## Status

Accepted — 2026-09-09

## Context

Brief "AI-assisted evaluation layer" (see also ADR-0006 for the provider mechanics
this phase reuses) names three more capabilities beyond MCC classification:
**statement extraction**, **current-rate analysis** ("deterministic arithmetic must be
separated from AI commentary"), and **risk signals** ("every warning must cite the
input field or document that caused it"). `docs/08-IMPLEMENTATION-PLAN.md` Phase 8
adds: fixture-driven extraction in mock mode, a response that "cleanly separates
extracted / calculated / commentary," and "async fallback (202 + PROCESSING + poll)
if the budget cannot be met."

## Decision

### Statement extraction is real multimodal, not just a fixture claim

`IEvaluationProvider.ExtractStatementAsync` takes the actual document bytes.
`MockEvaluationProvider` ignores them entirely and returns a labelled fixture (brief's
explicit allowance). `GeminiEvaluationProvider` sends the real bytes as an inline
multimodal part (`inlineData: {mimeType, data: base64}`) -- Gemini's API genuinely
reads PDF/image/text content this way. Verified against the live API twice during this
phase's build:
- A raw multimodal call correctly identified inline plain-text content.
- `GeminiEvaluationProvider.ExtractStatementAsync` itself, run directly (not through
  the HTTP API -- see "What's verified" below), correctly extracted every field
  (processor, monthly volume, discount rate, per-transaction/monthly/chargeback fees,
  statement period) from a realistic synthetic statement, plus a coherent one-sentence
  commentary, in ~6 seconds.

No repair retry here (unlike classify) -- resending the same, potentially large, file
bytes a second time risks the 20-second call cap on its own. `EvaluationService`'s
fallback to the mock extractor is the safety net for a failed extraction instead, same
policy as classification (ADR-0006).

### Deterministic arithmetic, structurally separated from AI output

`EffectiveRateCalculator.Calculate(StatementExtraction?, int monthlyTransactionCount)`
is a pure static function. It never reads `StatementExtraction.Commentary` or
`.Provider` -- only the five numeric fields, and even those are only reachable after
`EvaluationService` has accepted them from a provider call. This is the literal,
structural enforcement of "deterministic arithmetic must be separated from AI
commentary," not a documentation promise: there is no code path by which an AI
response's prose can reach the math.

`null` vs `0` is a deliberate distinction, not an oversight: `MonthlyVolume` and
`DiscountRatePercent` missing means "we don't know" (returns `null` -- a missing-data
case); `MonthlyVolume = 0` is a legitimate calculable input (a real $0 month, giving a
0% effective rate). `PerTransactionFee`/`MonthlyFee`/`ChargebackFeeTotal` missing
default to `0` instead, since a statement that simply doesn't list a chargeback fee
line item reasonably means none was charged, not "unknown." Covered directly by
`EffectiveRateCalculatorTests`, per Phase 8's gate ("rate math tests including
zero-volume and missing-data cases").

### Deterministic risk-signal detection, not AI-judged flags

`RiskSignalDetector.Detect(Business, McClassification?, StatementExtraction?, Guid? statementDocumentId)`
covers every category the brief names (contradictions, missing evidence, unusually
high ticket, description/MCC mismatch, incomplete ownership data, regulated-activity
mentions) as plain domain logic over data already in the system -- not a model's
opinion. Same reasoning as the rate math: a risk flag a reviewer relies on should be
reproducible and explainable from the data alone, not something that could vary
between two calls with identical input. Every `RiskSignal` carries a required
`SourceField` (and `SourceDocumentId` when it concerns a specific upload), per brief
"no unexplained flags" -- enforced by the type itself having no optional-out for that
field, and asserted directly by
`RiskSignalDetectorTests.EveryReturnedSignalHasANonEmptySourceField` /
`EvaluationEndpointTests.EveryRiskSignalInTheResponseCarriesANonEmptySourceField`.

One deliberate simplification: "regulated-license claims without evidence" doesn't
check for an actual missing license *document*, only that the business description
mentions a regulated-activity keyword -- checking for a specific document's presence
would need a "list documents by application and type" repository query that doesn't
exist yet (documents are looked up by known ID only, per ADR/Phase 4's design; see
below). Flagged honestly, not silently narrowed.

### Response contract: extracted / calculated / commentary, literally separated

`EvaluationResponse` has `Extracted` (raw provider-reported figures, no commentary),
`Calculated` (`EffectiveRateResult`, purely computed), and a **top-level sibling**
`Commentary` string -- pulled out of the extraction record specifically so the HTTP
contract itself embodies "cleanly separates extracted / calculated / commentary," not
just the internal domain model.

### No "list documents by application" query -- the caller names the statement

`POST /v1/applications/{id}/evaluate` takes an optional `processingStatementDocumentId`
in its body rather than the server auto-discovering "the" processing statement for an
application. A new list-by-type DynamoDB access pattern (a GSI or a `Query` with
`sk begins_with "DOC#"`, filtered client-side by type) was in scope but deliberately
deferred: the client already has the ID from its own presign/complete response, so
requiring it explicitly is simpler, avoids a new access pattern under time pressure,
and is unambiguous when an application eventually has multiple documents of the same
type. A real product would likely still want a browsable document list; noted as a
Known Gap.

### Async fallback: a real, testable contract, not a real queue

Per brief "async fallback (202 + PROCESSING + poll) if the budget cannot be met":
`EvaluationService.EvaluateAsync` checks `budget.CanAttempt(5_000ms)` before doing
anything else. If the caller's own remaining deadline budget is already below that
(e.g. a retry deep into an already-long invocation), the `Evaluation` record is marked
`Processing` and returned immediately -- `EvaluationEndpoints` maps that to `202
Accepted` with a `Location` header pointing at the poll endpoint. **Honestly scoped:**
there is no background worker (SQS/Step Functions) that later completes a `Processing`
record on its own -- the contract (the shape of the response, the poll endpoint, the
domain state machine) is real and tested, but nothing currently *drives* a stuck
`Processing` record forward except a client calling `evaluate` again with more budget.
A real production version would need an async completion path; this is explicitly
Phase 10's territory ("deadline hardening," bounded retry with backoff), not invented
here to avoid overclaiming a capability that isn't actually wired end-to-end.

In practice this path is rare: the one AI call involved (statement extraction) already
self-limits via `GeminiCallExecutor`'s independent 20-second cap and falls back to the
instant mock extractor on failure, so the *usual* reason a whole request would run out
of budget doesn't apply here the way it might for a slower, unbounded operation.

## What's verified

- `EffectiveRateCalculatorTests`: zero-volume (calculable, not missing-data),
  missing-MonthlyVolume/DiscountRatePercent (returns null), missing optional fees
  default to zero, and a full realistic breakdown with exact expected numbers.
- `RiskSignalDetectorTests`: every named category, both flagged and not-flagged cases,
  and that every signal carries a source.
- `EvaluationServiceTests`: no-Business failure, no-statement-supplied path (still
  computes signals), a real statement (via `InMemoryDocumentStorage.SimulateFullUpload`)
  producing both extraction and calculated rate, fallback-to-mock on a primary-provider
  failure, wrong document type/incomplete upload/unknown document each rejected
  correctly, and the budget-exhausted 202/Processing path.
- `EvaluationEndpointTests`: the full HTTP contract, including a real presign/complete
  document upload through the actual endpoints (not a shortcut) before evaluating
  against it.
- `GeminiEvaluationProviderTests`: the real multimodal request shape (inlineData
  present only when extracting, never a stray null field on classify), missing-field
  tolerance, and no-retry-on-failure for extraction specifically.
- **Manually, against the live Gemini API**, `GeminiEvaluationProvider.ExtractStatementAsync`
  was run directly against a realistic synthetic statement (not through the full HTTP
  stack, since populating `InMemoryDocumentStorage` requires either a test process or a
  real S3 upload this session has no AWS account for) and correctly extracted every
  field plus a coherent commentary in ~6 seconds. A first attempt hit a real HTTP 503
  (free-tier overload, the same failure mode `DependencyUnavailableException` exists
  to handle) before a retry succeeded -- both outcomes are genuine, not staged.

## Consequences

- The "regulated-license claims without evidence" signal is weaker than its ideal form
  (keyword-only, not evidence-checked) until a list-documents-by-type query exists.
- The async-fallback path is a real, tested contract with no real background completion
  behind it yet -- a client that hits `202` and never calls `evaluate` again with more
  budget will see a `Processing` record indefinitely. Acceptable for this phase's
  scope; a genuine gap for a production system, documented rather than hidden.
