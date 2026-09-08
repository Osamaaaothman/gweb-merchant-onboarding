# 00 — Product Brief

Working summary of pinned product decisions. If this conflicts with a direct
instruction from Osama, **flag the conflict — do not silently pick one.**

---

## 1. What this is

A commercial **ERP-Lite / accounting system** sold to businesses in **Saudi Arabia**
first, then other Gulf markets (UAE, Oman later).

- **Not** an internal system for one company — it is a product with many customers.
- **General-purpose**, not industry-specific. Trade/distribution is the primary use
  case today; manufacturing and retail are future tiers.
- Serves **small, medium, and large** companies from one codebase — no separate
  edition per size.
- **Arabic and English from day one.** Full RTL and LTR. Not a later translation pass.

---

## 2. Delivery model — **SaaS primary, single-tenant deployable**

> **This supersedes the earlier "on-premise / single-tenant only" decision.**
> Osama has confirmed the product is a **SaaS**.

### What that changes
| Was (on-prem) | Now (SaaS) |
|---|---|
| One database per customer install | **Multi-tenant platform** — see `docs/03-MULTI-TENANCY-RULES.md` |
| License key + activation middleware | **Subscription + entitlements**; license keys are obsolete for the SaaS path |
| Customer runs upgrades | **We run migrations** across all tenants, zero-downtime |
| Customer owns backups | **We own backup, restore, RPO/RTO, and data residency** |
| Customer holds their own ZATCA keys | **We custody tenant cryptographic material** — a serious new security obligation |
| Docker Compose per customer | Compose stays for local dev; production needs a real deployment target |

### What is preserved
The Gulf enterprise market still contains buyers who will demand an on-premise
install. Therefore:

- The application must remain **deployable as a single-tenant instance** with no code
  changes — only configuration (`DEPLOYMENT_MODE=saas | single_tenant`).
- This is achieved by making tenancy an **infrastructure concern behind one boundary**,
  not by sprinkling `if (saas)` through the code.
- In single-tenant mode there is exactly one tenant row; everything else is identical.

**Do not build two codebases. Do not fork.**

---

## 3. Architecture pattern

**Modular Monolith.** Not microservices. One deployable API, strong internal module
boundaries.

```
repo/
├── apps/
│   ├── api/                 NestJS — composes all modules into one API
│   └── web/                 React + TypeScript + Vite
├── packages/
│   ├── core/                Fixed platform — the five contracts live here
│   ├── modules/
│   │   ├── inventory/
│   │   ├── purchasing/
│   │   └── sales/
│   ├── compliance/
│   │   ├── contract/        The country-agnostic interface
│   │   └── zatca-sa/        Saudi implementation
│   ├── shared/              Money, dates, errors, result types, logging
│   └── db/                  Prisma schema, migrations, seeds
├── docker/
└── docs/
```

Rationale for monolith over microservices: a financial ledger needs **transactional
consistency across modules** (an invoice, its stock movement, and its journal entry
must commit together). Distributed transactions here buy nothing and cost correctness.

---

## 4. Core (fixed platform)

- Tree-structured **Chart of Accounts**, customisable per tenant
- **Posting engine** — automatic double-entry
- Multi-company / multi-branch, **multi-currency** with exchange rates
- Central **permissions** at screen and action level
- Configurable **approval workflows**
- Full **audit trail** on every create/update/delete
- Central, per-tenant configurable **document numbering**
- Core financial reports: **balance sheet, income statement, cash flow**
- *(SaaS additions — see §8)* tenant management, subscriptions/entitlements,
  background job runner, notification service

---

## 5. The five integration contracts (mandatory for every module)

No module implements these itself. Ever.

1. `IAccountingEngine` — modules request a posting; they never write to the ledger
2. `IPermissionService` — same roles and users as Core
3. `INumberingService` — every document gets its number from the central generator
4. `IAuditLogger` — one mechanism for all audit records
5. **Unified reporting layer** — module data appears in the general financial reports,
   never in an isolated report

Details and the module checklist: `docs/02-ARCHITECTURE-RULES.md`.

---

## 6. Scope — Tier 1 only

### Inventory
- Internal purchase request from inventory staff, status: pending / processed / rejected
- Item ↔ warehouse binding, reorder point with automatic alert
- Receive purchase orders → update quantity → post journal entry (inventory increase)
- Inventory valuation — **PENDING DECISION** (FIFO vs weighted average). Design the
  schema so it supports both; see `docs/01-OPEN-DECISIONS.md`
- Stock count adjustments (physical vs book difference)

### Purchasing
- Receive purchase requests from Inventory
- Supplier and price selection, with multi-supplier comparison later
- Formal Purchase Order linked to supplier, item, price
- Optional approval path above a configurable amount
- On receipt: simplified **3-way matching** (PO ↔ receipt ↔ invoice) before final approval

### Sales & Accounts Receivable
- Customer request intake (manual for now) → **Quotation**
- Quotation → Sales Order → **formal Invoice**
- The invoice is part of the accounting cycle — **not a detached PDF**
- The invoice passes through the compliance pack (ZATCA) before it is final
- Email quotation/invoice as a **bilingual (AR/EN) PDF**
- Collections tracking, customer statement, **aging report**

### Compliance pack — ZATCA (Saudi Arabia)
See `docs/06-COMPLIANCE-PACK-RULES.md`. Mandatory in Tier 1.

### Tier 2 (not now)
Full accounts payable, payroll, fixed assets, POS, UAE/Oman compliance packs.

### Tier 3 (not now)
Manufacturing/BOM, budgeting and financial planning, advanced BI, external integrations.

---

## 7. Fixed tech stack — **do not propose alternatives**

| Layer | Choice |
|---|---|
| Backend | **NestJS** (TypeScript, `strict`) |
| ORM | **Prisma** |
| Database | **PostgreSQL** |
| Frontend | **React + TypeScript + Vite** |
| UI library | **PrimeReact** (RTL support, DataTable/TreeTable for CoA and statements) |
| State | **Zustand** (local UI state) + **TanStack Query** (server state) |
| Forms | React Hook Form + **Zod** |
| i18n | **i18next** |
| Backend validation | class-validator / class-transformer |
| Logging | **Pino** |
| Local dev deployment | **Docker Compose** (api + db + web) |

---

## 8. Additions required by the SaaS model

These are **not** stack replacements — they are components the SaaS model requires that
the original plan did not have. Each needs Osama's approval before it is introduced
(`docs/13-COLLABORATION-PROTOCOL.md`):

| Need | Why it is unavoidable | Proposed |
|---|---|---|
| Background job queue | ZATCA B2C reporting within 24h, retries with backoff, email sending, aging report generation, month-end jobs. HTTP requests cannot own these. | **BullMQ + Redis** |
| Tenant provisioning pipeline | A new customer must get a schema, seeded CoA, admin user, numbering series, fiscal calendar — reproducibly | Core `tenant-provisioning` service |
| Subscription & entitlements | Replaces the obsolete license-key mechanism; gates modules and limits per plan | Core `billing` module |
| Secret custody for ZATCA keys | We now hold customers' cryptographic stamp private keys | KMS / envelope encryption — see `docs/09-SECURITY-RULES.md` §5 |
| Backup / restore / PITR | We own customer financial data | Managed Postgres with PITR |
| Observability | Multi-tenant incidents must be traceable to a tenant | Structured logs + metrics + traces, `tenantId` on everything |
| Data residency | Saudi PDPL and customer procurement will ask where data lives | **PENDING DECISION** — see `docs/01-OPEN-DECISIONS.md` |

---

## 9. Regional requirements that are easy to miss

Treat these as functional requirements, not polish:

- **Timezone:** `Asia/Riyadh` default per tenant; all timestamps stored in UTC,
  rendered in tenant timezone. Fiscal period boundaries use **tenant local time**.
- **Hijri calendar:** Saudi business documents commonly show both Gregorian and Hijri
  dates. Store Gregorian; render both. Do not compute Hijri by hand — use a library.
- **Arabic-Indic numerals** (٠١٢٣): a display preference, never a storage format.
- **Arabic is legally required on Saudi tax invoices.** The invoice PDF and the ZATCA
  XML must carry Arabic content, regardless of the user's UI language.
- **VAT:** standard rate, zero-rated, exempt, and out-of-scope are **different**
  treatments with different ledger and reporting consequences. Model them as distinct
  tax treatment codes, never as "rate = 0".
- **Currency:** SAR base for Saudi tenants, but multi-currency is Core. Every foreign
  currency transaction stores the amount, the currency, the rate used, **and the rate
  date** — the rate is snapshotted at transaction time, never re-derived later.
