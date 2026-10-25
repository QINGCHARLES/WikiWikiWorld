#!/usr/bin/env bash
# Pull the published image and replace the running wikiwikiworld container.
# Installed on the host as /home/deploy/update.sh; see doc/server-rebuild.md.
set -euo pipefail

# Optional argument: an image reference to run instead of :latest, e.g. a previous image ID for rollback.
IMAGE="${1:-ghcr.io/qingcharles/wikiwikiworld:latest}"
# Host-only secrets (JwtSettings__Secret=...), mode 600. Never commit this file.
SECRETS_FILE="/home/deploy/secrets/wikiwikiworld.env"

# Check everything the new container needs before touching the running one.
if [[ ! -r "$SECRETS_FILE" ]] || ! grep -q '^JwtSettings__Secret=.\{32,\}' "$SECRETS_FILE"; then
    echo "Missing or incomplete $SECRETS_FILE; the running container was left alone." >&2
    exit 1
fi

if [[ $# -eq 0 ]]; then
    echo "Pulling $IMAGE..."
    docker pull "$IMAGE"
else
    docker image inspect "$IMAGE" > /dev/null
fi

echo "Stopping old container..."
docker stop wikiwikiworld || true
docker rm wikiwikiworld || true

echo "Starting..."
# --log-opt caps the json-file log at 3 x 10MB. Without it the container log grows
# without bound for the life of the container.
docker run -d \
  --name wikiwikiworld \
  --restart unless-stopped \
  --log-opt max-size=10m \
  --log-opt max-file=3 \
  --env-file "$SECRETS_FILE" \
  -p 80:80 \
  -p 443:443 \
  -v /opt/wiki/certs:/app/certs \
  -v /opt/wiki/data:/app/data \
  "$IMAGE"

echo "Done! WikiWikiWorld is live."
