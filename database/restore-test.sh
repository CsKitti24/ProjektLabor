#!/bin/bash
# Helyreállítási próba: a legutóbbi (vagy a megadott) mentést egy KÜLÖN, ideiglenes adatbázisba
# állítja vissza, lefuttatja a DBCC CHECKDB-t, összeveti a fő táblák sorszámát az éles adatbázissal,
# majd törli az ideiglenes adatbázist. Az éles adatbázishoz nem nyúl.
#   docker compose run --rm db-backup restore-test [KeyAndRoomDb_YYYYmmdd_HHMMSS.bak]
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
DB_NAME="${MSSQL_DATABASE:-KeyAndRoomDb}"
TEST_DB="${DB_NAME}_RestoreTest"
BACKUP_PATH="/var/opt/mssql/backup"

SQLCMD=/opt/mssql-tools18/bin/sqlcmd
[[ -x "$SQLCMD" ]] || SQLCMD=/opt/mssql-tools/bin/sqlcmd
sql() { "$SQLCMD" -S "${MSSQL_HOST:-mssql}" -U sa -P "$MSSQL_SA_PASSWORD" -C -b -h -1 -W -s '|' "$@"; }

if [[ -n "${1:-}" ]]; then
  [[ "$1" =~ ^[A-Za-z0-9_.-]+\.bak$ ]] || { echo "Érvénytelen fájlnév: $1" >&2; exit 1; }
  FILE="$BACKUP_PATH/$1"
else
  FILE="$(ls -1t "$BACKUP_PATH/${DB_NAME}"_*.bak 2>/dev/null | head -n 1 || true)"
fi
[[ -n "$FILE" && -f "$FILE" ]] || { echo "Nem található mentés a $BACKUP_PATH mappában." >&2; exit 1; }
echo "Helyreállítási próba ebből: $FILE"

# A mentésben lévő logikai fájlnevek (adat + napló), hogy új helyre állíthassuk vissza.
MOVES=""
while IFS='|' read -r logical _physical type _rest; do
  [[ -z "$logical" ]] && continue
  if [[ "$type" == "D" ]]; then target="/var/opt/mssql/data/${TEST_DB}.mdf"; else target="/var/opt/mssql/data/${TEST_DB}_log.ldf"; fi
  MOVES+=", MOVE N'$logical' TO N'$target'"
done < <(sql -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$FILE';")

cleanup() {
  sql -Q "IF DB_ID(N'$TEST_DB') IS NOT NULL BEGIN ALTER DATABASE [$TEST_DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$TEST_DB]; END" >/dev/null || true
}
trap cleanup EXIT
cleanup

sql -Q "RESTORE DATABASE [$TEST_DB] FROM DISK = N'$FILE' WITH CHECKSUM, RECOVERY, REPLACE$MOVES;" >/dev/null
echo "Visszaállítás kész: $TEST_DB"
sql -Q "DBCC CHECKDB (N'$TEST_DB') WITH NO_INFOMSGS, ALL_ERRORMSGS;"
echo "DBCC CHECKDB: nincs hiba."

STATUS=0
printf '%-22s %10s %10s\n' "Tábla" "Mentés" "Éles"
for table in Felhasznalok Termek Kulcsok Foglalasok Kulcsmozgasok Hibajegyek AuditLogok; do
  restored="$(sql -Q "SET NOCOUNT ON; IF OBJECT_ID(N'[$TEST_DB].dbo.[$table]') IS NOT NULL SELECT COUNT_BIG(*) FROM [$TEST_DB].dbo.[$table] ELSE SELECT -1;" | tr -d '[:space:]')"
  live="$(sql -Q "SET NOCOUNT ON; IF OBJECT_ID(N'[$DB_NAME].dbo.[$table]') IS NOT NULL SELECT COUNT_BIG(*) FROM [$DB_NAME].dbo.[$table] ELSE SELECT -1;" | tr -d '[:space:]')"
  printf '%-22s %10s %10s\n' "$table" "$restored" "$live"
  # A mentés óta keletkezhettek új sorok, de ha a mentésben egy létező tábla hiányzik, az hiba.
  if [[ "$restored" == "-1" && "$live" != "-1" ]]; then STATUS=1; fi
done

if [[ $STATUS -ne 0 ]]; then
  echo "HIBA: a mentésből tábla hiányzik." >&2
  exit 1
fi
echo "A helyreállítási próba sikeres; az ideiglenes adatbázis törlődik."
