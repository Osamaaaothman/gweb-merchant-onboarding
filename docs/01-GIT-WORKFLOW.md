# 01 — Git Workflow Rules

The client stated in writing that they will **review the development history**. Git
history is a graded artifact here, not bookkeeping. Treat every commit as something a
reviewer will read.

---

## 1. Absolute prohibitions

| Never | Why |
|---|---|
| `git commit` everything in one giant commit | Explicitly called out by the client as a fail |
| `git merge --squash` | Destroys the evolution they asked to see |
| `git rebase -i` to squash/reword merged history | Same |
| `git push --force` / `--force-with-lease` on `main` | Rewrites reviewed history |
| `git commit --amend` on a pushed commit | Same |
| Committing generated `node_modules/`, `bin/`, `obj/`, `.aws-sam/`, `cdk.out/` | Noise, hides real work |
| Committing `.env`, credentials, real ARNs, account IDs, real PII | Security fail, instant credibility loss |

Amending is allowed **only** on a local commit that has not been pushed, and only to
fix the message or add a forgotten file from the same logical change.

---

## 2. Branch model

`main` is always in a working state. All work happens on branches, merged back with
`--no-ff` so the merge point is visible in the graph.

### Naming

```
feat/<area>-<short-description>
fix/<area>-<short-description>
refactor/<area>-<short-description>
test/<area>-<short-description>
docs/<short-description>
chore/<short-description>
infra/<short-description>
```

Examples:
```
infra/sam-skeleton
feat/application-lifecycle
feat/dynamo-single-table
feat/document-presign
feat/mcc-catalog-import
feat/risk-policy-engine
feat/ai-adapter-mock
feat/deadline-budget
test/slow-dependency-timeout
refactor/extract-storage-adapter
fix/presign-content-type-validation
docs/architecture-diagram
```

### Rules
- **One branch = one coherent change.** If the branch is doing two unrelated things,
  split it.
- A branch that touches more than ~10 files or ~500 lines is probably two branches.
  Say so and propose the split before starting.
- Branch from up-to-date `main`:
  `git checkout main && git pull && git checkout -b feat/xyz`

---

## 3. Commit rules

### Cadence
Commit at each **logically complete step**, not at the end of the branch. A typical
feature branch should have **3–8 commits**, e.g.:

```
feat(documents): add document metadata model and lifecycle states
feat(documents): add presign handler with content-type allowlist
test(documents): unit tests for presign validation
fix(documents): reject presign when extension and MIME disagree
refactor(documents): move S3 key generation into storage adapter
docs(documents): document upload flow and lifecycle in README
```

**Do not manufacture fake history.** But equally, do not hide the real iteration —
if you wrote something, found a bug in it, and fixed it, that fix is its own commit
and it is *good* for the review.

### Format — Conventional Commits

```
<type>(<scope>): <imperative summary, ≤ 72 chars>

<optional body: what changed and WHY. Wrap at 72 columns.>
<optional: tradeoffs considered and rejected>
<optional: BREAKING CHANGE / Refs #n>
```

Types: `feat` `fix` `refactor` `test` `docs` `chore` `infra` `perf` `style`

Scopes (suggested): `applications` `applicant` `business` `documents` `mcc` `risk`
`ai` `evaluation` `submission` `storage` `db` `infra` `deadline` `web` `ci`

### Message quality bar
- Bad: `update code`, `fix bug`, `wip`, `changes`, `final`
- Bad: `feat: add stuff to handler`
- Good: `fix(deadline): propagate remaining budget to S3 client instead of using a fixed 10s timeout`
- Good: `refactor(ai): make the model adapter return a validated DTO so schema failures surface at the boundary`

The body should answer **"why"**, because the diff already shows "what".

### What must NOT be in one commit
- A feature + an unrelated refactor
- Formatting-only churn mixed with logic changes (do formatting in its own `style:` commit)
- Generated files mixed with hand-written source

---

## 4. Merging

```bash
# on the feature branch
git status                     # clean?
<run the full test suite>      # green?
git checkout main
git pull
git merge --no-ff feat/xyz -m "merge: <what this branch delivered and why>"
git push
git branch -d feat/xyz         # local delete only; keep the remote branch
```

Rules:
- **`--no-ff` always.** The merge commit is part of the story.
- Never merge a branch with failing tests. If tests fail, fix on the branch first.
- Never merge a branch that leaves `main` non-runnable.
- The merge commit message is a mini changelog: one line summary, then 2–4 bullets of
  what landed and any known gap.

Optional but recommended: push branches to GitHub and open a **real Pull Request** with
a description, even if Osama merges it himself. Reviewers can then see PR discussion
and self-review comments — that is strong evidence of engineering process.

---

## 5. Pre-commit checklist (run every time)

```
[ ] git diff --staged reviewed line by line — no surprises
[ ] no secrets, keys, tokens, real ARNs, account IDs, real emails, real PII
[ ] no debug prints, no commented-out dead code
[ ] tests for this change exist and pass
[ ] file/folder placement matches docs/03-ARCHITECTURE-RULES.md
[ ] commit message follows the format above and explains WHY
```

Run `git diff --staged | grep -iE "aws_secret|api[_-]?key|password|BEGIN .*PRIVATE KEY|AKIA"`
before committing. If anything matches, stop.

---

## 6. Required repo hygiene (set up in the very first branch)

- `.gitignore` covering: `.env*` (except `.env.example`), `node_modules/`, `bin/`,
  `obj/`, `dist/`, `.aws-sam/`, `cdk.out/`, `*.pem`, `*.log`, `.DS_Store`, coverage output
- `.gitattributes` for consistent line endings
- `README.md` present from commit #1 and updated as the project grows —
  **not written all at once at the end**. Reviewers can see the timestamps.
- `AI-USAGE.md` present from early on and appended to progressively, for the same reason.
- `docs/adr/` — one short Architecture Decision Record per significant choice
  (see `docs/03-ARCHITECTURE-RULES.md` §7)
- GitHub Actions CI running lint + tests on push (a green CI badge is cheap credibility)

---

## 7. Pacing

Do not create the entire history in one burst at the end. Commit timestamps are
visible. Work in real sessions, commit as you actually progress, and let the history
reflect genuine development. This costs nothing extra and directly satisfies what the
client asked for.

---

## 8. When you are unsure

If you are about to do anything that rewrites, deletes, or collapses history — **stop
and ask Osama first**, and explain the risk. There is no history operation in this
project urgent enough to do without confirmation.
