#!/usr/bin/env bash
# Rolling update with zero downtime, then rollback. Polls query-api the whole time and counts failures.
source "$(dirname "$0")/common.sh"

# The old pod keeps answering during its preStop sleep (by design), so wait for the new version.
wait_for_version() {
  for _ in $(seq 1 30); do
    v="$(curl -fsS --max-time 2 "$QUERY_URL/" 2>/dev/null || true)"
    if [[ "$v" == *"\"$1\""* ]]; then echo "  query-api now reports: $v"; return; fi
    sleep 1
  done
  echo "  query-api still not on $1: $v"
}

poll() {
  local ok=0 fail=0
  while [[ -f "$ROOT/.deploy/polling" ]]; do
    if curl -fsS -o /dev/null --max-time 2 "$QUERY_URL/"; then ok=$((ok+1)); else fail=$((fail+1)); fi
    sleep 0.1
  done
  echo "  requests during rollout: ok=$ok failed=$fail"
}

old="$(kubectl -n "$NAMESPACE" get deploy query-api -o jsonpath='{.spec.template.spec.containers[0].image}')"
new_tag="rollout-$(date +%s)"
log "Current image: $old → building $new_tag"
"$ROOT/scripts/build-images.sh" "$new_tag" query-api >/dev/null 2>&1

mkdir -p "$ROOT/.deploy"; touch "$ROOT/.deploy/polling"; poll & poller=$!
log "Rolling out query-api:$new_tag (maxSurge 1, maxUnavailable 0)"
kind load docker-image "traffic-events/query-api:$new_tag" --name "$CLUSTER" >/dev/null
kubectl -n "$NAMESPACE" set image deployment/query-api "query-api=traffic-events/query-api:$new_tag"
kubectl -n "$NAMESPACE" annotate deployment/query-api kubernetes.io/change-cause="rollout-demo: image $new_tag" --overwrite >/dev/null
kubectl -n "$NAMESPACE" rollout status deployment/query-api
wait_for_version "$new_tag"
rm -f "$ROOT/.deploy/polling"; wait $poller

log "Rollout history"
kubectl -n "$NAMESPACE" rollout history deployment/query-api | tail -4

touch "$ROOT/.deploy/polling"; poll & poller=$!
log "Rolling back with 'kubectl rollout undo'"
kubectl -n "$NAMESPACE" rollout undo deployment/query-api
kubectl -n "$NAMESPACE" rollout status deployment/query-api
wait_for_version "${old##*:}"
rm -f "$ROOT/.deploy/polling"; wait $poller
echo "  (set image is used here only to show a one-off rollout/undo; real deploys go through 'make deploy')"
