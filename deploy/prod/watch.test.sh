#!/bin/bash
# The test of the watchdog (ADR 0056): one round of watch.sh against stand-ins (watch-test-servers.mjs), where every
# check fails, then the same round again, then a round where every check passes. Needs bash, curl, openssl and Node,
# as the CI's runner and Git Bash have them; the check of the disk uses the disk of the working directory.
#   bash deploy/prod/watch.test.sh
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
http_port="${WATCH_TEST_HTTP_PORT:-18381}"
smtp_port="${WATCH_TEST_SMTP_PORT:-18325}"
mails="$work/mails.txt"
failures=0

node "$here/watch-test-servers.mjs" "$http_port" "$smtp_port" "$mails" > "$work/servers.log" 2>&1 &
servers=$!
trap 'kill $servers 2>/dev/null; rm -rf "$work"' EXIT
for _ in $(seq 1 50); do grep -q ready "$work/servers.log" 2>/dev/null && break; sleep 0.1; done

expect() {
    if [ "$2" = "$3" ]; then
        echo "ok   $1"
    else
        echo "FAIL $1: expected $2, got $3"
        failures=$((failures + 1))
    fi
}

# A certificate that expires tomorrow, and one that has a year. Git Bash would read /CN as a path: there it is //CN.
subject="/CN=test"
[ -n "${MSYSTEM:-}" ] && subject="//CN=test"
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$work/k1" -out "$work/expiring.pem" -days 1 -subj "$subject" 2>/dev/null
openssl req -x509 -newkey rsa:2048 -nodes -keyout "$work/k2" -out "$work/fresh.pem" -days 365 -subj "$subject" 2>/dev/null
expect "the two certificates are made" 2 "$(ls "$work"/*.pem | wc -l | tr -d ' ')"
printf 'test-key' > "$work/seq_api_key"

round() {
    WATCH_ONCE=1 WATCH_STATE_DIR="$1" WATCH_HEALTH_URL="http://127.0.0.1:$http_port/$2/health/ready" \
    WATCH_SEQ_URL="http://127.0.0.1:$http_port/$3" WATCH_SEQ_KEY_FILE="$work/seq_api_key" \
    WATCH_DISK_PATHS="$work" WATCH_DISK_PERCENT="$4" WATCH_CERT_FILE="$5" WATCH_MAIL_PASSWORD_FILE=/dev/null \
    DOMAIN=test.example ALERT_MAIL_TO=oncall@test.example MAIL_HOST=127.0.0.1 MAIL_PORT="$smtp_port" \
    MAIL_FROM=watch@test.example MAIL_ENABLE_TLS=false \
        bash "$here/watch.sh" > "$work/round.log" 2>&1
}

subjects() { grep -c '^Subject: \[BillingRecon alert\]' "$mails"; }

# Everything wrong: the API down, 25 errors, a disk over its limit, the log 90% used, the last backup 30 hours old,
# the certificate a day from its end.
mkdir -p "$work/bad"
echo 90 > "$work/bad/db-log-used-percent"
echo $(( $(date -u +%s) - 30 * 3600 )) > "$work/bad/backup-last-ok"
round "$work/bad" down busy 0 "$work/expiring.pem"
expect "a bad round exits 1" 1 $?
expect "six alerts are mailed" 6 "$(subjects)"
for check in health errors disk log backup cert; do
    expect "the alert of $check is mailed" 1 "$(grep -c "^Subject: \[BillingRecon alert\] $check on test.example" "$mails")"
done
expect "the mail goes to the operator" 6 "$(grep -c '^To: oncall@test.example' "$mails")"
expect "it says what to do first" 6 "$(grep -c 'docs/runbook.md section 20.3' "$mails")"
expect "the certificate is said to expire, not to be missing" 1 "$(grep -c 'certificate expires' "$mails")"

# The same problems five minutes later: in the log again, and no second mail the same day.
round "$work/bad" down busy 0 "$work/expiring.pem"
expect "the next bad round exits 1" 1 $?
expect "no mail is sent twice a day" 6 "$(subjects)"
expect "the problems are logged again" 6 "$(grep -c ' ALERT ' "$work/round.log")"

# Everything right.
mkdir -p "$work/good"
echo 12 > "$work/good/db-log-used-percent"
date -u +%s > "$work/good/backup-last-ok"
round "$work/good" ok calm 100 "$work/fresh.pem"
good=$?
expect "a good round exits 0" 0 "$good"
expect "a good round mails nothing" 6 "$(subjects)"
expect "a good round logs six checks as ok" 6 "$(grep -c ' ok' "$work/round.log")"

# Without a mail server the problem is in the log only.
mkdir -p "$work/nomail"
WATCH_ONCE=1 WATCH_STATE_DIR="$work/nomail" WATCH_HEALTH_URL="http://127.0.0.1:$http_port/down/health/ready" \
WATCH_SEQ_KEY_FILE=/dev/null WATCH_DISK_PATHS="$work" WATCH_DISK_PERCENT=100 WATCH_CERT_FILE="$work/fresh.pem" \
    bash "$here/watch.sh" > "$work/round.log" 2>&1
expect "without a mail server nothing is mailed" 6 "$(subjects)"
expect "and the log says so" 1 "$(grep -c 'the alert is in this log only' "$work/round.log" | awk '{ print ($1 >= 1) }')"
expect "the count of errors is off without a key" 1 "$(grep -c 'errors not checked: no Seq API key' "$work/round.log")"

if [ "$failures" -gt 0 ]; then
    echo "$failures checks of the watchdog failed"
    exit 1
fi

echo "the watchdog passed every check"
