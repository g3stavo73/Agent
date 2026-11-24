#!/bin/sh
set -eu

: "${AGENT_DB_USER:?AGENT_DB_USER is required}"
: "${AGENT_DB_PASSWORD:?AGENT_DB_PASSWORD is required}"

# The PostgreSQL image entrypoint has already executed 001_initial_postgres.sql
# immediately before this script (the file names are ordered lexicographically).
MIGRATION=/docker-entrypoint-initdb.d/001_initial_postgres.sql
CHECKSUM="$(sha256sum "$MIGRATION" | awk '{print $1}')"
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 \
  --set checksum="$CHECKSUM" <<'SQL'
INSERT INTO schema_migrations(version, name, checksum_sha256)
VALUES (1, '001_initial_postgres', :'checksum')
ON CONFLICT (version) DO NOTHING;
SQL

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 \
  --set app_user="$AGENT_DB_USER" --set app_password="$AGENT_DB_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L INHERIT', :'app_user', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user')
\gexec
ALTER ROLE :"app_user" WITH LOGIN PASSWORD :'app_password'
    INHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
GRANT agent_runtime TO :"app_user";
SQL
