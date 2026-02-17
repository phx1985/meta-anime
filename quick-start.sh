#!/bin/bash
# quick-start.sh - Quick start script for Meta Anime API with Docker

set -e

echo "================================================"
echo "Meta Anime API - Docker Quick Start"
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

# Check if docker-compose.override.yml exists
if [ ! -f docker-compose.override.yml ]; then
    echo "⚠️  docker-compose.override.yml not found."
    echo ""
    read -p "Do you want to create it from the template? (y/n) " -n 1 -r
    echo ""
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        cp docker-compose.override.yml.template docker-compose.override.yml
        echo "✅ Created docker-compose.override.yml"
        echo ""
        echo "⚠️  IMPORTANT: Please edit docker-compose.override.yml to add your configuration:"
        echo "   - Authentication__ApiKey (required)"
        echo "   - Sonarr, Plex, MyAnimeList credentials (optional)"
        echo ""
        read -p "Press Enter to continue with default configuration, or Ctrl+C to exit and configure..." 
    fi
fi

echo "================================================"
echo "Starting services..."
echo "================================================"
echo ""

# Build and start services
docker compose up -d --build

echo ""
echo "================================================"
echo "✅ Services are starting!"
echo "================================================"
echo ""
echo "Service Status:"
docker compose ps
echo ""
echo "📋 Checking logs:"
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
echo "   - API: http://localhost:8080"
echo ""
echo "🔑 Authentication:"
echo "   Add header: X-Api-Key: <your-api-key>"
echo "   (Check docker-compose.yml or docker-compose.override.yml for the key)"
echo ""
echo "📚 Documentation:"
echo "   - See DOCKER.md for detailed documentation"
echo "   - See README.md for API usage"
echo ""
echo "🛠️  Useful Commands:"
echo "   - View logs: docker compose logs -f meta-anime-api"
echo "   - Stop services: docker compose down"
echo "   - Restart: docker compose restart meta-anime-api"
echo "   - View database: docker compose exec postgres psql -U metaanime -d metaanime"
echo ""
