#!/usr/bin/env bash
# Run reviewed SQL using the application's filesystem identity on the Docker host.
set -euo pipefail

if [[ $# -lt 4 || $# -gt 5 ]]; then
    echo 'Usage: db-update.sh production|development|test DATABASE SQL VERIFY_SQL [PRECHECK_SQL]' >&2
    exit 2
fi
Environment=$1
case "$Environment" in production|development|test) ;; *) echo 'Unknown environment.' >&2; exit 2 ;; esac
Database=$(realpath -e -- "$2")
Sql=$(realpath -e -- "$3")
Verify=$(realpath -e -- "$4")
ScriptDirectory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
Mounts=(--mount "type=bind,src=$Sql,dst=/maintenance/update.sql,readonly"
        --mount "type=bind,src=$Verify,dst=/maintenance/verify.sql,readonly")
Arguments=()
if [[ $# -eq 5 ]]; then
    Precheck=$(realpath -e -- "$5")
    Mounts+=(--mount "type=bind,src=$Precheck,dst=/maintenance/precheck.sql,readonly")
    Arguments+=(--precheck /maintenance/precheck.sql)
fi

echo "Environment: $Environment; database: $Database; SQL: $Sql"
echo "Host backup directory: $(dirname -- "$Database")/.db-backups"
exec docker run --rm --pull=never --network=none --user 1654:1654 \
    --mount "type=bind,src=$(dirname -- "$Database"),dst=/data" \
    --mount "type=bind,src=$ScriptDirectory/db_update.py,dst=/maintenance/db_update.py,readonly" \
    "${Mounts[@]}" python:3.14-slim \
    python -B /maintenance/db_update.py "$Environment" "/data/$(basename -- "$Database")" \
    /maintenance/update.sql /maintenance/verify.sql "${Arguments[@]}"
