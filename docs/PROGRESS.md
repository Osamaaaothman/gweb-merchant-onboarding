# Progress Log

Session discipline per `docs/06-COLLABORATION-PROTOCOL.md` §8. Updated at the end of
every session, not rewritten from scratch.

---

## 2026-09-08 / 2026-09-09 — Phases 0 and 1

### What landed

- **Phase 0 (merged):** repo hygiene (`.gitignore`/`.gitattributes`/`.env.example`),
  TypeScript/ESLint/Jest tooling, GitHub Actions CI, README skeleton, `docs/adr/`.
- **Phase 1 (merged):** `DeadlineBudget` + `IClock` (System/Fake), correlation context
  (AsyncLocalStorage), structured logger + redaction utility (with the required
  "fully populated fixture leaks nothing" security test), domain error taxonomy +
  HTTP error mapper, fail-fast config loader, `handlers/health.ts`, SAM skeleton
  (`infra/template.yaml`: DynamoDB table, S3 bucket, HTTP API, health function),
  ADR-0001, ADR-0002.
- Also fixed: `docs/00-PRODUCT-BRIEF.md` previously described an unrelated ERP/SaaS
  product (see the flagged conflict at the start of this session) — replaced with an
  accurate summary of the actual GWEB assessment.
- 55 tests passing, 99.37% statement coverage on everything that exists so far.
  `sam validate --lint` and `sam build` verified locally.

### In progress

Nothing mid-flight — both phases are merged to `main` in a clean, tested state.

### Blocked on Osama

1. **Kickoff decisions were never actually confirmed** — the session moved to "just
   build it" with Claude's stated defaults before Osama answered. Recorded as an open
   item in ADR-0001. Specifically still open:
   - Runtime/language: TypeScript was picked; .NET 8 was the collaboration protocol's
     own default recommendation. **This needs a real answer** — it affects whether
     Osama can defend the code as confidently as the grading rubric requires.
   - AWS account/region/profile/billing limit — not yet provided; local-first
     development continues either way.
   - AI provider credential — mock-only so far, which is fine per the brief, but
     confirm whether a real key will be added later.
   - GitHub repo visibility (public/private) and who else needs access.
   - Time budget / hard deadline — affects how much of §15 Bonus gets attempted.
2. **Comprehension check for Phases 0–1** delivered in chat at the end of this
   session — mandatory per CLAUDE.md §5 before further phases should be considered
   "signed off," even though work continued past it at Osama's request.
3. **`git push` to `origin/main` has not happened.** Commits exist locally only,
   pending Osama's go-ahead (pushing is a shared-visibility action).
4. **`sam local start-api` unverified** — no Docker in the build sandbox. Needs
   Osama to actually run it and confirm the HTTP layer behaves like the unit tests
   predict.

### What the next session should start with

Phase 2 — Application lifecycle & persistence (`feat/application-lifecycle`):
ADR-0003 (DynamoDB access-pattern table, written before the code), `Application`/
`Person`/`Business` domain entities, `IApplicationRepository` + DynamoDB + in-memory
implementations, optimistic concurrency, `POST /v1/applications`, `GET /v1/applications/{id}`.

Before that: resolve the runtime/language question above if at all possible — every
phase after this one gets more expensive to port if it turns out .NET was the right
call.

---

## 2026-09-09 (same day, later) — Runtime pivot: TypeScript → ASP.NET Core / .NET 10

### What landed

Osama answered the open runtime question from earlier the same day: **ASP.NET, and
.NET 9** ("already installed on my device"). Before implementing, current AWS
documentation was checked rather than assumed — .NET 9 on Lambda is container-image-
only and deprecates 2026-11-10; .NET 10 is a managed runtime, also already installed
locally, supported through 2028. Built on .NET 10 on that basis; flagged to Osama.

- Entire TypeScript backend (`handlers/`, `shared/`, `config/`, `tests/*.ts`,
  `tsconfig.json`, `eslint.config.js`, `jest.config.js`, `package.json`) **deleted**,
  not left alongside the new code.
- Reimplemented in C# on a new solution: `src/Gweb.Shared` (Clock, Deadline,
  Correlation, Logging/Redactor, Errors), `src/Gweb.Config`, `src/Gweb.Api` (ASP.NET
  Core Minimal API, hosted via `Amazon.Lambda.AspNetCoreServer.Hosting`), 53 xUnit
  tests in `tests/Gweb.Tests` (99.6% line / 96.7% branch coverage, generated code
  excluded).
- `infra/template.yaml` rewritten: `Runtime: dotnet10`, **one** Lambda function
  (`ApiFunction`) hosting the entire API behind a `$default` HTTP API route, rather
  than one Lambda per route — a deliberate, documented deviation from
  `docs/03-ARCHITECTURE-RULES.md` §1 (see ADR-0001).
- ADR-0001 rewritten to record the actual decision and the architecture-rule
  deviation; ADR-0002 corrected (no longer references esbuild/TypeScript).
- **Docker Desktop installed and started** (via `winget`; the Windows service needed
  one manual admin approval from Osama mid-session — noted for anyone else setting
  this machine up).
- **`sam local start-api` verified for real**, end to end, through an actual Docker
  container running the `dotnet10` Lambda runtime emulation image — closing the gap
  flagged at the end of the Phase 0–1 session. Caught and fixed a real mistake in the
  process: pointing `sam local` at the source template instead of
  `.aws-sam/build/template.yaml` produces a `502` for a compiled runtime (nothing to
  mount at `/var/task`). See README "Prerequisites and local setup" for the corrected
  command and why it matters.

### Blocked on Osama

1. **AWS account/region/profile/billing limit, AI provider credential, GitHub repo
   visibility, time budget** — unchanged from the earlier entry above; still open.
2. **Comprehension check for the Phase 0–1 primitives** was delivered before this
   pivot; the C# reimplementation has **not** yet had its own comprehension check.
   Given the whole backend was rewritten, treat the earlier check as void and redo it
   against the actual C# code before Phase 2 starts.
3. **`git push` still has not happened.** Everything remains local-only.
4. **Whether ASP.NET's one-Lambda-for-the-whole-API shape is actually acceptable** —
   ADR-0001 documents the IAM/cold-start tradeoff honestly, but Osama should
   explicitly sign off on it (or ask for the per-bounded-context split described as
   the production mitigation) before Phase 2 builds more routes onto this function.

### What the next session should start with

Same as above — Phase 2 — but now against the C# codebase. Also worth a few minutes
first: confirm Osama has actually reviewed `src/Gweb.Shared/Deadline/DeadlineBudget.cs`
and `src/Gweb.Shared/Logging/Redactor.cs`, since two full implementations of those now
exist in git history and only the C# one is live.

---

## 2026-09-09 (same day, later still) — Phase 2: application lifecycle & persistence

Osama explicitly signed off on the one-Lambda-for-the-whole-API design (item 4 above,
resolved) and chose to skip a fresh comprehension check in favor of moving straight to
Phase 2 (item 2 above — still technically open, just deprioritized by Osama's own
choice, not dropped by Claude).

### What landed

- ADR-0003 (single-table DynamoDB design, full access-pattern table), written before
  any persistence code.
- `Gweb.Domain`: `Application` aggregate with an explicit `Submit()` state-machine
  guard, `IApplicationRepository`. Zero infrastructure imports beyond the shared error
  taxonomy.
- `Gweb.Adapters.Persistence`: `DynamoDbApplicationRepository` (conditional
  `PutItem`/strongly-consistent `GetItem`, budget-derived per-call timeouts, AWS SDK
  errors translated to the domain taxonomy without leaking internal messages) and
  `InMemoryApplicationRepository`.
- `Gweb.Services.ApplicationService` (create/get orchestration).
- `POST /v1/applications`, `GET /v1/applications/{id}` — wired into the same
  `RequestExecution` boilerplate the health endpoint now also uses (extracted during
  this phase to avoid repeating it a third time).
- `infra/template.yaml`: `ApiFunction` now has `APPLICATIONS_TABLE_NAME` and a
  `Policies:` block scoped to exactly `dynamodb:PutItem`/`dynamodb:GetItem` on the one
  table ARN.
- 78 tests passing (up from 53), 96.4% line / 90.5% branch coverage.
- **Two real bugs found by actually running tests, not by inspection** — full detail
  in `AI-USAGE.md` §5: (1) a broad exception catch was swallowing
  `ConditionalCheckFailedException` before the caller's specific handler saw it; (2) a
  test's assumption about `GetItemResponse.IsItemSet` for a missing item was wrong
  (production code was already correct).
- **Verified twice, for real, against real backends:** `dotnet run` directly against
  DynamoDB Local (full create/resume/404/400 flow, real HTTP, real DynamoDB API), and
  `sam build`/`sam validate` for the updated template. `sam local start-api
  --env-vars` for in-memory override was attempted and did **not** work as documented
  in this environment — recorded as a known limitation in the README rather than
  glossed over.

### Blocked on Osama

1. AWS account/region/profile/billing limit, AI provider credential, GitHub repo
   visibility, time budget — still open, unchanged.
2. `git push` still has not happened.
3. Comprehension check for Phases 0–2 (all of it — the C# codebase has never had one)
   — deferred at Osama's explicit choice, not forgotten. Should happen before this
   goes much further; every phase adds more surface area to catch up on later.
4. Whether the `sam local --env-vars` limitation is worth root-causing, or just living
   with the documented `dotnet run` + DynamoDB Local workaround permanently.

### What the next session should start with

Phase 3 — Applicant & business intake (`feat/applicant-business-intake`): full data
model from the brief §3.1/§3.2, schema validation (accept + reject cases), `PATCH
/v1/applications/{id}/applicant` and `.../business`, beneficial owners/ownership
percentage rules, masking of government ID/tax ID/bank metadata at capture, and the
completeness calculation. This is where `Person`/`Business` domain entities (deferred
from Phase 2 on purpose) actually get built, once their real fields are known.

---

## 2026-09-09 (same day, later still) — Phase 3: applicant & business intake

`git push` happened this session (all of Phases 0–2 are on `origin/main` now, not just
local). Osama gave a blanket "continue, and don't leave anything incomplete" — worth
being clear in this log that "incomplete" was interpreted as "each phase I actually
build is real and verified," not "the whole 16-phase plan finishes in one session."

### What landed

- `Applicant`/`Business` domain entities, full field set from brief §3.1/§3.2, PATCH
  (partial-update) semantics: every field optional, all provided fields validated
  together (errors collected, not fail-fast), either the whole batch applies or none
  of it does.
- Masking at capture for government ID, EIN/UBI, and bank account numbers -- the full
  value is extracted to last-4 and discarded in the same conversion call that
  constructs the domain value object; it's never stored, logged, or returned.
- `OwnershipValidator`: cross-entity check (applicant's own percentage + all
  beneficial owners' percentages ≤ 100%) -- lives in Domain as a pure function since
  neither entity alone has both sides of the data.
- `CompletenessChecker`: what's still missing per section, reused later by Phase 9's
  submission gate so the review screen and the actual submit block can't disagree.
- `DynamoDbApplicantRepository`/`DynamoDbBusinessRepository` + in-memory equivalents,
  same optimistic-concurrency convention as Phase 2's `Application` repository.
  Extracted the shared timeout/error-wrapping code (`DynamoDbCallExecutor`) and
  optional-field mapping helpers (`DynamoDbItemMapping`) rather than tripling them.
- `PATCH /v1/applications/{id}/applicant` and `.../business`; `GET
  /v1/applications/{id}` now returns the full aggregate (applicant, business,
  completeness), not just the envelope.
- The required Phase 3 security test: PATCHing a fully populated applicant + business
  through the real HTTP pipeline while capturing stdout, asserting no raw sensitive
  value ever appears in a log line.
- 152 tests passing (up from 78), 92.2% line / 84.8% branch coverage.

### Four real bugs found by actually running tests, not by inspection

1. `DynamoDbApplicationRepository`'s broad exception catch swallowed
   `ConditionalCheckFailedException` before the caller's specific handler saw it.
2. A test's wrong assumption about AWSSDK v4's `GetItemResponse.IsItemSet` semantics
   for a missing item (production code was already correct; the test fixture wasn't).
3. **(new this phase)** The in-memory repositories stored/returned live object
   references on both save and get. Since `Applicant`/`Business` are mutable, a
   caller mutating an object it just saved or just loaded (exactly what
   `ApplicantService.UpdateApplicantAsync` does on every call) silently corrupted the
   "persisted" state before the optimistic-concurrency check ran. Fixed with a
   `Snapshot()` method on each entity, used on both the read and write side of all
   three in-memory repositories. Two regression tests added.
4. **(new this phase)** Enum fields ("Llc", "Passport") failed to (de)serialize over
   HTTP -- `System.Text.Json` defaults to numeric enum values. Fixed with a
   `JsonStringEnumConverter` registered globally via `ConfigureHttpJsonOptions`.

Full detail on all of these, including the "why it mattered" questions for Osama to
answer, is in `AI-USAGE.md` §5.

### Blocked on Osama

1. AWS account/region/profile/billing limit, AI provider credential, GitHub repo
   visibility, time budget — still open, unchanged from Phase 0–1.
2. Comprehension check for Phases 0–3 — still deferred at Osama's explicit choice.
   Now covers meaningfully more surface area (the full applicant/business validation
   logic, the masking-at-capture pattern, the ownership cross-check) than when this
   was first deferred.
3. Whether the `sam local --env-vars` limitation (flagged end of Phase 2) is worth
   root-causing — unchanged, still open.
4. **New:** Docker's Windows service needed re-approval again mid-session (it isn't
   persistent across whatever caused it to stop since Phase 2). Phase 3's DynamoDB
   repositories were consequently not verified against a live DynamoDB Local --
   README says exactly what was and wasn't verified instead.

### What the next session should start with

Phase 4 — Documents & S3 (`feat/document-upload`): document metadata model + lifecycle
state machine (`REQUESTED -> UPLOADING -> RECEIVED -> PROCESSING -> ACCEPTED |
NEEDS_REVIEW | REJECTED`), `IDocumentStorage` + S3 + in-memory implementations,
`POST /v1/applications/{id}/documents/presign` (pre-signed PUT, pinned content-type,
size cap, non-guessable key) and `.../documents/{documentId}/complete` (checksum
verification, idempotent, magic-byte check). This is the first phase touching S3, so
worth budgeting time for the presigned-URL IAM policy and the file-signature
validation specifically -- both are explicitly graded (docs/04-SECURITY-RULES.md §3).

---

## 2026-09-09 (same day, later still) — Docker fix attempt + comprehension-check outcome

### Docker

Docker Desktop crashed with "initializing Ingest server ... rename
sailor-ingest.sock -> sailor-ingest.sock.stale: The file cannot be accessed by the
system" (screenshot from Osama). Root cause: a stale socket file survived an earlier
crash and Windows wouldn't let Docker's own process rename or delete it (nor would
`Remove-Item`, even with the service and all Docker processes stopped, even after
`wsl --shutdown`). Fixed by renaming the entire `%LOCALAPPDATA%\Docker\run` directory
aside (not deleted -- recoverable) so Docker recreates it fresh. That specific crash
should not recur. **Still blocked:** `com.docker.service` needs one more elevated
(UAC) approval from Osama to actually start -- same one-time step as the first Docker
setup earlier this session. Not yet re-verified live against DynamoDB Local pending
that.

### Comprehension check for Phases 0-3

Presented 8 questions covering the deadline budget, correlation context, optimistic
concurrency, the one-Lambda architecture tradeoff, PII masking, the ownership
cross-check, the snapshot bug, and the no-logging-of-request-bodies decision. Osama's
response, in full: *"هذول كلهم انا مالي دخل فيهم انا باعتلك الملفات الي بتحتاجها
كلها بتقدر ترجع لملف الـ PDF وتشوف وقرر الاحسن انا مش خبير بنوك"* (none of this is
my concern; I've given you the files you need; go back to the PDF and decide what's
best; I'm not a banking expert).

**This is now on record as Osama's explicit, informed choice, not a gap Claude
introduced or hid.** He is delegating domain-specific judgment calls (the kind
CLAUDE.md's collaboration protocol would otherwise ask him about) to Claude's reading
of the assessment brief, and is not going to personally verify implementation details
phase by phase. Saved as a standing preference (see session memory
`feedback_comprehension_checks`) so future sessions stop presenting these as blocking
gates and instead just log them for the record.

**Honest risk this carries, stated once and not repeated every phase:** CLAUDE.md's
whole premise is that Osama needs to be able to defend this code in an interview.
Whether the domain judgment calls made on his behalf (ownership-percentage handling,
beneficial-owner scoping, etc.) are ones he can actually explain later is now his risk
to manage, not something Claude can verify from here. `docs/INTERVIEW-NOTES.md`
(personal prep, near the end per `docs/06-COLLABORATION-PROTOCOL.md` §7) is the right
place to close this gap before submission -- worth revisiting then, not now.

---

## 2026-09-09 (same day, continued) — Phase 4 (Documents & S3) built and merged

Picked up mid-implementation: `Document` domain entity/state machine, `AllowedContentTypes`,
`DocumentKeyGenerator`, `FilenameSanitizer`, `FileSignatureValidator` were already
written and committed (`016de54`) before the interruption that triggered this session's
context compaction. Continued from there.

**What got built this stretch:**
- `Gweb.Adapters.Storage` project: `S3DocumentStorage` (presigned **POST**, not PUT --
  verified against the installed `AWSSDK.S3` XML docs that only POST can carry a
  content-length-range condition), `S3CallExecutor`, `InMemoryDocumentStorage`.
- `DynamoDbDocumentRepository` / `InMemoryDocumentRepository` (same
  snapshot-on-save/get + optimistic-concurrency pattern as every other repository).
- `DocumentService` (`Gweb.Services.Documents`): presign validates content type/size/
  checksum presence, creates the record, calls storage. `complete()` is idempotent (a
  document that already left `Uploading` just returns as-is), verifies checksum + size
  + file signature, and marks `Received` or `Rejected` accordingly -- rejection is a
  normal `200` outcome, not an exception.
- Endpoints: `POST /v1/applications/{id}/documents/presign`,
  `POST /v1/applications/{id}/documents/{documentId}/complete`,
  `GET /v1/applications/{id}/documents/{documentId}`.
- IAM: `ApiFunction`'s policy extended with `s3:PutObject`/`s3:GetObject`/
  `s3:GetObjectAttributes` scoped to the documents bucket ARN.

**Two real mistakes caught before committing (full detail in `AI-USAGE.md` §5):**
1. Guessed `CreatePresignedPostResponse`'s constructor and `Assert.StartsWith` on a
   `byte[]` -- both compile errors, both fixed by checking the real API shape instead
   of guessing twice.
2. The first IAM policy draft granted only `s3:GetObject*`, reasoning that a presigned
   POST needs no grant on the signer's own role. That reasoning is wrong -- SigV4
   presigning authorizes the eventual request against the *signer's* permissions, so
   without `s3:PutObject` every client upload would 403 at S3 despite a validly-signed
   presign response. Caught by re-reasoning about how presigning actually works, not
   by any test (nothing in this repo exercises the real IAM policy against real AWS --
   now an explicit Known Gap).

**Verified for real:** `dotnet build` (0 warnings, 0 errors, `TreatWarningsAsErrors`
still on), `dotnet test` -- 215/215 passing (up from 152 at the Phase 3 merge; +63
covering the domain, adapter, service, and HTTP-endpoint layers of this phase),
`sam validate --lint` against the updated template. **Not verified:** a live AWS S3
bucket -- see README "Known gaps" for exactly what stands in for that (mocked
`IAmazonS3` built from the real SDK's own request/response types) and what it does not
prove.

Merged to `main` with `--no-ff` per `docs/01-GIT-WORKFLOW.md`; pushed per Osama's
standing authorization ("ok now lets contenue also make the merge and pushes i dont
wat to see anything incomplete now").
