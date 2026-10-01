#!/usr/bin/env bash
# The 60-second tour: realistic traffic with duplicates, malformed and poison events,
# then show what came out the other end.
source "$(dirname "$0")/common.sh"
require_secrets
key="$(secret_value INGEST_API_KEY)"

log "1. Send one event by hand"
event_id="$(uuidgen | tr 'A-Z' 'a-z')"
now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
curl -sS -X POST "$INGEST_URL/events" -H "X-Api-Key: $key" -H 'Content-Type: application/json' \
  -d "{\"eventId\":\"$event_id\",\"sensorId\":\"S-104\",\"junctionId\":\"J-17\",\"type\":\"measurement\",\"timestamp\":\"$now\",\"vehicleCount\":42,\"avgSpeedKmh\":31.5}" \
  -w '  → HTTP %{http_code}\n'

log "2. A bad one is rejected with a clear error"
curl -sS -X POST "$INGEST_URL/events" -H "X-Api-Key: $key" -H 'Content-Type: application/json' \
  -d '{"eventId":"00000000-0000-0000-0000-000000000000","type":"teleport"}' -w '\n  → HTTP %{http_code}\n'

log "3. 30s of simulated traffic in-cluster (5% duplicates, 1% malformed, 1% poison)"
"$ROOT/scripts/simulator-job.sh" demo-traffic --rate 50 --duration 30 --duplicates 0.05 --malformed 0.01 --poison 0.01
kubectl -n "$NAMESPACE" wait --for=condition=complete job/demo-traffic --timeout=90s
kubectl -n "$NAMESPACE" logs job/demo-traffic | tail -2
sleep 3

log "4. Query: junction status, speed aggregate, recent incidents"
curl -sS "$QUERY_URL/junctions/J-17/status"; echo
curl -sS "$QUERY_URL/junctions/J-3/speed?minutes=15"; echo
curl -sS "$QUERY_URL/incidents" | head -c 600; echo

log "5. Queues: alerts raised, poison messages dead-lettered"
"$ROOT/scripts/queues.sh"
