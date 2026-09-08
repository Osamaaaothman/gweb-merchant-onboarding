# 08 — Implementation Plan

One branch per phase. Merge with `--no-ff` after tests pass. **Confirm each phase with
Osama before starting it.**

Rough sizing assumes the client's stated ~6–8 focused hours for the core, plus
refinement time.

---

## Phase 0 — Repository foundation
**Branch:** `chore/repo-scaffolding`

- `README.md` (skeleton — grows every phase, never written all at once at the end)
- `AI-USAGE.md` (skeleton — appended every phase)
- `.gitignore`, `.gitattributes`, `.env.example`
- `docs/adr/` directory
- License / basic project metadata
- GitHub Actions CI: lint + build + test
- Copy the assessment PDF into `docs/assessment/`

**Gate:** repo pushed to GitHub, CI green on an empty test.

---

## Phase 1 — Skeleton + cross-cutting primitives
**Branch:** `infra/lambda-skeleton`

- Chosen runtime project structure per `docs/02-ENGINEERING-STANDARDS.md` §2
- IaC skeleton: one Lambda + API Gateway + DynamoDB table + S3 bucket, all
  parameterized, private bucket, block-public-access, encryption
- Health endpoint proving the local run path works
- **Deadline budget primitive** (`shared/deadline`) + `IClock` — built now because
  everything else depends on it
- Structured logger + correlation context + redaction utility
- Config loader with fail-fast validation at cold start
- Domain error taxonomy + HTTP error mapper
- ADR-0001 runtime choice, ADR-0002 IaC choice

**Gate:** `sam local` (or equivalent) responds; unit tests for deadline + redaction pass.

---

## Phase 2 — Application lifecycle & persistence
**Branch:** `feat/application-lifecycle`

- ADR-0003: DynamoDB table strategy + **access-pattern table** written first
- Domain entities: Application, Person/Controller, Business
- `IApplicationRepository` + DynamoDB impl + in-memory impl
- Optimistic concurrency (`version`) + idempotent creation
- Endpoints: `POST /v1/applications`, `GET /v1/applications/{id}`
- Audit fields on every item

**Gate:** create + resume works; concurrency conflict test returns 409.

---

## Phase 3 — Applicant & business intake
**Branch:** `feat/applicant-business-intake`

- Full data model from brief §3.1 and §3.2
- Schema validation for every field, both accept and reject cases
- `PATCH /v1/applications/{id}/applicant`, `PATCH /v1/applications/{id}/business`
- Beneficial owners / controlling persons, ownership percentage rules
- Masking of government ID, tax ID, bank metadata **at capture**
- Completeness calculation (what is missing, per section)

**Gate:** validation test suite passes; logger test proves no PII in logs.

---

## Phase 4 — Documents & S3
**Branch:** `feat/document-upload`

- Document metadata model + lifecycle state machine
- `IDocumentStorage` + S3 impl + in-memory impl
- `POST /v1/applications/{id}/documents/presign` — TTL, pinned content type, size cap,
  non-guessable key
- `POST /v1/applications/{id}/documents/{documentId}/complete` — checksum verification,
  idempotent, magic-byte validation where practical
- Required vs conditional document rules per brief §4
- S3 lifecycle / retention approach documented

**Gate:** presign rejects bad MIME/extension/size; `complete` is idempotent; illegal
transitions rejected.

---

## Phase 5 — MCC catalog
**Branch:** `feat/mcc-catalog`

- Import script from a **real** source (Visa Merchant Data Standards Manual), with a
  documented refresh process
- `IMccCatalog` + storage impl (DynamoDB or packaged static — justify in ADR)
- `GET /v1/mcc?query=...` with sensible search behavior
- ADR-0004: catalog storage + refresh strategy

**Gate:** search returns correct codes for representative queries; refresh script documented.

---

## Phase 6 — Risk policy engine
**Branch:** `feat/risk-policy`

- Policy as **configuration data**, fully separate from the catalog
- Attributes: `standard` / `enhanced-review` / `restricted`
- Provider-specific overrides
- Enhanced-review demonstrated for **6012, 6051, 6211**
- ADR-0005: policy representation and versioning
- **No auto-approval path exists anywhere in the code**

**Gate:** same MCC yields different outcomes under two provider configs, proven by test.

---

## Phase 7 — AI adapter + classification
**Branch:** `feat/ai-adapter-and-classify`

- `IEvaluationProvider` interface + `MockEvaluationProvider` (default) + real impl behind config
- Strict response schema validation, size/token limits, budget-derived timeout,
  bounded repair retry, safe fallback
- PII minimization in prompts, documented
- `POST /v1/applications/{id}/classify` — candidate MCCs + confidence + explanation
- Persist **both** applicant-selected activity and system-proposed MCC; flag mismatch
- Applicant confirm/correct flow

**Gate:** invalid model JSON, oversized output, and timeout each produce a safe
structured result.

---

## Phase 8 — Evaluation & rate analysis
**Branch:** `feat/evaluation-engine`

- Statement extraction into normalized values (from fixture in mock mode)
- **Deterministic** effective-rate arithmetic in `domain/` — never from the model
- Response cleanly separates `extracted` / `calculated` / `commentary`
- Risk signals with `sourceField` / `sourceDocumentId` on **every** warning
- `POST /v1/applications/{id}/evaluate`, `GET /v1/applications/{id}/evaluation`
- Async fallback (`202` + `PROCESSING` + poll) if the budget cannot be met

**Gate:** rate math tests including zero-volume and missing-data cases; every warning
cites a source.

---

## Phase 9 — Review & submit
**Branch:** `feat/submission`

- Completeness gate: submission **blocked** with a precise missing-items list
- Normalized internal review payload for downstream processor mapping
- Version/lock the submitted application
- Terminal state is `READY_FOR_MANUAL_REVIEW` — never `APPROVED`

**Gate:** blocked-submission test and complete-submission payload test both pass.

---

## Phase 10 — Deadline hardening & timeout tests
**Branch:** `test/deadline-and-slow-dependency`

- Audit **every** I/O path for budget propagation
- Hanging-dependency mock + tests per `docs/05-TESTING-RULES.md` §1
- Bounded retry with backoff + jitter, budget-aware
- Structured timeout response + preserved state for safe retry
- ADR-0006: timeout strategy

**Gate:** tests prove exit before the hard limit, state preserved, retries bounded.

---

## Phase 11 — Frontend
**Branch:** `feat/web-multistep-form`

- Multi-step form with save/resume by application ID
- Client validation mirroring server rules
- Upload progress against pre-signed URL, with retry
- MCC suggestion confirm/correct UI
- Final review screen: all data, missing items, warnings, document status, proposed MCC
  + reason, evaluation summary
- Masked display of sensitive values
- Clear error recovery

**Gate:** full journey completable in the browser against the local backend.

---

## Phase 12 — Integration test & end-to-end
**Branch:** `test/end-to-end-journey`

- The mandatory single integration test:
  create → update business → presign → mock upload complete → classify → evaluate → submit
- Runnable with one documented command

**Gate:** green from a clean checkout.

---

## Phase 13 — Documentation & IAM hardening
**Branch:** `docs/architecture-and-security`

- `docs/ARCHITECTURE.md` + diagram
- OpenAPI spec / Postman collection
- `docs/SECURITY.md` threat model + IAM approach + redaction decisions
- `docs/TEST-EVIDENCE.md` with **real** output
- `docs/DEMO.md` walkthrough
- **IAM audit:** remove every permission that cannot be justified out loud
- README completed: assumptions, tradeoffs, known gaps, cleanup commands
- `AI-USAGE.md` finalized

**Gate:** fresh-clone test — follow the README on a clean directory and it works.

---

## Phase 14 — Bonus (only if all must-pass items are green)
**Branch:** `feat/<specific-bonus>`

Priority order by rubric value:
1. Reviewer/admin screen (risk flags, document status, provenance, reason codes)
2. Duplicate-application detection via non-sensitive fingerprints
3. Rule-versioning for processor-specific policy
4. Event-driven async evaluation preserving the 45 s sync boundary
5. Document OCR with confidence + manual correction UI

Do **not** start any of these while a must-pass item is red.

---

## Phase 15 — Final sweep
**Branch:** `chore/final-review`

Run `docs/07-DELIVERY-CHECKLIST.md` §E in full. Then submit.

---

## If time runs short

Cut in this order — and **document every cut** in Known Gaps:
1. Bonus items (all)
2. Frontend polish → keep it functional but plain
3. OCR / real statement parsing → fixture-driven mock only
4. Real AWS deployment → local simulation with clear instructions

**Never cut:** the 45-second tests, security/redaction, the integration test, the
README, `AI-USAGE.md`, or git history quality. Those are where the points are.
