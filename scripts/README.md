# Developer scripts

| Script | What it does |
|---|---|
| `run-dev.sh` | Starts the API, and with it the front end, on http://localhost:5246 in Development; Ctrl+C stops it. |
| `smoke.sh` | Checks a running API: health, login, a few pages, and that the security rules hold (anonymous access refused, AI off, debug endpoints gone). |

```bash
./scripts/run-dev.sh
ITSM_USERNAME=<admin> ITSM_PASSWORD=<password> ./scripts/smoke.sh   # optional argument: base URL
```
