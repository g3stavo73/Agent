#!/usr/bin/env bash
set -euo pipefail

: "${DATABASE_URL:?Set DATABASE_URL to a privileged migration connection string}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATIONS="$ROOT/database/postgres/migrations"

for file in "$MIGRATIONS"/*.sql; do
  [[ -f "$file" ]] || continue
  name="$(basename "$file" .sql)"
  version="${name%%_*}"
  [[ "$version" =~ ^[0-9]+$ ]] || { echo "Invalid migration name: $name" >&2; exit 1; }
  checksum="$(sha256sum "$file" | awk '{print $1}')"

  has_history="$(psql "$DATABASE_URL" -X -A -t -v ON_ERROR_STOP=1 \
    -c "SELECT to_regclass('public.schema_migrations') IS NOT NULL")"
  existing=""
  if [[ "$has_history" == "t" ]]; then
    existing="$(psql "$DATABASE_URL" -X -A -t -v ON_ERROR_STOP=1 -v version="$version" <<'SQL'
SELECT checksum_sha256 FROM schema_migrations WHERE version = :'version'::integer;
SQL
    )"
  fi
  if [[ -n "$existing" ]]; then
    [[ "$existing" == "$checksum" ]] || { echo "Checksum mismatch for applied migration $name" >&2; exit 1; }
    echo "Already applied: $name"
    continue
  fi

  echo "Applying: $name"
  psql "$DATABASE_URL" -X -v ON_ERROR_STOP=1 -f "$file"
  psql "$DATABASE_URL" -X -v ON_ERROR_STOP=1 \
    -v version="$version" -v name="$name" -v checksum="$checksum" <<'SQL'
INSERT INTO schema_migrations(version, name, checksum_sha256)
VALUES (:'version'::integer, :'name', :'checksum');
SQL
done
