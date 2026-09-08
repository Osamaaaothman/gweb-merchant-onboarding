# 07 — Delivery Checklist

Only tick a box when it is **actually done and verified**. An untrue checkmark here is
worse than an empty one.

---

## A. Must-pass acceptance criteria

- [ ] Backend application compute is **Lambda-only** (no EC2/ECS/EKS/Fargate/App Runner)
- [ ] **S3** for uploaded documents, **DynamoDB** for metadata/workflow state
- [ ] No synchronous request can exceed **45 s**; slow-dependency behavior demonstrated by a test
- [ ] Applicant + business + ownership data can be entered, validated, saved, **resumed**, and reviewed
- [ ] Documents upload securely with metadata and full lifecycle state
- [ ] Business type self-selected; system suggests MCC from a **real** catalog
- [ ] Risk policy is **configurable** and clearly separated from MCC taxonomy
- [ ] AI/rate evaluation returns **structured, explainable** output and fails safely
- [ ] Submission **blocks** on missing items; produces a normalized internal review payload when complete
- [ ] Tests, documentation, deployment instructions, and **no-real-PII** fixtures included

---

## B. Deliverables

- [ ] **Source repository** — app code, tests, IaC, seeding/import utilities, fixtures
- [ ] **Runnable demo** — deployed endpoint OR reproducible local simulation + instructions
- [ ] **Architecture document** — `docs/ARCHITECTURE.md` with **one diagram** covering:
      request flow · S3 upload path · DynamoDB state · AI/external calls · timeouts ·
      retries · failure states
- [ ] **API documentation** — OpenAPI spec / Postman collection, with example requests
- [ ] **MCC implementation** — seed/import source, refresh script, search behavior,
      self-selected mapping, proposed-MCC workflow, configurable risk-policy example
- [ ] **Test evidence** — `docs/TEST-EVIDENCE.md` with real output + the ≤45 s note
- [ ] **Security note** — `docs/SECURITY.md`: threats, IAM approach, logging/redaction
      decisions, production improvements
- [ ] **Demo walkthrough** — `docs/DEMO.md`: start → submission → review payload
- [ ] **`AI-USAGE.md`** — complete, honest, specific
- [ ] **Full unsquashed git history** with meaningful commits
- [ ] Repository URL ready to send

---

## C. README requirements (the brief names each of these)

- [ ] What this is, and the explicit boundary: intake/decision-support prototype,
      **not** authoritative verification or approval
- [ ] Architecture overview + link to the diagram
- [ ] Prerequisites and local setup
- [ ] How to run locally (one command path)
- [ ] How to run tests (unit + integration + the timeout test)
- [ ] How to seed/import the MCC catalog
- [ ] Deployment instructions
- [ ] **Cleanup / teardown commands** (explicitly required)
- [ ] Full environment variable table
- [ ] API examples (curl or equivalent) for the whole journey
- [ ] **Assumptions** — every one, especially auth
- [ ] **Tradeoffs** — what was chosen and what was rejected
- [ ] **Known gaps / incomplete portions** — with the reason and how you would finish them
- [ ] Which parts are real and which are mocked
- [ ] Test coverage numbers (measured, not claimed)

---

## D. Rubric self-audit (score yourself honestly before submitting)

| Category | Pts | Self-score | Evidence (file / test / doc) |
|---|---|---|---|
| Functional completeness | 25 | | |
| Architecture & Lambda discipline | 20 | | |
| Security & privacy | 15 | | |
| 45-second reliability | 15 | | |
| Code quality & testing | 15 | | |
| MCC / underwriting logic | 10 | | |

For every category scoring below 70%, write one line on what would raise it and whether
there is time.

---

## E. Final pre-submission sweep

- [ ] `git log --oneline --graph --all` reviewed — history reads as a real development story
- [ ] No squashed history, no force-pushed branches
- [ ] Secret scan clean: `git log -p | grep -iE "AKIA|api[_-]?key|secret|password|BEGIN .*PRIVATE KEY"`
- [ ] No `.env`, no credentials, no real ARNs or account IDs anywhere in history
- [ ] No real PII in fixtures, tests, screenshots, or docs
- [ ] Fresh clone → follow README → it actually works (**test this on a clean directory**)
- [ ] All tests pass on a fresh clone
- [ ] CI is green
- [ ] No `TODO` / `FIXME` / `REVIEW:` left without a corresponding entry in Known Gaps
- [ ] No commented-out code, no debug prints
- [ ] `AI-USAGE.md` is complete, specific, and honest
- [ ] Every claim in the README is true
- [ ] AWS resources torn down (or the demo endpoint intentionally left up and documented)
- [ ] Repository access granted to whoever needs to review it
- [ ] `docs/INTERVIEW-NOTES.md` prepared for Osama (personal prep, not a deliverable)

---

## F. Submission message contents

Send back:
1. GitHub repository URL
2. Confirmation that full commit history is preserved
3. Link/path to `AI-USAGE.md`
4. Setup and deployment instructions (point to README)
5. Live demo endpoint if deployed, otherwise the local-run command
6. A short note on scope: what is complete, what is partial, what was deliberately
   left out and why

Be straightforward about what is incomplete. The brief explicitly asks for
"documentation of assumptions, tradeoffs, and incomplete portions" — honesty here is
scored, not penalized.
