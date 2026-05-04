#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/infrastructure/docker-compose.yml"
PROJECT_NAME="${COMPOSE_PROJECT_NAME:-hookah-platform}"

VOLUMES=0

usage() {
  cat <<USAGE
Usage: scripts/local-down.sh [options]

Stops the Hookah CRM Platform local Docker Compose stack.

Options:
  --volumes        Remove named volumes (destructive to local DB/cache).
  -h, --help       Show this help.

Environment:
  COMPOSE_PROJECT_NAME      Compose project name, default: hookah-platform
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --volumes)
      VOLUMES=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

compose() {
  docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" "$@"
}

cd "$ROOT_DIR"

DOWN_ARGS=(down --remove-orphans)
if (( VOLUMES == 1 )); then
  DOWN_ARGS+=(-v)
fi

compose "${DOWN_ARGS[@]}"

