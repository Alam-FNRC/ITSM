# Contributing

This is FNRC's internal IT service management platform. This page covers how changes are made and checked. To set up a machine, see [README.md](README.md). For the architecture and the full rules, see [CLAUDE.md](CLAUDE.md).

## Rules every change follows

- **Security first.** Every endpoint needs a login and a permission or ownership check. An endpoint without one needs a written reason.
- **No secrets in the repo.** Keep passwords, keys and connection strings out of `appsettings*.json` and `docker-compose.yml`. Use user-secrets locally and environment variables elsewhere. Never seed accounts or default passwords.
- **Audit access and data changes.** Changes to roles, permissions, group membership, logins and ticket data must be audit-logged. Audit rows are append-only; the application refuses to change or delete them.
- **Tests come with the change.** Every feature has tests. Access-control rules need integration tests that run the real API (see below).
- **One concern per commit.** Write the subject line in the imperative ("Apply deny permission overrides"). Use the body to explain why the change is needed.
- **No copied code from iTop (AGPL) or GLPI (GPL).** Use them only for ideas and data models. Never copy, translate or paraphrase their source code.
- **Changes to scope, data or security need William's decision.**

## Branches

- Small, self-contained fixes can go straight to `main`.
- Anything larger goes on a branch (`feature/...` or `fix/...`): security changes, schema changes, or work that touches several areas. Merge it with `git merge --no-ff` once all tests pass, then delete the branch.
- Commit with your FNRC name and e-mail address, not a personal Git identity.

## Running the tests

```bash
dotnet test ItsTool.sln                  # unit tests and integration tests
node --test tests/frontend/*.test.js     # front-end tests
```

- **Unit tests** (`tests/ItsTool.UnitTests`) use the EF Core in-memory provider. That provider ignores constraints and hides SQL translation errors, so any change to a query also needs an integration test.
- **Integration tests** (`tests/ItsTool.IntegrationTests`) run against SQL Server. Each run creates and drops its own `FnrcItsm_Test_*` database on LocalDB. To use another server, set `ITSM_TEST_SQL`. For access-control tests, use `ApiFixture` in `Api/`: `CreateUserAsync(permissions)` and `LoginAsync(user)` return a client that holds a real token.
- **CI** (`.github/workflows/ci.yml`) runs all three suites on every push and pull request to `main`. It starts its own SQL Server for the integration tests.

## Database changes

Add a migration for every model change:

```bash
dotnet ef migrations add <Name> --project src/ItsTool.Infrastructure --startup-project src/ItsTool.API
```

A test fails if the model and the migrations drift apart. Migrations run when the API starts. They must work on a database that already holds data, so check the data that is there first (for example, duplicates before adding a unique index).

## Reporting problems

Open an issue in [Alam-FNRC/ITSM](https://github.com/Alam-FNRC/ITSM/issues). For a bug, include the steps to reproduce it and what you expected to happen. Report security problems directly to the IT team, not in an issue.
