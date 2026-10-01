#!/usr/bin/env bash
# Create the kind cluster (idempotent) and install metrics-server, which the HPA needs.
source "$(dirname "$0")/common.sh"

METRICS_SERVER_VERSION="v0.9.0"

if kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
  log "kind cluster '$CLUSTER' already exists"
else
  log "Creating kind cluster '$CLUSTER'"
  kind create cluster --config "$ROOT/k8s/kind-config.yaml" --wait 120s
fi
kubectl config use-context "kind-$CLUSTER" >/dev/null

log "Installing metrics-server $METRICS_SERVER_VERSION"
kubectl apply -f "https://github.com/kubernetes-sigs/metrics-server/releases/download/$METRICS_SERVER_VERSION/components.yaml"
# kind's kubelets use self-signed serving certs; metrics-server must skip verifying them.
# (Local clusters only — on AKS/EKS metrics-server is managed and verifies certs.)
if ! kubectl -n kube-system get deploy metrics-server -o jsonpath='{.spec.template.spec.containers[0].args}' | grep -q kubelet-insecure-tls; then
  kubectl -n kube-system patch deployment metrics-server --type=json \
    -p '[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]'
fi
kubectl -n kube-system rollout status deployment/metrics-server --timeout=180s
log "Cluster ready. Next: make deploy"
