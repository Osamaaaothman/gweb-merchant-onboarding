# ADR-0003: DynamoDB table strategy — single table

## Status

Accepted — 2026-09-09

## Context

`docs/03-ARCHITECTURE-RULES.md` §4 asks for the access-pattern table to be written
*before* the code, and says reviewers "care about access patterns, not fashion." The
access patterns below are the union of what the assessment brief and
`docs/00-PRODUCT-BRIEF.md` actually require — not a speculative full schema.

Every access pattern in this system is scoped to a single application (`applicationId`)
except one (MCC catalog search, which is global and read-only). That shape —
one hot partition key per aggregate, several item types living under it — is the
textbook case for a single DynamoDB table.

## Decision

**Single table**, physical name `gweb-applications-{stage}` (already created in
`infra/template.yaml` since Phase 1). Generic `pk`/`sk` string keys; an `entityType`
attribute on every item disambiguates what it is.

### Access patterns

| # | Access pattern | PK | SK | Index | Consistency | Phase built |
|---|---|---|---|---|---|---|
| 1 | Load application by ID | `APP#{applicationId}` | `META` | base table | strong | **2 (this one)** |
| 2 | List people/owners for an application | `APP#{applicationId}` | `PERSON#{personId}` | base table | strong | 3 |
| 3 | List documents for an application | `APP#{applicationId}` | `DOC#{documentId}` | base table | strong | 4 |
| 4 | Fetch current evaluation | `APP#{applicationId}` | `EVAL#CURRENT` | base table | strong | 8 |
| 5 | Find submission status | (same item as #1 — `status` attribute on `META`) | — | base table | strong | 9 |
| 6 | Query MCC catalog by code/keyword | `MCC#{code}` (or static asset — see ADR-0004) | `MCC#{code}` | `GSI1` (`GSI1PK=MCC`, `GSI1SK={description keywords}`) or packaged static data | eventual | 5 |

`Query(pk = APP#{applicationId})` with no further filter returns the whole
application aggregate — application metadata, people, documents, current evaluation —
in one strongly-consistent call. This is the shape every later phase builds on;
nothing here is speculative beyond what's already specified in the brief.

### Item shape (every item, per docs/03-ARCHITECTURE-RULES.md §4)

Every item carries: `pk`, `sk`, `entityType`, `version`, `createdAt`, `updatedAt`,
`createdBy` (actor/source), `correlationId`. The `META` item additionally carries
`applicationId` (denormalized — same value as the PK's suffix, but readable without
parsing the key) and `status`.

### Optimistic concurrency and idempotency (this phase)

- **Create** (`POST /v1/applications`): a conditional `PutItem` with
  `attribute_not_exists(pk)`. The application ID is server-generated (a new GUID) so a
  create can never collide by construction — see "Idempotency" below for why this is
  still enough.
- **Update** (not yet — Phase 3): conditional `PutItem`/`UpdateItem` with
  `version = :expectedVersion`, incrementing `version` on write. A mismatch surfaces
  as `409 Conflict` (`ConflictException`), never a silent overwrite.
- **Idempotency:** `POST /v1/applications` takes no client-supplied identity to
  deduplicate against — every call creates a genuinely new application, by design (the
  brief's flow is "start an application," not "upsert one"). Idempotency on a
  *specific* request replay becomes relevant once mutating fields exist (Phase 3's
  `PATCH` endpoints, which will use an `Idempotency-Key`-style conditional write) —
  documented here as a forward pointer, not implemented yet.

## Alternatives considered

- **Multi-table** (a table each for applications, people, documents, evaluations).
  Rejected: every real access pattern above needs data scoped to one application at
  once; a multi-table design would mean N separate `GetItem`/`Query` calls (one per
  table) to assemble what a single `Query` returns here, with no consistency
  guarantee across the N calls without a transaction.
- **DynamoDB Streams-triggered denormalization.** Not needed yet — no derived views
  exist yet that would justify it. Revisit if/when a reviewer/admin screen (Phase 14
  bonus) needs a "list all applications by status" pattern, which a single
  `APP#{id}`-partitioned table cannot serve efficiently without a GSI.

## Consequences

- Every new item type (Person, Document, Evaluation, ...) just needs its own `SK`
  prefix under the same `PK` — no new table, no new IaC resource, per new entity.
- A future "list all applications" or "list by status" access pattern (not currently
  required) would need a GSI (e.g. `GSI1PK=STATUS#{status}`, `GSI1SK=createdAt`) —
  flagged here so it isn't a surprise when a reviewer/admin screen is attempted.
- No scans anywhere in the request path (per docs/03-ARCHITECTURE-RULES.md §4) — every
  pattern above is a `GetItem` or a `Query` against a known `pk` (or `GSI1` for MCC).
