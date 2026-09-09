# AI Usage Report

> **Instructions for Claude Code:** this file is a living document. Append to it at the
> end of **every merged branch** — never write it all at once at the end. Commit
> timestamps make backfilled honesty obvious. Osama must read and edit every entry
> before it is committed; the first-person voice below is his, not yours.
>
> Delete this instruction block before submission.

---

## 1. Tools and models used

| Tool | Model | Used for |
|---|---|---|
| Claude Code (CLI) | claude-sonnet-5 | Implementation, refactoring, tests, documentation (Phases 0–1, 2026-09-08/09, including a full backend runtime rewrite mid-session — see §2 and §5) |

---

## 2. What AI was used for

Be specific per area, not generic.

| Area | AI involvement | My involvement |
|---|---|---|
| Repo scaffolding (tsconfig/eslint/jest/CI) | Generated all config files and resolved TS6/ESLint10 config-breakage from version drift (moduleResolution deprecation, isolatedModules requirement) | *(Osama: describe what you reviewed/changed)* |
| Deadline/timeout primitive | Generated `DeadlineBudget`/`IClock`/`FakeClock` and their tests | *(Osama: fill in)* |
| Structured logger + redaction | Generated `redact()`, the logger, and the "fully populated fixture" security test | *(Osama: fill in)* |
| Domain error taxonomy + HTTP mapper | Generated | *(Osama: fill in)* |
| Config loader | Generated | *(Osama: fill in)* |
| IaC (SAM template) | Generated for both the original TypeScript stack and the .NET rewrite; caught and fixed two real issues by actually running the tools rather than assuming (nodejs20.x already past its Lambda deprecation date; .NET 9 being container-image-only and deprecating 2026-11-10 on Lambda vs. .NET 10 as a managed runtime) | *(Osama: fill in)* |
| Runtime/language rewrite (TS → ASP.NET Core/.NET 10) | Directed to do this by Osama mid-session; Claude removed the TypeScript backend entirely and reimplemented every Phase 0–1 primitive in C#, then verified the result with `dotnet test` and a real `sam local start-api` run through Docker | *(Osama: fill in — was this the right call, and could/should this have been avoided by confirming the runtime choice before Phase 0 started?)* |
| DynamoDB access-pattern design | ADR-0003 written before any persistence code, per docs/03-ARCHITECTURE-RULES.md §4 | *(Osama: fill in)* |
| Application domain entity + state machine | Generated `Application`/`ApplicationStatus`/`IApplicationRepository`; `Submit()` throws on an illegal transition rather than allowing it | *(Osama: fill in)* |
| DynamoDB + in-memory repository implementations | Generated; found and fixed two real bugs by actually running the tests against them rather than trusting the first version (an exception-swallowing bug in the timeout wrapper, and a wrong assumption about AWSSDK v4's `IsItemSet` semantics for a missing item) | *(Osama: fill in)* |
| Unit tests | Generated throughout; Moq introduced for the DynamoDB client boundary specifically (implementing the full `IAmazonDynamoDB` interface by hand was not worth it) | *(Osama: fill in)* |
| Integration test | `HealthEndpointTests`/`ApplicationEndpointsTests` via `WebApplicationFactory<Program>` -- real HTTP through the real ASP.NET Core pipeline; the applications endpoints were also verified a second way, directly against a real (local) DynamoDB, not just the in-memory adapter | *(Osama: fill in)* |
| IaC / IAM policies | `ApiFunction`'s `Policies:` block scoped to exactly `dynamodb:PutItem`/`dynamodb:GetItem` on the one table ARN -- extended, not widened, as Phase 3+ needs more actions | *(Osama: fill in)* |
| Validation logic (Applicant/Business, ~30 fields) | Generated every field's accept/reject rules from the brief §3.1/§3.2, plus the cross-entity ownership-percentage check (`OwnershipValidator`) that neither entity alone could enforce | *(Osama: fill in -- did you spot-check these against the brief field-by-field, or trust the test count?)* |
| PII masking at capture | `GovernmentIdentification`/`RegistrationIdentifier`/`SettlementBankAccount.FromFull*()` extract only last4 and discard the rest immediately, so the full value never exists in a loggable field | *(Osama: fill in)* |
| Persistence bugs (in-memory repositories) | Found and fixed two more real bugs this phase by running tests, both variations on "the caller mutated a live reference the repository was also holding" -- full detail in §5 | *(Osama: fill in)* |
| PATCH endpoints + enum serialization | Found a real bug via HTTP-level tests: `System.Text.Json`'s default enum handling expects numbers, not strings ("Llc"/"Passport"), fixed with `JsonStringEnumConverter` | *(Osama: fill in)* |
| Documentation (README, ADRs, this file's factual tables) | Generated | *(Osama: fill in)* |
| Document domain entity + state machine (Phase 4) | Generated `Document`/`DocumentStatus`/`DocumentType`, `FileSignatureValidator` (magic-byte check), `DocumentKeyGenerator` (non-guessable S3 key, extension server-derived), `FilenameSanitizer`, `AllowedContentTypes`, `DocumentUploadLimits` | *(Osama: fill in)* |
| S3 presigned-upload adapter (Phase 4) | Generated `S3DocumentStorage` using presigned **POST** (not PUT) specifically because only POST can enforce a content-length-range condition -- verified against the installed `AWSSDK.S3` package's own XML docs before writing the code, not assumed. Pins Content-Type/size/checksum as S3 policy conditions; `complete()` re-verifies via `ChecksumMode.ENABLED` plus a 16-byte ranged read | *(Osama: fill in)* |
| `DocumentService` (presign + complete orchestration) | Generated; complete() is idempotent (a repeat call after the document leaves `Uploading` just returns the current record without re-verifying) and treats a checksum/size/signature mismatch as a normal `Rejected` outcome, not an exception | *(Osama: fill in)* |
| Document endpoints + IAM (Phase 4) | `POST .../documents/presign`, `POST .../documents/{id}/complete`, `GET .../documents/{id}`; `ApiFunction`'s policy extended with `s3:PutObject`/`s3:GetObject`/`s3:GetObjectAttributes` scoped to the one documents bucket ARN -- see §5 for why `PutObject` is required even though the Lambda never uploads a byte itself | *(Osama: fill in)* |

*(Osama: the "My involvement" column is intentionally blank — Claude should not write
this in your voice. Fill it in with what you actually reviewed, questioned, or would
change.)*

---

## 3. Attribution: AI-generated vs. author-written

| Component | Substantially AI-generated | Primarily written by me | Notes |
|---|---|---|---|
| `shared/`, `config/`, `handlers/health.ts`, `infra/template.yaml` (Phases 0–1) | ☑ | ☐ | Everything in Phases 0–1 was Claude-authored, under a fast-moving "just build it" instruction rather than a design-first-then-implement flow. That is a fact worth being honest about in the interview — see the comprehension check in `docs/PROGRESS.md`. |

Be honest and granular. The client stated explicitly that they do **not** penalize more
AI use — they penalize inability to evaluate and own the output.

---

## 4. Representative prompts

Include 4–8 real prompts that show **direction**, not just requests. Prefer ones where
constraints were specified up front.

<details>
<summary>Prompt 1 — <short title></summary>

```
<the actual prompt>
```

**What I was trying to achieve:** …
**What came back:** …
**What I changed:** …
</details>

---

## 5. Where I changed, rejected, or debugged AI output

This is the most important section. Aim for **at least 5 concrete, specific entries.**
Vague entries ("I reviewed everything") are worth nothing.

### Example shape

**Issue:** The generated presign handler trusted the client-supplied `Content-Type`
without pinning it in the S3 presign conditions.
**Why it mattered:** A client could presign for `image/png` and upload an arbitrary
payload; the metadata would then misrepresent the object.
**What I did:** Pinned `Content-Type` in the presign condition, added a size condition,
and added a magic-byte check on `complete`.
**Test added:** `presign_rejects_when_extension_and_mime_type_disagree`.

---

**Issue:** Claude built and merged all of Phases 0–1 in TypeScript/Node before I had
actually confirmed the runtime — it stated TypeScript as a default and kept moving
after I said "get it done," rather than treating "confirm the runtime" as a real
blocker. When I did weigh in (ASP.NET, and .NET 9 because it's already installed),
the entire backend needed reimplementing from scratch in C#.
**Why it mattered:** *(Osama: fill in — how much time did this actually cost you,
and would you rather Claude had blocked on this question even after you said to move
fast?)*
**What I did:** *(Osama: fill in — did you review the C# rewrite line-by-line, or
did you accept it based on the tests passing?)*
**Test added:** N/A — this was a reimplementation of existing tests (53 xUnit tests
port the same 55 Jest tests, roughly 1:1), not new coverage.

---

**Issue:** I asked for .NET 9. Claude checked current AWS documentation instead of
just complying, and found .NET 9 is container-image-only on Lambda with a Lambda
deprecation date of 2026-11-10 (about two months away), while .NET 10 — also already
installed on my machine — is a fully managed runtime supported through 2028.
**Why it mattered:** *(Osama: fill in — do you agree with using .NET 10 instead of
what you literally asked for? This is exactly the kind of substitution the repo rules
say must be flagged, not silently made — was it flagged clearly enough before it
happened?)*
**What I did:** *(Osama: fill in)*
**Test added:** N/A — this changed the target framework/Lambda runtime, not behavior.

---

**Issue:** The first attempt at `sam local start-api` was pointed at
`infra/template.yaml` (the source template) instead of `.aws-sam/build/template.yaml`
(the built one). For a compiled runtime like .NET, that mounts the *source* directory
into the Lambda container instead of the published executable, and the request failed
with a `502` and `Error: executable assembly ... not found`.
**Why it mattered:** This would have been a confusing, hard-to-diagnose failure for
anyone following the README's setup steps if it had shipped uncorrected — "it built
fine but doesn't run" is exactly the kind of gap the assessment penalizes.
**What I did:** *(Osama: fill in)*
**Fix:** Re-ran pointed at `.aws-sam/build/template.yaml`; documented the distinction
explicitly in the README's "Prerequisites and local setup" section so it isn't
repeated.

---

**Issue:** `DynamoDbApplicationRepository`'s first version wrapped every AWS SDK call
in one generic `catch (AmazonDynamoDBException)` that translated everything to
`DependencyUnavailableException` -- including `ConditionalCheckFailedException`
(a subtype), which should have become `ConflictException` instead. This was caught by
actually running `CreateTranslatesAConditionalCheckFailureIntoConflictException` and
watching it fail with the wrong exception type, not by inspection.
**Why it mattered:** *(Osama: fill in — this is exactly the kind of bug that a test
suite catches and a code review skim doesn't; does that change how much you trust the
"it compiles and looks right" bar for reviewing AI output?)*
**What I did:** *(Osama: fill in)*
**Test added:** `CreateTranslatesAConditionalCheckFailureIntoConflictException` (the
one that caught it) plus an exception filter (`when (ex is not ConditionalCheckFailedException)`)
in `DynamoDbApplicationRepository.ExecuteAsync`.

---

**Issue:** A test simulating "item not found" set `GetItemResponse.Item = []` (empty
dictionary). It failed with `KeyNotFoundException` instead of the expected null
result -- because in AWSSDK v4, `GetItemResponse.IsItemSet` reflects whether `Item`
was assigned at all, not whether it's non-empty, and a real "not found" response
leaves `Item` as `null`. Confirmed against the installed package's own XML docs before
fixing (not from memory).
**Why it mattered:** The *production* code (`response.IsItemSet ? FromItem(...) : null`)
was actually correct; the *test* was simulating AWS's behavior wrong. Easy to have
shipped a passing-for-the-wrong-reason test if the assertion had been looser.
**What I did:** *(Osama: fill in)*
**Fix:** Changed the test fixtures to `Item = null`, with a comment explaining why,
so the next person editing this test doesn't reintroduce the same wrong assumption.

---

**Issue:** `InMemoryApplicantRepository`/`InMemoryBusinessRepository`'s `SaveAsync`
stored the caller's live object reference instead of a copy. `Applicant`/`Business`
are mutable classes, so a second `ApplyUpdate` call on the same in-memory object after
saving silently mutated the "persisted" copy too, breaking the optimistic-concurrency
version check. Caught by `SecondUpdateMergesOntoTheFirst` failing for real with a
`ConflictException` that made no logical sense given the test's own sequence of calls.
**Why it mattered:** *(Osama: fill in — this bug wouldn't affect the real DynamoDB
repository at all, since serializing to AttributeValues is inherently a copy. Does
that change how seriously you'd weigh a test-double-only bug versus a bug in the real
adapter?)*
**What I did:** *(Osama: fill in)*
**Fix:** Added `Snapshot()` to `Applicant`/`Business`/`Application`, called it in both
`SaveAsync` (store a copy) and `GetByApplicationIdAsync`/`GetByIdAsync` (return a
copy) in all three in-memory repositories. Two regression tests added, one per side of
the bug (mutate-after-save, mutate-after-get).

---

**Issue:** PATCH requests carrying enum fields as strings (`"entityType": "Llc"`,
`"governmentId": {"type": "Passport", ...}`) failed to deserialize --
`System.Text.Json`'s default enum handling expects the numeric underlying value, not
the name, unless a `JsonStringEnumConverter` is registered. Two endpoint tests failed
for real with unexpected status codes/unparseable response bodies before this was
diagnosed.
**Why it mattered:** *(Osama: fill in — every curl example in the README sends enums
as strings; without this fix, the documented API examples would not have worked as
written, which is exactly the kind of gap that erodes trust in documentation.)*
**What I did:** *(Osama: fill in)*
**Fix:** `builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))`
in `Program.cs`, applied globally so it covers both request deserialization and
response serialization consistently.

---

**Issue:** The first cut of `ApiFunction`'s IAM policy for S3 granted only
`s3:GetObject`/`s3:GetObjectAttributes` on the documents bucket, reasoning (in a code
comment) that "the presigned POST itself needs no Lambda-side IAM grant -- the
signature is what authorizes the client's upload." That reasoning is wrong: a
presigned request is signed with the *signer's own* IAM credentials, so S3 authorizes
the eventual upload against the Lambda role's permissions, not against some
signature-derived allowance. Without `s3:PutObject` on the role, every client upload
using a validly-signed presigned POST would still get a `403` from S3.
**Why it mattered:** This would have been a working `sam validate`/`sam build`, a
working presign response, and a `403` only at actual upload time -- exactly the kind
of gap that looks fine in every test that doesn't hit real S3, and was caught only by
re-reasoning about how SigV4 presigning actually authorizes a request, not by any test
in this repo (none exercise the real IAM policy).
**What I did:** *(Osama: fill in -- this is a good example of "compiles and the tests
pass" not being sufficient to trust; do you want a note added to the Known Gaps
section flagging that IAM policies specifically are unverified against real AWS?)*
**Fix:** Added `s3:PutObject` to the policy statement in `infra/template.yaml`,
corrected the comment to explain why it's required.

---

**Issue:** A test for `S3DocumentStorage` guessed `CreatePresignedPostResponse`'s
constructor as `new CreatePresignedPostResponse(url, fields)` (2 positional args).
`dotnet build` failed immediately: the real type has a parameterless constructor with
`Url` (a `string`, not a `Uri`) and `Fields` as settable properties. A second test
compared a `byte[]` prefix using `Assert.StartsWith`, which only has string overloads
in xUnit and doesn't work on byte arrays at all.
**Why it mattered:** Both were caught at compile time, not runtime -- cheap mistakes,
but exactly why this project's rule is "verify against the installed package's XML
docs before writing the call," which is what resolved both: checked
`CreatePresignedPostResponse`'s real member list in `AWSSDK.S3.xml` rather than
guessing a second time.
**What I did:** *(Osama: fill in)*
**Fix:** Object-initializer syntax with the real property names/types; replaced
`Assert.StartsWith` with `Assert.Equal(expected, actual[..4])` for the byte-array
comparison.

---

## 6. Errors and weaknesses I found in AI suggestions

Categories worth watching for, with real examples from this project:

- **Outdated or hallucinated AWS SDK APIs** — a web search for how to access
  `ILambdaContext` from `Amazon.Lambda.AspNetCoreServer.Hosting` paraphrased the API
  as a `GetRemainingTimeInMillis()` method. Claude cross-checked against the actual
  installed NuGet package's XML doc comments (`Amazon.Lambda.Core.xml`) before writing
  any code and found the real member is a `RemainingTime` property (a `TimeSpan`), not
  a method — the search summary was a paraphrase, not the literal API. Ground-truthed
  against the installed package rather than trusting the search result.
- **Over-broad IAM** (`s3:*`, `Resource: "*"`) proposed by default — …
- **Timeouts not propagated** — plausible-looking async code with no deadline threading — …
- **Tests that cannot fail** — asserted on mock behavior rather than production code — …
- **Silent error swallowing** in `catch` blocks — the first version of
  `DynamoDbApplicationRepository.ExecuteAsync` caught `AmazonDynamoDBException`
  broadly enough to swallow `ConditionalCheckFailedException` before the caller's own
  more specific handler ever saw it, turning an expected "already exists" conflict
  into a generic "unavailable" error. Caught by a test, fixed with an exception
  filter — full details in §5.
- **PII leaking into logs or prompts** — …
- **Invented MCC codes or a stale "high-risk MCC" list** instead of the real catalog — …
- **Over-engineering** — abstractions with a single implementation and no reason to exist — …

---

## 7. How I verified AI-generated code

State what verification actually happened. Only claim what you did.

- [ ] Read every line before committing; rejected anything I could not explain
- [ ] Wrote or reviewed tests for each behavior, including failure paths
- [ ] Verified AWS API behavior against current official documentation, not model memory
- [ ] Ran the full journey end-to-end locally
- [ ] Verified timeout behavior with a deliberately hanging dependency
- [ ] Reviewed every IAM statement and removed anything I could not justify
- [ ] Grepped logs and history for secrets and PII
- [ ] Confirmed no auto-approval path exists anywhere in the codebase
- [ ] Deliberately broke implementations to confirm tests actually fail

---

## 8. What I would have done differently

Honest retrospective: where AI sped things up, where it cost time, where I should have
written it myself first, and what I would change about how I directed it.

---

## 9. Ownership statement

I have reviewed all code in this repository. I understand the architecture, the
tradeoffs, and the known gaps, and I can explain and defend any part of it. Where I was
unsure, I have said so explicitly in the README under **Known Gaps** rather than
presenting untested code as complete.

— Osama Ammar Othman
