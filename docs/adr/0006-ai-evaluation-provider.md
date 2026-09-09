# ADR-0006: AI evaluation provider (classification)

## Status

Accepted — 2026-09-09

## Context

Brief "AI-assisted evaluation layer": "Must work through an adapter with a mock
implementation when no credentials exist. The interface and failure behavior must be
production-minded, even in mock mode." Phase 7 of `docs/08-IMPLEMENTATION-PLAN.md`
scopes MCC classification specifically: `IEvaluationProvider` + `MockEvaluationProvider`
(default) + a real implementation behind config, strict response-schema validation,
size/token limits, budget-derived timeout, bounded repair retry, safe fallback, PII
minimization in prompts, `POST /v1/applications/{id}/classify`.

`docs/06-COLLABORATION-PROTOCOL.md` names "Anthropic / OpenAI / Bedrock" as example
providers when asking whether Osama has an API key. Mid-session, he supplied a real
**Gemini** API key instead, with the instruction to use only free-tier models and to
build so the system works whether or not a real key is ultimately available at grading
time. Nothing in the brief mandates a specific vendor -- "an adapter with a mock
implementation" is the actual requirement, and Gemini satisfies it exactly the same way
Anthropic would have. `AiProvider` (previously `{Mock, Anthropic}`, unimplemented) was
changed to `{Mock, Gemini}` rather than kept alongside a real Gemini implementation and
a fictitious Anthropic option that was never built -- an enum value with no
implementation behind it is worse than no value at all.

## Decision

### Two providers behind one interface

`IEvaluationProvider.ClassifyMccAsync(BusinessProfileInput, IReadOnlyList<McCandidateSeed>, DeadlineBudget, CancellationToken)`
(`src/Gweb.Domain/Evaluation/`). Two implementations in `Gweb.Adapters.Evaluation`:

- `MockEvaluationProvider` -- deterministic, offline, reuses whatever catalog hints
  it's given (see "Grounding" below) rather than a hand-maintained fixture list, so it
  can never suggest a code that doesn't exist.
- `GeminiEvaluationProvider` -- calls the real Gemini API. Verified against the live
  API multiple times during this phase's build, not assumed from documentation --
  see "What's verified" below for what that actually proved.

### Grounding: never let the model invent a code

Both providers receive the same `IReadOnlyList<McCandidateSeed>` -- real
`(code, description)` pairs pulled from `IMccCatalog`. The prompt explicitly instructs
Gemini to choose only from that list. `ClassificationService` then independently
re-validates every returned code against `IMccCatalog.GetByCode` and drops anything
that isn't real, so a hallucinated code is defense-in-depth-filtered even if the model
ignores the instruction. `GeminiEvaluationProvider` itself never has a reference to
`IMccCatalog` at all -- it structurally cannot special-case around the catalog, the
same reasoning `IRiskPolicy` uses to stay decoupled from `IMccCatalog` (ADR-0005).

**A real bug this grounding step exposed, found manually verifying the live
integration, not by a unit test:** the first version of `ClassificationService` passed
`business.BusinessDescription` -- a full free-text sentence -- straight into
`IMccCatalog.Search` as one query string. `StaticMccCatalog.Search` does
substring/prefix matching (ADR-0004's design, tuned for a short typed query like
"grocery"), so a whole sentence essentially never matches anything: the live test
against a real grocery-store description returned **zero** catalog hints, meaning
neither Gemini nor the mock fallback had anything real to rank against. Fixed by
splitting the description into significant words (≥4 characters) and searching per
keyword, unioning the results, falling back to the catalog's own browsing default only
if every keyword search comes up empty. Covered by
`ClassificationServiceTests.ClassifyBuildsCatalogHintsFromDescriptionKeywordsNotAsOneLiteralSubstring`.
This is exactly the kind of bug a mocked-everything test suite cannot catch --
`MockEvaluationProviderTests`/`ClassificationServiceTests`' fake providers never
exercised the *real* catalog's actual matching behavior against realistic input, only
against hand-picked hint lists.

### PII minimization

`BusinessProfileInput` (`src/Gweb.Domain/Evaluation/BusinessProfileInput.cs`) is the
*only* thing either provider ever sees: legal business name, business description,
website, entity type. No Applicant field (name, DOB, government ID, address) and no
other Business field (registration identifier, bank account, beneficial owners) is
reachable from it -- enforced by the record's own shape, not a redaction step applied
at call time that could be forgotten.

### Schema validation, size limits, and the repair retry

`GeminiEvaluationProvider` treats every response as untrusted:

- The response body is capped (`MaxResponseBodyChars`) before any parsing is attempted.
- The model's own JSON payload is deserialized into a strict schema; malformed entries
  (empty code/explanation) are dropped rather than failing the whole response;
  confidence is clamped to `[0, 1]`.
- **Exactly one** repair retry: if the first response isn't valid JSON, a corrective
  follow-up prompt ("your previous response was not valid JSON... reply again with
  ONLY the JSON object") is sent once. A second failure is a hard failure -- never an
  unbounded loop.
- A response truncated by the model's own output-token cap (`finishReason: MAX_TOKENS`)
  is deliberately **not** thrown as a special exception -- an earlier version did throw
  there, which skipped the repair-retry path entirely (an exception from inside the
  single HTTP-call helper propagates straight past the retry logic, never through it).
  Caught by this phase's own test suite (`TreatsAMaxTokensFinishReasonAsATruncatedResponseAndRetries`
  failed against the buggy version). Fixed by letting the truncated (syntactically
  invalid) text flow into the same JSON-parse-failure path a garbled response already
  uses, so it retries through the one mechanism that actually exists.

### Timeout: an empirical finding, not an assumption

`GeminiCallExecutor` mirrors `DynamoDbCallExecutor`/`S3CallExecutor`'s budget-derived
timeout shape, with one real difference: a hard `MaxCallMs = 20_000` cap, independent of
how much of the request's deadline budget remains. This is not a guess --
manually verifying the real API during this phase's build showed `gemini-3.6-flash` is
a "thinking" model that spends a large share of its output-token budget on hidden
reasoning tokens before producing visible text (confirmed via the raw
`usageMetadata.thoughtsTokenCount` field: 430-470 tokens of thinking against a
`maxOutputTokens` budget that needed to be pushed to 2048 just to leave room for a
complete 3-candidate answer). A real classify call during manual testing took as long
as ~30 seconds for a small prompt, and once genuinely returned an HTTP 503 (temporary
overload) after ~3 seconds. Against a 35-second internal target, letting one dependency
call consume unbounded time is not acceptable -- capping this one call at 20 seconds
regardless of remaining budget guarantees there is always time left for
`ClassificationService` to fall back to the mock provider and still answer within
budget, rather than gambling the entire request on a single slow call.

### Fallback policy lives in the orchestrator, not either provider

`ClassificationService` (`src/Gweb.Services/Evaluation/ClassificationService.cs`)
catches any `DomainException` the primary provider throws (timeout, invalid JSON after
retry, oversized response, HTTP error) and retries once against
`MockEvaluationProvider`, labeling the result `"provider": "mock"` and logging the real
failure reason (`evaluation_provider_fallback`, with the domain error code) for
observability. This is the direct implementation of brief "fall back safely" +
"mock output is always labelled" -- a caller always gets a usable, honestly-labelled
result, never a raw 503 for what should be a graceful degradation. When
`AI_PROVIDER=mock`, the primary provider *is* the fallback provider (same DI-resolved
singleton instance), so this path is a harmless no-op in that configuration, not dead
code left over for a scenario that can't occur -- it is what makes "the interface and
failure behavior must be production-minded, even in mock mode" literally true, not just
asserted.

### API key handling

The key travels as the `x-goog-api-key` HTTP header, not a `?key=` query-string
parameter -- both forms were verified to work against the real API; the header was
chosen so the key never appears in a URL that request-logging middleware or a proxy
might capture. `GeminiCallExecutor`'s `HttpRequestException` handler deliberately omits
the exception message from the thrown `DependencyUnavailableException` (whose
`Details` property is serialized straight into the client-facing HTTP response by
`HttpErrorMapper`) -- an `HttpRequestException.Message` can carry hostnames/connection
details that have no business reaching a client. A first draft of this file's comment
claimed this convention while the code next to it violated it (passed `ex.Message`
through); caught and fixed before commit, not after.

The real key Osama supplied lives only in a local, git-ignored `.env` file, never in
source, following the exact same discipline as every other credential in this project
(`docs/04-SECURITY-RULES.md`).

## What's verified

Manually, against the real Gemini API, multiple times during this phase's build --
not simulated, not assumed from documentation:

- `GET /v1beta/models` confirmed the API key is valid and enumerated the real model
  lineup -- `gemini-2.5-flash` (assumed from general knowledge) turned out to be
  retired for new users; the API's own 404 error named the replacement
  (`gemini-3.6-flash`), which is what this codebase actually uses.
- A real `generateContent` call with the exact JSON-schema-constrained prompt this
  codebase sends returned a well-formed, correctly-classified answer for a realistic
  grocery-store description (MCC 5411, confidence 0.98) -- through the **entire** real
  stack: `POST /v1/applications/{id}/classify` → `ClassificationService` →
  `GeminiEvaluationProvider` → real HTTPS call → parsed, catalog-validated, persisted
  to (in-memory, for this run) storage → HTTP response.
- A separate real call returned an HTTP 503 (temporary overload), which
  `GeminiCallExecutor` correctly translated to `DependencyUnavailableException`, which
  `ClassificationService` correctly caught and used to fall back to the mock provider,
  logged with the real failure reason -- the fallback path was exercised for real, not
  only in a unit test with a fake exception.
- `POST .../classify/confirm` and `GET .../classify` were both exercised against the
  real classification the live Gemini call produced, including a rejection of an
  unknown MCC code.

The full automated test suite (`Gweb.Tests`) never depends on any of this --
`TestEnvironment.cs` forces `AI_PROVIDER=mock` unconditionally, so CI and every
`dotnet test` run stay fast, deterministic, and require no network access or API key.
`GeminiEvaluationProvider` itself is separately unit-tested with a fake
`HttpMessageHandler` covering the retry, timeout, size-limit, and non-success-status
paths without any real network call.

## Consequences

- Free-tier Gemini quota/rate limits are real and were hit once during manual testing
  (the 503). The fallback-to-mock design means this degrades a classification's
  quality, not the request's success -- documented as a Known Gap, not hidden.
- If Google renames or retires `gemini-3.6-flash` again (as it already has once during
  this build), only `GEMINI_MODEL` needs to change -- no code change, per
  `AppConfigLoader`'s existing env-var-driven config pattern.
- The 20-second hard cap on a single Gemini call is a deliberate, documented ceiling
  below what this model can sometimes take -- if a future, faster model becomes
  available on this account, this cap should be revisited rather than left as a
  historical artifact of one slow model generation.
