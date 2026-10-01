#!/usr/bin/env bash
# Build service images with one tag. The shared build stage compiles the solution once.
#   scripts/build-images.sh [tag] [service...]     (default: all services)
source "$(dirname "$0")/common.sh"

tag="${1:-$(image_tag)}"
shift || true
services=("$@")
[[ ${#services[@]} -gt 0 ]] || services=("${SERVICES[@]}")

for svc in "${services[@]}"; do
  log "Building traffic-events/$svc:$tag"
  docker build --target "$svc" --build-arg "APP_VERSION=$tag" -t "traffic-events/$svc:$tag" "$ROOT"
done

# Only a full build defines the tag that deploy/load/simulator use.
if [[ $# -eq 0 ]]; then
  echo "$tag" > "$ROOT/.last-image-tag"
fi
