# The API container image (ADR 0017). Build from the repository root, which is the context:
#
#   docker build -f src/BillingRecon.Api/Dockerfile -t billingrecon-api .
#
# Multi-stage: the SDK image restores, builds and publishes; the ASP.NET Core runtime image runs the result. The
# runtime image is the Debian-based one, which ships the time zone data the API needs at startup (Asia/Kolkata,
# ADR 0010). curl is the one addition to it, for the health check. The container runs as the image's non-root user
# and listens on 8080, the image's default. It needs Database__ConnectionString and Jwt__SigningKey in its
# environment and refuses to start without them (docs/runbook.md sections 6 and 9).

# The tags follow the major version. global.json pins the SDK feature band (10.0.4xx on 2026-10-17) and must accept
# the SDK the tag carries; if they ever drift apart, pass the exact tag: --build-arg SDK_TAG=10.0.402
ARG SDK_TAG=10.0
ARG RUNTIME_TAG=10.0

# ---- build --------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${SDK_TAG} AS build
WORKDIR /source

# The files that decide the restore come first, so that a code change does not invalidate the package layer.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Directory.Build.props src/BannedSymbols.txt src/
COPY src/BillingRecon.Domain/BillingRecon.Domain.csproj src/BillingRecon.Domain/
COPY src/BillingRecon.Application/BillingRecon.Application.csproj src/BillingRecon.Application/
COPY src/BillingRecon.Infrastructure/BillingRecon.Infrastructure.csproj src/BillingRecon.Infrastructure/
COPY src/BillingRecon.Api/BillingRecon.Api.csproj src/BillingRecon.Api/
RUN dotnet restore src/BillingRecon.Api/BillingRecon.Api.csproj

COPY src/ src/
RUN dotnet publish src/BillingRecon.Api/BillingRecon.Api.csproj --configuration Release --no-restore --output /app

# ---- runtime ------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${RUNTIME_TAG} AS runtime

# curl for the HEALTHCHECK below. Nothing else is added to the runtime image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# The non-root user the image defines (uid 1654). ASPNETCORE_HTTP_PORTS=8080 is the image's default.
USER app
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "BillingRecon.Api.dll"]
