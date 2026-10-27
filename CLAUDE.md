# FNRC Service Desk (based on dotnet-enterprise-itsm)

Internal ITSM / service desk for FNRC IT, replacing the current custom-built tool.
Owner and decision-maker: William. Claude is the main developer and architect.
Claude proposes; William decides anything that changes scope, data, or security.

## Target platform (decided)
- .NET 10 (upgrade from .NET 8, which goes out of support on 10 Nov 2026).
  Deferred: development stays on .NET 8; the upgrade is a separate step before staging.
- SQL Server. Done Oct 2026: Npgsql removed, migrations regenerated as one SQL Server `InitialCreate`.
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

## Local development
- Needs: .NET 9 SDK (pinned in `global.json`; it builds the net8.0 projects), .NET 8 runtime, SQL Server LocalDB, Node 22 for front-end tests.
- `dotnet tool restore` installs `dotnet-ef` (pinned in `.config/dotnet-tools.json`).
- `dotnet run --project src/ItsTool.API --launch-profile http` starts the API on http://localhost:5246. In Development it applies migrations and seeds demo data into `FnrcItsm_Dev`.
- Login tokens are signed with `Jwt:Secret`. Without one, Development uses a random key per run, so you log in again after each restart; to keep logins, run `dotnet user-secrets set Jwt:Secret "<32+ random characters>" --project src/ItsTool.API`. Every other environment refuses to start without a secret.
- `dotnet test ItsTool.sln` runs unit tests (EF in-memory) and integration tests (real SQL Server; each run creates and drops a `FnrcItsm_Test_*` database; set `ITSM_TEST_SQL` to use another server).
- `node --test tests/frontend/*.test.js` runs the front-end tests.
- Schema change: `dotnet ef migrations add <Name> --project src/ItsTool.Infrastructure --startup-project src/ItsTool.API`. A test fails if the model and the migrations drift apart.
- The in-memory provider hides SQL translation errors and ignores constraints: anything that changes a query needs an integration test.
- Access-control tests go in `tests/ItsTool.IntegrationTests/Api/`: `ApiFixture` starts the real API on its own test database; `CreateUserAsync(permissions)` + `LoginAsync(user)` give a client with a real token.

## Roadmap
1. Security hardening (critical findings from the Oct 2026 review).
   The SQL Server switch from phase 2 was done first (approved Oct 2026) so security tests run against a real database.
2. .NET 10 + SQL Server (done) + AD login + Arabic/RTL
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
