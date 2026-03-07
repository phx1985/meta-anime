# =============================================================================
# Stage 1: Build Angular UI
# =============================================================================
FROM --platform=$BUILDPLATFORM node:22-alpine AS ui-builder

WORKDIR /src
COPY ui/meta-anime-ui/package*.json ./
RUN npm ci
COPY ui/meta-anime-ui/ ./
RUN npm run build

# =============================================================================
# Stage 2: Build .NET API
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-builder

WORKDIR /src
# Copy the api projects and the shared ServiceDefaults project.
# MetaAnime.Presentation references ServiceDefaults via ../../MetaAnime.ServiceDefaults,
# so it must sit two levels above the .csproj file inside the build container.
COPY api/ ./
COPY MetaAnime.ServiceDefaults/ /MetaAnime.ServiceDefaults/
RUN dotnet publish MetaAnime.Presentation/MetaAnime.Presentation.csproj \
    -c Release \
    -r linux-musl-x64 \
    --self-contained true \
    -o /app/publish

# =============================================================================
# Stage 3: Final production image (Alpine + linuxserver s6-overlay base)
# =============================================================================
FROM ghcr.io/linuxserver/baseimage-alpine:3.21

# Install PostgreSQL server + client, openssl (for key generation), and jq (for JSON parsing)
RUN apk add --no-cache \
        postgresql17 \
        postgresql17-client \
        openssl \
        jq \
        icu-libs

# Copy compiled .NET application
COPY --from=api-builder /app/publish /app

# Copy Angular static files into the .NET wwwroot
COPY --from=ui-builder /src/dist/meta-anime-ui/browser /app/wwwroot

# Copy s6 service definitions and init scripts
COPY root/ /

# Ensure all s6 scripts are executable
RUN chmod +x /etc/cont-init.d/10-init-config \
    && chmod +x /etc/services.d/postgres/run \
    && chmod +x /etc/services.d/postgres/finish \
    && chmod +x /etc/services.d/metaanime/run \
    && chmod +x /etc/services.d/metaanime/finish

# Persistent data volume (PostgreSQL data + generated config)
VOLUME ["/config"]

# Health check — probes the .NET app via Alpine's built-in wget (no curl needed).
# start-period accounts for first-boot PostgreSQL init + EF migrations.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/api/health || exit 1

# Expose only the unified HTTP port
EXPOSE 8080
