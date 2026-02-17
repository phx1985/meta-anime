# Docker Setup Guide

This guide explains how to run Meta-Anime API in Docker containers with PostgreSQL database.

## Quick Start

### Prerequisites

- Docker Engine 20.10+
- Docker Compose 2.0+

### Running with Docker Compose

1. **Build and start all services (API + PostgreSQL):**
   ```bash
   docker-compose up -d
   ```

2. **View logs:**
   ```bash
   docker-compose logs -f meta-anime-api
   ```

3. **Stop all services:**
   ```bash
   docker-compose down
   ```

4. **Stop and remove volumes (⚠️ this will delete your database):**
   ```bash
   docker-compose down -v
   ```

5. **Access the API:**
   - Swagger UI: http://localhost:8080/swagger
   - API: http://localhost:8080

## Configuration

### Environment Variables

The docker-compose.yml includes default configuration. **You must change the API key before deploying to production!**

Key environment variables:
- `Authentication__ApiKey` - API key for authentication (⚠️ CHANGE THIS!)
- `Postgres__*` - Database connection settings
- `Sonarr__*` - Sonarr integration settings
- `Plex__*` - Plex integration settings
- `MyAnimeList__*` - MyAnimeList API settings
- `Kometa__*` - Kometa integration settings

### Local Development with Override File

For local development with custom configuration:

1. **Copy the template:**
   ```bash
   cp docker-compose.override.yml.template docker-compose.override.yml
   ```

2. **Edit `docker-compose.override.yml` with your settings:**
   ```yaml
   services:
     meta-anime-api:
       environment:
         - Authentication__ApiKey=your_local_key
         - Sonarr__Host=http://your-sonarr:8989
         - Sonarr__ApiKey=your_api_key
         # ... other settings
   ```

3. **Run with override:**
   ```bash
   docker-compose up -d
   ```

   Docker Compose automatically uses `docker-compose.override.yml` if it exists.

**Note:** `docker-compose.override.yml` is gitignored and won't be committed to version control.

## Debugging in Docker

### Option 1: Using docker-compose.debug.yml

For development with hot reload:

```bash
# Start with debug configuration
docker-compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.debug.yml up

# The application will rebuild automatically when you change code
```

### Option 2: Using Visual Studio / VS Code

1. **Visual Studio 2022:**
   - Open the solution
   - Select "Docker Compose" from the debug target dropdown
   - Press F5 to start debugging

2. **VS Code:**
   - Install Docker extension
   - Open the command palette (Ctrl+Shift+P)
   - Select "Docker: Compose Up"
   - Attach to the container for debugging

### Debugging Features

- **Hot Reload:** Code changes are automatically detected and the app rebuilds
- **Source Mapping:** Debug with breakpoints in your IDE
- **Port 4026:** Debugger port exposed for remote debugging

## Container Details

### Images Used

- **Base Image:** `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`
  - Lightweight Alpine Linux distribution
  - Official Microsoft .NET 10 runtime
  - Optimized for production

- **Build Image:** `mcr.microsoft.com/dotnet/sdk:10.0-alpine`
  - Complete .NET SDK for building
  - Only used during build stage (multi-stage build)

- **Database:** `postgres:17-alpine`
  - Latest PostgreSQL 17 on Alpine Linux
  - Lightweight and production-ready

### Security Features

- Non-root user (`appuser`) runs the application
- Minimal attack surface using Alpine Linux
- Health checks for database readiness
- Separated build and runtime stages

## Database

### PostgreSQL Configuration

Default credentials (defined in docker-compose.yml):
- **Host:** postgres
- **Port:** 5432
- **Database:** metaanime
- **User:** metaanime
- **Password:** metaanime_password (⚠️ change for production!)

### Automatic Migrations

The application automatically runs EF Core migrations on startup. It will:
1. Wait for PostgreSQL to be ready (with retries)
2. Apply any pending migrations
3. Start accepting requests

### Database Volume

Data is persisted in a Docker volume named `postgres_data`. To backup:

```bash
# Backup
docker-compose exec postgres pg_dump -U metaanime metaanime > backup.sql

# Restore
docker-compose exec -T postgres psql -U metaanime metaanime < backup.sql
```

## Network

All services run on the `meta-anime-network` bridge network, allowing them to communicate using service names:
- API can reach database at `postgres:5432`
- External access via published ports

## Production Deployment

### Security Checklist

- [ ] Change `Authentication__ApiKey` to a strong, unique value
- [ ] Change `Postgres__Password` to a strong password
- [ ] Use secrets management (Docker Swarm secrets, Kubernetes secrets, etc.)
- [ ] Enable HTTPS with proper certificates
- [ ] Configure firewall rules
- [ ] Set up monitoring and logging
- [ ] Regular database backups

### Performance Tuning

1. **Resource Limits:**
   ```yaml
   services:
     meta-anime-api:
       deploy:
         resources:
           limits:
             cpus: '2'
             memory: 2G
           reservations:
             cpus: '1'
             memory: 1G
   ```

2. **PostgreSQL Tuning:**
   ```yaml
   services:
     postgres:
       environment:
         - POSTGRES_INITDB_ARGS=-E UTF8 --locale=C
       command: postgres -c shared_buffers=256MB -c max_connections=200
   ```

## Troubleshooting

### Container won't start

```bash
# Check logs
docker-compose logs meta-anime-api

# Check database connection
docker-compose exec postgres pg_isready -U metaanime
```

### Database connection errors

The API waits 10 retries × 5 seconds (50 seconds total) for the database. If it still fails:

```bash
# Verify database is running
docker-compose ps postgres

# Check database logs
docker-compose logs postgres

# Restart the API service
docker-compose restart meta-anime-api
```

### Permission errors

Ensure the non-root user can access mounted volumes:

```bash
# Fix ownership on Linux
sudo chown -R 1000:1000 ./api
```

### Port conflicts

If ports 8080, 8081, or 5432 are already in use:

```yaml
# Change port mapping in docker-compose.override.yml
services:
  meta-anime-api:
    ports:
      - "9080:8080"  # Change host port
      - "9081:8081"
```

## Building for Different Architectures

```bash
# Build for ARM64 (Apple Silicon, Raspberry Pi)
docker buildx build --platform linux/arm64 -t meta-anime-api:latest .

# Multi-platform build
docker buildx build --platform linux/amd64,linux/arm64 -t meta-anime-api:latest .
```

## Additional Resources

- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [.NET Docker Documentation](https://learn.microsoft.com/en-us/dotnet/core/docker/)
- [PostgreSQL Docker Documentation](https://hub.docker.com/_/postgres)
