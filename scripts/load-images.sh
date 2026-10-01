#!/usr/bin/env bash
# Side-load images into the kind node (kind nodes can't pull from the host's Docker).
#   scripts/load-images.sh <tag>
source "$(dirname "$0")/common.sh"

tag="${1:-$(cat "$ROOT/.last-image-tag" 2>/dev/null || echo dev)}"
for svc in "${SERVICES[@]}"; do
  log "Loading traffic-events/$svc:$tag into kind"
  kind load docker-image "traffic-events/$svc:$tag" --name "$CLUSTER"
done
