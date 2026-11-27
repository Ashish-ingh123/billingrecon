#!/bin/bash
# The watchdog of production (ADR 0056, docs/runbook.md section 20): every WATCH_EVERY_SECONDS it checks what the API
# cannot see of itself, and mails the operator (ALERT_MAIL_TO) through the mail server of the API (MAIL_*) once a day
# per problem. Every check writes one line to the log (`dc logs watch`). The server itself being down is not seen
# here: the external uptime monitor of section 20.2 sees it.
#
#   health   /health/ready through nginx answers 200
#   errors   fewer than WATCH_ERRORS errors in Seq in the last 15 minutes (needs the seq_api_key secret; empty: off)
#   disk     the volumes of the database and its backups are less than WATCH_DISK_PERCENT full
#   log      the database's transaction log is less than WATCH_LOG_PERCENT used (written by the backup loop)
#   backup   the last verified full backup is younger than WATCH_BACKUP_HOURS (written by the backup loop)
#   cert     the certificate has more than WATCH_CERT_DAYS days left
#
# WATCH_ONCE=1 runs one round and exits 1 when a check failed: the tests run it so (OperationsWatchTests).
set -uo pipefail

every="${WATCH_EVERY_SECONDS:-300}"
state="${WATCH_STATE_DIR:-/ops}"
health_url="${WATCH_HEALTH_URL:-https://${DOMAIN:-localhost}/health/ready}"
health_resolve="${WATCH_HEALTH_RESOLVE:-}"
seq_url="${WATCH_SEQ_URL:-http://seq}"
seq_key_file="${WATCH_SEQ_KEY_FILE:-/run/secrets/seq_api_key}"
disk_paths="${WATCH_DISK_PATHS:-/var/opt/mssql /var/opt/backups}"
cert_file="${WATCH_CERT_FILE:-/etc/letsencrypt/live/${DOMAIN:-localhost}/fullchain.pem}"
mail_password_file="${WATCH_MAIL_PASSWORD_FILE:-/run/secrets/mail_password}"

mkdir -p "$state"
failed=0

say() { echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) $*"; }

# Mails one alert, at most once a day for each check; without an address or a server it is in the log only.
alert() {
    local check="$1" text="$2"
    local mark="$state/alerted-$check-$(date -u +%Y%m%d)"
    say "ALERT $check: $text"
    failed=1
    [ -e "$mark" ] && return 0
    if [ -z "${ALERT_MAIL_TO:-}" ] || [ -z "${MAIL_HOST:-}" ]; then
        say "ALERT_MAIL_TO or MAIL_HOST is empty: the alert is in this log only"
        touch "$mark"
        return 0
    fi

    local message
    message="$(mktemp)"
    printf 'From: %s\r\nTo: %s\r\nSubject: [BillingRecon alert] %s on %s\r\nDate: %s\r\n\r\n%s\r\n\r\nWhat to do first: docs/runbook.md section 20.3.\r\n' \
        "$MAIL_FROM" "$ALERT_MAIL_TO" "$check" "${DOMAIN:-this server}" "$(date -u -R)" "$text" > "$message"
    local tls=(--ssl-reqd)
    [ "${MAIL_ENABLE_TLS:-true}" = "false" ] && tls=()
    local login=()
    if [ -n "${MAIL_USER:-}" ]; then
        login=(--user "$MAIL_USER:$(cat "$mail_password_file" 2>/dev/null)")
    fi

    if curl -sS --max-time 30 "${tls[@]}" "${login[@]}" --url "smtp://$MAIL_HOST:${MAIL_PORT:-587}" \
        --mail-from "$MAIL_FROM" --mail-rcpt "$ALERT_MAIL_TO" --upload-file "$message"; then
        touch "$mark"
    else
        say "THE ALERT MAIL COULD NOT BE SENT"
    fi

    rm -f "$message"
}

check_health() {
    local resolve=()
    [ -n "$health_resolve" ] && resolve=(--resolve "$health_resolve")
    local code
    code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "${resolve[@]}" "$health_url" 2>/dev/null)"
    if [ "$code" = "200" ]; then
        say "health ok"
    else
        alert health "$health_url answered ${code:-nothing} instead of 200: the API is down, not ready, or nginx is."
    fi
}

check_errors() {
    local key
    key="$(cat "$seq_key_file" 2>/dev/null | tr -d '\r\n')"
    if [ -z "$key" ]; then
        say "errors not checked: no Seq API key"
        return
    fi

    local since body count
    since="$(date -u -d '-15 minutes' +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -v-15M +%Y-%m-%dT%H:%M:%SZ)"
    if ! body="$(curl -sSf --max-time 20 -H "X-Seq-ApiKey: $key" -G "$seq_url/api/events" \
        --data-urlencode "filter=@Level = 'Error'" --data-urlencode "fromDateUtc=$since" \
        --data-urlencode "count=${WATCH_ERRORS:-20}" --data-urlencode "render=false" 2>/dev/null)"; then
        alert errors "Seq at $seq_url did not answer the count of errors: the log store may be down."
        return
    fi

    count="$(printf '%s' "$body" | grep -o '"Id"' | wc -l | tr -d ' ')"
    if [ "$count" -ge "${WATCH_ERRORS:-20}" ]; then
        alert errors "$count or more errors in the last 15 minutes in Seq. Read them there (section 19.8)."
    else
        say "errors ok: $count in 15 minutes"
    fi
}

check_disk() {
    local path used
    for path in $disk_paths; do
        used="$(df -P "$path" 2>/dev/null | awk 'NR == 2 { gsub("%", "", $5); print $5 }')"
        if [ -z "$used" ]; then
            alert disk "The disk of $path could not be read."
        elif [ "$used" -ge "${WATCH_DISK_PERCENT:-85}" ]; then
            alert disk "The disk of $path is $used% full (the limit is ${WATCH_DISK_PERCENT:-85}%)."
        else
            say "disk ok: $path $used%"
        fi
    done
}

check_database() {
    local log_file="$state/db-log-used-percent" backup_file="$state/backup-last-ok"
    if [ -r "$log_file" ]; then
        local used
        used="$(cut -d. -f1 < "$log_file" | tr -d '\r\n ')"
        if [ -n "$used" ] && [ "$used" -ge "${WATCH_LOG_PERCENT:-80}" ]; then
            alert log "The database's transaction log is $used% used: log backups may be failing (section 20.3)."
        else
            say "log ok: ${used:-?}%"
        fi
    else
        alert log "The backup loop has not written the use of the transaction log ($log_file)."
    fi

    if [ -r "$backup_file" ]; then
        local last now age
        last="$(tr -d '\r\n ' < "$backup_file")"
        now="$(date -u +%s)"
        age=$(( (now - last) / 3600 ))
        if [ "$age" -ge "${WATCH_BACKUP_HOURS:-26}" ]; then
            alert backup "The last verified full backup is $age hours old (the limit is ${WATCH_BACKUP_HOURS:-26})."
        else
            say "backup ok: $age hours old"
        fi
    else
        alert backup "No verified full backup is recorded ($backup_file)."
    fi
}

check_cert() {
    if [ ! -r "$cert_file" ]; then
        alert cert "The certificate $cert_file is not there."
        return
    fi

    local days="${WATCH_CERT_DAYS:-14}"
    if openssl x509 -in "$cert_file" -noout -checkend $(( days * 86400 )) > /dev/null; then
        say "cert ok: more than $days days left"
    else
        alert cert "The certificate expires $(openssl x509 -in "$cert_file" -noout -enddate | cut -d= -f2): fewer than $days days. Certbot renews at 30 days, so its renewal is failing (dc logs certbot)."
    fi
}

while true; do
    failed=0
    check_health
    check_errors
    check_disk
    check_database
    check_cert

    # The marks of earlier days are forgotten.
    find "$state" -name 'alerted-*' -mtime +2 -delete 2>/dev/null

    if [ "${WATCH_ONCE:-0}" = "1" ]; then
        exit "$failed"
    fi

    sleep "$every"
done
