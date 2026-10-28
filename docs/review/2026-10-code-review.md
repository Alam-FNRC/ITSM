# Code review of the original repo (18 Oct 2026)

Static review of dotnet-enterprise-itsm as received (not built or run). Paths are relative to `src/`. Use this as the checklist for Phase 1 (security) and Phase 3 (audit trail).

## Fix status

Each fix has integration tests in `tests/ItsTool.IntegrationTests/Api/` that run against the real API and SQL Server.

| # | Status | Fixed by |
|---|---|---|
| A1 | Fixed | Attachment downloads need a login and access to the ticket (`b6ed559`); the ticket page fetches attachments with the token (`2f2bdeb`) |
| A2 | Mitigated | AI copilot off by default (`AI:Enabled`, `4d3009a`): every AI endpoint returns 404. Must be fixed before turning AI on: admin-only AI config, endpoint allow-list, key handling |
| A3 | Fixed | Every ticket endpoint is limited to tickets the caller can see (`b6ed559`); editing needs ticket.edit or ticket.assign; internal notes need ticket.comment.internal (`9c5f13e`) |
| A4 | Mitigated | Same as A2. Must be fixed before turning AI on: ticket access check on AI routes, keep internal notes out of prompts |
| A5 | Fixed | Debug and seed endpoints removed, e-mail ingest needs admin.manage, the AI status needs a login, and a login is required by default (`657f3c2`, `4d3009a`) |
| A6 | Fixed | Password change needs the current password; one policy (12-64 characters) for every password; admin-set passwords must be changed; the API enforces the change (`4e377d6`) |
| A7 | Fixed | The API will not start with a missing, short or published JWT key; no defaults in appsettings or docker-compose (`a9e1124`) |
| A8 | Fixed | No seeded accounts or startup role grants; the first admin comes from Bootstrap settings and must change the password (`011e7d6`) |
| A9 | Fixed | Exact login match and unique usernames and e-mails (`7ef2a29`), login audit in AuthEvents (`f061746`), account lockout (`aa0a596`), per-IP login rate limit (`6ab77d7`) |
| A10 | Mostly fixed | Every request is checked against the current account: roles and permissions are reloaded, deactivated users and sessions before a password change are refused (`242ce3c`). Still open: the token is kept in browser localStorage |
| A11 | Fixed | Server-made file names, server-chosen content types, no .svg, file signature checks (`f4d264f`) |
| A12 | Fixed | The hub reads the token from the query string (`f38c7ea`) |
| A13 | Fixed | The repo was started with fresh history, without the uploaded files; `uploads/` is git-ignored |
| — | Fixed | Found during the fixes: the ticket timeline returned internal note text to requesters (`500aad4`) |
| — | Fixed | Found during the fixes: attachment responses included the server's file path (`314bdee`) |
| Audit | Fixed | Restoring a comment no longer deletes the history of its deletion (`bdeb3d5`) |
| Audit | Partly fixed | Logins and password changes are in AuthEvents (`f061746`). Role assignments, role permissions and permission overrides are audited with names and the acting administrator (`4682777`). The application refuses to change or delete audit rows (`14e94d0`). Still open (Phase 3): protection at the database level, tamper evidence (hash chain), IP address and request ID on audit rows |
| — | Fixed | Found during the fixes: deny permission overrides were ignored, so a role's permission could not be taken away from one user (`5380920`) |
| — | Fixed | Found during the fixes: stored script injection. Pages inserted user text (ticket titles, survey comments, names, KB article HTML, notification text) as HTML or inside inline handlers, so any user could run script in an administrator's browser and take the login token. The pages now escape or sanitize it (`8978c67`, `cfed0a6`..`64c94c0`), DOMPurify is updated to 3.4.17 (`eac6682`), ticket colours must be #RRGGBB (`d2f4445`) and notification e-mails encode user text (`da056fe`) |
| — | Fixed | Found during the fixes: the survey and knowledge-base article pages put `?id=` from the address into API paths, so a crafted link sent their request to another endpoint (verified: rating a ticket through `survey.html?id=../users/2/roles/1?x=` gave user 2 SuperAdmin). Only numeric ids are accepted (`ca3eee9`) |
| — | Fixed | Found during the fixes: CSV exports (tickets, audit log, ticket logs) wrote user text as it was, so a value starting with `=`, `+`, `-` or `@` ran as a spreadsheet formula; such cells now start with an apostrophe (`40328e1`) |
| — | Fixed | Found during the fixes: the ticket CSV export read the user id from a claim the token does not have and returned no rows for anyone (`a89a785`) |

# Part 1: Security and data layer

I rate this repo **not production-ready for an ISO 27001 environment** in its current state. It is a workable prototype, but before go-live you would need to close several Critical access-control holes, fill the audit trail gaps and move to SQL Server. Everything below comes from reading the code. Nothing was built or run, so the runtime-behaviour findings are inferred from how EF Core and ASP.NET Core work, not tested. Paths are relative to ``.

## 1. Authentication and authorization

| # | Sev | Finding |
|---|---|---|
| A1 | **Critical** | **Attachments can be downloaded with no login.** `ItsTool.API/Controllers/TicketController.cs:281` marks the download endpoint as anonymous. It is reached by guessable sequential IDs, and `TicketService.cs:787-796` does no access check. |
| A2 | **Critical** | **Any logged-in user can change where AI requests go.** No permission is needed to redirect the LLM endpoint globally (`AiController.cs:79-87`, static override `LlmService.cs:100`). Sending a masked `***` key keeps the real API key (`LlmService.cs:111-121`), so the key and all ticket text then go to that new endpoint. `GET /api/ai/models?endpoint=` (`AiController.cs:89`, `LlmService.cs:172,206-216`) also sends the stored key to any URL the caller names, and lets them make the server call arbitrary URLs (SSRF). |
| A3 | **Critical** | **Ticket access is not limited per user (IDOR).** Search is scoped to the user (`TicketQueryHelpers.cs:14-36`). But `GetTicket` (`TicketController.cs:46-55`, `TicketService.cs:186`) only needs the general `ticket.view` permission. Update (`:110`), comment (`:201`), upload (`:260`), timeline and eligible-users have **no permission or ownership check at all**. Any user can edit any ticket's title, description or priority. `IsInternal` is taken straight from the client (`:205`), so any user can post internal notes. |
| A4 | **Critical** | **AI endpoints leak internal notes for any ticket.** `AiController.cs:143,175,199,251` (suggest / draft / ask / summarize) only require a login. They load any ticket by ID, and their prompts include internal notes (`ResolutionCopilotAgent.cs:137-141,311-315`). An end user can ask the copilot about any ticket and get internal notes summarized back. |
| A5 | High | **Anonymous endpoints left open:** `POST /api/system/seed`, `GET /api/system/db-test` (returns the user count and exception text), `POST /api/system/ingest-email` (creates users and tickets with no auth; `SystemController.cs:23,45,68`), `GET /api/test/roles` (lists every role and its permissions; `TestController.cs:13-19`), and `GET /api/ai/status` (reveals the internal endpoint; `AiController.cs:35`). |
| A6 | High | **Change-password does not check the current password** (`AuthService.cs:139-163`), and the minimum length is 6. `MustChangePassword` is only enforced in the browser, not by the API. |
| A7 | High | **JWT signing secrets are guessable.** `Program.cs:160` falls back to a hardcoded secret if none is configured. `appsettings.json:23` ships a placeholder string that will be accepted as the real key if not overridden. `docker-compose.yml:43` has a default secret, and `:12` a default DB password `Postgres123!`. |
| A8 | High | **Seeded demo accounts and passwords.** `admin/Admin123!`, `manager/Manager123!` and others (`DataSeeder.cs:224-231`). Every time the seeder runs it resets these passwords (`:519-520`). It also makes any role-less user whose username contains "admin" or "engineer", or whose email contains "berkay", a **SuperAdmin** (`:249-252`). Docker-compose runs with Development and AutoSeed on (`docker-compose.yml:40-41`), so this happens on every startup. |
| A9 | Medium | **No rate limiting, no account lockout, and no login auditing anywhere.** Login matches the username with SQL `LIKE` (`AuthService.cs:33`), so `%` acts as a wildcard. |
| A10 | Medium | **Permissions live inside the JWT for 120 minutes and there is no revocation.** Removing someone's access does not take effect until their token expires, and `/refresh` (`AuthController.cs:60`) re-issues tokens on demand. The JWT is stored in browser `localStorage` (`wwwroot/js/api.js:43`). |
| A11 | High | **Upload path traversal on Windows.** The client-supplied filename is used raw (`LocalFileStorageService.cs:42-43`). Windows resolves `..` in paths lexically, so a name like `x\..\..\..\wwwroot\a.svg` can escape the upload folder. Uploads are checked by file extension only, with no content check. `.svg` and `.py` are allowed, and `.tar.gz` can never match (`:29-30`). The stored ContentType comes from the client. |
| A12 | Low | **SignalR hub auth may fall back to long-polling.** The hub is `[Authorize]`, but no `OnMessageReceived` handler reads `access_token` from the query string, so WebSocket/SSE connections will fail auth. The hub methods themselves are safe. |
| A13 | Low | **Real uploaded files are committed to the repo:** WhatsApp images, PDFs and Markdown files under `ItsTool.API/uploads/14/` and `uploads/15/`. CORS is properly restricted to a configured list (`Program.cs:176-187`). |

## 2. Audit trail

- **How it works:** an EF Core SaveChanges interceptor (`Data/Interceptors/SystemAuditInterceptor.cs`) records entity type, ID, action, field, old/new value, user ID and timestamp. It does not record IP, user agent or request ID. Records are rows in the same database; there is no append-only protection or hash chain.
- **High: it skips the records that matter most for ISO 27001** (`:97-116`): **UserRole, RolePermission and GroupRole changes (privilege grants), Ticket, TicketAttachment, TicketAssignment and all Comments.** Logins are not audited at all.
- **High: users can erase evidence through the API.** A comment author can delete and then restore their own comment, and the restore **deletes the "CommentDeleted" history row** (`TicketService.cs:726-735`). Because comments and history are excluded from the system audit, no trace remains.
- **Medium:** "Created" entries are written in a second save after the main one (`:69-89`), so a failure there loses audit records silently. The audit controller is read-only (`AuditLogController.cs:23`), but it also filters out role/permission entries (`:29-39`).

## 3. Data layer

- **PostgreSQL only.** `UseNpgsql` is used at `Program.cs:87` and `ItsToolDbContextFactory.cs:39`. Npgsql-specific code outside migrations is limited to `EF.Functions.ILike` (`ResolutionCopilotAgent.cs:193`). There is no jsonb and no raw SQL.
- **All migrations would need regenerating for SQL Server.** There are 23 migrations, all written for Postgres (`timestamp with time zone`, `interval`, Npgsql identity annotations). The port is moderate effort.
- **High: some queries will probably fail at runtime against a real database.** `string.Equals`/`Contains` with a `StringComparison` argument has no SQL translation in EF Core 8. This affects email ingestion (`EmailIngestionService.cs:33`) and ticket keyword search (`TicketQueryHelpers.cs:60-62`). The tests use the InMemory provider, which runs these in memory, so they hide the failure.
- **Soft delete is handled by hand in each query.** A global filter (`HasQueryFilter`) exists only for notifications (`NotificationConfiguration.cs:11,25`). Some links (role, permission, group, project membership) are hard-deleted (`UserService.cs:151,183`, `RoleService.cs:97,135`, `GroupService.cs:131`).

## 4. AI copilot

- **Provider:** any OpenAI-compatible endpoint. The default is LM Studio at `127.0.0.1:1234` with the dummy key `"lm-studio"` (`appsettings.json:25-31`).
- **Key storage:** the API key lives in plain config, plus a runtime override held in a static variable (not persisted, global to all users).
- **PII risk:** ticket title and description, **internal notes**, and comment author IDs are sent to the model, along with similar tickets and knowledge-base excerpts (`ResolutionCopilotAgent.cs:137-154,287,335`).
- **Prompt injection:** user text, including anonymous email bodies and the "ask" question, is inserted directly into the prompt (`:335`). AI output is saved as an internal note attributed to user ID 1, which is hardcoded (`:207-214`).
- If the LLM is unreachable, the copilot falls back to template-based "heuristic" text (`:96-97`).

## 5. Bugs, stubs and dead code

- **Medium: CSV export always returns an empty file.** It reads a `"UserId"` claim that the token never contains, so the user ID is always 0 (`ReportsController.cs:21`).
- **Low: permission denials return 500 instead of 403.** `Forbid(ex.Message)` treats the message as an authentication scheme name, which throws (`PermissionsController.cs:61,81,99`).
- Email ingestion skips assignment, SLA and notifications, as its own comment admits (`EmailIngestionService.cs:116-117`).
- Without SMTP configured, `StubEmailService` prints full email bodies to the console (`StubEmailService.cs:11-12`).
- A one-off orphan-cleanup job runs on every startup (`Program.cs:239-256`).
- `TestController` is debug code that should be removed.
- `TicketController.cs:66` lets `ticket.edit` delete tickets.
- `AddCommentAsync` does not check that the ticket exists (`TicketService.cs:589`).
- The README says the demo password is `123456`, which does not match the seeder (`README.md:359`).

## 6. Test suite

- **513** xUnit `[Fact]`/`[Theory]` tests plus 11 JS tests, using Moq and the EF InMemory provider (`TestBase.cs:13`).
- File names like `MegaBranchCoverageBoostTests` and `FinalPushBranchCoverageTests` suggest they were written to raise coverage numbers rather than to test behaviour.
- **No integration tests** (no WebApplicationFactory). Controllers are created directly in tests, so **the `[Authorize]` attributes and policies are never exercised**. None of the access-control holes above would be caught.

## Recommendation

It is usable as a reference design, but not adoptable as-is. Before production you would need to:
1. Fix A1–A8.
2. Bring role and permission changes, comments, attachments and logins into the audit trail, and make audit records tamper-evident.
3. Remove the history-row deletion on comment restore.
4. Port to SQL Server and regenerate the migrations.
5. Add integration tests that check access control.

Budget a significant hardening effort.

# Part 2: Features, ITIL coverage and reporting

I reviewed the code statically (it couldn't be built here). In short: this is a capable incident and service-request ticketing tool with good SLA timing, but it is not yet a full ITIL service management suite, and its reporting is too thin for ISO 27001 or ISO/IEC 20000-1 audit evidence. Paths below are relative to ``.

## 1. README claims vs code

| Claim | Verdict | Evidence |
|---|---|---|
| AI Resolution Copilot (suggest fix, draft reply, summarise, handoff) | Implemented | `ItsTool.API/Controllers/AiController.cs` (suggest-resolution, draft-reply, ask, summarize, config, test); `ItsTool.Infrastructure/Agents/ResolutionCopilotAgent.cs`, `TicketHandoffSwarm.cs`; UI in `ticket-detail.html` |
| SLA engine: first-response and resolution targets per priority and project, pause on hold, business hours, warnings before breach, escalation | Implemented, with one gap | `Domain/Entities/SLA/*`, `Infrastructure/Services/SlaEngine.cs` (holidays and business hours, `PausesSla`, warned/breached flags, `TryEscalateAsync`), `API/HostedServices/SlaCheckerService.cs`, `sla.html`. Gap: there is no screen or API to maintain business hours or holidays. |
| Custom fields without schema changes (EAV) | Implemented | `Domain/Entities/Config/CustomFields.cs`, `DynamicFormService.cs`, `DynamicFormController.cs`, `admin-fields.html` |
| Configurable workflow transitions | Implemented | `Domain/Entities/Workflow/WorkflowEntities.cs` (transitions can require a permission), `WorkflowController.cs` |
| Roles plus per-user permission overrides | Implemented | `Domain/Entities/Auth/*`, `PermissionCalculator.cs`, `API/Security/PermissionAuthorizationHandler.cs` |
| Real-time push (SignalR), @mentions | Implemented | `API/Hubs/NotificationHub.cs`, `NotificationDispatcher.cs`, `js/notifications.js`, `js/mentions.js` |
| Dashboard: KPI cards, SLA compliance trends, workload heatmaps, CSV/PDF export | Partial | The dashboard has KPI cards plus status and priority charts (`dashboard.html:151,173`). There are no compliance trends and no heatmap; workload is a list (`workload.html`). There is **no PDF export anywhere**. The `/api/dashboard/sla-compliance` endpoint exists but the UI never calls it. |
| Knowledge base with hierarchy, view counts, four-eyes approval | Implemented | `KbEntities.cs`; self-approval is blocked in `KnowledgeBaseService.cs` around line 315; `kb.html`, `kb-article.html` |
| Plain JavaScript UI, dark/light theme, Turkish/English | Implemented | See section 4 |
| Audit trail across entities | Implemented | `Data/Interceptors/SystemAuditInterceptor.cs` records field-level old/new values; `audit-log.html` |
| 534 tests, ~97% coverage, SonarQube results | Can't confirm | Test files exist under `tests/`, but nothing could be run. Several test files are named like coverage padding (`MegaBranchCoverageBoostTests.cs`, `UltraBranchCoverageFinalTests.cs`). |
| Webhooks | Implemented, though the roadmap lists it as not done | `WebhookDispatcher.cs`, `webhooks.html` |

## 2. Data model compared with ITIL / iTop

**What exists:**
- Ticket, with only two seeded types, Incident and Service Request (`DataSeeder.cs:73-74`)
- Category, Status, Priority, TicketType
- Ticket comments, attachments, history, watchers, assignments, satisfaction surveys
- SLA policy, targets, business hours, holidays, and per-ticket SLA tracking
- Workflow and transitions
- Knowledge base categories and articles
- User, Department, Group and memberships (Department is the closest thing to an organisation)
- Project (acts as a tenant or application), roles and permissions, assignment rules
- Notifications and per-user preferences, webhooks, saved filters, system audit log

**What is missing:** I searched the code and found no entity or service for any of these:
- Problem
- Change, or any CAB approval
- Known Error
- CMDB or configuration items with relationships (the roadmap lists this as v1.1, not built)
- Assets
- A real service catalogue (`CatalogController` only manages categories, types, statuses and priorities)
- OLAs or underpinning contracts
- Contracts and suppliers
- Customer or external organisations
- Locations

There is also no approval workflow on tickets themselves; the only approval is for knowledge base articles.

## 3. Reporting and KPIs

**Exports that exist:**
- Ticket list to CSV on the server (`ReportService.cs`, `GET /api/reports/tickets/csv`, capped at 10,000 rows)
- Audit log to CSV, built in the browser from what's loaded (`audit-log.html:502`)
- Per-ticket history and comment-log CSV (`ticket-detail.html`)

There is no Excel, PDF, scheduled or emailed reporting.

**Likely bug in the CSV export:** `ReportsController.cs` reads a `"UserId"` claim, but `TokenService.cs` never issues one (it issues `sub`, Name, role and permission). The user ID therefore falls back to 0, so the security filter probably runs for a user that doesn't exist and the export comes back empty or wrong. Every other controller reads the standard name-identifier claim.

| KPI | Status |
|---|---|
| SLA compliance % (first response and resolution) | Computed in `DashboardService.GetSlaComplianceAsync`, but all-time only, with no date range, and not shown in the UI |
| Mean time to resolve | Partial: an average of creation to resolution in the same method. It counts elapsed time, ignoring business hours and paused time. |
| First-response time | The time is stored per ticket (`FirstResponseMetAt`), but no average or report is calculated |
| Open, critical, breached, at-risk, unassigned counts; satisfaction average | Yes (`GetOverviewAsync`), as current snapshots only |
| Tickets by status, priority, category | Yes (`GetDistributionsAsync`), no time filter |
| Workload per department or agent | Yes |
| Backlog ageing | No |
| Reopen rate | No. Reopens are written to ticket history (`TicketService.cs:413`) but never aggregated. |
| Change success rate | No (there is no change module) |
| Access-review evidence | Weak. Role, group and permission edits are captured by the audit interceptor, but logins aren't logged (`AuthService.cs`) and there is no user-access review report. |

**What auditors will notice missing:** reporting by period (monthly or quarterly), trends, breakdowns of SLA results per period, PDF evidence packs, KPIs per service, and problem, change and capacity reporting for ISO/IEC 20000-1.

## 4. Front end

- **How it's built:** `ItsTool.Web` is just a static file host (`Program.cs` is 7 lines: default files plus static files). The UI is 19 hand-written HTML pages under `wwwroot`, using plain JavaScript modules, Bootstrap 5, Chart.js, Quill, FullCalendar and DOMPurify, all bundled locally.
- **Languages:** only English and Turkish, as two dictionaries in `js/i18n.js` (`"en"` at line 4, `"tr"` at line 643). The chosen language is saved in the browser (`itsm_lang`).
- **Arabic and right-to-left:** there is no RTL support. The code sets the page language but never the text direction, and the CSS has no RTL rules. Adding Arabic means a third dictionary, plus RTL CSS work.
- **Hard-coded Turkish:** a lot of UI text bypasses the translation system. Approximate counts of Turkish lines with no translation key:
  - `ticket-detail.html` ~146
  - `sla.html` ~60
  - `login.html` ~36
  - also `tickets.html`, `users.html`, `audit-log.html` and `kb.html`
  - JavaScript: `js/notifications.js` ~36 (event labels), `layout.js`, `ui.js`
  - Back end also produces Turkish text: audit labels in `SystemAuditInterceptor.cs:130`, notification subjects in `NotificationDispatcher.cs:359`, and error messages in `KnowledgeBaseService.cs:317`.

## 5. Email, intake channels and directory integration

- **Outbound email:** present. It uses SMTP via MailKit (`SmtpEmailService.cs`) behind an in-memory queue (`InMemoryEmailQueue.cs`, `EmailBackgroundService.cs`), with HTML templates and per-user preferences. It falls back to a stub that sends nothing if SMTP isn't configured. Queued email is lost if the service restarts.
- **Email-to-ticket:** partial. `POST /api/system/ingest-email` (`SystemController.cs`) accepts already-parsed JSON. It removes duplicates by message ID, creates unknown senders as users, and routes by `[PROJECTKEY]` in the subject. However:
  - there is no IMAP, POP or Microsoft Graph mailbox polling;
  - the code's own comment admits assignment and notifications are skipped (`EmailIngestionService.cs:118`);
  - **the endpoint needs no login**, so anyone who can reach it can create tickets and user accounts.
- **Other unauthenticated endpoints:** `POST /api/system/seed` (does nothing once users exist) and `GET /api/test/roles`, which exposes the full role-to-permission map.
- **Other intake channels:** the web portal and webhooks going out only. There is no chat, Teams or self-service catalogue.
- **Directory integration:** none. There is no Active Directory, LDAP, Entra ID, OpenID Connect or SAML code or packages; login is local username and password with JWT. UAE Pass or Entra single sign-on would have to be built.

## Bottom line

This is a solid base for incident and service-request handling with SLA tracking, a knowledge base and audit logging. To meet audit expectations it needs:
- Problem, Change/CAB, CMDB/asset and supplier modules
- Period-based KPI reporting with PDF export, plus a fix for the CSV export claim bug
- Arabic and RTL support, and moving the hard-coded Turkish text into the translation system
- Entra ID or LDAP single sign-on
- Mailbox polling for email-to-ticket
- Locking down the unauthenticated endpoints