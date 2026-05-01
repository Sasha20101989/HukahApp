#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/infrastructure/docker-compose.yml"
PROJECT_NAME="${COMPOSE_PROJECT_NAME:-hookah-platform}"

BUILD=1
PULL=0
RESET=0
FOLLOW_LOGS=0
WAIT_TIMEOUT_SECONDS="${WAIT_TIMEOUT_SECONDS:-180}"

usage() {
  cat <<USAGE
Usage: scripts/local-up.sh [options]

Starts the full Hookah CRM Platform locally through Docker Compose:
PostgreSQL, Redis, RabbitMQ, EF migrator, all backend services, API Gateway,
CRM app, client app and Nginx.

Options:
  --no-build       Start existing images without rebuilding.
  --pull           Pull base images before build/start.
  --reset          Stop the stack and remove Compose volumes before starting.
  --logs           Follow logs after the stack becomes ready.
  -h, --help       Show this help.

Environment:
  COMPOSE_PROJECT_NAME      Compose project name, default: hookah-platform
  WAIT_TIMEOUT_SECONDS      Health wait timeout, default: 180
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      BUILD=0
      shift
      ;;
    --pull)
      PULL=1
      shift
      ;;
    --reset)
      RESET=1
      shift
      ;;
    --logs)
      FOLLOW_LOGS=1
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

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command '$1' is not installed or not in PATH." >&2
    exit 1
  fi
}

compose() {
  docker compose -p "$PROJECT_NAME" -f "$COMPOSE_FILE" "$@"
}

wait_for_http() {
  local name="$1"
  local url="$2"
  local deadline=$((SECONDS + WAIT_TIMEOUT_SECONDS))

  printf 'Waiting for %s (%s)' "$name" "$url"
  until curl -fsS --max-time 2 "$url" >/dev/null 2>&1; do
    if (( SECONDS >= deadline )); then
      echo
      echo "Timed out waiting for $name at $url" >&2
      echo "Recent container status:" >&2
      compose ps >&2 || true
      exit 1
    fi
    printf '.'
    sleep 2
  done
  echo " ready"
}

require_command docker
require_command curl

if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose v2 is required: 'docker compose version' failed." >&2
  exit 1
fi

cd "$ROOT_DIR"

echo "Hookah CRM Platform local startup"
echo "Root: $ROOT_DIR"
echo "Compose file: $COMPOSE_FILE"
echo "Compose project: $PROJECT_NAME"

if (( RESET == 1 )); then
  echo "Resetting stack and volumes..."
  compose down --remove-orphans -v
fi

if (( PULL == 1 )); then
  echo "Pulling base/service images..."
  compose pull --ignore-buildable
fi

UP_ARGS=(up -d --remove-orphans)
if (( BUILD == 1 )); then
  UP_ARGS+=(--build)
fi

echo "Starting full stack..."
compose "${UP_ARGS[@]}"

echo "Waiting for public endpoints..."
wait_for_http "API Gateway" "http://localhost:8080/health"
wait_for_http "CRM app" "http://localhost:3000"
wait_for_http "Client app" "http://localhost:3001"

cat <<READY

Hookah CRM Platform is running.

Public URLs:
  Client app:      http://localhost:3001
  CRM app:         http://localhost:3000
  API Gateway:     http://localhost:8080
  Nginx client:    http://localhost
  Nginx CRM:       http://localhost/crm/
  RabbitMQ UI:     http://localhost:15672  (guest / guest)
  PostgreSQL:      localhost:5432          (hookah / hookah, db: hookah)
  Redis:           localhost:6379

Useful commands:
  docker compose -p $PROJECT_NAME -f infrastructure/docker-compose.yml ps
  docker compose -p $PROJECT_NAME -f infrastructure/docker-compose.yml logs -f api-gateway
  docker compose -p $PROJECT_NAME -f infrastructure/docker-compose.yml down

READY

if (( FOLLOW_LOGS == 1 )); then
  compose logs -f --tail=150
fi
