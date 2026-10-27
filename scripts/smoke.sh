#!/bin/bash
# Quick check that a running FNRC ITSM API answers and enforces its security rules.
# Usage: ITSM_USERNAME=<admin> ITSM_PASSWORD=<password> ./scripts/smoke.sh [base URL]
# The account must not have a pending password change.

BASE_URL="${1:-http://localhost:5246}"
API="$BASE_URL/api"
: "${ITSM_USERNAME:?Set ITSM_USERNAME}"
: "${ITSM_PASSWORD:?Set ITSM_PASSWORD}"

failures=0
check() { # name, expected status, actual status
    if [ "$2" == "$3" ]; then echo "  ok    $1 ($3)"; else echo "  FAIL  $1: expected $2, got $3"; failures=$((failures + 1)); fi
}
status() { curl -s -o /dev/null -w "%{http_code}" "$@"; }

echo "Smoke test against $BASE_URL"

check "health check works without a login" 200 "$(status "$API/health")"
check "API needs a login" 401 "$(status "$API/ticket/search")"
check "AI status needs a login" 401 "$(status "$API/ai/status")"

LOGIN=$(curl -s -X POST "$API/auth/login" -H "Content-Type: application/json" \
    -d "{\"username\": \"$ITSM_USERNAME\", \"password\": \"$ITSM_PASSWORD\"}")
TOKEN=$(echo "$LOGIN" | grep -o '"token":"[^"]*' | cut -d'"' -f4)
if [ -z "$TOKEN" ]; then
    echo "  FAIL  login: $LOGIN"
    exit 1
fi
echo "  ok    login"
AUTH="Authorization: Bearer $TOKEN"

check "profile" 200 "$(status "$API/auth/me" -H "$AUTH")"
check "lookup data" 200 "$(status "$API/lookup" -H "$AUTH")"
check "ticket search" 200 "$(status "$API/ticket/search" -H "$AUTH")"
check "dashboard" 200 "$(status "$API/dashboard/overview" -H "$AUTH")"
check "AI endpoints are off" 404 "$(status -X POST "$API/ai/tickets/1/suggest-resolution" -H "$AUTH")"
check "removed debug endpoint" 404 "$(status "$API/test/roles" -H "$AUTH")"
check "front end is served" 200 "$(status "$BASE_URL/login.html")"

if [ "$failures" -gt 0 ]; then
    echo "$failures check(s) failed"
    exit 1
fi
echo "All checks passed"
