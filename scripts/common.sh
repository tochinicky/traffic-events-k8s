# Shared settings for the scripts. Sourced, not executed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLUSTER="${CLUSTER:-traffic-events}"
NAMESPACE="traffic-events"
SECRETS_FILE="$ROOT/k8s/overlays/dev/secrets.env"
SERVICES=(ingest-api processor query-api simulator)
# Where the APIs are reachable. From the host: the kind NodePort mappings. From a container on the
# kind network (Jenkins): override with http://traffic-events-control-plane:30080 etc.
INGEST_URL="${INGEST_URL:-http://localhost:30080}"
QUERY_URL="${QUERY_URL:-http://localhost:30081}"
RABBIT_UI_URL="${RABBIT_UI_URL:-http://localhost:15672}"

# Always talk to this project's cluster, whatever the current kubectl context is (Docker Desktop
# can switch it to its own built-in cluster). The kind kubeconfig names the context the same in Jenkins.
kubectl() { command kubectl --context "kind-$CLUSTER" "$@"; }

log() { printf '\033[1;34m==> %s\033[0m\n' "$*"; }

# Image tag: the git commit, plus a timestamp if the tree is dirty (so a rebuild really rolls out).
image_tag() {
  if [[ -n "${TAG:-}" ]]; then echo "$TAG"; return; fi
  local sha
  sha="$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo dev)"
  if [[ -n "$(git -C "$ROOT" status --porcelain 2>/dev/null)" ]]; then
    echo "${sha}-$(date +%s)"
  else
    echo "$sha"
  fi
}

secret_value() { grep -E "^$1=" "$SECRETS_FILE" | cut -d= -f2-; }

require_secrets() {
  [[ -f "$SECRETS_FILE" ]] || { echo "Missing $SECRETS_FILE — run 'make secrets' first." >&2; exit 1; }
}
