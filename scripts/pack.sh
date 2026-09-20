#!/usr/bin/env bash
# Pack framework packages for local/CI verification (does not push).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${PACK_OUT:-$ROOT/artifacts/nuget}"
CFG="${CONFIGURATION:-Release}"
mkdir -p "$OUT"

echo "==> Packing StayOta.Agent.Abstractions → $OUT"
dotnet pack "$ROOT/backend/src/Modules/Agent/StayOta.Agent.Abstractions/StayOta.Agent.Abstractions.csproj" \
  -c "$CFG" -o "$OUT" --nologo

echo "==> Packing StayOta.Agent → $OUT"
dotnet pack "$ROOT/backend/src/Modules/Agent/StayOta.Agent/StayOta.Agent.csproj" \
  -c "$CFG" -o "$OUT" --nologo

echo "==> Packing StayOta.Agent.Plugins.Echo (sample) → $OUT"
dotnet pack "$ROOT/backend/src/Modules/Agent/StayOta.Agent.Plugins.Echo/StayOta.Agent.Plugins.Echo.csproj" \
  -c "$CFG" -o "$OUT" --nologo

echo "==> Packages:"
ls -la "$OUT"/*.nupkg
