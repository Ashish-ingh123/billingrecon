#!/bin/sh
# The local certificate (ADR 0045 point 2): made once, on the first start, into the certs volume, self-signed for
# localhost and for the compose name of this container (the demo seed calls it so). A browser warns once about it.
set -eu

dir=/etc/nginx/certs
if [ ! -s "$dir/tls.crt" ] || [ ! -s "$dir/tls.key" ]; then
    mkdir -p "$dir"
    openssl req -x509 -newkey rsa:2048 -nodes -days 825 -sha256 \
        -subj "/CN=localhost" \
        -addext "subjectAltName=DNS:localhost,DNS:frontend,IP:127.0.0.1" \
        -keyout "$dir/tls.key" -out "$dir/tls.crt"
    chmod 644 "$dir/tls.crt"
    echo "Made a local certificate for localhost."
fi
