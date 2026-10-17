#!/usr/bin/env bash
# End-to-end smoke test of a running compose stack: health, login, week view, rule-checked assignment, CSV export.
# Usage: scripts/smoke.sh [base-url]   (default http://localhost:8090)
set -euo pipefail

BASE="${1:-http://localhost:8090}"
EMAIL="${ADMIN_EMAIL:-admin@judesk.local}"
PASSWORD="${ADMIN_PASSWORD:-ChangeMe123!}"
JAR="$(mktemp)"
trap 'rm -f "$JAR"' EXIT

echo "== waiting for $BASE/health"
for _ in $(seq 1 60); do
  if curl -fsS "$BASE/health" >/dev/null 2>&1; then break; fi
  sleep 2
done
[ "$(curl -fsS "$BASE/health")" = "Healthy" ] || { echo "API not healthy"; exit 1; }

echo "== anonymous access is refused"
code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/schedule/week")
[ "$code" = "401" ] || { echo "expected 401, got $code"; exit 1; }

echo "== login"
curl -fsS -c "$JAR" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" "$BASE/api/auth/login" >/dev/null

echo "== week view has shifts and technicians"
week=$(curl -fsS -b "$JAR" "$BASE/api/schedule/week")
shifts=$(echo "$week" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["shifts"]))')
techs=$(echo "$week" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["technicians"]))')
[ "$shifts" -gt 0 ] && [ "$techs" -gt 0 ] || { echo "empty week: $shifts shifts, $techs technicians"; exit 1; }
echo "   $shifts shifts, $techs technicians"

echo "== rules reject an unqualified technician"
pair=$(echo "$week" | python3 -c '
import json,sys
w=json.load(sys.stdin)
for s in w["shifts"]:
    if len(s["assignments"]) < s["headcount"]:
        for t in w["technicians"]:
            if t["active"] and s["requiredSkillName"] not in t["skills"]:
                print(s["id"], t["id"]); raise SystemExit
')
set -- $pair
code=$(curl -s -o /dev/null -w '%{http_code}' -b "$JAR" -H 'Content-Type: application/json' \
  -d "{\"technicianId\":\"$2\"}" "$BASE/api/shifts/$1/assignments")
[ "$code" = "409" ] || { echo "expected 409, got $code"; exit 1; }

echo "== auto-assign dry run proposes something"
proposed=$(curl -fsS -b "$JAR" -H 'Content-Type: application/json' \
  -d "{\"weekStart\":\"$(echo "$week" | python3 -c 'import json,sys; print(json.load(sys.stdin)["weekStart"])')\",\"dryRun\":true}" \
  "$BASE/api/schedule/auto-assign" | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["assignments"]))')
[ "$proposed" -gt 0 ] || { echo "auto-assign proposed nothing"; exit 1; }
echo "   $proposed assignments proposed"

echo "== CSV export"
header=$(curl -fsS -b "$JAR" "$BASE/api/schedule/export" | head -1)
[ "$header" = "date,start,end,hours,site,skill,technician,source,notes" ] || { echo "unexpected CSV header: $header"; exit 1; }

echo "== web serves the app"
curl -fsS "$BASE/" | grep -q '<div id="app">' || { echo "index.html not served"; exit 1; }

echo "smoke test passed"
