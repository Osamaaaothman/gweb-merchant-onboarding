# 00 — Project Brief

Condensed, actionable version of the GWEB assessment. The original PDF is the source
of truth; this file is the working summary. If they conflict, the PDF wins — and you
must flag the conflict to Osama.

---

## Objective

Build a **merchant onboarding processing layer** that:
1. Collects applicant + business information with resumable state
2. Accepts supporting documents
3. Classifies the merchant using **Merchant Category Codes (MCC)**
4. Produces an **explainable underwriting-assistance result**
5. Emits a **normalized internal review payload** for later mapping to processor APIs

### Hard boundary
This is an **intake and decision-support prototype**. It must never present itself as
performing authoritative identity verification, credit decisions, sanctions screening,
or final merchant approval. Mock all external providers where credentials are absent,
and label mock output as mock in the response payload.

---

## Required user journey

1. **Session** — create or resume an application by unique application ID; state persists
2. **Individual / controller info** — applicant + beneficial owners / controlling persons
3. **Corporate verification intake** — legal entity, registration IDs, ownership, addresses, business profile
4. **Document upload** — registration evidence, IDs, licenses, bank evidence, optional processor statements
5. **Business classification** — applicant self-selects activity → system proposes MCC + risk tags → applicant confirms or corrects
6. **Rate & business evaluation** — if a processing statement exists, extract/accept rates and produce AI-assisted comparison; flag items needing manual review
7. **Review & submit** — show all data, missing items, warnings, document status, proposed MCC, evaluation summary
8. **Internal review payload** — normalized record for downstream mapping

---

## Minimum data model

### Individual / control person
- Legal first / middle (optional) / last name
- Date of birth
- Residential address: line1, line2, city, state/province, postal code, country
- Email, phone
- Role/title in business
- Ownership percentage (if applicable)
- Government ID **type + masked/last-four only** in metadata
- Consent/attestation timestamps + version of terms accepted

### Business / legal entity
- Legal business name + DBA / trade name
- Entity type: LLC, corporation, partnership, sole proprietor, nonprofit, other
- Formation country + state/province
- Registration identifier: EIN / federal tax ID, state UBI / local registration number, or jurisdiction equivalent
- Registered address + operating address
- Website URL + customer-facing business description
- Business start date
- Volume profile: expected annual card volume, average ticket, highest ticket, monthly transaction count, card-present vs card-not-present mix, e-commerce percentage
- Ownership / beneficial-owner structure + controlling persons
- Settlement bank account **metadata only** — never raw online-banking credentials
- Existing payment processor (if any) + optional processing history

---

## Documents

| Document | Required? | Metadata to retain |
|---|---|---|
| Government ID | Required for relevant individuals | type, owner, S3 key, checksum, status, uploaded_at, expires_at |
| Business registration | Required | jurisdiction, identifier, S3 key, status |
| Business license | Conditional | license type, issuer, masked number, expiry |
| Bank evidence | Required | account holder, bank name, last4, statement date, S3 key |
| Processing statement | Optional (drives rate evaluation) | processor, period, extracted metrics, confidence |
| Additional underwriting evidence | Conditional | document category, reason requested, review status |

### Upload rules
- **Pre-signed S3 URLs.** Client uploads directly to S3. Never proxy document bodies through Lambda.
- Allow at minimum PDF, JPG/JPEG, PNG. Validate MIME type, extension, size, and file signature (magic bytes) where practical.
- Unique, non-guessable S3 keys. Bucket blocks public access, always.
- Store SHA-256 checksum + upload timestamp (integrity + idempotency).
- Lifecycle: `REQUESTED → UPLOADING → RECEIVED → PROCESSING → ACCEPTED | NEEDS_REVIEW | REJECTED`
- Sensitive values are **never** written to logs. Mask IDs, tax identifiers, bank account numbers in logs and in UI after capture.

---

## MCC classification & risk policy

**Core design rule: MCC taxonomy and risk policy are two separate things.** Risk
policy must be changeable without touching the catalog.

- Import or seed a **real MCC dataset** (see Reference sources below). Do not invent codes.
- Ship a script or documented process to refresh the dataset.
- Risk is a **configurable policy attribute** — e.g. `standard`, `enhanced-review`,
  `restricted/unsupported`. Never hard-label an MCC as universally "high risk."
- Support **provider-specific overrides** — different acquirers apply different policy
  to the same MCC.
- Demonstrate enhanced-review logic for financial / quasi-cash / security-related
  activity. Mastercard documentation calls out MCC **6012, 6051, 6211** for certain
  specialized purchase types.
- Persist **both** the applicant-selected activity **and** the system-proposed MCC so
  reviewers can inspect mismatches.
- **Never auto-approve a merchant because a model labelled it low risk.**

---

## AI-assisted evaluation layer

Must work through an adapter with a mock implementation when no credentials exist.
The **interface and failure behavior must be production-minded**, even in mock mode.

Capabilities:
- **Business profile summary** — what they sell, sales channel, geography, fulfillment model, recurring/subscription behavior, customer type
- **MCC suggestion** — candidate MCCs + confidence + short explanation, from structured fields + description
- **Statement extraction** — normalize processor, monthly volume, discount/effective rate, transaction fees, monthly fees, chargeback fees, statement period
- **Current-rate analysis** — transparent effective processing cost. **Deterministic arithmetic must be separated from AI commentary.**
- **Risk signals** — contradictions, missing evidence, unusually high ticket, description/MCC mismatch, incomplete ownership data, regulated-license claims without evidence

Rules:
- **Every warning must cite the input field or document that caused it.** No unexplained flags.
- **AI output is untrusted structured input.** Validate against a schema, impose token/size limits, enforce timeouts, fall back safely.

---

## AWS architecture

| Layer | Service | Expectation |
|---|---|---|
| Client | Web UI (your choice) | Multi-step form, upload progress, resume, validation, final review |
| API ingress | API Gateway (recommended) | REST or HTTP API in front of Lambda; document routes + auth assumptions |
| Compute | **AWS Lambda — required** | Stateless handlers, bounded execution, retries, idempotency |
| State | **DynamoDB — required** | Application, person/entity, document metadata, status, MCC/evaluation records |
| Objects | **S3 — required** | Private, pre-signed direct upload, documented lifecycle/versioning |
| AI/external | Lambda adapter | Strict timeout budget, schema validation, mockable |
| Secrets | Secrets Manager / SSM | No keys in source |
| Observability | CloudWatch | Structured logs, correlation/application ID, metrics, failures, no raw sensitive values |

---

## The 45-second rule (15 rubric points — treat as a first-class feature)

- Every synchronous API operation returns within **45 seconds**, including S3 and
  external handshakes.
- Set an internal deadline of **≤ 35 seconds** to leave headroom for cleanup and
  response serialization.
- Set **explicit downstream timeouts**. Never wait indefinitely on AI, storage, or a
  third party.
- If a workflow can exceed the budget, convert it to an **asynchronous state
  transition** (`RECEIVED → PROCESSING`) and let the client poll a status endpoint.
  Each individual poll is still bound by the 45-second rule.
- Never hold Lambda open while a user uploads a file — pre-signed URL only.
- Bounded retries with backoff + jitter, **only when the remaining deadline permits**.
- Return a clear timeout/error state and preserve enough workflow state for safe retry.
- **Required deliverable:** at least one test where a mocked dependency hangs or
  responds slowly, and the function exits safely before the hard limit.

---

## API surface (naming flexible, behavior is not)

```
POST   /applications
GET    /applications/{id}
PATCH  /applications/{id}/applicant
PATCH  /applications/{id}/business
POST   /applications/{id}/documents/presign
POST   /applications/{id}/documents/{documentId}/complete
GET    /mcc?query=...
POST   /applications/{id}/classify
POST   /applications/{id}/evaluate
GET    /applications/{id}/evaluation
POST   /applications/{id}/submit
```

---

## DynamoDB expectations

- Choose **and document** single-table vs multi-table. Reviewers care about access
  patterns and consistency, not fashion.
- Idempotent writes via request IDs / conditional writes.
- Optimistic concurrency (version field) to prevent silent overwrites.
- Document at minimum these access patterns:
  load application · list people/owners · list documents · fetch current evaluation ·
  find submission status · query MCC catalog (DynamoDB or packaged static data, if justified)
- No document blobs in DynamoDB.

---

## Acceptance criteria (must all pass)

- [ ] Backend compute is Lambda-only
- [ ] S3 for documents, DynamoDB for metadata/workflow state
- [ ] No synchronous request exceeds 45s; slow-dependency behavior demonstrated
- [ ] Applicant + business + ownership data can be entered, validated, saved, resumed, reviewed
- [ ] Documents upload securely with metadata and lifecycle state
- [ ] Business type self-selected; MCC suggested from a **real** catalog
- [ ] Risk policy configurable and separated from MCC taxonomy
- [ ] AI/rate evaluation returns structured, explainable output and fails safely
- [ ] Submission blocks on missing required items; produces normalized review payload when complete
- [ ] Tests, documentation, deployment instructions, no-real-PII fixtures included

---

## Deliverables

1. **Source repository** — app code, tests, IaC, seeding/import utilities, fixtures
2. **Runnable demo** — deployed endpoint OR reproducible local Lambda/API simulation + deploy instructions
3. **Architecture document** — one diagram + explanation of request flow, S3 upload path, DynamoDB state, AI/external calls, timeouts, retries, failure states
4. **API documentation** — OpenAPI/Swagger, Postman collection, or equivalent
5. **MCC implementation** — seed/import source, search behavior, self-selected mapping, proposed-MCC workflow, configurable risk-policy example
6. **Test evidence** — automated test output + note demonstrating ≤45s timeout behavior
7. **Security note** — threat/abuse considerations, IAM approach, logging/redaction decisions, production improvements
8. **Demo walkthrough** — concise start-to-submission walkthrough
9. **`AI-USAGE.md`** — see the template in the repo root
10. **Full, unsquashed git history**

---

## Bonus (only after all "must pass" items are green)

Accessible polished form UX with save/resume · document OCR with confidence + manual
correction · rule-versioning for processor-specific policy · duplicate-application
detection via non-sensitive fingerprints · event-driven async evaluation preserving the
45s sync boundary · rate comparison cleanly separating known values / calculated
metrics / model interpretation · reviewer admin screen with risk flags, document status,
data provenance, reason codes.

---

## Reference sources (use current official docs, not stale third-party lists)

- Visa Merchant Data Standards Manual — public MCC listing and definitions
  `https://usa.visa.com/content/dam/VCOM/download/merchants/visa-merchant-data-standards-manual.pdf`
- Mastercard Rules / Compliance Programs
  `https://www.mastercard.com/us/en/business/support/rules.html`
- Mastercard Gateway documentation — 4-digit MCC, specialized purchase types (6012, 6051, 6211)
- AWS Lambda / S3 / DynamoDB / API Gateway docs — limits, IAM, presigned uploads, retries, timeouts

---

## Client's explicit evaluation notes (from the email)

> Looking especially closely at: code quality, structure, readability, maintainability ·
> architecture and technical decision-making · security and data-handling practices ·
> API and backend design · testing and error handling · ability to follow the Lambda /
> S3 / DynamoDB / 45-second constraints · documentation of assumptions, tradeoffs, and
> incomplete portions.

> AI tools are encouraged. The goal is whether the candidate can use AI effectively
> while still understanding, reviewing, testing, and taking responsibility for the
> resulting code.

> Submit a GitHub repository link. Preserve normal commit history — do not squash into
> a single final commit. Meaningful commits showing fixes, refactoring, testing, and
> architectural changes are expected.
