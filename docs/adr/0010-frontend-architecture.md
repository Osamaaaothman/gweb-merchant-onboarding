# ADR-0010: Frontend architecture

## Status

Accepted -- 2026-09-09

## Context

`docs/08-IMPLEMENTATION-PLAN.md` Phase 11 asks for a multi-step form with save/resume,
upload progress against pre-signed URLs, MCC suggestion confirm/correct UI, a final
review screen, masked display of sensitive values, and clear error recovery -- the
brief's "Client" row (`docs/03-ARCHITECTURE-RULES.md`'s architecture table) lists this
as required infrastructure, not a bonus item (bonus item #1 is specifically "polished"
UX on top of this baseline). Framework, state management, and design-system choices
were discussed with Osama before writing any code, per `CLAUDE.md` §4.

## Decision

### Vite + React + TypeScript, not Next.js

This is a single-purpose SPA calling a REST API the backend already fully owns --
routing, data-fetching, and rendering strategy don't need a meta-framework's server
runtime, file-based API routes, or React Server Components, none of which this project
uses. Next.js's real advantages (SSR for SEO, server-rendered data fetching, image
optimization) don't apply to an internal underwriting-intake tool with no public pages
to index. A plain Vite SPA is lighter, has a simpler mental model to defend in review
("why did you pick X" per the brief's evaluation criteria), and builds/dev-serves
faster. `frontend/` is its own npm project, deliberately not wired into the .NET
solution -- it is a separate deployable artifact (a static bundle) with its own
toolchain, same relationship a real production frontend/backend split would have.

### State: TanStack Query for server state, Zustand for UI-only state, react-hook-form + zod for forms

Nearly everything in this app *is* server state -- the backend already persists every
field via PATCH, so the frontend's job is mostly "fetch current state, render it,
PATCH on submit," not maintaining its own source of truth. TanStack Query owns that
(`src/hooks/use-application.ts`): caching, invalidation-on-mutation, loading/error
states, no hand-rolled `useEffect` data-fetching. Zustand (`src/stores/wizard-store.ts`,
`toast-store.ts`) is reserved for state that is genuinely client-only and has no
server representation -- which step-transition direction to animate, the toast queue --
never a shadow copy of server data that could drift from it. react-hook-form + zod
(`src/lib/validation.ts`) mirror the backend's own field-by-field validation rules
(`Applicant.cs`/`Business.cs`'s `ApplyUpdate` validators) as closely as two different
languages allow, directly answering the brief's "client validation mirroring server
rules" -- the server remains the actual source of truth; this is a close mirror, not a
shared implementation.

### shadcn/ui (hand-authored, not the CLI) + Tailwind v4, Fraunces + Inter

Tailwind + shadcn's copy-in-your-repo component model (Radix primitives underneath) was
chosen over an all-in-one component library (Mantine, Chakra) because the code stays
fully owned and readable by "another engineer" per the brief's production-mindset note
-- no opaque package boundary to work around later. The `shadcn@latest` CLI (v4.21.0)
was tried first and abandoned after two real, reproducible bugs on this machine: `init`
failed with "Could not load the workspace config" after writing a valid
`components.json`, and the one `add` command that did partially succeed wrote its
output to a literal `./@/` directory instead of resolving the `@/*` path alias to
`src/`. Every `src/components/ui/*.tsx` primitive here was hand-authored instead,
following the same well-documented "new-york" style shadcn's own registry publishes --
not a shortcut, the standard approach with a broken CLI worked around rather than
fought. Color palette: a warm ivory/near-black base (not stark white) with a deep
royal-indigo primary and a champagne-gold accent reserved for premium/success moments
(the confirmed-MCC badge, the submission celebration) -- deliberately not the generic
blue-on-white SaaS look, aimed at Osama's ask for a "business," premium feel without
resorting to a dark-mode-only aesthetic that would hurt long-form-fill readability.
Fraunces (a serif display face) for headings against Inter for body text is the one
typographic choice doing most of the "this feels considered" work -- a detail most
generic admin-panel UIs skip.

### Animation: motion (Framer Motion), used narrowly

Deliberately restrained per Osama's explicit ask ("مرتب ما يكون قوي" -- tidy, not
overpowering): step transitions slide+fade (`AppShell.tsx`), the sidebar's checkmarks
spring in and the connector line animates its fill on step completion
(`StepSidebar.tsx`), classification confidence bars animate their width in
(`ClassificationStep.tsx`), toasts spring in/out (`Toaster.tsx`) -- and exactly one
deliberately bigger moment, the post-submit celebration (`ReviewStep.tsx`'s
`SubmittedCelebration`), reserved for the one genuinely celebratory event in the whole
flow rather than spread thin across every button click.

### A real gap found while building this: no way to list an application's documents

Phase 9's `SubmissionService` already used `IDocumentRepository.ListByApplicationIdAsync`
internally, but no HTTP route ever exposed it -- the only client-facing document routes
were presign, complete, and get-by-ID. A resumed session (the brief's explicit "preserve
state so a partially completed application can be resumed" requirement) had no way to
discover which documents were already uploaded. Closed by adding
`GET /v1/applications/{id}/documents` (`src/Gweb.Api/Documents/DocumentEndpoints.cs`,
`DocumentService.ListDocumentsAsync`), following the same 404-on-unknown-parent
convention every sibling endpoint already uses, with its own test coverage
(`DocumentEndpointTests`). A backend gap discovered by building the frontend that
actually needed it -- not scope creep, the same pattern as ADR-0008's document-listing
addition for the submission gate.

### Real end-to-end verification: MinIO + DynamoDB Local, not just the in-memory adapters

`PERSISTENCE_PROVIDER=inmemory` (used for the automated backend test suite and quick
local iteration) returns a fake `https://in-memory-storage.invalid/...` presign URL --
nothing a real browser could ever POST to. To verify the frontend's upload flow for
real, this session stood up `minio/minio` and `amazon/dynamodb-local` in Docker and ran
the backend against them (`PERSISTENCE_PROVIDER=dynamodb`, `S3_SERVICE_URL`,
`DYNAMODB_SERVICE_URL` -- the exact local-endpoint override mechanism
`docs/adr/0003`/README already documented, just exercised for the first time this
session). This is the first genuinely real S3 upload in this project's history --
closes the README Known Gap that previously read "no real S3 bucket has ever received
an actual uploaded byte in this session." One real limitation hit and worked around:
this MinIO version's bucket-CORS API (`mc cors set`) rejected every
`AllowedOrigin`/`AllowedHeader` combination tried with "functionality that is not
implemented," so cross-origin browser uploads to `localhost:9000` would have been
blocked. Worked around with a Vite dev-server proxy (`vite.config.ts`) plus a small
same-origin URL rewrite in `uploadToPresignedUrl` (`src/lib/api-client.ts`) that only
ever matches a local MinIO URL -- documented inline as local-dev-only, since a real
deployed frontend talking to real S3 (with CORS configured directly on the bucket)
would never hit this rewrite at all.

## What's verified

- Full real journey driven through the actual rendered UI (Browser tool, not just API
  calls): create application -> fill applicant -> fill business -> upload all three
  required documents through the real presign -> MinIO upload -> complete flow ->
  classify -> confirm MCC -> run evaluation -> submit. Confirmed via
  `GET /v1/applications/{id}` afterward that the backend genuinely shows
  `"status":"Submitted"`.
- The three required document uploads went through real SigV4-signed presigned POST
  requests against MinIO (verified via raw `curl`, matching exactly what the browser's
  `uploadToPresignedUrl` sends) and real `complete()` checksum/size/signature
  verification against the actually-stored bytes -- not simulated.
- Mobile viewport (375px) checked directly; one real layout bug found and fixed (the
  document status badge visually overlapped a wrapped two-line document title on
  narrow screens -- `DocumentsStep.tsx`'s `DocumentCard` restructured to stack instead
  of overlap below `sm:`).
- `npx tsc -b --noEmit` and `npm run build` both clean.
- Backend: `dotnet build` clean, `dotnet test` 395/395 passing (up from 392 -- three new
  `ListDocumentsAsync` endpoint tests).

## Consequences

- The frontend has no automated test suite of its own yet (no Vitest/Playwright) --
  verification for this phase was real manual/scripted end-to-end testing (browser +
  curl), not repeatable automated coverage. A production version of this frontend
  would want component/integration tests; out of scope for this phase given the
  brief's testing requirements are scoped to the backend (`docs/05-TESTING-RULES.md`).
- The Vite-proxy MinIO CORS workaround is explicitly local-dev-only and documented as
  such at both the code site and here -- it must not be mistaken for something a real
  deployment needs.
- The production bundle is a single ~685KB (215KB gzipped) JS chunk -- unsplit. Fine at
  this app's current size (one route tree, no heavy third-party visualization
  libraries); flagged, not fixed, since code-splitting would be premature optimization
  for an app this size within this assessment's scope.
