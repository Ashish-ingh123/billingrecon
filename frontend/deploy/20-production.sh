#!/bin/sh
# Production only (ADR 0055), when DOMAIN is set: nginx serves the certificate of Let's Encrypt for that name as soon as
# certbot has one, and reads a renewed one within a minute. Until the first certificate exists it serves the
# self-signed one of 10-certificate.sh, so that port 80 can answer Let's Encrypt's challenge. Without DOMAIN (the local
# run) this does nothing.
set -eu

[ -n "${DOMAIN:-}" ] || exit 0

live="/etc/letsencrypt/live/$DOMAIN"
dir=/etc/nginx/certs

link() {
    ln -sf "$live/fullchain.pem" "$dir/tls.crt"
    ln -sf "$live/privkey.pem" "$dir/tls.key"
    cp -p "$live/fullchain.pem" "$dir/served.pem"
}

if [ -s "$live/fullchain.pem" ]; then
    link
fi

(
    while true; do
        sleep 60
        if [ -s "$live/fullchain.pem" ] && ! cmp -s "$live/fullchain.pem" "$dir/served.pem"; then
            link
            nginx -s reload && echo "nginx now serves the certificate of $DOMAIN."
        fi
    done
) &
