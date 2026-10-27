#!/usr/bin/env bash
# Prints a fresh first-run setup code (the previous one stops working). Only useful before the first
# admin exists; afterwards the API says so and exits.
# Usage: scripts/regen-setup-code.sh            (compose stack)
#        scripts/regen-setup-code.sh --local    (API built on this machine, uses backend/appsettings)
set -euo pipefail
cd "$(dirname "$0")/.."

if [ "${1:-}" = "--local" ]; then
  dotnet run --project backend/src/JuDesk.Api --no-launch-profile -- --regen-setup-code
else
  docker compose exec api dotnet JuDesk.Api.dll --regen-setup-code
fi
