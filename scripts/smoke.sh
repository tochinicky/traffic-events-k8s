#!/usr/bin/env bash
# End-to-end smoke test against the deployed cluster.
source "$(dirname "$0")/common.sh"
require_secrets

SMOKE_INGEST_URL="$INGEST_URL" SMOKE_QUERY_URL="$QUERY_URL" SMOKE_API_KEY="$(secret_value INGEST_API_KEY)" \
  dotnet test "$ROOT/tests/TrafficEvents.SmokeTests" ${SMOKE_TEST_ARGS:-}
