#!/bin/bash
# quick-start.sh - Quick start script for Meta Anime with Docker

set -e

echo "================================================"
echo "Meta Anime - Docker Quick Start"
echo "================================================"
echo ""

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed. Please install Docker first:"
    echo "   https://docs.docker.com/get-docker/"
    exit 1
fi

# Check if Docker Compose is available
if ! docker compose version &> /dev/null; then
    echo "❌ Docker Compose is not available. Please install Docker Compose:"
    echo "   https://docs.docker.com/compose/install/"
    exit 1
fi

echo "✅ Docker is installed: $(docker --version)"
echo "✅ Docker Compose is installed: $(docker compose version)"
echo ""

# ── Mode selection ────────────────────────────────────────────────────────────
echo "What would you like to start?"
echo "  1) Full stack – PostgreSQL + API + Angular UI  (recommended)"
echo "  2) API only   – PostgreSQL + API"
echo ""
read -p "Enter choice [1/2] (default: 1): " -r MODE_CHOICE
MODE_CHOICE="${MODE_CHOICE:-1}"
echo ""

if [[ "$MODE_CHOICE" == "2" ]]; then
    # ── API-only mode ─────────────────────────────────────────────────────────
    echo "Starting API-only mode (from api/ directory)..."
    echo ""

    if [ ! -d "api" ]; then
        echo "❌ 'api' directory not found. Please run this script from the repo root."
        exit 1
    fi

    cd api

    if [ ! -f docker-compose.override.yml ]; then
        echo "⚠️  docker-compose.override.yml not found."
        echo ""
        read -p "Do you want to create it from the template? (y/n) " -n 1 -r
        echo ""
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            cp docker-compose.override.yml.template docker-compose.override.yml
            echo "✅ Created api/docker-compose.override.yml"
            echo ""
            echo "⚠️  IMPORTANT: Edit api/docker-compose.override.yml to add your configuration:"
            echo "   - Authentication__ApiKey (required)"
            echo "   - Sonarr, Plex, MyAnimeList credentials (optional)"
            echo ""
            read -p "Press Enter to continue with default configuration, or Ctrl+C to exit and configure..."
        fi
    fi

    echo "================================================"
    echo "Starting API services..."
    echo "================================================"
    echo ""

    docker compose up -d --build

    echo ""
    echo "================================================"
    echo "✅ API services are starting!"
    echo "================================================"
    echo ""
    echo "Service Status:"
    docker compose ps
    echo ""
    sleep 5
    docker compose logs meta-anime-api --tail=50
    echo ""
    echo "================================================"
    echo "🎉 Meta Anime API is ready!"
    echo "================================================"
    echo ""
    echo "🌐 Access Points:"
    echo "   - Swagger UI: http://localhost:8080/swagger"
    echo "   - API:        http://localhost:8080"
    echo ""
    echo "🔑 Authentication:"
    echo "   Add header: X-Api-Key: <your-api-key>"
    echo "   (Check docker-compose.yml or docker-compose.override.yml for the key)"
    echo ""
    echo "🛠️  Useful Commands (run from api/ directory):"
    echo "   - View logs:     docker compose logs -f meta-anime-api"
    echo "   - Stop services: docker compose down"
    echo "   - Restart:       docker compose restart meta-anime-api"
    echo ""

else
    # ── Full-stack mode (default) ─────────────────────────────────────────────
    echo "Starting full-stack mode (PostgreSQL + API + Angular UI)..."
    echo ""

    # Must be run from the repo root (docker-compose.dev.yml lives here)
    if [ ! -f docker-compose.dev.yml ]; then
        echo "❌ docker-compose.dev.yml not found."
        echo "   Please run this script from the repository root directory."
        exit 1
    fi

    echo "================================================"
    echo "Building and starting all services..."
    echo "(First run may take a few minutes to build images)"
    echo "================================================"
    echo ""

    docker compose -f docker-compose.dev.yml up --build -d

    echo ""
    echo "================================================"
    echo "✅ All services are starting!"
    echo "================================================"
    echo ""
    echo "Service Status:"
    docker compose -f docker-compose.dev.yml ps
    echo ""
    sleep 5
    docker compose -f docker-compose.dev.yml logs meta-anime-api --tail=20
    echo ""
    echo "================================================"
    echo "🎉 Meta Anime full stack is ready!"
    echo "================================================"
    echo ""
    echo "🌐 Access Points:"
    echo "   - Angular UI: http://localhost:4200"
    echo "   - API:        http://localhost:8080"
    echo "   - Swagger UI: http://localhost:8080/swagger"
    echo ""
    echo "🔑 Authentication:"
    echo "   Add header: X-Api-Key: CHANGE_ME_SUPER_SECRET_KEY"
    echo "   ⚠️  This is the default dev key — change Authentication__ApiKey before any non-local use!"
    echo ""
    echo "♻️  Hot-Reload:"
    echo "   Edit files under ui/meta-anime-ui/src — the browser refreshes automatically."
    echo ""
    echo "🛠️  Useful Commands (run from repo root):"
    echo "   - View UI logs:  docker compose -f docker-compose.dev.yml logs -f meta-anime-ui"
    echo "   - View API logs: docker compose -f docker-compose.dev.yml logs -f meta-anime-api"
    echo "   - Stop services: docker compose -f docker-compose.dev.yml down"
    echo ""
    echo "📚 Documentation: See DOCKER.md for full details."
    echo ""
fi

