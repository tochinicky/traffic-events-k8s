#!/usr/bin/env bash
# Load images into kind (unless SKIP_LOAD=1) and apply the dev overlay with this tag, then wait for every rollout.
#   scripts/deploy.sh <tag>      (default: last built tag)
source "$(dirname "$0")/common.sh"
require_secrets

tag="${1:-$(cat "$ROOT/.last-image-tag" 2>/dev/null || echo dev)}"

if [[ -z "${SKIP_LOAD:-}" ]]; then
  "$ROOT/scripts/load-images.sh" "$tag"
fi

# A throwaway overlay on top of dev that pins the tag. Keeps the committed YAML tag-free and
# avoids `kubectl set image` drift (the next apply would silently revert it).
mkdir -p "$ROOT/.deploy"
cat > "$ROOT/.deploy/kustomization.yaml" <<EOK
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization
resources:
  - ../k8s/overlays/dev
images:
  - name: traffic-events/ingest-api
    newTag: "$tag"
  - name: traffic-events/processor
    newTag: "$tag"
  - name: traffic-events/query-api
    newTag: "$tag"
EOK

log "Applying manifests (tag $tag)"
kubectl apply -k "$ROOT/.deploy"

# Record why each revision exists, so `kubectl rollout history` is readable.
for d in ingest-api processor query-api; do
  kubectl -n "$NAMESPACE" annotate "deployment/$d" kubernetes.io/change-cause="deploy $tag" --overwrite >/dev/null
done

log "Waiting for rollouts"
kubectl -n "$NAMESPACE" rollout status statefulset/rabbitmq --timeout=300s
kubectl -n "$NAMESPACE" rollout status statefulset/mongodb --timeout=300s
for d in ingest-api processor query-api; do
  kubectl -n "$NAMESPACE" rollout status "deployment/$d" --timeout=300s
done
kubectl -n "$NAMESPACE" get pods -o wide
log "Deployed $tag. ingest-api: $INGEST_URL  query-api: $QUERY_URL  RabbitMQ UI: $RABBIT_UI_URL"
