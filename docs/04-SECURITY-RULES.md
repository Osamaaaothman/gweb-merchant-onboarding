# 04 — Security & Privacy Rules

15 rubric points, and the fastest way to lose credibility with a payments company.

---

## 1. Hard rules

1. **No card PAN or CVV** is accepted, stored, logged, or modelled anywhere. If a field
   would hold one, it does not exist in this system. Say so in the README.
2. **No raw online-banking credentials.** Settlement bank account is **metadata only**:
   account holder, bank name, last4, statement date.
3. **No secrets in source.** Not in code, not in IaC literals, not in tests, not in
   commit history, not in `.env` (only `.env.example` with placeholders is committed).
4. **No real PII in fixtures.** All test data is synthetic and obviously so
   (`Jane Testerson`, `test@example.invalid`, `EIN 00-0000000`).
5. **Private S3 bucket, Block Public Access on**, encryption at rest, HTTPS/TLS only
   in transit.
6. Never build an S3 key, file path, or DynamoDB key directly from user input.

---

## 2. Redaction — enforced by code, not by discipline

Build a single redaction utility in `shared/` and route **all** logging through it.

### Never appears in logs, ever
- Uploaded document content or any bytes
- Identity document images
- Bank statement content
- Government ID numbers, SSN, tax IDs / EIN (full value)
- Bank account or routing numbers (full value)
- Date of birth
- Raw secret values, tokens, pre-signed URLs (they are credentials)
- AI prompts containing unnecessary personal data
- Full request bodies of applicant/business PATCH endpoints

### Masking rules for responses and UI
| Field | Stored | Returned after capture |
|---|---|---|
| Government ID | type + last4 only | `type: PASSPORT, last4: 4821` |
| Tax ID / EIN | masked (last4) | `**-***4821` |
| Bank account | last4 only | `****4821` |
| DOB | full (needed for KYB) | masked or omitted unless explicitly required |
| Email / phone | full | partially masked in list views |

Write a unit test that asserts the logger output for a fully populated application
contains **none** of the sensitive values. This test is itself evidence for the
security note deliverable.

---

## 3. Input validation at every boundary

Guard against:
- Oversized bodies → enforce a max body size, return `413`
- Invalid content types → return `415`
- Malformed JSON → `400`, never a 502
- Path manipulation / traversal in IDs and filenames → strict format validation
  (UUID regex for IDs; never interpolate raw input into keys)
- Duplicate / replayed submissions → idempotency keys + conditional writes
- Unknown/extra fields → reject explicitly rather than ignore
- Type confusion (string where number expected, arrays where object expected)
- Unicode/homoglyph tricks in names and business descriptions where it affects matching
- Injection into the AI prompt — treat business description as untrusted text, delimit
  it clearly, and never let it change the system instruction

### Upload-specific validation
- Extension allowlist: `.pdf .jpg .jpeg .png`
- MIME allowlist, pinned in the presign conditions
- Max size limit, enforced in the presign policy **and** re-checked on `complete`
- **File signature (magic bytes)** check where practical — a `.pdf` extension proves nothing
- Extension and declared MIME must agree; mismatch is rejected
- SHA-256 checksum recorded and compared

---

## 4. IAM — least privilege is graded

- **One role per Lambda function.** No shared "app role."
- Never `Action: "*"`, never `Resource: "*"`, never `s3:*` or `dynamodb:*`.
- Scope to exact operations, e.g.:
  - Presign function: `s3:PutObject` on `arn:...:bucket/applications/*` only
  - Read handler: `dynamodb:GetItem`, `dynamodb:Query` on the table + specific GSIs
  - Nothing gets `s3:DeleteObject` unless a function genuinely deletes
- Secrets access scoped to the specific secret ARN.
- Be able to justify **every single permission** in an interview. If you cannot justify
  it, remove it.

---

## 5. Secrets management

- Secrets Manager or SSM Parameter Store (SecureString).
- Fetched at cold start, cached in memory for the container lifetime, never logged,
  never returned in a response, never written to DynamoDB.
- Rotation approach documented in the security note, even if not implemented.
- `.gitignore` covers `.env*` except `.env.example`.
- Run a secret scan before the final push. If a secret ever lands in history, it must
  be **rotated** — removing it from a later commit is not a fix, and you must tell
  Osama that immediately.

---

## 6. Data retention and deletion (required deliverable)

Document and, where cheap, implement:
- How an abandoned application is identified (e.g. no update for N days, status not `SUBMITTED`)
- How its DynamoDB items are removed (TTL attribute is the natural fit)
- How its S3 objects are removed (lifecycle rule keyed on prefix, or a cleanup Lambda)
- What is retained for audit and for how long, and why
- How a data-subject deletion request would be handled

This does not need to be fully automated, but the design must be written down.

---

## 7. Audit trail

Every record carries: `created_at`, `updated_at`, `actor`/`source`,
`request_id`/`correlation_id`. Every material status change is recorded as an event
(who, what, when, from-state, to-state, reason) — not just a mutated field.

---

## 8. Threat model (required deliverable — `docs/SECURITY.md`)

Cover at minimum:
| Threat | Mitigation in this build | Production improvement |
|---|---|---|
| Unauthorized access to another applicant's data | ... | ... |
| Pre-signed URL abuse / reuse / oversized upload | ... | ... |
| Malware or polyglot file upload | ... | ... |
| Prompt injection via business description | ... | ... |
| Replay / duplicate submission | ... | ... |
| PII leakage through logs | ... | ... |
| Enumeration of application IDs | ... | ... |
| Denial of wallet (cost abuse via AI calls) | ... | ... |
| Supply-chain risk in dependencies | ... | ... |

Be honest about what is **not** mitigated in a 3-day prototype. A clear-eyed "here is
the gap and here is how I would close it" scores better than a claim you cannot back.

---

## 9. The "do not pretend" rule

The brief is explicit: this system **must not pretend** to perform authoritative
identity verification, credit decisions, sanctions screening, or final approval.

Therefore:
- No code path produces `APPROVED`. The terminal positive state is
  `READY_FOR_MANUAL_REVIEW`.
- Mock providers are labelled `"provider": "mock"` in every response they touch.
- README states plainly what is real and what is simulated.
- No field or endpoint is named in a way that implies verification that is not happening
  (`kycVerified: true` on mock data would be a serious error).
