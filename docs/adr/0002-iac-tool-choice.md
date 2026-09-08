# ADR-0002: Infrastructure as Code tool — AWS SAM

## Status

Accepted — 2026-09-09

## Context

`docs/03-ARCHITECTURE-RULES.md` §10 requires one IaC tool, justified here, from SAM,
CDK, or Terraform. The stack is deliberately small and Lambda-centric: a handful of
functions, one HTTP API, one DynamoDB table, one S3 bucket. Local simulation
(`sam local start-api`) is the primary development loop per
`docs/06-COLLABORATION-PROTOCOL.md` §3 ("build local-first").

## Decision

AWS SAM, with the template at `infra/template.yaml`. `sam build` uses its native
`dotnet` build support (backed by `dotnet publish` via the Amazon.Lambda.Tools global
tool) — no separate bundler config to maintain. (An earlier version of this ADR
referenced esbuild, from when the backend was TypeScript; see ADR-0001 for why that
changed.)

## Alternatives considered

- **AWS CDK.** More programmatic and testable (CDK has its own assertions library),
  and would share a language with the backend. Rejected for now: it adds an
  abstraction layer (synthesizing to CloudFormation) on top of a stack simple enough
  that hand-written CloudFormation-via-SAM is easier to read end to end in a review,
  and SAM's built-in `sam local start-api` / `sam local invoke` give the fastest
  local-simulation loop without extra CDK-specific tooling.
- **Terraform.** Rejected: strongest for multi-cloud or already-Terraform shops;
  here it would mean maintaining IAM/API Gateway/Lambda wiring by hand that SAM's
  `AWS::Serverless::Function` transform generates for us (event source mappings,
  default execution roles), for no benefit given this is single-cloud, single-service.

## Consequences

- `sam build` + `sam local start-api` is the whole local dev loop; no LocalStack or
  additional emulation layer needed for Lambda/API Gateway.
- IAM is SAM's `Policies:` block on the function resource — least privilege is
  expressed directly in `infra/template.yaml`, not in a separate role-management
  layer, keeping the "can you justify every permission" review (docs/04-SECURITY-RULES.md
  §4) a matter of reading the template top to bottom. Note: since ADR-0001, this is
  **one** function for the whole API rather than one per route, so this is one
  `Policies:` block covering everything the API touches, not several narrowly scoped
  ones — a tradeoff recorded in ADR-0001, not this one.
- Tradeoff accepted: SAM's CloudFormation-based deploys are slower to iterate on than
  CDK's diff-based deploys for large stacks. Not a real cost here given the stack's
  size, but would need revisiting if this grew well beyond Tier-1 scope.
