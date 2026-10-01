#!/usr/bin/env bash
# Run the simulator as a Kubernetes Job inside the cluster (talks to the ingest-api Service
# directly, so the laptop's network isn't the bottleneck). Extra args go to the simulator.
#   scripts/simulator-job.sh <job-name> [simulator args...]
source "$(dirname "$0")/common.sh"

name="$1"; shift
tag="$(cat "$ROOT/.last-image-tag" 2>/dev/null || echo dev)"
# The Secret has a content-hash suffix; look up the current name from a Deployment that uses it.
secret="$(kubectl -n "$NAMESPACE" get deploy ingest-api \
  -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="Ingest__ApiKey")].valueFrom.secretKeyRef.name}')"

args_yaml=""
for a in --url http://ingest-api:8080 "$@"; do args_yaml+="            - \"$a\""$'\n'; done

kubectl -n "$NAMESPACE" delete job "$name" --ignore-not-found >/dev/null
kubectl -n "$NAMESPACE" apply -f - <<EOJ
apiVersion: batch/v1
kind: Job
metadata:
  name: $name
spec:
  backoffLimit: 0
  ttlSecondsAfterFinished: 600
  template:
    spec:
      restartPolicy: Never
      securityContext:
        runAsNonRoot: true
      containers:
        - name: simulator
          image: traffic-events/simulator:$tag
          imagePullPolicy: IfNotPresent
          args:
$args_yaml
          env:
            - name: INGEST_API_KEY
              valueFrom:
                secretKeyRef:
                  name: $secret
                  key: INGEST_API_KEY
          resources:
            requests: { cpu: 100m, memory: 64Mi }
            limits: { cpu: "1", memory: 192Mi }
EOJ
