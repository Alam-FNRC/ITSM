# FNRC Service Desk (based on dotnet-enterprise-itsm)

Internal ITSM / service desk for FNRC IT, replacing the current custom-built tool.
Owner and decision-maker: William. Claude is the main developer and architect.
Claude proposes; William decides anything that changes scope, data, or security.

## Target platform (decided)
- .NET 10 (upgrade from .NET 8, which goes out of support on 10 Nov 2026).
  Deferred: development stays on .NET 8; the upgrade is a separate step before staging.
- SQL Server (replace PostgreSQL / Npgsql; regenerate migrations).
  Local dev instance: `(localdb)\MSSQLLocalDB` (Windows authentication, no password)
- Login: on-premises Active Directory
- Email: Microsoft 365 (Graph API for sending and for email-to-ticket mailbox polling)
- Languages: English + Arabic with full RTL; no hard-coded UI text
- Front end: keep the existing HTML/JS for now; Blazor decision deferred
- Environments: local dev (William's machine, local SQL Server) → staging → production

## Hard rules
- iTop (AGPL) and GLPI (GPL) are references for ideas and data models only.
  Never copy, translate or paraphrase their source code into this repo.
- Security first: every endpoint requires auth and a permission or ownership check unless explicitly justified.
- No seeded default accounts or passwords; no secrets in appsettings or docker-compose.
- Every change to roles, permissions, group membership, logins and ticket data must be audit-logged; audit rows are append-only.
- Every feature gets tests; access-control rules get integration tests (WebApplicationFactory) against the real pipeline.
- Small, reviewable commits; one concern per commit.

## Roadmap
1. Security hardening (critical findings from the Oct 2026 review)
2. .NET 10 + SQL Server + AD login + Arabic/RTL
3. Audit trail and ISO 27001 KPI reports (monthly, PDF export)
4. ITIL modules: Problem → Change/CAB → CMDB/assets (from the iTop/GLPI feature spec)
5. Pilot alongside the old tool, then data migration and go-live

## Project documents (read before starting a phase)
- `docs/feature-spec.md` — what to build, priorities and phases
- `docs/review/2026-10-code-review.md` — security and quality defects in the original code, with file paths (Phase 1 and 3 checklist)
- `docs/reference/itop-model-notes.md`, `docs/reference/glpi-feature-notes.md` — clean-room notes on how iTop and GLPI model ITIL; concepts only

## Why
ISO 27001 auditors ask yearly for service desk KPIs and evidence the old tool cannot produce.
An IT governance ISO certification is also being pursued.
