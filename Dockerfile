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
    -o /app/publish \
    --no-self-contained

# =============================================================================
# Stage 3: Final production image (linuxserver.io base)
# =============================================================================
FROM ghcr.io/linuxserver/baseimage-ubuntu:noble

# Install PostgreSQL, openssl, and .NET 10 ASP.NET Core Runtime
# hadolint ignore=DL3008,DL3009
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        postgresql \
        postgresql-client \
        openssl \
        wget \
        ca-certificates \
        jq \
    && wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
        -O /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        aspnetcore-runtime-10.0 \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

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

# Expose only the unified HTTP port
EXPOSE 8080
