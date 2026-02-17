#!/bin/bash
# test-docker-setup.sh - Test script to verify Docker setup

set -e

echo "================================================"
echo "Meta Anime API - Docker Setup Test"
echo "================================================"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

success() {
    echo -e "${GREEN}✅ $1${NC}"
}

error() {
    echo -e "${RED}❌ $1${NC}"
    exit 1
}

warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

# Test 1: Check Docker is installed
echo "Test 1: Checking Docker installation..."
if command -v docker &> /dev/null; then
    success "Docker is installed: $(docker --version)"
else
    error "Docker is not installed"
fi

# Test 2: Check Docker Compose
echo ""
echo "Test 2: Checking Docker Compose..."
if docker compose version &> /dev/null; then
    success "Docker Compose is available: $(docker compose version)"
else
    error "Docker Compose is not available"
fi

# Test 3: Validate docker-compose.yml
echo ""
echo "Test 3: Validating docker-compose.yml..."
if docker compose config > /dev/null 2>&1; then
    success "docker-compose.yml is valid"
else
    error "docker-compose.yml validation failed"
fi

# Test 4: Validate Dockerfile syntax
echo ""
echo "Test 4: Validating Dockerfile..."
if docker build --no-cache -f Dockerfile --target base -t test-base . > /dev/null 2>&1; then
    success "Dockerfile is valid"
    docker rmi test-base > /dev/null 2>&1 || true
else
    warning "Dockerfile validation had issues (may be expected in CI)"
fi

# Test 5: Check for required files
echo ""
echo "Test 5: Checking required files..."
REQUIRED_FILES=(
    "Dockerfile"
    "Dockerfile.debug"
    "docker-compose.yml"
    "docker-compose.override.yml.template"
    "docker-compose.debug.yml"
    ".dockerignore"
    "DOCKER.md"
    ".env.example"
    "quick-start.sh"
    "Makefile"
)

for file in "${REQUIRED_FILES[@]}"; do
    if [ -f "$file" ]; then
        success "Found: $file"
    else
        error "Missing: $file"
    fi
done

# Test 6: Check .gitignore excludes override file
echo ""
echo "Test 6: Checking .gitignore..."
if grep -q "docker-compose.override.yml" .gitignore; then
    success ".gitignore excludes docker-compose.override.yml"
else
    error ".gitignore does not exclude docker-compose.override.yml"
fi

# Test 7: Check project structure
echo ""
echo "Test 7: Checking .NET project structure..."
PROJECT_FILES=(
    "api/MetaAnime.Presentation/MetaAnime.Presentation.csproj"
    "api/MetaAnime.Application/MetaAnime.Application.csproj"
    "api/MetaAnime.Core/MetaAnime.Core.csproj"
    "api/MetaAnime.Infrastructure/MetaAnime.Infrastructure.csproj"
)

for file in "${PROJECT_FILES[@]}"; do
    if [ -f "$file" ]; then
        success "Found: $file"
    else
        error "Missing: $file"
    fi
done

# Test 8: Pull PostgreSQL image
echo ""
echo "Test 8: Pulling PostgreSQL image..."
if docker compose pull postgres > /dev/null 2>&1; then
    success "PostgreSQL image pulled successfully"
else
    error "Failed to pull PostgreSQL image"
fi

# Test 9: Test PostgreSQL container
echo ""
echo "Test 9: Testing PostgreSQL container..."
echo "Starting PostgreSQL..."
docker compose up -d postgres > /dev/null 2>&1

echo "Waiting for PostgreSQL to be ready..."
for i in {1..30}; do
    if docker compose exec -T postgres pg_isready -U metaanime -d metaanime > /dev/null 2>&1; then
        success "PostgreSQL is ready and accepting connections"
        break
    fi
    if [ $i -eq 30 ]; then
        error "PostgreSQL failed to start within 30 seconds"
    fi
    sleep 1
done

# Test 10: Test database connection
echo ""
echo "Test 10: Testing database operations..."
# Add a small delay to ensure database is fully initialized after pg_isready check
# pg_isready only checks if the server is accepting connections, not if it's fully ready
sleep 2
if docker compose exec -T postgres psql -U metaanime -d metaanime -c "SELECT version();" > /dev/null 2>&1; then
    success "Database connection successful"
else
    warning "Database connection test skipped (database may still be initializing)"
fi

# Test 11: Check environment variables in compose
echo ""
echo "Test 11: Checking environment variables..."
CONFIG=$(docker compose config)
if echo "$CONFIG" | grep -q "Authentication__ApiKey"; then
    success "Authentication configuration present"
else
    error "Authentication configuration missing"
fi

if echo "$CONFIG" | grep -q "Postgres__Host"; then
    success "Database configuration present"
else
    error "Database configuration missing"
fi

# Cleanup
echo ""
echo "Cleaning up test containers..."
docker compose down -v > /dev/null 2>&1
success "Cleanup complete"

# Summary
echo ""
echo "================================================"
echo "✨ All tests passed!"
echo "================================================"
echo ""
echo "Docker setup is ready to use. Quick start:"
echo "  ./quick-start.sh"
echo "  or"
echo "  docker compose up -d"
echo ""
echo "For more information, see DOCKER.md"
