#!/bin/sh
# The certificate of production (ADR 0055): asked of Let's Encrypt for DOMAIN over the webroot nginx serves on port 80,
# kept while it has more than 30 days left, looked at twice a day. Accepting Let's Encrypt's subscriber agreement is the
# operator's decision: CERTBOT_AGREE_TOS=Y in the env file, or this does nothing.
set -u

if [ "${CERTBOT_AGREE_TOS:-}" != "Y" ]; then
    echo "CERTBOT_AGREE_TOS is not Y: no certificate is asked for. nginx keeps its self-signed one." >&2
    exec sleep infinity
fi

while true; do
    certbot certonly --webroot -w /var/www/acme -d "$DOMAIN" --email "$CERTBOT_EMAIL" \
        --agree-tos --non-interactive --keep-until-expiring || echo "certbot failed; trying again in 12 hours" >&2
    sleep 43200
done
