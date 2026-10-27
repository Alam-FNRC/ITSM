# FNRC ITSM

IT service management platform for FNRC IT: incidents and service requests with SLA tracking, a knowledge base, an audit trail, and, on the roadmap, problem, change and CMDB.

It is built on [dotnet-enterprise-itsm](https://github.com/Berkaybbayramoglu/dotnet-enterprise-itsm) by Berkay Bayramoğlu (MIT licence, see [LICENSE](LICENSE)), which FNRC is hardening and extending.

**Status:** in development, not yet in production. The critical findings of the October 2026 security review are fixed; see the status table in [docs/review/2026-10-code-review.md](docs/review/2026-10-code-review.md).

## What it is made of

| Part | Technology |
|---|---|
| API | ASP.NET Core 8 Web API (`src/ItsTool.API`), clean architecture: `Domain`, `Application`, `Infrastructure` |
| Database | SQL Server through EF Core 8 migrations (`src/ItsTool.Infrastructure/Migrations`) |
| Front end | Static HTML and JavaScript modules (`src/ItsTool.Web/wwwroot`), served by the API |
| Tests | xUnit unit tests, integration tests against a real SQL Server, Node tests for front-end modules |

## Running it locally

You need the .NET 9 SDK (it builds the .NET 8 projects; pinned in `global.json`), the .NET 8 runtime, SQL Server LocalDB, and Node 22 for the front-end tests.

```bash
dotnet tool restore

# No accounts are seeded. On an empty database, the first administrator is created from these
# settings and must change the password at first login. Values stay on your machine.
dotnet user-secrets set Bootstrap:AdminUsername "<your username>" --project src/ItsTool.API
dotnet user-secrets set Bootstrap:AdminEmail "<your e-mail>" --project src/ItsTool.API
dotnet user-secrets set Bootstrap:AdminPassword "<12+ characters>" --project src/ItsTool.API

# Optional: keeps you logged in across restarts (otherwise Development uses a new random key each run)
dotnet user-secrets set Jwt:Secret "<32+ random characters>" --project src/ItsTool.API

dotnet run --project src/ItsTool.API --launch-profile http
```

The app runs at http://localhost:5246 and creates the `FnrcItsm_Dev` database on `(localdb)\MSSQLLocalDB`. Swagger is at `/swagger` in Development.

To run the tests:

```bash
dotnet test ItsTool.sln              # unit and integration tests; integration tests create and drop a FnrcItsm_Test_* database
node --test tests/frontend/*.test.js # front-end tests
```

[CLAUDE.md](CLAUDE.md) has the full development notes: migrations, test conventions and project rules.

## Running it with Docker

`docker compose up` starts SQL Server and the app in Production mode. Create a `.env` file next to `docker-compose.yml` first; the file header lists what it needs (`DB_PASSWORD`, `JWT_SECRET`, and the bootstrap administrator for the first start). The app refuses to start without a proper `JWT_SECRET`.

## Security defaults

- Every endpoint needs a login, except login itself and `/api/health`.
- No demo accounts or default passwords. Passwords must be 12 to 64 characters, and passwords set by an administrator must be changed at next login.
- Five wrong passwords lock an account for 15 minutes; logins are limited per IP address; every login attempt is recorded in the `AuthEvents` table.
- Access changes (roles, permissions, deactivation, password changes) apply to the next request.
- The AI copilot is switched off (`AI:Enabled` is false) until an AI provider is approved.

## Keyboard shortcuts

These work on every page. Apart from <kbd>Esc</kbd>, they are ignored while you type in a field. The keyboard icon in the top bar, or <kbd>?</kbd>, shows the list.

| Key | Action |
|---|---|
| <kbd>/</kbd> or <kbd>Ctrl</kbd>+<kbd>K</kbd> | Quick search: jump to the page's search box |
| <kbd>Esc</kbd> | Close dialogs, menus and the profile panel |
| <kbd>?</kbd> | Show the shortcuts |
| <kbd>N</kbd> | New ticket |
| <kbd>T</kbd> | Ticket list |
| <kbd>D</kbd> | Dashboard |

## Documents

- [docs/feature-spec.md](docs/feature-spec.md): features planned for FNRC
- [docs/review/2026-10-code-review.md](docs/review/2026-10-code-review.md): security and feature review of the original code, with fix status
- [docs/reference/](docs/reference/): notes on iTop and GLPI data models (ideas only; their code is not used)
