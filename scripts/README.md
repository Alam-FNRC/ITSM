# Developer scripts

| Script | What it does |
|---|---|
| `run-local.ps1` | Windows: starts the app on http://localhost:5246 with its own LocalDB database (default `FnrcItsm_Demo`) and opens the login page. On a new database it asks for the first administrator, whose password is replaced at the first login. Ctrl+C stops it. |
| `run-dev.sh` | Starts the API, and with it the front end, on http://localhost:5246 in Development; Ctrl+C stops it. |
| `smoke.sh` | Checks a running API: health, login, a few pages, and that the security rules hold (anonymous access refused, AI off, debug endpoints gone). |

```powershell
powershell -ExecutionPolicy Bypass -File scriptsun-local.ps1                       # FnrcItsm_Demo
powershell -ExecutionPolicy Bypass -File scriptsun-local.ps1 -Database FnrcItsm_Try2  # a fresh, empty service desk
```

```bash
./scripts/run-dev.sh
ITSM_USERNAME=<admin> ITSM_PASSWORD=<password> ./scripts/smoke.sh   # optional argument: base URL
```
