#!/bin/sh
set -eu

: "${RESTIC_REPOSITORY:?Configure RESTIC_REPOSITORY em /opt/pericias/backup.env}"
: "${RESTIC_PASSWORD:?Configure RESTIC_PASSWORD em /opt/pericias/backup.env}"
: "${AWS_ACCESS_KEY_ID:?Configure AWS_ACCESS_KEY_ID em /opt/pericias/backup.env}"
: "${AWS_SECRET_ACCESS_KEY:?Configure AWS_SECRET_ACCESS_KEY em /opt/pericias/backup.env}"
: "${AWS_DEFAULT_REGION:?Configure AWS_DEFAULT_REGION em /opt/pericias/backup.env}"

database=/data/pericias.db
staging=/tmp/pericias-backup

if [ ! -r "$database" ]; then
    echo "Banco SQLite não encontrado em $database." >&2
    exit 1
fi

rm -rf "$staging"
mkdir -p "$staging"
trap 'rm -rf "$staging"' EXIT HUP INT TERM

# .backup usa a API de backup online do SQLite e inclui dados ainda no WAL.
sqlite3 "$database" ".backup '$staging/pericias.db'"

integrity=$(sqlite3 "$staging/pericias.db" 'PRAGMA integrity_check;')
if [ "$integrity" != "ok" ]; then
    echo "A cópia do SQLite falhou na verificação de integridade." >&2
    exit 1
fi

if [ -d /data/uploads ]; then
    cp -a /data/uploads "$staging/uploads"
fi

if [ -d /data/keys ]; then
    cp -a /data/keys "$staging/keys"
fi

restic backup --host pericias-vps --tag pericias-daily "$staging"
restic forget --host pericias-vps --keep-daily 7 --keep-monthly 3 --prune

echo "Backup diário criptografado concluído e retenção aplicada."
