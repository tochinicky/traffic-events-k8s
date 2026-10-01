#!/usr/bin/env bash
# Queue depths, plus the first dead-lettered message with its error headers, via the management API.
source "$(dirname "$0")/common.sh"
require_secrets
auth="$(secret_value RABBITMQ_USER):$(secret_value RABBITMQ_PASSWORD)"

curl -sS -u "$auth" "$RABBIT_UI_URL/api/queues/%2F" | python3 -c '
import json, sys
for q in json.load(sys.stdin):
    print("  {:<24} ready={:<6} unacked={:<4} consumers={}".format(
        q["name"], q.get("messages_ready", 0), q.get("messages_unacknowledged", 0), q.get("consumers", 0)))
'

echo "  First message in traffic.events.dlq (peeked, left in the queue):"
curl -sS -u "$auth" -H 'content-type: application/json' -X POST \
  "$RABBIT_UI_URL/api/queues/%2F/traffic.events.dlq/get" \
  -d '{"count":1,"ackmode":"ack_requeue_true","encoding":"auto"}' | python3 -c '
import json, sys
m = json.load(sys.stdin)
if not m:
    print("    (empty)")
else:
    print("    headers:", json.dumps(m[0]["properties"].get("headers", {})))
    print("    payload:", m[0]["payload"][:200])
'
