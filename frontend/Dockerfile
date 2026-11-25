# syntax=docker/dockerfile:1
# The frontend of the one-command local run (ADR 0045 point 2): the app built with Node, served by nginx, which ends TLS
# and passes /api to the API as its known proxy. Build context: frontend/.

# The build runs on Debian, not Alpine: on Alpine (musl) the native bindings of Vite 8 and its bundler Rolldown failed
# (the owner's first docker compose up, 2026-11-24; ADR 0045 built note). Only the build stage; nginx stays on Alpine.
FROM node:24-bookworm-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
# The optional packages are the platform's native bindings.
RUN npm ci --include=optional --no-audit --no-fund
COPY . .
# What the build is given (ADR 0047): the address "Contact support" writes to on the sign-in page; empty leaves the link out.
ARG VITE_SUPPORT_EMAIL=""
ENV VITE_SUPPORT_EMAIL=$VITE_SUPPORT_EMAIL
RUN npm run build

FROM nginx:1.29-alpine
# openssl makes the local certificate on first start (deploy/10-certificate.sh).
RUN apk add --no-cache openssl
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY deploy/10-certificate.sh /docker-entrypoint.d/10-certificate.sh
# In production, the certificate of Let's Encrypt once certbot has it (deploy/20-production.sh, ADR 0055).
COPY deploy/20-production.sh /docker-entrypoint.d/20-production.sh
RUN chmod +x /docker-entrypoint.d/10-certificate.sh /docker-entrypoint.d/20-production.sh
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80 443
# 127.0.0.1, not localhost: localhost can resolve to ::1, and nginx listens on IPv4 only (deploy/nginx.conf).
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD wget -q --spider --no-check-certificate https://127.0.0.1/ || exit 1
