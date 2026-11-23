# syntax=docker/dockerfile:1
# The frontend of the one-command local run (ADR 0045 point 2): the app built with Node, served by nginx, which ends TLS
# and passes /api to the API as its known proxy. Build context: frontend/.

FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM nginx:1.29-alpine
# openssl makes the local certificate on first start (deploy/10-certificate.sh).
RUN apk add --no-cache openssl
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY deploy/10-certificate.sh /docker-entrypoint.d/10-certificate.sh
RUN chmod +x /docker-entrypoint.d/10-certificate.sh
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80 443
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD wget -q --spider --no-check-certificate https://localhost/ || exit 1
