## What and why
What this change does and why it is needed.

Fixes #(issue)

## Checklist
- [ ] Every new or changed endpoint needs a login and a permission or ownership check
- [ ] Access-control changes have integration tests (`tests/ItsTool.IntegrationTests/Api/`)
- [ ] Changes to roles, permissions, group membership, logins or ticket data are audit-logged
- [ ] Model changes come with a migration that works on a database that already holds data
- [ ] No secrets, passwords or default accounts are committed
- [ ] `dotnet test ItsTool.sln` and `node --test tests/frontend/*.test.js` pass
- [ ] Changes to scope, data or security have William's approval
