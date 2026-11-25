#!/bin/bash
# Ütemezett adatbázismentés (docker compose "db-backup" szolgáltatás).
# Induláskor, majd BACKUP_INTERVAL_HOURS óránként teljes mentést készít a KeyAndRoomDb-ről a
# /var/opt/mssql/backup mappába (a gépen: BACKUP_DIR, alapból ./backups), és törli a
# BACKUP_RETENTION_DAYS napnál régebbi mentéseket.
#   Egyszeri mentés:        docker compose run --rm db-backup --once
#   Helyreállítási próba:   docker compose run --rm db-backup restore-test [fájl.bak]
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
DB_NAME="${MSSQL_DATABASE:-KeyAndRoomDb}"
BACKUP_PATH="/var/opt/mssql/backup"
INTERVAL_HOURS="${BACKUP_INTERVAL_HOURS:-24}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-14}"

if [[ ! "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "MSSQL_DATABASE may only contain letters, digits and underscores." >&2
  exit 1
fi
if [[ ! "$INTERVAL_HOURS" =~ ^[0-9]+$ || "$INTERVAL_HOURS" -lt 1 || ! "$RETENTION_DAYS" =~ ^[0-9]+$ || "$RETENTION_DAYS" -lt 1 ]]; then
  echo "BACKUP_INTERVAL_HOURS and BACKUP_RETENTION_DAYS must be positive integers." >&2
  exit 1
fi

SQLCMD=/opt/mssql-tools18/bin/sqlcmd
[[ -x "$SQLCMD" ]] || SQLCMD=/opt/mssql-tools/bin/sqlcmd

run_backup() {
  local stamp file
  stamp="$(date -u +%Y%m%d_%H%M%S)"
  file="$BACKUP_PATH/${DB_NAME}_${stamp}.bak"
  echo "[$(date -u +%FT%TZ)] Mentés indul: $file"
  # A fájlt az SQL Server írja (a saját konténerében ugyanez a mappa van csatolva).
  "$SQLCMD" -S "${MSSQL_HOST:-mssql}" -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q \
    "BACKUP DATABASE [$DB_NAME] TO DISK = N'$file' WITH INIT, CHECKSUM, NAME = N'$DB_NAME teljes mentés $stamp';
     RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM;"
  echo "[$(date -u +%FT%TZ)] Mentés kész és ellenőrizve: $file"

  find "$BACKUP_PATH" -maxdepth 1 -name "${DB_NAME}_*.bak" -type f -mtime "+$RETENTION_DAYS" -print -delete \
    | sed 's/^/Régi mentés törölve: /'
}

case "${1:-}" in
  "") ;;
  --once)
    run_backup
    exit 0 ;;
  restore-test)
    # docker compose run --rm db-backup restore-test [mentés.bak]
    shift
    exec /bin/bash /scripts/restore-test.sh "$@" ;;
  *)
    echo "Ismeretlen argumentum: $1 (használat: backup.sh [--once | restore-test [fájl.bak]])" >&2
    exit 2 ;;
esac

while true; do
  # Egy sikertelen mentés (pl. épp újrainduló adatbázis) ne állítsa le az ütemezőt.
  run_backup || echo "[$(date -u +%FT%TZ)] HIBA: a mentés nem sikerült, a következő körben újrapróbáljuk." >&2
  sleep "$((INTERVAL_HOURS * 3600))"
done
