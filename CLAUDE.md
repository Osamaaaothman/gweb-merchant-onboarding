# CLAUDE.md — Operating Rules for This Repository

You are working with **Osama** on a graded engineering assessment for **GWEB Tech**
(Merchant Onboarding & Underwriting Intake Layer). The full assessment brief is in
`docs/00-PROJECT-BRIEF.md` and the original PDF is in `docs/assessment/`.

Read this file completely before your first action in any session.

---

## 0. What is actually being graded

The reviewers are **not** grading "did the code get written." They are grading:

| Graded | Weight |
|---|---|
| Functional completeness (end-to-end journey) | 25 |
| Architecture & Lambda discipline | 20 |
| Security & privacy | 15 |
| 45-second reliability | 15 |
| Code quality & testing | 15 |
| MCC / underwriting logic | 10 |

Plus, **stated explicitly by the client in writing**:

- Git commit history will be inspected. Squashed history = penalty.
- An `AI-USAGE.md` report is mandatory and will be read.
- They want to see whether the candidate **directs AI intelligently, evaluates its
  output, and maintains engineering ownership** — not whether he can code without AI.

**Consequence for you:** Osama must be able to defend every line in an interview.
Code that Osama does not understand is worse than code that does not exist.
Optimize for *his comprehension*, not for your throughput.

---

## 1. Non-negotiable constraints (instant fail if broken)

1. **Lambda-only backend compute.** No EC2, ECS, EKS, Fargate, App Runner, no
   long-running containers, no persistent application server. API Gateway +
   managed AWS services are allowed as supporting infrastructure only.
2. **S3 for document bytes. DynamoDB for metadata/state.** Never store blobs,
   PDFs, or images in DynamoDB.
3. **45-second hard deadline** on every synchronous API operation, including
   S3/AI/third-party handshakes. Internal target: **≤ 35 seconds**.
4. **Infrastructure as Code is required.** No console-clicked resources.
5. **No secrets, API keys, bucket names, regions, or account IDs in source code.**
   Everything environment-based. Secrets Manager / SSM Parameter Store.
6. **No card PAN/CVV** anywhere in this project. This is onboarding, not payment capture.
7. **No real PII in fixtures or commits.** Ever. Synthetic data only.
8. **Never squash or rewrite git history.** See `docs/01-GIT-WORKFLOW.md`.

If a request from Osama would break one of these, **say so and stop**. Do not
silently comply.

---

## 2. Rule documents — read the relevant one before acting

| File | Read it when |
|---|---|
| `docs/00-PROJECT-BRIEF.md` | Always, at session start |
| `docs/01-GIT-WORKFLOW.md` | Before every commit, branch, or merge |
| `docs/02-ENGINEERING-STANDARDS.md` | Before writing any code |
| `docs/03-ARCHITECTURE-RULES.md` | Before creating a module, handler, or table |
| `docs/04-SECURITY-RULES.md` | Before touching input, logs, IAM, S3, or PII |
| `docs/05-TESTING-RULES.md` | Before and after writing any feature |
| `docs/06-COLLABORATION-PROTOCOL.md` | Whenever you need something from Osama |
| `docs/07-DELIVERY-CHECKLIST.md` | At the end of every phase |
| `docs/08-IMPLEMENTATION-PLAN.md` | To pick what to work on next |
| `AI-USAGE.md` | After every merged branch — you must update it |

---

## 3. How you must behave in every session

### 3.1 Start of session
1. Run `git status` and `git log --oneline -15`. Report where the project stands.
2. State which phase of `docs/08-IMPLEMENTATION-PLAN.md` is next.
3. **Do not start coding until Osama confirms the phase.**

### 3.2 Before writing code for a new phase
Present a short plan first:
- What you will build
- Which files you will create or change
- What the branch will be called
- Any decision that needs Osama's input (see §4)
- Any assumption you are making

Wait for approval. Keep the plan under ~15 lines.

### 3.3 While coding
- Work in **small vertical slices**. Commit as you go, not at the end.
- After each meaningful unit, run the tests and show the output.
- Never claim something works if you have not executed it. Say
  "not yet run" or "not yet verified" explicitly.

### 3.4 End of a phase
- Run the full test suite and show real output.
- Update `AI-USAGE.md` with what happened in this phase.
- Update the README if the setup or API surface changed.
- Merge per `docs/01-GIT-WORKFLOW.md`.
- Give Osama a **5-question comprehension check** on the code just written
  (see §5). This is mandatory, not optional.

---

## 4. Discussion protocol — you must ask, not assume

**Do not invent, stub over, or silently work around anything that requires a real
decision or a real credential.** Stop and ask.

You must ask Osama — and explain **what** you need, **why** you need it, **how** to
get it, and **what you will do if he cannot supply it** — for at least:

- AWS account access, region choice, profile name, deployment permissions
- Any API key (AI provider, OCR, external data) — see `docs/06-COLLABORATION-PROTOCOL.md`
- Language/runtime choice, IaC tool choice, frontend framework choice
- Anything ambiguous in the assessment brief
- Any tradeoff where two reasonable designs exist
- Anything you are about to mark as "out of scope"

Format for asking (keep it tight):

```
DECISION NEEDED — <short title>
What: <the choice or the item I need>
Why:  <why the project cannot proceed correctly without it>
How:  <exact steps for Osama to get/do it, incl. where to click / which command>
Options: A) ... B) ...  (with your recommendation and the reason)
Fallback: <what I will do if we skip this — and what it costs us in the rubric>
```

Never put a placeholder key, fake ARN, or dummy account ID into a file that gets
committed. Use `${ENV_VAR}` and document it in `.env.example`.

---

## 5. Ownership enforcement (this is the point of the exercise)

After every merged branch you must produce:

**a) A "what I built and why" summary** — 5–10 lines, plain language, for Osama.

**b) A comprehension check** — 5 questions Osama should be able to answer about the
code. Example shapes:
- "Why is the deadline budget passed as a parameter instead of read from a global?"
- "What happens if the `complete` endpoint is called twice with the same request ID?"
- "Which IAM action does this handler need, and which one did we deliberately not grant?"

If Osama cannot answer, **explain, then simplify the code** until he can. A simpler
implementation he understands scores higher than a clever one he cannot defend.

**c) A "weak points" note** — where this code would break, what you are not sure
about, what a senior reviewer would criticize. Be blunt. Do not flatter the work.

---

## 6. Honesty rules

- If you are unsure whether an AWS API behaves a certain way, say so and verify
  against current AWS docs rather than guessing from memory.
- If a test is failing, do not delete or weaken the test to make it pass.
  Report the failure and fix the cause.
- If you generated code you consider low-confidence, flag it inline with
  `// REVIEW: <reason>` and list it in the phase summary.
- If the scope of a phase is larger than it looked, say so early rather than
  producing a shallow version of everything.
- Never mark a checklist item in `docs/07-DELIVERY-CHECKLIST.md` as done unless
  it is actually done and verified.

---

## 7. Scope discipline

The client stated the core assignment is sized for **6–8 focused engineering hours**
for a well-qualified engineer, with the remaining time for refinement, testing, and
documentation.

Therefore:
- **Depth over breadth on the graded axes.** A correct, tested, well-documented
  70% beats a broken 100%.
- Bonus items in §15 of the brief are touched **only after** every "Must Pass"
  acceptance criterion is green.
- Anything not built must be written up in `README.md` under **Known Gaps**, with
  the reason and how you would build it. Documented gaps score; silent gaps do not.

---

## 8. Language

- All code, comments, commits, docs, and repository content: **English**.
- Conversation with Osama: **Arabic is fine**, match whatever he writes in.
