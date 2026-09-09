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

---

## 2026-09-09 (same day, continued) — Phase 5 (MCC catalog) built and merged

Osama: *"طيب كمل يلا ما معنا وقت بدنا ننجز"* (continue, no time, need to get this
done) — proceeded straight into Phase 5 with no phase-confirmation gate, consistent
with the standing delegation-of-judgment preference from earlier this session.

**Real decision made without asking (per that standing preference), logged here for
the record:** the brief names the Visa Merchant Data Standards Manual as the MCC
source. That's a paid/licensed document with no access from this session. Used the
well-established public MCC taxonomy instead (the same code/description pairs recur
across Visa/Mastercard/IRS references since the networks converged on one shared list
decades ago) and flagged the substitution explicitly in `docs/adr/0004-mcc-catalog-storage.md`
and the README, rather than either blocking on it or silently pretending it was the
licensed source.

**What got built:**
- `IMccCatalog`/`MccCode` (`Gweb.Domain.Mcc`) -- deliberately synchronous, no
  `DeadlineBudget` parameter, unlike every other repository interface in this codebase.
  Reasoning logged in the interface's own doc comment: it's an in-memory lookup with
  no I/O, so a budget parameter would be ceremony, not correctness.
- `tools/McCatalogImport` -- a real, runnable console tool that validates
  (4-digit codes, non-empty fields, no duplicates, fails loudly with the line number on
  any violation) and regenerates the packaged catalog from a checked-in pipe-delimited
  source file. Actually run against the 276-row source file this session, not just
  described -- "Wrote 276 MCC codes" with zero validation failures.
- `StaticMccCatalog` (`Gweb.Adapters.Mcc`) -- embedded-resource JSON, loaded once at
  cold start, in-memory ranked search (exact code, then code-prefix, then
  description-substring).
- `McCatalogService` + `GET /v1/mcc?query=...&limit=...`.
- No `infra/template.yaml` change needed -- no new AWS resource, since the catalog
  isn't DynamoDB-backed (justified in ADR-0004, not a default).

**One real test bug caught by actually running the suite:** a test asserted searching
`"GAMBLING"` would find MCC 7995. It failed -- 7995's real description says "Betting...
Casino Gaming..." and never contains the word "gambling" (that word only lives in the
Category grouping, which `Search` deliberately doesn't match against). Production code
was right; the test's assumed search term wasn't. Fixed the test's query to `"CASINO"`.
Full detail in `AI-USAGE.md` §5.

**Verified for real:** `dotnet build` (0 warnings/errors), `dotnet test` -- 238/238
passing (up from 215), including
`StaticMccCatalogTests.ContainsEveryMccThePhase6RiskPolicyMustBeAbleToClassify` proving
6012/6051/6211 (the three codes Phase 6's risk policy demo depends on) are actually in
the catalog before Phase 6 needs them. Coverage: 92.6%/84.7% overall (flat vs. Phase
4's 92.4%/84.4%).

Merged to `main` with `--no-ff`, pushed, per the same standing authorization as every
phase since Phase 3.

---

## 2026-09-09 (same day, continued) — Docker unblocked; real live verification closed two long-standing gaps

Osama ran `Start-Service com.docker.service` himself from an elevated PowerShell (the
manual step this repo's notes have flagged as blocking since Phase 3) and confirmed
`docker ps` worked. Used the window to close verification gaps that had been carried
as documented-but-not-closed for multiple phases, rather than just resuming Phase 6.

**`sam build` + `sam local start-api`, actually run against the real `dotnet10` Lambda
runtime container** (first run pulled the image fresh — "Building image..." took a few
minutes, confirmed genuine, not cached): `GET /v1/health` and `GET /v1/mcc?query=...`
both returned real `200`s from inside the container, not `WebApplicationFactory`.

**DynamoDB Local, spun up for real** (`docker run amazon/dynamodb-local`), table
created via a small throwaway console tool (`AWSSDK.DynamoDBv2`, not the AWS CLI --
not installed in this environment) against `http://localhost:8000`. Then, running the
app directly (`dotnet run --project src/Gweb.Api` with `PERSISTENCE_PROVIDER=dynamodb`
+ `DYNAMODB_SERVICE_URL`, the previously-verified-working path since `sam local
--env-vars` still doesn't override the persistence branch):

- Full application journey against the **real** table: create → resume → PATCH
  applicant (government ID masked to last4, persisted) → PATCH business (persisted) →
  GET full aggregate (both present, completeness correct) → presign a document (real
  `Document` row written, real SigV4-signed presign policy generated) → complete
  against a bucket that doesn't actually exist, correctly returning `503
  DEPENDENCY_UNAVAILABLE` rather than silently succeeding.
- 404 on an unknown application, 400 on a malformed GUID -- both still correct against
  the real table.

**This closes:** "Phase 3's Applicant/Business repositories were not verified against
a live DynamoDB Local" (closed -- they now have been, for real) and "Phase 4's
Document repository was never live-verified at all" (closed the same way, first time).

**Still open, honestly:** no actual byte has ever been uploaded through a presigned
URL to a real or local-S3-compatible bucket in this session -- that's the one part of
the document flow that remains verified only against a mocked `IAmazonS3`, not a live
one. Updated README's Known Gaps to reflect exactly this, replacing the older, now-stale
"not verified" entries rather than leaving them standing next to the new evidence.

Cleaned up afterward: stopped the `dotnet run` process, stopped and removed the
`dynamodb-local` container, left `sam local start-api` process to exit on its own.
Nothing left running that outlives this session.

---

## 2026-09-09 (same day, continued) — Phase 6 (risk policy engine) built and merged

Osama asked a clarifying question before continuing: whether closing the S3-live-upload
and IAM-live-verification gaps was actually required by the brief, or optional. Checked
`docs/00-PRODUCT-BRIEF.md` directly rather than guessing -- line 208 says "Runnable
demo — **deployed endpoint OR reproducible local Lambda/API simulation** + deploy
instructions" and the acceptance checklist asks for "deployment **instructions**," not
an executed deployment. Confirmed and told him: neither gap blocks anything: an AWS
account is optional, only needed if he personally wants a live deploy demo later.

Then: *"طيب يلا نكمل روح ع الي بعده وكمل"* -- proceeded into Phase 6.

**What got built:**
- `IRiskPolicy`/`RiskLevel` (`Gweb.Domain.RiskPolicy`) -- deliberately no reference to
  `IMccCatalog`/`MccCode` anywhere, the structural enforcement of the brief's "MCC
  taxonomy and risk policy are two separate things."
- `StaticRiskPolicy` (`Gweb.Adapters.RiskPolicy`) -- packaged JSON, same mechanism as
  the MCC catalog but a different justification (hand-authored business policy, not an
  external dataset -- no import-tool pipeline built for it, documented why not in
  ADR-0005).
- Real provider overrides proving the Phase 6 gate directly: MCC 6012 evaluates to
  `EnhancedReview` (no provider), `Restricted` (`acquirer-conservative`), `Standard`
  (`acquirer-permissive`) -- three outcomes, one MCC, from configuration alone.
- `NoAutoApprovalPathTests` (`tests/Gweb.Tests/Architecture/`) -- a real, run test
  that fails immediately if `ApplicationStatus` or `RiskLevel` ever grows an
  "Approved"/"Rejected" value. Turns "never auto-approve" from a promise in the brief
  into something a CI run actually checks.
- No new endpoint -- the brief's API surface has none for risk policy; registered in
  DI, ready for Phase 7/8 to consume.

**Verified for real:** `dotnet build` (0 warnings/errors), `dotnet test` -- 248/248
passing (up from 238). Coverage 92.8%/84.7% (flat vs. Phase 5's 92.6%/84.7%).

Merged to `main` with `--no-ff`, pushed.

---

## 2026-09-09 (same day, continued) — Phase 7 (AI adapter & MCC classification) built, verified live against real Gemini, merged

Osama asked "بزبط معك gemenai ai؟" (does Gemini work with you?) and, after confirming
the brief mandates no specific vendor (just "an adapter with a mock implementation"),
supplied a real Gemini API key -- explicitly: use only free-tier models, and build so
the system works whether or not a real key is available at grading time. Key stored
only in a local, git-ignored `.env` (confirmed git-ignored before writing anything to
it), never in source or a commit.

**Before writing any adapter code, verified the real API directly with curl** (per
this session's standing practice of checking real behavior over memory):
`gemini-2.5-flash` (the model assumed from general knowledge) turned out to already be
retired for new users -- the API's own 404 named the replacement, `gemini-3.6-flash`,
which is what got built against. Also discovered empirically: this model spends a
large share of its output-token budget on hidden "thinking" tokens before visible text
(`thoughtsTokenCount` in the raw response), meaning a naive `maxOutputTokens` budget
silently truncates the JSON answer, and a single call can take 15-30+ seconds --
uncomfortably close to the system's 35s internal deadline target.

**What got built:** `IEvaluationProvider`/`McClassification` (own aggregate, not
bolted onto `Business`, to avoid touching already-shipped, fully-tested code) in
`Gweb.Domain.Evaluation`; `MockEvaluationProvider` + `GeminiEvaluationProvider` in a
new `Gweb.Adapters.Evaluation` project (schema validation, one bounded repair retry,
hallucination guard via real catalog re-validation, a hardcoded 20s cap on the Gemini
call specifically because of the latency finding above); `ClassificationService`
(fallback-to-mock policy, keyword-based catalog hint building); `POST/GET
.../classify`, `POST .../classify/confirm`.

**Three real bugs found and fixed this phase** (full detail in `AI-USAGE.md` §5):
1. Passing the whole free-text business description as one literal search query
   returned zero catalog hints -- found by manually running the real end-to-end flow,
   not by any test (every existing test handed hints in directly). Fixed with
   per-keyword search + union, added a regression test, re-verified live.
2. `finishReason: MAX_TOKENS` was thrown as a hard exception, which skipped the
   repair-retry path entirely despite a comment claiming it triggered one -- caught by
   this phase's own test suite failing, not by inspection.
3. An `HttpRequestException`'s message was passed as `DomainException.Details`, which
   `HttpErrorMapper` serializes straight into the client response -- a security-relevant
   regression that copy-pasted a safety comment without copying the behavior it
   described. Caught before ever running, during the build itself.

**Verified for real against the live Gemini API** (not simulated): a full request
through the entire real stack -- `POST /v1/applications/{id}/classify` →
`ClassificationService` → `GeminiEvaluationProvider` → real HTTPS call → parsed,
catalog-validated, persisted → HTTP response -- correctly classified a grocery-store
description as MCC 5411, confidence 0.98. A separate real call returned an actual HTTP
503 (free-tier overload), which the fallback-to-mock path correctly caught and
degraded from, logged with the real reason. `POST .../classify/confirm` and
`GET .../classify` both exercised against that real classification, including
rejecting an unknown MCC code. The automated test suite itself never depends on any of
this -- `TestEnvironment.cs` now forces `AI_PROVIDER=mock` unconditionally, so
`dotnet test` stays fast/offline/deterministic; `GeminiEvaluationProvider` is
separately unit-tested with a fake `HttpMessageHandler`.

**Verified for real, standard checks:** `dotnet build` (0 warnings/errors),
`dotnet test` -- 294/294 passing (up from 248), `sam validate --lint` against the
updated template (new `AiApiKey`/`GeminiModel` parameters). Coverage 92.6%/82.2%
(branch coverage dipped from 84.7% -- explained honestly in the README, not smoothed
over: `GeminiEvaluationProvider` has more independent failure-mode branches than the
test suite exercises every pairwise combination of).

Merged to `main` with `--no-ff`, pushed.

---

## 2026-09-09 (same day, continued) — Phase 8 (rate evaluation & risk signals) built, verified live, merged

Continued straight from Phase 7 on "يس كمل" (yes, continue).

**What got built:** `IEvaluationProvider` extended with `ExtractStatementAsync`
(`MockEvaluationProvider` returns a labelled fixture; `GeminiEvaluationProvider` sends
the actual document bytes as a real multimodal `inlineData` part -- verified against
the live API). `EffectiveRateCalculator` (pure, deterministic -- never reads AI
commentary) and `RiskSignalDetector` (deterministic detection for every brief-named
signal category, each citing a `sourceField`) in `Gweb.Domain.Evaluation`. `Evaluation`
as its own aggregate (`sk=EVALUATION`), same reasoning as `McClassification` --
`EvaluationService` orchestrating extraction-with-fallback, the math, the signals, and
a real "budget too low, mark Processing, return 202" path per brief's async-fallback
requirement. `POST /v1/applications/{id}/evaluate`, `GET .../evaluation`. New
`IDocumentStorage.DownloadObjectAsync` (full bytes, size-capped) alongside the existing
16-byte signature-check read. No `infra/template.yaml` change needed at all --
`Evaluation` lives on the same DynamoDB table and uses the same S3 actions already
granted.

**Real bugs found and fixed this phase** (full detail in `AI-USAGE.md` §5):
1. Two new repository tests misapplied the optimistic-concurrency `expectedVersion`
   convention (passed the object's post-mutation Version instead of what was actually
   persisted before the mutation) -- caught by the tests failing for real, not by
   inspection, despite the same convention having been used correctly six times over in
   earlier phases.
2. A genuine C# gotcha: a `using Evaluation = ...;` alias could not resolve an
   ambiguity between the domain type and a sibling test namespace this same phase
   created (`Gweb.Tests.Adapters.Evaluation`) -- namespace-member lookup in an
   enclosing scope beats a same-file alias, per the C# spec's lookup order. Fixed with
   full `global::` qualification instead, documented so it isn't "simplified" back.

**Verified for real, live, against the actual Gemini API** (not simulated): ran
`GeminiEvaluationProvider.ExtractStatementAsync` directly (a small standalone harness
in the session scratchpad, not part of the repo) against a realistic synthetic
merchant statement. First attempt hit a genuine HTTP 503 (free-tier overload -- the
exact failure mode `DependencyUnavailableException` exists to handle); a retry
succeeded in ~6 seconds and extracted every field exactly right (processor, monthly
volume $48,732.15, discount rate 2.65%, per-transaction/monthly/chargeback fees,
statement period) plus a coherent one-sentence commentary. Full HTTP-level
orchestration (`EvaluationService`, the endpoints, a real presign/complete document
upload) is tested end-to-end against a simulated upload with a fake provider; only the
combination of "real HTTP upload all the way through + real Gemini multimodal call in
one live run" is unverified, honestly, since this session has no AWS account to put a
real file in S3 for a real end-to-end run -- documented in ADR-0007 and the README's
Known Gaps rather than glossed over.

**Verified for real, standard checks:** `dotnet build` (0 warnings/errors),
`dotnet test` -- 355/355 passing (up from 294), coverage 93.7%/83.0% (up from Phase
7's 92.6%/82.2% -- the new domain code is small, pure, and thoroughly edge-case tested
by design).

Merged to `main` with `--no-ff`, pushed.

---

## 2026-09-09 (same day, continued) — Phase 9 (review & submit gate) built, verified, merged

Continued straight from Phase 8 on "يس كمل" (yes, continue).

**What got built:** The real "list documents for an application" DynamoDB `Query`
(`IDocumentRepository.ListByApplicationIdAsync`) that ADR-0003 planned as an access
pattern since Phase 2 but no caller needed until now -- not scope creep, the fulfillment
of an already-planned design. `Gweb.Domain.Submission.SubmissionChecker` reuses the
existing `CompletenessChecker` for applicant/business fields and adds required-document
logic on top (Government ID, Business Registration, Bank Evidence -- the brief's
"Conditional" types are explicitly out of scope, no rule engine exists for them).
`IApplicationRepository.UpdateAsync` added as a deliberate third method (alongside the
existing `CreateAsync`/`GetByIdAsync`) rather than retrofitting `Application` onto the
`SaveAsync`/`expectedVersion=0` convention every other entity uses -- `Application`
already had a different-shaped repository since Phase 2, and `Application.Version`
starts at 1, not 0, so unifying the conventions would have given `expectedVersion` two
different meanings depending on which method reads it. `SubmissionService` orchestrates
the full check-then-submit flow. `POST /v1/applications/{id}/submit` returns the
normalized review payload (masked applicant/business, every document's status, current
MCC classification, current evaluation) on success, `400 VALIDATION_FAILED` with the
precise missing-item list on a blocked submission, and relies on `Application.Submit()`'s
existing state-machine guard for a correct `409` on a genuine double-submit -- no new
special-casing needed. `infra/template.yaml`'s IAM policy gains `dynamodb:Query`, scoped
to the same one table ARN as every other action -- its own comment had literally
anticipated this exact addition since Phase 4. No new `ApplicationStatus` value added;
`Submitted` (existing since Phase 2) is the terminal "ready for manual review" state the
brief describes, still enforced structurally by `NoAutoApprovalPathTests`.

**No behavioral bugs found this phase** (full detail in `AI-USAGE.md` §5's Phase 8
entries, still the most recent real bugs on record): one minor `CA1859` analyzer fix in
a test helper's return type, otherwise `dotnet build` and all 383 tests passed clean on
the first run after each incremental addition. The Phase 8 namespace-collision lesson
(a same-named `using` alias losing to a sibling namespace) was applied proactively this
time -- `SubmissionService.cs` uses differently-named aliases (`DomainDocument`,
`DomainEvaluation`) from the start rather than hitting the same bug again.

**Verified for real:** the full real-HTTP journey in `SubmitEndpointTests` -- create an
application, PATCH applicant, PATCH business, real presign/complete for all three
required documents (through the actual `IDocumentStorage`/S3-adapter simulation, not
shortcut), then submit -- confirming a blocked submission reports the precise missing
items over HTTP, a successful submission's payload never contains an unmasked
government ID or bank account number, and a second submit attempt correctly returns
`409`. `sam validate -t infra/template.yaml --lint` confirms the extended IAM policy is
still well-formed.

**Verified for real, standard checks:** `dotnet build` (0 warnings/errors),
`dotnet test` -- 383/383 passing (up from 355), coverage 94.5%/84.4% (up from Phase 8's
93.7%/83.0% -- the new submission-gate code is thoroughly covered by design: every
missing-item category, every document-status edge case, the full real-HTTP journey).

**Known gaps documented honestly, not silently skipped** (full detail in README "Known
gaps"): Conditional document types (Business License, Additional Evidence) are not
enforced by the gate -- needs a jurisdiction/business-type rule engine this system
doesn't have. No aggregate "review screen" endpoint exists ahead of Phase 11's
frontend -- the granular endpoints already serve that need, and `POST /submit`'s own
response is the normalized review payload the brief asks for.

Merged to `main` with `--no-ff`, pushed.

---

## 2026-09-09 (same day, continued) — Phase 10 (deadline hardening & bounded retry) built, verified, merged

Continued on "يلا نروح ع الخطوة الي بعدها" (let's go to the next step) after Osama asked
for an honest risk assessment against the assessment PDF's rubric and named this
phase's items -- specifically bounded retry with backoff/jitter -- as one of the
highest-risk gaps if left undone before the deadline.

**What got built:** An audit of every DynamoDB/S3/Gemini call site for
`DeadlineBudget` propagation -- found no gaps; every call was already routed through a
budget-derived timeout since the phase each adapter was built (Phases 2-8). A new
`Gweb.Shared.Resilience.BoundedRetry` (bounded retry with exponential backoff and full
jitter, reusing the existing `DomainException.Retryable` flag as the retry-worthiness
signal, never sleeping toward a retry the remaining budget could not afford) wired into
`GeminiEvaluationProvider` only -- deliberately not into the DynamoDB/S3 call
executors, since the AWS SDK for .NET already retries transient failures on those
internally, and a second, uncoordinated app-level retry loop on top would risk retry
amplification rather than add safety. A third hanging-dependency test
(`S3DocumentStorageTests`), closing the one coverage gap the audit found -- DynamoDB
and Gemini each already had one. `docs/adr/0009-deadline-hardening-and-retry.md`
documents all of this, including why "structured timeout response + state preserved
for safe retry" needed no new code: `HttpErrorMapper`'s existing 504/503 mapping and
every entity's existing optimistic-concurrency conditional write already satisfy it.

**Also fixed mid-phase:** Osama reported the real per-account Gemini free-tier quota --
20 requests/day for `gemini-3.6-flash` (this project's default since Phase 7), easy to
exhaust during grading or a live demo, versus 500/day for `gemini-3.5-flash-lite`.
Verified both `gemini-3.5-flash-lite` and the other option he named
(`gemini-3.1-flash-lite`) against the real API before choosing -- 3.5 Flash Lite
returned a real 200; 3.1 Flash Lite returned a real 503 ("high demand") at the moment of
checking. Switched the default in `AppConfig.cs`, `infra/template.yaml`,
`.env.example`, and the local `.env` -- only the env var changed, no code change,
exactly the mechanism `docs/adr/0006-ai-evaluation-provider.md` already anticipated the
second time Google's free-tier lineup shifted. Documented as an addendum to ADR-0006
rather than rewriting its original reasoning.

**A real (if narrow) bug caught by reasoning, not by a test failing:** wiring retry
into `GeminiEvaluationProvider` initially left one existing hanging-dependency test
technically passing but no longer testing what it claimed -- its `Budget()` helper's
`FakeClock` never advances with real wall-clock time, so `BoundedRetry`'s
budget-remaining check saw "plenty left" and silently retried twice against the still-
hanging handler before giving up, making the test ~3x slower and its own "runs in well
under a second" comment quietly false. Full detail in `AI-USAGE.md` §5. Fixed by pinning
that test (and two others whose call-count assertions would have silently changed) to
`maxCallAttempts: 1`, and adding a dedicated `FakeDelay`-based test that correctly
exercises retry-respects-budget by wiring the fake delay to the same `FakeClock` the
budget reads from.

**Verified for real, standard checks:** `dotnet build` (0 warnings/errors), `dotnet
test` -- 392/392 passing (up from 383; the new hanging/retry tests add real but small
wall-clock cost via `FakeDelay`, not the multi-second cost a naive implementation
without fake-delay injection would have), coverage 94.49%/84.36% (flat vs. Phase 9's
94.5%/84.4% -- no new untested surface, retry code layered onto already-covered paths).
`sam validate -t infra/template.yaml --lint` re-confirmed valid after the `GeminiModel`
default change.

Merged to `main` with `--no-ff`, pushed.

---

## 2026-09-09 (same day, continued) — Phase 11 (frontend) built, verified live end-to-end, merged

Discussed the stack with Osama before writing code, per his request ("تعال ندردش شوي
عن الfrontend"): Vite + React + TypeScript over Next.js (this is a REST-backed SPA,
not a site needing SSR/SEO); Tailwind v4 + shadcn/ui-style components for a premium
"business" look he asked for ("بدي اشي فخم"); Zustand (his explicit ask) for UI-only
state alongside TanStack Query for server state; react-hook-form + zod; `motion` for
animation, kept deliberately restrained per his instruction ("انيميشن حلو ومرتب ما
يكون قوي") with exactly one bigger celebratory moment reserved for a successful
submission.

**What got built:** The full 6-step wizard (`frontend/src/`) -- Applicant, Business,
Documents, Classification, Rate Evaluation, Review & Submit -- each step reading and
writing the real backend via `src/hooks/use-application.ts` (TanStack Query), with
client-side zod validation mirroring `Applicant.cs`/`Business.cs`'s exact field rules.
A premium visual identity: warm ivory/near-black base, deep royal-indigo primary,
champagne-gold accent reserved for premium/success moments, Fraunces serif headings
against Inter body text. An animated sidebar stepper with spring-in checkmarks and an
animated connector-line fill; step transitions slide+fade; MCC confidence bars animate
their width in; a genuinely bigger, confetti-adjacent celebration animation on
successful submission only. Document upload reports real progress (XHR upload
events, not simulated) against real pre-signed URLs.

**A real backend gap found and closed while building this:** no client-facing route
ever listed an application's documents -- `SubmissionService` used
`IDocumentRepository.ListByApplicationIdAsync` internally since Phase 9, but nothing
exposed it over HTTP, so a resumed session had no way to discover which documents were
already uploaded (the brief's own "preserve state so a partially completed application
can be resumed" requirement). Added `GET /v1/applications/{id}/documents`
(`DocumentService.ListDocumentsAsync`, `DocumentEndpoints.cs`), same
404-on-unknown-parent convention as every sibling endpoint, with its own three new
endpoint tests.

**Verified for real, end-to-end, through the actual rendered UI, not just API calls:**
stood up MinIO (S3-compatible) and DynamoDB Local in Docker, pointed the backend at
both (`PERSISTENCE_PROVIDER=dynamodb`, `S3_SERVICE_URL`, `DYNAMODB_SERVICE_URL`), and
drove the complete journey through a real browser: create application -> fill
applicant -> fill business -> upload all three required documents through the real
presign -> MinIO -> complete flow -> get an MCC suggestion -> confirm it -> run risk
evaluation -> submit. Confirmed via a direct `GET /v1/applications/{id}` afterward that
the backend genuinely returned `"status":"Submitted"`. This is the first real
S3-compatible upload in this project's history -- closes the single longest-standing
Known Gap in the README ("no real S3 bucket has ever received an actual uploaded
byte"). The three document uploads were also independently verified via raw `curl`
(matching exactly what the browser's `uploadToPresignedUrl` sends), confirming the
mechanics were correct before ever touching the browser.

**Two real bugs found by actually testing, not by inspection:**
1. A mobile layout bug (375px viewport): the document status badge visually overlapped
   a wrapped two-line document title. Root cause: a `flex items-center` row centers a
   short right-hand column against a now-two-line-tall left column, landing the badge
   mid-overlap rather than at the top. Fixed by restructuring `DocumentsStep.tsx`'s
   `DocumentCard` to stack badge/button below the title on narrow screens instead of
   sitting beside it.
2. A longstanding README documentation bug: every curl example across the whole
   document used port 5280, but the real `dotnet run --project src/Gweb.Api` port
   (confirmed by actually starting it and reading the log) is 5243, from
   `Properties/launchSettings.json`. Fixed with a global replace (23 occurrences).

**Also hit and worked around:** this MinIO version's bucket-CORS configuration API
(`mc cors set`) rejected every configuration tried, so a real browser's cross-origin
upload to `localhost:9000` would have been blocked. Worked around with a Vite
dev-server proxy plus a small same-origin URL rewrite, local-dev-only and documented
as such -- see `docs/adr/0010-frontend-architecture.md`.

**Verified for real, standard checks:** `npx tsc -b --noEmit` clean, `npm run build`
clean (685KB/215KB gzipped single bundle, flagged as unsplit but acceptable at this
app's size). Backend: `dotnet build` clean, `dotnet test` -- 395/395 passing (up from
392 -- three new `ListDocumentsAsync` endpoint tests).

`docs/adr/0010-frontend-architecture.md` documents the full stack rationale, the
shadcn CLI bugs worked around, and the MinIO CORS workaround.

---

## 2026-09-09 (same day, continued) — Two real bugs found via live manual testing with Osama, plus a manual MCC search feature

Osama manually tested the frontend against the running backend (MinIO + DynamoDB
Local) and worked through it himself, deliberately probing edge cases -- exactly the
kind of testing an automated suite alone doesn't replace.

**Real bug 1: misleading classification confidence on a no-match fallback.** Osama
typed "a gym serve athelates" as a business description and got back "0742 Veterinary
Services" at 90% confidence. Traced to `ClassificationService.BuildCatalogHints`
silently falling back to the catalog's browsing default (an arbitrary 25-code list)
whenever every per-keyword search comes up empty ("gym" is 3 characters, below the
4-character matching threshold; "athelates" is a typo for "athletes," and neither
spelling nor "gym"/"fitness"/"athletic" appears anywhere in the catalog text --
confirmed live against the real catalog) -- but nothing downstream knew a fallback had
happened, so the provider confidently proposed one of those arbitrary codes at full
confidence. Fixed with a `NoKeywordMatchConfidenceCeiling` (35%) applied generically in
`ClassificationService` (the one place that already owns this kind of cross-cutting
policy decision) whenever the fallback path was used -- the explanation is also
replaced with an honest "no confident match" message. Two new tests cover both the
capped and uncapped paths.

**Real bug 2: a genuine 500 on GET, found setting up the classification test.** Any
application whose Business had never had its `EntityType` explicitly set threw
`ArgumentException: Requested value 'BUSINESS' was not found` on `GET
/v1/applications/{id}`. Root cause: `DynamoDbBusinessRepository` has a local `const
string EntityType = "BUSINESS"` (a write-only per-item record-type marker, the same
copy-pasted pattern in all six DynamoDB repositories) that shares both its C#
identifier *and* its DynamoDB attribute key with `Business`'s own real `EntityType`
field -- the one domain entity, of six, whose real field happens to collide with this
generic marker's name. Invisible to the whole automated suite (`InMemoryBusinessRepository`
never round-trips through DynamoDB attribute maps at all) and to every prior manual
test this session (the frontend's dropdown always defaults to `Llc`, so a real value
was always set) -- only reachable by exercising the real DynamoDB-backed path with a
genuinely partial business record, exactly what setting up a fresh test app for the
classification sweep did. Same fix shape as the Phase 8 sibling-namespace shadowing
gotcha: renamed the colliding identifier (`RecordTypeMarker`) and, more importantly,
moved its DynamoDB attribute key off the shared `"entityType"` slot onto a
non-colliding `"recordType"`. A new `Mock<IAmazonDynamoDB>` round-trip test reproduces
the exact scenario.

**Feature added in response to Osama's own question** ("لو ما تطابق يدخله يدوي؟" --
if it doesn't match, can the user enter it manually?): `ClassificationStep.tsx` gained
a live manual search box wired to `GET /v1/mcc?query=...`, showing real code + name +
description + category as the applicant types, selectable and confirmable independent
of whatever the automatic candidates suggested -- directly closes the gap the "gym"
edge case exposed (searching "sport" surfaces 7941/7997, the correct codes, when the
automatic keyword match had nothing to work with).

**Verified for real:** a live sweep of 9 more business descriptions through the actual
rendered UI (grocery, restaurant, bar, auto repair, pharmacy, hotel, gas station,
casino/gambling, jewelry) -- 6 genuine exact matches, 2 partial (the correct code
present but not ranked first -- "automotive" and generic words outweighing more
specific ones like "repair"/"service station" in the naive keyword-overlap ranking),
1 correct honest fallback. The manual search feature verified live too: searching
"sport" returned real catalog results, selecting one updated the confirm button
correctly.

**Verified for real, standard checks:** `dotnet build` clean, `dotnet test` --
398/398 passing (up from 397 -- Phase 11's original merge already added the
list-documents tests; this round added two more for the confidence cap plus one for
the DynamoDB round-trip fix). `npx tsc -b --noEmit` clean.

Merged to `main` with `--no-ff`, pushed.

---

## 2026-09-09 (same day, continued) — Phase 12 (mandatory end-to-end journey test) built, verified, merged

Continued on "كمل" (continue) after wrapping up Phase 11's live-testing round.

**What got built:** `tests/Gweb.Tests/EndToEnd/EndToEndJourneyTests.cs` -- the brief's
specifically-named mandatory single integration test, one continuous
`WebApplicationFactory` run through the real HTTP pipeline: create application →
update applicant → update business → pre-sign + mock-complete all three required
document types → classify → confirm → evaluate → submit. Deliberately one test method,
not several -- the point is proving every stage's state survives into the next one in
sequence (a document uploaded in step 4 is still there when step 8 submits), which
splitting into independent tests would stop verifying. Rides along a handful of
free assertions at points already reached in the sequence: an early `submit` (before
documents exist) correctly blocked with the precise missing-items list; masked values
(government ID, EIN, bank account) confirmed absent, unmasked, from the final submit
payload; a second `submit` after success correctly returns `409`, not a silent no-op.
`docs/adr/0011-end-to-end-journey-test.md` documents why it's shaped this way, and why
"mock upload completion" reuses the existing `SimulateUpload` pattern rather than
re-verifying the real MinIO path Phase 11 already proved live.

**Verified for real:** passed on the first run, no debugging needed -- every stage it
chains was already individually correct from earlier phases; this is new coverage of
the *sequence*, not of any single stage's logic. Runs with zero external dependencies
(`dotnet test --filter FullyQualifiedName~EndToEndJourneyTests`), satisfying the
brief's "green from a clean checkout" and "runnable with one documented command"
literally.

**Verified for real, standard checks:** `dotnet build` clean, `dotnet test` --
399/399 passing (up from 398). Coverage 94.56%/86.13% (up from 94.49%/84.36% --
`Gweb.Adapters.Persistence` branch coverage jumped most, from the prior round's
DynamoDB round-trip regression test finally exercising a previously-untested branch).

Merged to `main` with `--no-ff`, pushed.

---

## 2026-09-09 (same day, continued) — Phase 13 (final documentation & IAM hardening) built, merged

Continued on Osama's "طيب اوك كمل الباقي كاملا وجهزلي كل اشي" (ok, complete the rest
fully and prepare everything) after he asked directly whether every brief requirement
was actually done -- it wasn't; this phase is the honest answer to that question.

**What got built, all five brief-mandated deliverable documents:**

- `docs/ARCHITECTURE.md` -- one Mermaid diagram (renders natively on GitHub) plus
  prose covering request flow, the DynamoDB and S3 data flows, the AI external call,
  and a consolidated "Timeouts, retries, and failure states" section pulling together
  what Phases 1-10 built into one place a reviewer can read without cross-referencing
  six ADRs.
- `docs/openapi.yaml` -- a complete, hand-written OpenAPI 3.0 spec (15 paths, 22
  schemas), built directly from the real C# request/response DTOs rather than
  guessed. Validated for real: parses as YAML (`npx js-yaml`), and every single
  `$ref` in the document resolves to an actually-defined schema (checked
  programmatically, not by eye).
- `docs/SECURITY.md` -- filled in the brief's own threat-model table
  (`docs/04-SECURITY-RULES.md` §8) honestly for all nine named threats, an IAM audit
  (see below), the KMS-vs-current-encryption tradeoff the brief explicitly asks to be
  addressed, redaction decisions, secrets management, and the data retention/deletion
  design (not implemented, but the brief only requires it be written down).
- `docs/TEST-EVIDENCE.md` -- real output from actually running `dotnet build`/`dotnet
  test` this session (399/399, 3s), plus the specific hanging-dependency tests run in
  isolation with their real per-test millisecond timings, plus the real structured-log
  durations captured from one live run of the Phase 12 end-to-end journey test --
  every individual step of a 15-step real journey completed in under 25ms against the
  in-memory adapters, concrete evidence the 45-second budget is nowhere close to
  binding under normal operation.
- `docs/DEMO.md` -- a frontend walkthrough plus an equivalent `curl` walkthrough.
  The `curl` version was not just written from expectation -- run step-by-step
  against a real running backend before being committed, confirming every response
  shown (including the exact classify result, MCC 5411 at 90% confidence for the
  grocery description) is what the system actually returns, not what it should
  return.

**IAM audit** (`docs/SECURITY.md` "IAM approach", per `docs/04-SECURITY-RULES.md` §4's
"be able to justify every single permission... if you cannot justify it, remove it"):
re-reviewed `infra/template.yaml`'s policy action-by-action against the real code
paths that use each one. Conclusion: nothing to remove -- the policy was already
exactly the minimum the code needs (no `UpdateItem`/`DeleteItem`/`Scan`/`s3:DeleteObject`/
`ListBucket`, because nothing in the codebase calls any of them). Documented as a
completed audit with a "nothing found" result, not left unaudited.

**A real inaccuracy caught before it shipped:** the first draft of `docs/SECURITY.md`
claimed a PII-redaction regression test (the brief's own suggested security evidence)
didn't exist and listed it as a gap. Checking before asserting turned up
`PiiRedactionEndToEndTests.PatchingAFullyPopulatedApplicantAndBusinessNeverLogsAnySensitiveRawValue`,
already built in an earlier phase -- corrected the document to cite it as real
evidence instead of a false gap claim.

**README updates:** every remaining `(TBD — Phase 13)` marker replaced with a real
link (architecture doc, OpenAPI spec, deployment instructions, cleanup/teardown --
both written correctly but not executed against a real AWS account, honestly labelled
as such); a new "Deliverables" table near the top mapping every brief-named
deliverable to exactly where it lives in this repo.

**Verified for real, standard checks:** `dotnet build` clean, `dotnet test` --
399/399 passing (no source code changed this phase, documentation only -- the number
is unchanged from Phase 12, re-confirmed rather than assumed). Every command shown in
`docs/DEMO.md`'s curl walkthrough was actually executed against a real running
backend and its real output compared against what the document claims.

Merged to `main` with `--no-ff`, pushed.
