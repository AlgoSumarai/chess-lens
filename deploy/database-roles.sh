#!/bin/sh
set -eu
# Runs only during creation of a new production PostgreSQL volume.
# Runtime processes receive a DML role; migrations use a separate DDL role.
psql --username "$POSTGRES_USER" --dbname chesslens --set ON_ERROR_STOP=1 <<'SQL'
CREATE ROLE chesslens LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE ROLE chesslens_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
REVOKE ALL ON DATABASE chesslens FROM PUBLIC;
GRANT CONNECT ON DATABASE chesslens TO chesslens, chesslens_migrator;
GRANT TEMPORARY ON DATABASE chesslens TO chesslens_migrator;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO chesslens, chesslens_migrator;
GRANT CREATE ON SCHEMA public TO chesslens_migrator;
ALTER DEFAULT PRIVILEGES FOR ROLE chesslens_migrator IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO chesslens;
ALTER DEFAULT PRIVILEGES FOR ROLE chesslens_migrator IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO chesslens;
SQL

# psql's password command hashes the password before sending ALTER ROLE.
# No plaintext password enters SQL text, command arguments or console output.
chesslens_app_password="$(cat /run/secrets/app_password)"
printf '%s\n%s\n' "$chesslens_app_password" "$chesslens_app_password" |
    psql --username "$POSTGRES_USER" --dbname chesslens --set ON_ERROR_STOP=1 -c '\password chesslens'
unset chesslens_app_password
chesslens_migration_password="$(cat /run/secrets/migration_password)"
printf '%s\n%s\n' "$chesslens_migration_password" "$chesslens_migration_password" |
    psql --username "$POSTGRES_USER" --dbname chesslens --set ON_ERROR_STOP=1 -c '\password chesslens_migrator'
unset chesslens_migration_password
