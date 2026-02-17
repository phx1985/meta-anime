# Meta Anime API

Meta-Anime-API is a robust RESTful API designed to aggregate, cross-reference, and manage anime metadata from multiple sources including Sonarr, Plex, MyAnimeList (MAL), and Kometa. Built with modern .NET technologies, it provides a centralized way to synchronize and match anime entries across your media ecosystem.

## 🚀 Features

*   **Multi-Source Aggregation**: Fetches anime data from Sonarr, Plex, and MyAnimeList.
*   **Intelligent Matching**: Cross-references anime using external IDs (Tvdb, Imdb, Tmdb) and Kometa mapping files.
*   **Metadata Synchronization**: Keeps your anime metadata consistent across platforms.
*   **Docker Ready**: specific configurations for seamless containerized deployment.
*   **Secure**: API Key based authentication.
*   **Swagger/OpenAPI Support**: Interactive API documentation.

## 🛠️ Tech Stack

*   **Framework**: .NET 10 (C# 14)
*   **Architecture**: Clean Architecture (Core, Application, Infrastructure, Presentation)
*   **Database**: PostgreSQL / Entity Framework Core
*   **API**: ASP.NET Core Web API
*   **Containerization**: Docker

## ⚙️ Configuration

The application uses the .NET Options pattern. You can configure it using `appsettings.json` or Environment Variables (recommended for Docker).

| Category | Environment Variable | Description |
|----------|----------------------|-------------|
| **Authentication** | `Authentication__ApiKey` | **Required**. The API Key required to access the API endpoints (Header: `X-Api-Key`). |
| **Database** | `Postgres__Host` | Database host address. |
| | `Postgres__Db` | Database name. |
| | `Postgres__User` | Database user. |
| | `Postgres__Password` | Database password. |
| **Sonarr** | `Sonarr__Host` | URL of your Sonarr instance. |
| | `Sonarr__ApiKey` | API Key for Sonarr. |
| | `Sonarr__AnimeTags` | Comma-separated list of tags to identify anime in Sonarr (e.g., `anime`). |
| **Plex** | `Plex__Host` | URL of your Plex Media Server (e.g., `http://192.168.1.5:32400`). |
| | `Plex__Token` | Plex Authentication Token (X-Plex-Token). |
| | `Plex__AnimeSections` | Comma-separated list of Plex library section IDs containing anime. |
| **MyAnimeList** | `MyAnimeList__ApiBaseUrl` | MAL API Base URL (default: `https://api.myanimelist.net/v2`). |
| | `MyAnimeList__ApiToken` | MAL Client ID / Token. |
| | `MyAnimeList__WebBaseUrl` | MAL Web URL (default: `https://myanimelist.net`). |
| **Kometa** | `Kometa__AnimeIdsJsonUrl` | URL to the Kometa anime IDs JSON mapping file. |

## 📦 Getting Started

### Prerequisites

*   [.NET 10 SDK](https://dotnet.microsoft.com/download) (for local development)
*   PostgreSQL (for local development)
*   **Docker & Docker Compose** (recommended for quickest setup)

### 🐳 Quick Start with Docker (Recommended)

The fastest way to get started is using Docker:

1.  **Clone the repository**
    ```bash
    git clone https://github.com/phx1985/meta-anime.git
    cd meta-anime
    ```

2.  **Start with Docker Compose**
    ```bash
    docker compose up -d
    ```
    
    Or use the quick-start script:
    ```bash
    ./quick-start.sh
    ```

3.  **Access the API**
    - Swagger UI: http://localhost:8080/swagger
    - API: http://localhost:8080

4.  **Configure (Optional)**
    For custom configuration, copy the override template:
    ```bash
    cp docker-compose.override.yml.template docker-compose.override.yml
    # Edit docker-compose.override.yml with your settings
    ```

**📚 See [DOCKER.md](DOCKER.md) for complete Docker documentation, debugging, and production deployment.**

### 💻 Local Development (Without Docker)

1.  **Clone the repository**
    ```bash
    git clone https://github.com/phx1985/meta-anime.git
    cd meta-anime
    ```

2.  **Configure User Secrets or appsettings.json**
    Update `appsettings.Development.json` with your secrets or use the `dotnet user-secrets` tool.

3.  **Run the Database Migrations**
    ```bash
    cd api
    dotnet ef database update --project MetaAnime.Infrastructure --startup-project MetaAnime.Presentation
    ```

4.  **Run the Application**
    ```bash
    dotnet run --project MetaAnime.Presentation
    ```

5.  **Access Swagger UI**
    Navigate to `https://localhost:5001/swagger/index.html` (port may vary).

### 🛠️ Docker Commands (Quick Reference)

```bash
# Start services
docker compose up -d

# View logs
docker compose logs -f meta-anime-api

# Stop services
docker compose down

# Rebuild and restart
docker compose up -d --build

# Use Makefile for easier management
make up      # Start services
make logs    # View logs
make down    # Stop services
make help    # See all commands
```

## 🔒 Authentication

All API endpoints are protected. You must include the configured API Key in the request header:

`X-Api-Key: <Your-Configured-Api-Key>`

## 📄 License

[MIT](LICENSE)
