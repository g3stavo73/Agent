-- Initial Agent PostgreSQL schema.
-- Run as the database owner (the deploy init scripts do this before granting
-- the restricted application role membership in agent_runtime).

DO $role$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'agent_runtime') THEN
        CREATE ROLE agent_runtime
            NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
    END IF;
END
$role$;

ALTER ROLE agent_runtime
    WITH NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

CREATE TABLE IF NOT EXISTS public.schema_migrations
(
    version integer PRIMARY KEY,
    name text NOT NULL,
    checksum_sha256 char(64) NOT NULL,
    applied_at_utc timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.app_users
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    issuer varchar(500) NOT NULL,
    subject varchar(500) NOT NULL,
    email varchar(320),
    created_at_utc timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_app_users_issuer_nonempty CHECK (length(btrim(issuer)) > 0),
    CONSTRAINT ck_app_users_subject_nonempty CHECK (length(btrim(subject)) > 0),
    CONSTRAINT uq_app_users_issuer_subject UNIQUE (issuer, subject)
);

CREATE TABLE IF NOT EXISTS public.agent_profiles
(
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES public.app_users(id) ON DELETE CASCADE,
    name varchar(80) NOT NULL,
    description varchar(1600) NOT NULL DEFAULT '',
    avatar_reference varchar(500),
    engine varchar(32) NOT NULL DEFAULT 'codex',
    can_read_repositories boolean NOT NULL DEFAULT true,
    can_write_repositories boolean NOT NULL DEFAULT false,
    can_browse_web boolean NOT NULL DEFAULT true,
    use_semantic_navigation boolean NOT NULL DEFAULT true,
    prefer_updated_documentation boolean NOT NULL DEFAULT true,
    apply_architecture_rules boolean NOT NULL DEFAULT true,
    aisto_configuration jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at_utc timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_agent_profiles_name_nonempty CHECK (length(btrim(name)) > 0),
    CONSTRAINT ck_agent_profiles_engine CHECK (engine = 'codex'),
    CONSTRAINT ck_agent_profiles_write_requires_read CHECK (NOT can_write_repositories OR can_read_repositories)
);

CREATE INDEX IF NOT EXISTS ix_agent_profiles_user_updated
    ON public.agent_profiles (user_id, updated_at_utc DESC);

-- The API connects with a restricted role. It cannot read app_users directly;
-- this SECURITY DEFINER function resolves the external identity using only the
-- validated issuer + subject pair (email is mutable metadata, never an identity key).
CREATE OR REPLACE FUNCTION public.resolve_app_user(
    p_issuer text,
    p_subject text,
    p_email text
)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $function$
DECLARE
    resolved_id uuid;
    normalized_email text;
BEGIN
    IF p_issuer IS NULL OR length(btrim(p_issuer)) = 0 OR length(p_issuer) > 500 THEN
        RAISE EXCEPTION 'A valid issuer is required';
    END IF;
    IF p_subject IS NULL OR length(btrim(p_subject)) = 0 OR length(p_subject) > 500 THEN
        RAISE EXCEPTION 'A valid subject is required';
    END IF;

    normalized_email := CASE
        WHEN p_email IS NOT NULL AND length(btrim(p_email)) BETWEEN 1 AND 320
            THEN btrim(p_email)
        ELSE NULL
    END;

    INSERT INTO public.app_users (issuer, subject, email)
    VALUES (btrim(p_issuer), btrim(p_subject), normalized_email)
    ON CONFLICT (issuer, subject)
    DO UPDATE SET
        email = EXCLUDED.email,
        updated_at_utc = CURRENT_TIMESTAMP
    RETURNING id INTO resolved_id;

    RETURN resolved_id;
END
$function$;

REVOKE ALL ON FUNCTION public.resolve_app_user(text, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.resolve_app_user(text, text, text) TO agent_runtime;

GRANT USAGE ON SCHEMA public TO agent_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.agent_profiles TO agent_runtime;
REVOKE ALL ON public.app_users FROM PUBLIC, agent_runtime;
REVOKE ALL ON public.schema_migrations FROM PUBLIC, agent_runtime;

-- Fail closed: a query without the transaction-local user context sees no rows.
ALTER TABLE public.agent_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.agent_profiles FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS agent_profiles_user_isolation ON public.agent_profiles;
CREATE POLICY agent_profiles_user_isolation
    ON public.agent_profiles
    FOR ALL
    TO agent_runtime
    USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid)
    WITH CHECK (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);
