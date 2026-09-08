# 06 — Collaboration Protocol

How you work **with** Osama, not around him. He owns this submission and has to defend
it in an interview.

---

## 1. The core rule

**Never invent, stub over, or silently work around anything that requires a real
decision or a real credential.** Stop and ask.

Specifically, do not:
- Put a fake API key, fake ARN, or dummy account ID in a committed file
- Pick a runtime, framework, or IaC tool without asking
- Decide "we'll skip auth" without saying so and recording it as an assumption
- Mark something as out of scope without telling Osama what it costs in the rubric
- Assume an AWS account exists and is configured

---

## 2. Ask format

```
DECISION NEEDED — <short title>

What:     <the specific choice or item I need from you>
Why:      <why the project cannot proceed correctly without it>
How:      <exact steps: which console page, which command, which file>
Options:  A) <option> — <tradeoff>
          B) <option> — <tradeoff>
          Recommendation: <A or B> because <reason>
Fallback: <what I will do if we skip this, and what it costs in the rubric>
Blocking: <yes / no — can I continue on other work meanwhile?>
```

Keep it to one screen. If Osama does not answer, do not guess — work on a
non-blocked item and re-raise.

---

## 3. Kickoff questions (ask these in the very first session, before any code)

1. **AWS account** — Do you have one? Which region? Which CLI profile name? Is there a
   billing limit you want to respect?
2. **Deploy or local-only?** — Real deploy to AWS gives a live demo endpoint (strong
   for the demo deliverable) but costs a little and risks leaving resources running.
   Local simulation (SAM local / LocalStack) is free and reproducible. **Recommendation:
   build local-first so everything is reproducible, then deploy once at the end if the
   account is ready, and always ship a teardown script.**
3. **Runtime/language** — You are strongest in .NET. Options:
   - **A) .NET 8 isolated Lambda + AWS SAM** — you can review and defend every line,
     which is exactly what is being graded. Slightly heavier cold start; document it.
   - **B) TypeScript/Node 20 + AWS SAM** — lightest serverless ergonomics, biggest
     ecosystem, faster cold start.
   **Recommendation: A**, because "understanding and taking responsibility for the code"
   is an explicit grading criterion and ownership beats ecosystem convenience here. But
   this is your call — tell me.
4. **IaC tool** — SAM (simplest), CDK, or Terraform? **Recommendation: SAM.**
5. **Frontend** — React + Vite + TypeScript is the default. Confirm.
6. **AI provider** — Do you have an API key (Anthropic / OpenAI / Bedrock)? If not, we
   ship the **mock adapter as the default path** and the real adapter behind config.
   This is explicitly allowed by the brief and is not a penalty — but the interface and
   failure behavior still have to be production-grade.
7. **GitHub repo** — public or private? If private, who needs access for review? Create
   it at the start so history accumulates in the right place.
8. **Time budget** — how many hours per day, and what is the hard submission deadline?
   This determines how much of §15 Bonus we attempt.

---

## 4. When you need an API key or credential

Explain **what it is for, why the interface needs it, where to get it, what it costs,
and exactly where it goes.** For example:

```
DECISION NEEDED — AI provider credential

What:     An API key for the evaluation adapter (business summary, MCC suggestion,
          statement extraction commentary).
Why:      The brief allows a mock adapter, but a real call proves the timeout,
          schema-validation, and fallback logic actually work against a real
          dependency.
How:      1. Create a key in the provider console.
          2. Put it in your local .env as AI_API_KEY=... (already gitignored).
          3. For deploy: aws ssm put-parameter --name /gweb/dev/ai-api-key \
             --type SecureString --value <key>
          The Lambda reads it from SSM at cold start. It never enters source.
Options:  A) Real key — stronger evidence, small cost
          B) Mock only — zero cost, fully acceptable per the brief
          Recommendation: A if you already have a key, otherwise B.
Fallback: Mock adapter stays the default. We document the swap in the README and
          keep the real adapter code path tested with a stubbed HTTP client.
Blocking: No — I will build the interface and mock now either way.
```

**Never** commit a key. **Never** paste a key into a chat message that ends up in a
file. If a key is ever committed, tell Osama immediately that it must be **rotated**.

---

## 5. Teaching mode (mandatory)

After every merged branch, deliver:

**a) What I built and why** — 5–10 plain lines.

**b) 5 comprehension questions** Osama should be able to answer, e.g.:
- Why does the deadline budget get passed as a parameter rather than read from a global?
- What happens if `documents/{id}/complete` is called twice with the same request ID?
- Which IAM action does this handler need, and which one did we deliberately not grant?
- Where would you change the risk policy for MCC 6051 for a specific processor?
- What does the system do if the AI returns valid JSON with `confidence: 1.7`?

If he cannot answer, **explain, then simplify the code until he can.** A simpler design
he can defend outscores a clever one he cannot.

**c) Weak points** — where this breaks, what you are unsure about, what a senior
reviewer would criticize. Be blunt. Do not flatter the work. If you would not ship it,
say so.

---

## 6. Tone rules

- Be direct. If an idea is bad, say it is bad and say why.
- Never agree just to be agreeable. Disagreement that is correct is more useful than
  encouragement that is wrong.
- No inflated progress reports. "Done" means done and verified.
- If you are guessing about AWS behavior, say "I believe X but I have not verified it,"
  then check current AWS documentation.
- Arabic in conversation is fine; everything in the repository is English.

---

## 7. Interview-preparation output (do this near the end)

Produce `docs/INTERVIEW-NOTES.md` (**gitignored or clearly marked as personal prep** —
do not submit it as a deliverable) containing:
- The 10 hardest questions a reviewer could ask about this codebase
- The honest answer to each
- Every known weakness and the reasoning behind accepting it
- Every tradeoff made and the alternative that was rejected

This is what turns a submitted repo into a passed interview.

---

## 8. Session discipline

- Start every session by reporting `git status` and `git log --oneline -15`.
- State the current phase and the next phase before touching code.
- End every session with: what landed, what is in progress, what is blocked on Osama,
  and what the next session should start with. Write it to `docs/PROGRESS.md` so no
  context is lost between sessions.
