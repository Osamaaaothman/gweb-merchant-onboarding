# ADR-0011: The mandatory end-to-end journey test

## Status

Accepted -- 2026-09-09

## Context

`docs/08-IMPLEMENTATION-PLAN.md` Phase 12 and `docs/02-ENGINEERING-STANDARDS.md` name a
specific, mandatory deliverable distinct from every other test in this suite: "the
mandatory single integration test: create → update business → pre-sign upload → mock
upload completion → classify → evaluate → submit," runnable with one documented
command, green from a clean checkout. Every prior phase already built thorough
endpoint-level tests for its own feature (`SubmitEndpointTests`, `EvaluationEndpointTests`,
`ClassificationEndpointTests`, `DocumentEndpointTests`, ...), but none of them chains
every stage together in one continuous run the way the brief specifically asks for.

## Decision

### One test method, not a suite of its own

`tests/Gweb.Tests/EndToEnd/EndToEndJourneyTests.cs` has exactly one `[Fact]`,
`TheFullMerchantOnboardingJourneySucceedsEndToEnd`. Deliberately not split into several
smaller tests -- the brief's whole point is proving the stages hold together in
sequence (a document uploaded in step 4 is still there when step 8 submits; a
classification confirmed in step 6 is still the one returned in step 8's payload), a
property that splitting into independent tests would stop verifying. Per-feature edge
cases (a rejected checksum, an unknown MCC code, a concurrent-submit conflict) already
have dedicated coverage elsewhere and are deliberately not repeated here.

### Exercises the real HTTP pipeline, never a service-layer shortcut

Uses `WebApplicationFactory<Program>` throughout, the same pattern every endpoint test
in this suite already uses -- every step is a real HTTP call through the real ASP.NET
Core request pipeline (model binding, `RequestExecution`'s deadline-budget wrapper,
`HttpErrorMapper`), not a direct call into `ApplicationService`/`SubmissionService`/etc.
A service-layer shortcut would prove the domain logic works but not that the wiring
between HTTP and that logic is correct -- exactly the gap this test exists to close.

### "Mock upload completion" reuses the established simulated-upload pattern

Every document-upload test in this suite (`DocumentEndpointTests`, `EvaluationEndpointTests`,
`SubmitEndpointTests`) presigns for real, then calls `InMemoryDocumentStorage.SimulateUpload`
directly against the app's own resolved `IDocumentStorage` singleton to stand in for the
client's real direct-to-S3 upload, then completes for real. This test follows the same
shape rather than inventing a new one -- consistent with the brief's own wording
("mock upload completion"), and the *real* (non-mocked) S3-compatible upload path was
already verified live against MinIO in Phase 11 (see
`docs/adr/0010-frontend-architecture.md`) -- this test is not where that gets
re-proven.

### Applicant update included, even though the brief's phase bullet only says "business"

A real `submit` requires both `Applicant` and `Business` to be complete
(`CompletenessChecker`) plus all three required documents -- omitting the applicant PATCH
would make the final `submit` call in this test always return `400`, never actually
reaching a genuine success. Including it is not scope creep; it is what the brief's own
named final step ("submit") requires to actually succeed, matching the acceptance
criterion "Submission... produces a normalized internal review payload when complete."

### Assertions beyond the happy path, without turning this into a second edge-case suite

A handful of assertions ride along the main journey precisely because they're free at
that point in the sequence and directly named by the brief's acceptance criteria: an
early `submit` attempt (before documents exist) is confirmed blocked with the exact
missing-items list; masked values (government ID, EIN, bank account) are asserted
absent, unmasked, from the final submit payload; a second `submit` after a successful
one is confirmed to return `409`, not a silent no-op. These reuse state the test
already built rather than standing up new fixtures, so they cost nothing extra to
verify here.

## What's verified

Green on the first run, no debugging required -- every stage it chains together
(applicant/business PATCH, presign/complete for three document types, classify,
confirm, evaluate, submit, and the resulting locked/`Submitted` state) was already
individually tested and correct from earlier phases; this test is new coverage of the
*sequence*, not new coverage of any single stage's logic. Runs with no external
dependency (`dotnet test --filter FullyQualifiedName~EndToEndJourneyTests`) since
`TestEnvironment.cs` forces `PERSISTENCE_PROVIDER=inmemory`/`AI_PROVIDER=mock` for the
whole suite -- satisfies "green from a clean checkout" literally, not just in spirit.

## Consequences

- This test is intentionally not exhaustive of every branch (e.g., it doesn't exercise
  the `202 Processing` async-fallback path for evaluation, or a rejected document
  checksum) -- those already have dedicated tests elsewhere
  (`EvaluationEndpointTests`, `DocumentEndpointTests`), and duplicating them here would
  work against the "one test proving the sequence holds" purpose this ADR describes.
- Like the rest of the automated suite, this test never touches real AWS, real S3, or
  a real Gemini call -- see `docs/adr/0010-frontend-architecture.md` for where that
  real, live, MinIO/DynamoDB-Local-backed verification actually happened this session.
