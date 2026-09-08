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
| Claude Code (CLI) | claude-sonnet-5 | Implementation, refactoring, tests, documentation (Phases 0–1, 2026-09-08/09) |

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
| IaC (SAM template) | Generated; also caught and fixed a real issue via `sam validate --lint` (nodejs20.x already past its update-deprecation date) | *(Osama: fill in)* |
| Documentation (README, ADRs, this file's factual tables) | Generated | *(Osama: fill in)* |

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

**Issue:**
**Why it mattered:**
**What I did:**
**Test added:**

---

## 6. Errors and weaknesses I found in AI suggestions

Categories worth watching for, with real examples from this project:

- **Outdated or hallucinated AWS SDK APIs** — …
- **Over-broad IAM** (`s3:*`, `Resource: "*"`) proposed by default — …
- **Timeouts not propagated** — plausible-looking async code with no deadline threading — …
- **Tests that cannot fail** — asserted on mock behavior rather than production code — …
- **Silent error swallowing** in `catch` blocks — …
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
