#!/usr/bin/env bash
# Generate k8s/overlays/dev/secrets.env with random values (git-ignored). Never overwrites.
source "$(dirname "$0")/common.sh"

if [[ -f "$SECRETS_FILE" ]]; then
  echo "$SECRETS_FILE already exists; leaving it alone."
  exit 0
fi
umask 077
cat > "$SECRETS_FILE" <<EOS
INGEST_API_KEY=$(openssl rand -hex 16)
RABBITMQ_USER=traffic
RABBITMQ_PASSWORD=$(openssl rand -hex 16)
MONGO_USER=traffic
MONGO_PASSWORD=$(openssl rand -hex 16)
EOS
echo "Wrote $SECRETS_FILE"
