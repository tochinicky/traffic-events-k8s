#!/usr/bin/env bash
# Verify the local toolchain. Prints install hints instead of installing anything.
source "$(dirname "$0")/common.sh"

missing=0
check() {
  if command -v "$1" >/dev/null 2>&1; then
    printf '  ✓ %-8s %s\n' "$1" "$($2 2>/dev/null | head -1)"
  else
    printf '  ✗ %-8s missing — %s\n' "$1" "$3"; missing=1
  fi
}
check docker  "docker --version"            "install Docker Desktop (give it 5-6 GB RAM)"
check kind    "kind version"                "brew install kind"
check kubectl "kubectl version --client"    "brew install kubectl"
check dotnet  "dotnet --version"            "install the .NET 10 SDK"
docker info >/dev/null 2>&1 || { echo "  ✗ docker daemon not running"; missing=1; }
mem=$(docker info --format '{{.MemTotal}}' 2>/dev/null || echo 0)
if (( mem > 0 && mem < 4800000000 )); then
  echo "  ! Docker has $((mem / 1024 / 1024)) MiB; 5-6 GB recommended (Docker Desktop → Settings → Resources)"
fi
exit $missing
