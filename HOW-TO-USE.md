# How to use this rules pack

## 1. Place the files

Create the project repo first, then copy these in:

```
gweb-merchant-onboarding/
├── CLAUDE.md                      ← Claude Code loads this automatically
├── AI-USAGE.md                    ← required deliverable, filled in progressively
├── HOW-TO-USE.md                  ← delete before submission
└── docs/
    ├── 00-PROJECT-BRIEF.md
    ├── 01-GIT-WORKFLOW.md
    ├── 02-ENGINEERING-STANDARDS.md
    ├── 03-ARCHITECTURE-RULES.md
    ├── 04-SECURITY-RULES.md
    ├── 05-TESTING-RULES.md
    ├── 06-COLLABORATION-PROTOCOL.md
    ├── 07-DELIVERY-CHECKLIST.md
    ├── 08-IMPLEMENTATION-PLAN.md
    ├── assessment/
    │   └── GWEB_Intern_Coding_Assessment.pdf   ← the original PDF
    └── adr/                                     ← created as you go
```

Then:

```bash
git init
git add .
git commit -m "chore: add project rules, assessment brief, and AI usage template"
```

Doing this as commit #1 is itself good evidence — the reviewer sees that constraints
were established before code was written.

---

## 2. Delete before submission

- `HOW-TO-USE.md` (this file)
- The instruction block at the top of `AI-USAGE.md`
- `docs/INTERVIEW-NOTES.md` if you create it (personal prep, not a deliverable)

Keep everything else. `docs/00-*` through `docs/08-*` are legitimate project
documentation and show deliberate process — they help, not hurt.

---

## 3. Kickoff prompt for Claude Code

Paste this as your first message:

```
Read CLAUDE.md and every file in docs/ before doing anything else.
The original assessment PDF is at docs/assessment/.

Then, before writing any code:
1. Confirm you understand the non-negotiable constraints in CLAUDE.md §1.
2. Ask me the kickoff questions in docs/06-COLLABORATION-PROTOCOL.md §3,
   with your recommendation and reasoning for each.
3. Wait for my answers.

Do not scaffold, do not create files, do not commit until I confirm the
Phase 0 plan.
```

---

## 4. Start of every later session

```
Run git status and git log --oneline -15. Tell me where we are, which phase
of docs/08-IMPLEMENTATION-PLAN.md is next, and what is blocked on me.
Then propose the plan for the next phase and wait for my approval.
```

---

## 5. Your own responsibilities (nobody can do these for you)

The client is grading whether **you** understand, review, test, and own the code.

- Read every diff before it is committed. If you cannot explain a line, ask until you can.
- Answer the comprehension questions honestly. "I don't know" → make Claude simplify it.
- Write section 5 and 8 of `AI-USAGE.md` in your own words. Those sections are where a
  reviewer decides whether you actually directed the work or just accepted output.
- Build `docs/INTERVIEW-NOTES.md` as you go. You will be asked about this code.

A smaller, well-understood submission scores higher than a large one you cannot defend —
the brief says this almost word for word in §16.
