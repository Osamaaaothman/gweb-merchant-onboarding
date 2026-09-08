# 05 — Testing Rules

The brief names specific tests as **required deliverables**. These are not optional and
they are not "if there is time."

---

## 1. Required by the brief

### Unit tests — mandatory coverage areas
1. **Validation logic** — every rule, both accept and reject cases
2. **MCC / risk mapping** — suggestion logic, policy resolution, provider overrides
3. **Rate arithmetic** — effective rate calculation, edge cases, division by zero
4. **Lambda handlers / service logic** — happy path + failure paths

### Integration / end-to-end — mandatory, at least one
Full path, in one test:
```
create application
  → update business
  → pre-sign upload
  → mock upload completion
  → classify
  → evaluate
  → submit
```
Run against local Lambda/API simulation with in-memory or local (DynamoDB Local /
LocalStack / MinIO) adapters. Must be runnable with a single documented command.

### The 45-second test — explicitly required
> "Demonstrate at least one test where a mocked external dependency hangs or responds
> slowly and your function exits safely before the hard limit."

Build these:
- A mock adapter that sleeps longer than the budget
- A test asserting the handler returns **before 45s** (assert against the internal
  target, ~35s, using a controllable clock so the test itself is fast)
- A test asserting the response is a **structured timeout**, not a crash or a 502
- A test asserting **workflow state was preserved** for safe retry
- A test asserting retries are bounded and do not fire when the budget is exhausted

Use a `FakeClock` / injected time source so these tests run in milliseconds. A test
suite that actually waits 45 seconds is a design failure — say so if you find yourself
writing one.

---

## 2. Test quality standards

- **Never weaken or delete a test to make it pass.** Report the failure, fix the cause.
- **Never write a test that cannot fail.** If commenting out the implementation still
  leaves it green, it is not a test.
- Test names describe behavior, not method names:
  - Bad: `testPresign1`
  - Good: `presign_rejects_when_extension_and_mime_type_disagree`
- Arrange / Act / Assert structure, visibly separated.
- One logical assertion per test. Multiple `expect`s on the same behavior are fine;
  testing three unrelated behaviors in one test is not.
- No sleeps, no wall-clock dependence, no network calls to real AWS in unit tests.
  Inject the clock, inject the adapters.
- Tests are deterministic. If a test is flaky, it is broken — fix or delete it, never
  retry-loop it.

---

## 3. What to test, specifically

### Domain / validation
- Missing required fields for each entity
- Ownership percentages > 100% total
- DOB in the future; business start date in the future
- Invalid country/state codes, invalid postal formats
- Malformed EIN / registration identifier
- Body over the size limit → `413`
- Wrong content type → `415`
- Malformed JSON → `400` with a clean error, not a 502
- Unknown fields rejected

### Documents
- Presign rejects disallowed extension, disallowed MIME, oversize declared size
- Generated S3 key is non-guessable and does not contain user-supplied filename
- `complete` is idempotent — calling twice with the same checksum yields one state
- `complete` rejects a checksum that does not match
- Illegal lifecycle transitions are rejected (`REJECTED → UPLOADING`)

### MCC / risk
- Search returns expected codes for representative queries
- Suggestion returns code + confidence + explanation
- Ambiguous/sensitive categories are flagged for manual review
- **6012 / 6051 / 6211 route to enhanced review**
- Provider override changes the policy outcome for the same MCC
- Both applicant-selected activity and system-proposed MCC are persisted, and a
  mismatch produces a flag

### Rate arithmetic
- Effective rate from known volume + fees
- Zero volume, zero transactions → no division by zero, explicit "insufficient data"
- Missing fields → partial result with explicit gaps, not silent zeros
- **Calculated values never come from the AI adapter** — assert this

### AI adapter
- Invalid JSON from the model → schema validation error → safe fallback
- Schema-valid but semantically wrong (confidence > 1, unknown MCC) → rejected
- Oversized model output → truncated/rejected, recorded
- Timeout → structured degraded result, request still returns
- Mock mode is labelled `"provider": "mock"` in output
- Prompt injection in the business description does not change system behavior

### Persistence
- Optimistic concurrency: two concurrent updates → one succeeds, one gets `409`
- Idempotent create: same request ID twice → one record
- Access patterns from the ADR each have a test

### Security
- The logger test: a fully populated application produces logs containing none of the
  sensitive values (assert on the actual strings)
- Error responses contain no stack traces, table names, bucket names, or ARNs

### Submission
- Blocked when required data or documents are missing, with a precise missing-items list
- Produces the normalized review payload when complete
- Submitted application is locked/versioned

---

## 4. Coverage

Coverage is a signal, not a goal. But:
- `domain/`, `policy/`, and validation should be near-fully covered — they are pure
  and cheap to test
- Handlers need at least happy path + one failure path each
- Do not chase coverage on adapter glue with meaningless tests

Report actual coverage numbers in the README. Do not claim a number you have not measured.

---

## 5. Test evidence deliverable

The brief requires "automated test output plus a short note demonstrating the ≤45-second
timeout behavior."

Produce `docs/TEST-EVIDENCE.md` containing:
- The command(s) to run the suites
- **Real, pasted output** from an actual run (not fabricated)
- Coverage summary
- A dedicated section on the timeout test: what it simulates, what it asserts, what
  the output shows
- Any known-failing or skipped tests, with the reason — **do not hide them**

---

## 6. CI

GitHub Actions workflow that runs on every push: install → lint → build → unit tests →
integration tests. Keep it fast. A green CI on the default branch is cheap, visible
credibility for a reviewer skimming the repo.

---

## 7. The honesty rule

Never report a test as passing without having executed it. If you have not run it, say
"written but not yet executed." If output is truncated, say so. Fabricated test results
in a repo that will be inspected is the single fastest way to fail this assessment.
