#!/usr/bin/env bash
# HPA demo: push sustained load and watch the processor scale out, then back in.
source "$(dirname "$0")/common.sh"

log "Starting 3 minutes of high-rate traffic in-cluster"
"$ROOT/scripts/simulator-job.sh" load-test --rate 600 --batch 50 --concurrency 16 --duration 180 --duplicates 0.02 --malformed 0
log "Watch it scale (Ctrl+C to stop watching):"
echo "  kubectl -n $NAMESPACE get hpa processor -w"
echo "  kubectl -n $NAMESPACE top pods"
kubectl -n "$NAMESPACE" get hpa processor -w
