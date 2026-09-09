# ADR-0004: MCC catalog storage and refresh strategy

## Status

Accepted — 2026-09-09

## Context

`docs/00-PRODUCT-BRIEF.md` requires MCC codes to come from a **real** catalog, kept
strictly separate from risk policy (Phase 6), and asks for a documented refresh
process. `docs/03-ARCHITECTURE-RULES.md`'s DynamoDB expectations explicitly allow
either "DynamoDB or packaged static data, if justified" for this one access pattern.
ADR-0003 deferred the actual choice here.

**On the data source:** the brief names the Visa Merchant Data Standards Manual as the
reference source. That manual is a paid/licensed document; this session has no access
to it. Merchant Category Codes are, in practice, a de facto industry-standard taxonomy
— the same ~4-digit code/description pairs recur nearly verbatim across Visa,
Mastercard, and IRS-published references, because the networks converged on one shared
list decades ago. The catalog shipped here (`tools/McCatalogImport/source/mcc-codes-source.psv`,
276 codes) was compiled from that well-established public reference, not scraped from
the licensed manual. **This is a deliberate, flagged substitution** (the same category
of honest substitution as the .NET 9→10 decision in ADR-0001), not a silent gap: the
exact code-to-description text has not been cross-checked word-for-word against the
actual paid manual, and should be before this catalog is relied on for real
underwriting. Every code required by the assessment's own acceptance criteria is
present and verified by name — see "What's verified" below.

## Decision

**Packaged static data**, not DynamoDB, for the catalog itself.

### Why not DynamoDB

- The catalog is read-only reference data with no per-application state and no writes
  in this system's lifetime — it doesn't need a database's durability or consistency
  guarantees, it needs a lookup table.
- "Sensible search behavior" (the Phase 5 gate) means matching a partial code or a
  substring of a description. DynamoDB has no free-text search primitive; a GSI keyed
  on description keywords (sketched as the fallback in ADR-0003's access-pattern table)
  would need either a rigid keyword-tokenization scheme or a second search service
  (OpenSearch) — real infrastructure for a ~300-row table that fits in memory with room
  to spare.
- Every DynamoDB read costs an IAM permission, a possible throttle, and a network
  round-trip inside the 45-second budget for zero benefit over an in-memory list here.
- No extra AWS resource means no extra `infra/template.yaml` change, no extra IAM
  statement to justify at the Phase 13 audit.

### Why packaged (not fetched live from anywhere at runtime)

The whole point of "packaged" is that a cold Lambda execution environment has the
catalog the instant it starts, with no network dependency and nothing to time out on.
`IMccCatalog` (`src/Gweb.Domain/Mcc/IMccCatalog.cs`) is deliberately synchronous and
takes no `DeadlineBudget` — see that file's doc comment for why forcing an async/budget
shape onto a call that can never do I/O would be dishonest ceremony, not correctness.

### Shape

- `src/Gweb.Adapters.Mcc/Resources/mcc-codes.json` — an embedded resource (baked into
  the assembly, so it ships with the Lambda zip automatically, no separate deployment
  step to forget).
- `StaticMccCatalog` (`Gweb.Adapters.Mcc`) loads it once at construction (Lambda cold
  start), builds an in-memory `Dictionary<code, MccCode>` plus a code-ordered list, and
  serves every `Search`/`GetByCode` call from memory.
- Registered as a singleton in `Program.cs`, **not** gated by `PERSISTENCE_PROVIDER`
  (unlike every DynamoDB-backed repository) — it involves no AWS resource in either
  branch, so there is nothing to switch.

### Refresh process

The refresh path is real and was actually run, not just described:

1. Edit `tools/McCatalogImport/source/mcc-codes-source.psv` (pipe-delimited —
   deliberately not comma-separated CSV, since MCC descriptions routinely contain
   commas and this avoids all quote-escaping edge cases).
2. Run `dotnet run --project tools/McCatalogImport`.
3. The tool validates every row (4-digit numeric code, non-empty description/category,
   no duplicate codes — fails loudly with the exact line number on any violation,
   rather than silently emitting bad data) and regenerates
   `src/Gweb.Adapters.Mcc/Resources/mcc-codes.json`, sorted by code.
4. Commit the regenerated JSON alongside the source-file change, review the diff like
   any other change, `dotnet test`.

No code change is required to refresh the catalog — only the source file and a
re-run of the tool. This is documented in the README under "Refreshing the MCC
catalog."

### Search ranking

`StaticMccCatalog.Search(query, limit)`: empty query returns the first `limit` codes
ordered by code (a browsing default). A non-empty query ranks exact code match, then
code-prefix match, then case-insensitive description-substring match, each group
ordered by code — so typing "60" surfaces 6010/6011/6012 ahead of an unrelated code
whose description happens to contain "60" somewhere.

## What's verified

- **The three codes Phase 6's risk policy must demonstrate enhanced review for**
  (6012, 6051, 6211, per `docs/08-IMPLEMENTATION-PLAN.md` Phase 6) are present in the
  catalog and covered by `StaticMccCatalogTests.ContainsEveryMccThePhase6RiskPolicyMustBeAbleToClassify`
  — a real, run test, not an assumption Phase 6 will discover is false.
- The import tool was actually executed against the checked-in source file (not
  simulated): it reported "Wrote 276 MCC codes" with zero validation failures.
- Every code is unique and exactly 4 digits (`StaticMccCatalogTests.EveryCodeIsUniqueAndFourDigits`).
- Search ranking, the empty-query browsing default, the limit cap, and the HTTP
  endpoint are all covered by real, run tests (`StaticMccCatalogTests`,
  `McCatalogServiceTests`, `McEndpointTests`).

## Consequences

- If the catalog ever needs to grow beyond a few thousand rows, or needs real-time
  updates without a redeploy, this decision should be revisited — packaged static data
  stops being the right call at that scale. Not a concern for ~300 rows of reference
  data that changes on the order of "the networks publish an update," not per request.
- A future DynamoDB-backed `IMccCatalog` implementation is a drop-in replacement behind
  the existing interface (same seam every other repository in this codebase uses) --
  the interface would need to become async and budget-aware at that point, which is a
  real signature change, not just a new class, and should be made deliberately, not
  silently.
