# ADR-0009: Deadline hardening -- I/O budget audit and bounded retry

## Status

Accepted -- 2026-09-09

## Context

`docs/08-IMPLEMENTATION-PLAN.md` Phase 10 asks for four things: (1) an audit that
*every* I/O path actually propagates and respects the request's `DeadlineBudget`, (2)
hanging-dependency tests demonstrating safe exit before the 45s hard limit, per
`docs/05-TESTING-RULES.md` §1, (3) "bounded retry with backoff + jitter, budget-aware",
and (4) a structured timeout response with state preserved for safe retry. This ADR is
numbered 0009, not 0006 as the plan document's stale text suggests -- ADR-0006 was
already taken by the Phase 7 AI-provider decision by the time this phase was reached.

## Decision

### 1. I/O budget-propagation audit: no gaps found

Every outbound call in `src/` -- all six DynamoDB repositories, both S3 upload/download
paths, and every Gemini call -- already routes through one of three small
`*CallExecutor` classes (`DynamoDbCallExecutor`, `S3CallExecutor`, `GeminiCallExecutor`),
each of which derives a real per-call timeout from the caller's `DeadlineBudget` via
`ForCall(reserve, perCallMax)` and applies it with a linked `CancellationTokenSource`.
This was true before this phase started (Phases 2-8 built it in as each adapter was
added) and this phase's audit is the first time it was checked end-to-end and written
down rather than assumed. No call site needed a code change for this item.

### 2. Bounded retry with backoff + jitter: added, but scoped to Gemini only

A new `Gweb.Shared.Resilience.BoundedRetry.ExecuteAsync` wraps any budget-bound
operation with exponential backoff and *full jitter* (`delay = Random(0, min(cap, base
* 2^attempt))`, the shape AWS's own retry guidance recommends over a fixed or
capped-only delay). It retries only a `DomainException` whose own `Retryable` flag is
`true` -- reusing that existing classification (`DependencyTimeoutException` /
`DependencyUnavailableException`, both already flagged `Retryable => true` since Phase
1's error taxonomy) as the single source of truth for "worth retrying," rather than
inventing a second one. It never sleeps toward a retry the budget cannot afford: before
waiting, it checks `budget.CanAttempt(reserve + jitteredDelay)`; if that is false, the
most recent failure is rethrown immediately instead of guaranteeing the next attempt
just dies at `DeadlineBudget.ForCall <= 0` anyway.

**This is wired into `GeminiCallExecutor`'s caller (`GeminiEvaluationProvider.CallOnceAsync`)
only -- not into `DynamoDbCallExecutor` or `S3CallExecutor`.** Deliberately, not an
oversight: the AWS SDK for .NET already applies its own configurable retry policy
(exponential backoff for throttling/5xx/transient network errors) to every DynamoDB and
S3 call before an exception ever reaches our `catch (AmazonDynamoDBException)` /
`catch (AmazonS3Exception)` blocks. Stacking a second, uncoordinated app-level retry
loop on top of a client that already retries internally is a known anti-pattern (retry
amplification -- the two layers' backoff schedules aren't aware of each other, and a
transient overload gets hit by up to `SDK attempts x app attempts` requests instead of a
bounded, predictable number). Gemini is called via a raw `HttpClient` with no SDK and no
built-in retry of any kind, and it is also the one adapter that has hit a real transient
failure during this project (a genuine HTTP 503 from the free-tier API during Phase 8's
live verification) -- the adapter where an app-level retry both matters and does not
double up on anything else.

`GeminiEvaluationProvider` gained two optional constructor parameters --
`IDelay? delay = null` (defaults to a real `TaskDelay`) and `int maxCallAttempts = 3` --
so every existing call site (`Program.cs`'s DI registration, every existing test)
keeps working unchanged, while tests that specifically exercise retry behavior can
inject a `FakeDelay` for deterministic, instant-completing backoff.

### 3. Hanging-dependency tests

Already existed for DynamoDB (`DynamoDbApplicationRepositoryTests`) and Gemini
(`GeminiEvaluationProviderTests`) before this phase. This phase adds the third and
closes the one gap the audit found: `S3DocumentStorageTests.GetUploadedObjectThrowsDependencyTimeoutExceptionWhenTheCallHangsPastItsBudget`,
same shape as the other two (a `TaskCompletionSource` that never completes, a
budget sized so the real `CancelAfter` timeout is ~50ms, so the test runs in well under
a second).

New, dedicated retry tests (`BoundedRetryTests`, plus `GeminiEvaluationProviderTests.RetriesATransientHttpFailureAndSucceedsOnALaterAttempt`
/ `GivesUpAfterTheDefaultThreeAttemptsAgainstAPersistentFailure`) prove: a transient
failure is retried and a later success is still returned; retries are bounded and the
last failure is rethrown once attempts are exhausted; a non-retryable exception
(`ValidationException`) is never retried; and retry correctly stops early when the
remaining budget cannot afford the backoff plus another attempt, using a `FakeDelay`
wired to the same `FakeClock` the budget reads from so simulated backoff genuinely
consumes simulated budget the same way real backoff consumes real budget in production.

Three pre-existing Gemini tests were updated to pass `maxCallAttempts: 1` explicitly:
two (`ThrowsImmediatelyOnANonSuccessStatusCodeWithoutRetrying`,
`ThrowsImmediatelyWhenTheResponseBodyExceedsTheSizeLimit`) because retry is now the
real default behavior and their whole point is testing single-attempt exception
translation in isolation from that policy; one
(`ThrowsDependencyTimeoutExceptionWhenTheCallHangsPastItsBudget`) because its
`Budget()` helper uses a `FakeClock` frozen at 0, which never reflects the real
wall-clock time a `CancelAfter` timeout actually consumes -- without pinning to a
single attempt, `BoundedRetry` would see "plenty of budget left" (the fake clock never
moved) and retry against the still-hanging handler, which happened to still pass by
accident (the eventual exception type was still correct) but silently took ~3x longer
and no longer tested what its name says. Pinning to one attempt keeps these three tests
fast, deterministic, and honest about what they each verify.

### 4. Structured timeout response + state preserved for safe retry

No new mechanism needed -- both halves were already real, from earlier phases, and this
item is about confirming and documenting that, not building it:

- **Structured response:** `HttpErrorMapper` already turns `DependencyTimeoutException`
  into `504 DEPENDENCY_TIMEOUT` and `DependencyUnavailableException` into
  `503 DEPENDENCY_UNAVAILABLE`, both carrying `Retryable: true` in the JSON body (Phase
  1's error taxonomy) -- a client already gets a machine-readable signal for whether
  its *own* retry (at the HTTP level, above this system) is worthwhile.
- **Safe retry:** every write in this system already goes through a conditional
  DynamoDB `PutItem` (`ConditionExpression` on `attribute_exists`/`version`), which is
  atomic at the DynamoDB level -- a client-observed timeout on a write means the
  condition either committed or it didn't, never a partial state. A retried request
  with the same `expectedVersion` is therefore naturally idempotent: if the original
  write actually landed, the retry gets a clean `ConflictException` (409) rather than a
  silent double-write; if it didn't land, the retry succeeds normally. This is the same
  optimistic-concurrency design every entity has used since Phase 2 (`Application`,
  `Applicant`, `Business`, `Document`, `McClassification`, `Evaluation`), not a new
  mechanism -- Phase 10 is the first time it's named explicitly as the answer to "state
  preserved for safe retry" rather than only "prevent silent overwrites."

## What's verified

- `BoundedRetryTests` (6 tests): first-attempt success takes no delay; a transient
  failure is retried to an eventual success; a non-retryable `DomainException` is never
  retried; retries stop and the last failure is rethrown once `maxAttempts` is reached;
  a retry is skipped (failing fast) once the remaining budget could not afford the
  backoff plus another attempt; `maxAttempts < 1` is rejected.
- `GeminiEvaluationProviderTests`: the two new retry-demonstration tests above, plus
  every pre-existing test (updated where retry now changes call counts, otherwise
  unchanged) still passing.
- `S3DocumentStorageTests.GetUploadedObjectThrowsDependencyTimeoutExceptionWhenTheCallHangsPastItsBudget`:
  the third hanging-dependency test, closing the one gap the I/O audit found.
- Full suite: 392/392 passing, run in ~4s -- the new hang/retry tests add real but
  small wall-clock cost (jittered backoff capped at 100-200ms per retry with the
  default `maxAttempts: 3`), not the multi-second cost a naive implementation without
  `FakeDelay` injection would have added.

## Consequences

- `BoundedRetry`'s backoff constants (`BaseDelayMs: 100`, `MaxDelayMs: 2_000`,
  `MinReserveToRetryMs: 200`) are hardcoded, not environment-configurable -- consistent
  with this project's general preference (see ADR-0005, ADR-0006) for a small number of
  named, documented constants over a wider config surface not asked for by the brief.
  Trivial to promote to config later if a real production tuning need appears.
- The AWS-SDK-built-in-retry reasoning for DynamoDB/S3 is a documented architectural
  decision, not something this session verified against a live retry-storm scenario --
  this project has no AWS deployment to observe that against (see README "Known gaps").
  It rests on well-established, generally-known AWS SDK for .NET behavior
  (`ClientConfig`'s retry policy), not a guess.
