#!/bin/bash
# Egyszeri adatbázis-előkészítés (docker compose "db-init" szolgáltatás).
# Az alkalmazás nem az "sa" rendszergazdával csatlakozik, hanem egy csak a KeyAndRoomDb adatbázishoz
# jogosult felhasználóval (db_owner: a migrációkhoz sémát is módosíthat, a szerver többi részéhez nem fér hozzá).
# Ismételten futtatható: a meglévő login jelszavát a .env aktuális értékére igazítja.
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
: "${MSSQL_APP_PASSWORD:?MSSQL_APP_PASSWORD is required}"
DB_NAME="${MSSQL_DATABASE:-KeyAndRoomDb}"
APP_LOGIN="${MSSQL_APP_USER:-projektlabor_app}"

# Az értékek SQL-szövegbe kerülnek, ezért az idézőjelet és a szögletes zárójelet nem engedjük.
case "$MSSQL_APP_PASSWORD" in
  *\'*|*\"*) echo "MSSQL_APP_PASSWORD must not contain quote characters." >&2; exit 1 ;;
esac
if [[ ! "$DB_NAME" =~ ^[A-Za-z0-9_]+$ || ! "$APP_LOGIN" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "MSSQL_DATABASE and MSSQL_APP_USER may only contain letters, digits and underscores." >&2
  exit 1
fi
if [[ ${#MSSQL_APP_PASSWORD} -lt 12 ]]; then
  echo "MSSQL_APP_PASSWORD must be at least 12 characters long." >&2
  exit 1
fi

SQLCMD=/opt/mssql-tools18/bin/sqlcmd
[[ -x "$SQLCMD" ]] || SQLCMD=/opt/mssql-tools/bin/sqlcmd

"$SQLCMD" -S "${MSSQL_HOST:-mssql}" -U sa -P "$MSSQL_SA_PASSWORD" -C -b -I <<SQL
IF DB_ID(N'$DB_NAME') IS NULL
    CREATE DATABASE [$DB_NAME];
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$APP_LOGIN')
    CREATE LOGIN [$APP_LOGIN] WITH PASSWORD = N'$MSSQL_APP_PASSWORD', CHECK_POLICY = ON, DEFAULT_DATABASE = [$DB_NAME];
ELSE
    ALTER LOGIN [$APP_LOGIN] WITH PASSWORD = N'$MSSQL_APP_PASSWORD';
GO
USE [$DB_NAME];
IF USER_ID(N'$APP_LOGIN') IS NULL
    CREATE USER [$APP_LOGIN] FOR LOGIN [$APP_LOGIN];
ALTER ROLE db_owner ADD MEMBER [$APP_LOGIN];
GO
SQL

echo "Database user '$APP_LOGIN' is ready for database '$DB_NAME'."
