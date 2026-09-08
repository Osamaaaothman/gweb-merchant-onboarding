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

AWS SAM, with the template at `infra/template.yaml` and per-function esbuild bundling
via `Metadata.BuildMethod: esbuild` (native SAM CLI support for TypeScript Lambdas —
no separate bundler config to maintain).

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
- IAM roles are SAM's per-function `Policies:` blocks — least privilege is expressed
  directly next to the function that needs it, not in a separate role-management
  layer, which keeps the "can you justify every permission" review (docs/04-SECURITY-RULES.md
  §4) a matter of reading `infra/template.yaml` top to bottom.
- Tradeoff accepted: SAM's CloudFormation-based deploys are slower to iterate on than
  CDK's diff-based deploys for large stacks. Not a real cost here given the stack's
  size, but would need revisiting if this grew well beyond Tier-1 scope.
