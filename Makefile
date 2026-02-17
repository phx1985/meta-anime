# Makefile for Meta Anime API Docker operations

.PHONY: help build up down restart logs ps clean setup-dev test-build

# Default target
help:
	@echo "Meta Anime API - Docker Management"
	@echo ""
	@echo "Available targets:"
	@echo "  make build       - Build the Docker image"
	@echo "  make up          - Start services in detached mode"
	@echo "  make down        - Stop and remove services"
	@echo "  make restart     - Restart services"
	@echo "  make logs        - View logs (follow mode)"
	@echo "  make ps          - Show running services"
	@echo "  make clean       - Stop services and remove volumes (⚠️  deletes data)"
	@echo "  make setup-dev   - Setup local development environment"
	@echo "  make test-build  - Test build without cache"
	@echo "  make db-backup   - Backup PostgreSQL database"
	@echo "  make db-restore  - Restore PostgreSQL database"
	@echo "  make db-shell    - Open PostgreSQL shell"

# Build the Docker image
build:
	docker compose build

# Start services
up:
	docker compose up -d

# Stop services
down:
	docker compose down

# Restart services
restart:
	docker compose restart

# View logs
logs:
	docker compose logs -f meta-anime-api

# Show running services
ps:
	docker compose ps

# Clean up (removes volumes)
clean:
	@echo "⚠️  WARNING: This will delete all data including the database!"
	@read -p "Are you sure? (y/N) " confirm; \
	if [ "$$confirm" = "y" ] || [ "$$confirm" = "Y" ]; then \
		docker compose down -v; \
		echo "✅ Cleaned up"; \
	else \
		echo "Cancelled"; \
	fi

# Setup local development environment
setup-dev:
	@if [ ! -f docker-compose.override.yml ]; then \
		cp docker-compose.override.yml.template docker-compose.override.yml; \
		echo "✅ Created docker-compose.override.yml"; \
		echo "⚠️  Please edit docker-compose.override.yml with your configuration"; \
	else \
		echo "⚠️  docker-compose.override.yml already exists"; \
	fi

# Test build without cache
test-build:
	docker compose build --no-cache

# Backup database
db-backup:
	@mkdir -p backups
	@BACKUP_FILE=backups/metaanime-backup-$$(date +%Y%m%d-%H%M%S).sql; \
	docker compose exec -T postgres pg_dump -U metaanime metaanime > $$BACKUP_FILE; \
	echo "✅ Database backed up to $$BACKUP_FILE"

# Restore database (Usage: make db-restore BACKUP=backups/metaanime-backup-YYYYMMDD-HHMMSS.sql)
db-restore:
	@if [ -z "$(BACKUP)" ]; then \
		echo "❌ Error: Please specify BACKUP file"; \
		echo "Usage: make db-restore BACKUP=backups/metaanime-backup-YYYYMMDD-HHMMSS.sql"; \
		exit 1; \
	fi
	@if [ ! -f "$(BACKUP)" ]; then \
		echo "❌ Error: Backup file $(BACKUP) not found"; \
		exit 1; \
	fi
	docker compose exec -T postgres psql -U metaanime metaanime < $(BACKUP)
	@echo "✅ Database restored from $(BACKUP)"

# Open PostgreSQL shell
db-shell:
	docker compose exec postgres psql -U metaanime -d metaanime

# Debug mode (with docker-compose.debug.yml)
debug:
	docker compose -f docker-compose.yml -f docker-compose.debug.yml up

# Install/Update dependencies
update-deps:
	docker compose exec meta-anime-api dotnet restore
