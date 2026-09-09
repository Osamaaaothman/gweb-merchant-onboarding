# ADR-0005: Risk policy representation and versioning

## Status

Accepted — 2026-09-09

## Context

Brief "MCC classification & risk policy" states the core rule this ADR exists to
satisfy: **"MCC taxonomy and risk policy are two separate things. Risk policy must be
changeable without touching the catalog."** It also requires:

- Risk as a configurable policy attribute (`standard` / `enhanced-review` /
  `restricted`), never hard-labeled on an MCC.
- Provider-specific overrides — different acquirers apply different policy to the same
  MCC.
- Enhanced-review demonstrated for MCC 6012, 6051, 6211 (Mastercard guidance).
- **Never auto-approve** a merchant because a model, or anything else, labelled it low
  risk.

`docs/08-IMPLEMENTATION-PLAN.md` Phase 6's gate: "same MCC yields different outcomes
under two provider configs, proven by test."

## Decision

### Separation from the catalog

`IRiskPolicy` (`src/Gweb.Domain/RiskPolicy/IRiskPolicy.cs`) takes a bare MCC code
string and an optional provider id — it has **no reference to `IMccCatalog` or
`MccCode`** anywhere in its signature or implementation. The catalog could be swapped
for a completely different storage strategy (see ADR-0004) without this interface or
`StaticRiskPolicy` changing at all, and vice versa. This is the literal, structural
enforcement of "two separate things," not just a documentation claim.

### Representation: packaged JSON, same mechanism as the MCC catalog, different reason

`StaticRiskPolicy` (`Gweb.Adapters.RiskPolicy`) loads an embedded JSON resource once at
construction — same pattern as `StaticMccCatalog`. The reasoning is different, though,
and worth stating explicitly rather than copy-pasting ADR-0004's justification:

- The MCC catalog is *synced from an external reference* (ADR-0004) — hence the
  `tools/McCatalogImport` pipeline with strict validation.
- Risk policy is *this organization's own business decision* — there is no external
  source to sync from. `src/Gweb.Adapters.RiskPolicy/Resources/risk-policy.json` is
  hand-authored and reviewed like any other code change (a PR diff on the JSON file
  itself is the review artifact), so no separate import tool was built for it — that
  would be process theater for a file a human is meant to read and edit directly.

### Versioning

The policy document has no explicit version field today. "Changeable without touching
the catalog" is satisfied (editing `risk-policy.json` and redeploying never touches
`mcc-codes.json` or vice versa), but this is **not** the same as "changeable without a
deploy" — a Known Gap, stated plainly: a production system handling real underwriting
risk would likely want this behind a config service (SSM Parameter Store, a dedicated
DynamoDB table with its own audit trail) so a policy change is an auditable event
independent of a code deployment, and so a specific policy version can be pinned to a
specific evaluation for compliance replay. That is real, deliberate future work, not
assumed to be out of scope by accident.

### Resolution order

`StaticRiskPolicy.Evaluate(mccCode, providerId)`:

1. If `providerId` is given and that provider has an override rule for exactly that
   MCC, return it.
2. Else, if the base rule set has an entry for that MCC, return it.
3. Else, return the document's own `defaultLevel` (currently `Standard`).

No hardcoded fallback exists anywhere in code outside this one config value — auditable
by reading one JSON file, not grepping for magic strings across the codebase.

### No endpoint yet

The brief's API surface lists no dedicated risk-policy route. `IRiskPolicy` is
registered in DI (`Program.cs`) and ready for Phase 7 (`classify`) and Phase 8
(`evaluate`) to consume once they exist, but this phase adds no `GET`/`POST` for it —
building one now would be an endpoint the brief never asked for, ahead of the feature
that would actually call it.

### Enforcing "never auto-approve" structurally, not just by convention

`NoAutoApprovalPathTests` (`tests/Gweb.Tests/Architecture/`) asserts that no
domain outcome enum (`ApplicationStatus`, `RiskLevel`) ever contains an
"Approved"/"Rejected"-shaped value name. This is a cheap, real test: if anyone adds
`RiskLevel.Approved` or `ApplicationStatus.Approved` in a later phase, this test fails
by name at the exact moment the invariant would first be violated, rather than relying
on a reviewer noticing during a PR skim.

## What's verified

- `StaticRiskPolicyTests.TheSameMccYieldsDifferentOutcomesUnderTwoDifferentProviderConfigs`
  is the Phase 6 gate, proven directly: MCC 6012 evaluates to `EnhancedReview` with no
  provider, `Restricted` under `acquirer-conservative`, `Standard` under
  `acquirer-permissive` — three outcomes, one MCC, purely from configuration data.
- All three brief-named codes (6012, 6051, 6211) are `EnhancedReview` under the base
  policy (`FlagsEveryMccMastercardGuidanceNamesForEnhancedReviewUnderTheBasePolicy`).
- An override on one MCC under a provider doesn't leak onto a different MCC under that
  same provider (`AProviderOverrideOnlyAppliesToTheMccItNames`).
- An MCC with no rule at all falls back to the document default, not an exception or a
  silently-wrong level (`AnMccWithNoRuleAtAllFallsBackToTheDocumentDefault`).
- The no-auto-approval invariant is enforced by a real, run test, not just this
  document's prose.

## Consequences

- Adding a new provider or overriding a new MCC is a JSON edit + redeploy, not a code
  change — satisfies the brief's letter, flagged above as not the strongest possible
  version of "configurable."
- If risk policy needs to change hourly, or needs a change-approval workflow with its
  own audit log, this representation should be revisited in favor of a real config
  service. Not a concern at this assessment's scope.
