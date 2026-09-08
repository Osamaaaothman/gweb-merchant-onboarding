# 03 — Architecture Rules

Worth 20 rubric points on its own, and it gates the "must pass" criteria.

---

## 1. Compute — Lambda only

Allowed: AWS Lambda, API Gateway (REST or HTTP API), and managed AWS data/security
services as supporting infrastructure.

Forbidden: EC2, ECS, EKS, Fargate, App Runner, Elastic Beanstalk, any persistent
application server, any container running as a long-lived service, any "just run
Express on a box" shortcut.

Note in the README **explicitly** that compute is Lambda-only and point to the IaC
lines that prove it. Make the reviewer's verification trivial.

### Handler granularity
Prefer **one Lambda per logical route group** rather than one monolithic proxy handler.
- Better IAM least-privilege (each function gets only the actions it needs — a rubric item)
- Smaller cold start surface
- Clearer CloudWatch log separation

If shared code grows, use a Lambda Layer or a shared internal package, and say why in
an ADR.

---

## 2. Storage split — absolute

| Data | Goes to |
|---|---|
| Document bytes (PDF/JPG/PNG) | **S3 only** |
| Document metadata, checksums, status | **DynamoDB only** |
| Application / person / business state | **DynamoDB** |
| MCC catalog | DynamoDB **or** packaged static data (justify in an ADR) |
| Evaluation results | DynamoDB (bounded size; large artifacts → S3 with a pointer) |

Never put a blob, base64 payload, or full document text into a DynamoDB item.
If an AI extraction result could be large, store it in S3 and keep a reference +
summary in DynamoDB.

---

## 3. S3 rules

- Private bucket. **Block Public Access = ON** at bucket level, verified in IaC.
- Encryption at rest enabled (SSE-S3 minimum). Document in the security note whether
  and how you would use **SSE-KMS with a customer-managed key** in production, and why
  it was or was not used here.
- **Pre-signed PUT URLs** for upload. Lambda never receives document bodies.
- Pre-signed URL constraints:
  - Short TTL (minutes, not hours) — named constant, documented
  - Pin `Content-Type` and enforce a size limit in the presign conditions
  - Object key is unique and **non-guessable**, e.g.
    `applications/{applicationId}/documents/{documentId}/{uuid}{ext}` — never a raw
    filename, never a user-controlled path segment
  - Sanitize the original filename before storing it as metadata; never use it to
    build the key
- On `complete`, verify what you can server-side: object exists, size, `ETag`/checksum
  matches the client-declared SHA-256, content type. Validate **file signature (magic
  bytes)** where practical — extension and MIME headers are attacker-controlled.
- Enable versioning **or** document explicitly why not, plus the lifecycle policy for
  abandoned applications (retention/deletion is a graded deliverable).

---

## 4. DynamoDB rules

- Pick single-table or multi-table, then **write the ADR before writing the code**.
  The reviewers said they care about access patterns, not fashion — so lead with the
  access-pattern table.
- Document every access pattern in `docs/` as a table:

| # | Access pattern | PK | SK | Index | Consistency |
|---|---|---|---|---|---|
| 1 | Load application by ID | `APP#{id}` | `META` | base | strong |
| 2 | List people/owners for application | `APP#{id}` | `PERSON#...` | base | strong |
| 3 | List documents for application | `APP#{id}` | `DOC#...` | base | strong |
| 4 | Fetch current evaluation | `APP#{id}` | `EVAL#CURRENT` | base | strong |
| 5 | Find submission status | ... | ... | ... | ... |
| 6 | Query MCC catalog by code / keyword | ... | ... | GSI | eventual |

(fill in for real — this is illustrative)

- **Every item carries:** `pk`, `sk`, `entityType`, `version`, `createdAt`, `updatedAt`,
  `createdBy/actor`, `correlationId`.
- **Optimistic concurrency:** updates use `ConditionExpression` on `version`; a
  mismatch surfaces as `409 Conflict`, never a silent overwrite.
- **Idempotency:** creation and completion writes use conditional puts on the request
  ID so replays are safe.
- Use `TransactWriteItems` when two items must change together (e.g. document status
  + application completeness). Document why where used.
- No scans in a request path. If you need one, it belongs in a script, and you must
  say so.

---

## 5. Adapters and replaceability

Every one of these is an interface with a real and a mock implementation:

```
IApplicationRepository   → DynamoDbApplicationRepository   | InMemoryApplicationRepository
IDocumentStorage         → S3DocumentStorage               | InMemoryDocumentStorage
IEvaluationProvider (AI) → <Provider>EvaluationProvider    | MockEvaluationProvider
IMccCatalog              → DynamoDbMccCatalog / StaticMccCatalog
IRiskPolicyProvider      → ConfigRiskPolicyProvider
IClock                   → SystemClock                     | FakeClock
```

Rules:
- Interfaces live in `domain/` or `services/`, implementations in `adapters/`.
- Wiring happens in one composition root, driven by config. Swapping mock ↔ real is a
  config change, **not** a code change.
- The AI adapter **must** default to mock when no credentials are configured, and the
  response payload must state `"provider": "mock"` so nothing is misrepresented.

---

## 6. AI layer contract

Treat model output as hostile input.

- The adapter returns a **validated DTO**, never raw text passed through.
- Schema-validate the model response. On failure: one bounded retry with a repair
  prompt if budget permits, then fall back to a safe degraded result.
- Enforce input and output size/token limits. Truncate deterministically and record
  that truncation happened.
- Enforce a per-call timeout derived from the deadline budget.
- **Never send unnecessary PII to the model.** Send the business description and
  structured business fields; strip names, DOB, government IDs, bank details, and
  addresses unless a field is genuinely required — and document what is sent.
- Separate the two kinds of output in the response payload and in the code:
  ```json
  {
    "extracted":   { "...": "values read from the statement", "confidence": 0.0 },
    "calculated":  { "effectiveRate": "deterministic arithmetic, no model involved" },
    "commentary":  { "text": "model-generated interpretation", "provider": "mock" }
  }
  ```
  Deterministic arithmetic never comes from the model.
- Every warning/risk signal carries a `sourceField` or `sourceDocumentId`. An
  unexplainable warning is a bug.
- **No auto-approval path exists in the code at all.** The most positive outcome the
  system can produce is `READY_FOR_MANUAL_REVIEW`.

---

## 7. Async fallback

If any operation cannot reliably finish inside the budget:
- Return `202` with the workflow moved to `PROCESSING`
- Persist enough state to resume safely
- Expose a status endpoint the client polls
- Each poll is itself bound by the 45-second rule
- Use a Lambda-compatible managed trigger for the background work (e.g. DynamoDB
  Streams, EventBridge, SQS → Lambda). **Still Lambda-only compute.**

Document the state machine and the poll contract in the architecture document.

---

## 8. API design

- Consistent resource-oriented routes, per the suggested surface in the brief.
- Correct status codes: `201` create, `200` read, `202` accepted-async, `400`
  validation, `404` not found, `409` conflict/idempotency, `413` payload too large,
  `415` unsupported media type, `429` throttled, `504`/structured timeout for deadline
  exhaustion.
- Every response carries the correlation ID.
- **Document your auth assumptions explicitly.** If you are not implementing full auth
  (reasonable for a prototype), state exactly what you assumed, what you stubbed, what
  production would require (e.g. Cognito authorizer, per-application access scoping),
  and where the seam is in the code. An honest documented assumption scores; a silent
  gap does not.
- Versioning: prefix routes with `/v1`.
- OpenAPI spec is generated or maintained alongside the code, not written at the end.

---

## 9. Frontend

- Multi-step form with save/resume by application ID
- Client-side validation mirroring server rules, with the server as the authority
- Upload progress against the pre-signed URL, with retry
- Final review screen showing: all data, missing items, warnings, document status,
  proposed MCC + reason, evaluation summary
- Masked display of sensitive values after capture (`***-**-1234`)
- The frontend never holds AWS credentials — only the pre-signed URL it was given

---

## 10. Infrastructure as Code

- One tool, chosen and justified in an ADR (SAM is the lowest-friction default;
  CDK and Terraform are equally acceptable).
- IaC defines: Lambdas, API Gateway, DynamoDB table(s), S3 bucket + block-public-access
  + encryption + lifecycle, IAM roles **per function** with least privilege, log groups
  with retention, environment variables sourced from parameters.
- **Least privilege is graded.** No `s3:*`, no `dynamodb:*`, no `Resource: "*"`.
  Scope actions to the exact operations and ARNs each function needs, and be prepared
  to explain each one.
- Provide `scripts/` for: deploy, seed MCC catalog, run locally, and **teardown/cleanup**
  (the brief explicitly asks for cleanup commands).
