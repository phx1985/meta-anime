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
*   [Node.js and npm](https://nodejs.org/) (for the Angular UI)
*   **Docker & Docker Compose** (recommended for quickest setup)

### 🚀 Quick Start with .NET Aspire (Recommended for Local Development)

.NET Aspire orchestrates all services (PostgreSQL, API, and Angular UI) with a single command, includes a developer dashboard, and supports live reload for the Angular UI.

#### Prerequisites

*   [.NET 10 SDK](https://dotnet.microsoft.com/download)
*   [Node.js and npm](https://nodejs.org/) (for the Angular UI)
*   [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the PostgreSQL container)

#### Steps

1.  **Clone the repository**
    ```bash
    git clone https://github.com/phx1985/meta-anime.git
    cd meta-anime
    ```

2.  **Install npm dependencies** (first time only)
    ```bash
    cd ui/meta-anime-ui && npm install && cd ../..
    ```

3.  **Run the full stack**
    ```bash
    dotnet run --project MetaAnime.AppHost --launch-profile https
    ```
    Aspire will start:
    - A PostgreSQL container
    - The .NET API (with automatic DB migration)
    - The Angular UI dev server with **live hot-reload**
    - The Aspire Developer Dashboard (automatically opened in your browser)

    > **Visual Studio / Rider**: Open `api/MetaAnime.sln`, set `MetaAnime.AppHost` as the startup project, and press **F5**. The `https` launch profile is selected by default.

4.  **Access the services**

    | Service | URL |
    |---------|-----|
    | Aspire Dashboard | https://localhost:17042 |
    | Angular UI | http://localhost:4200 |
    | API | shown in Aspire Dashboard |
    | Swagger | `<api-url>/swagger` |

5.  **Configure local credentials** (Aspire equivalent of `docker-compose.override.yml`)

    Copy the template and fill in your values:
    ```bash
    cp api/MetaAnime.Presentation/appsettings.Development.json.template \
       api/MetaAnime.Presentation/appsettings.Development.json
    # Edit appsettings.Development.json with your local API keys and service URLs
    ```
    `appsettings.Development.json` is gitignored and will never be committed.

    > **What goes in this file?** Authentication key, Sonarr, Plex, and MAL credentials. You do **not** need any `Postgres__*` settings — Aspire injects the connection string automatically.

    > **Prefer User Secrets?** Store sensitive values outside the repo instead:
    > ```bash
    > dotnet user-secrets set "Authentication__ApiKey" "your-key" \
    >     --project api/MetaAnime.Presentation
    > dotnet user-secrets set "Sonarr__ApiKey" "your-key" \
    >     --project api/MetaAnime.Presentation
    > ```

> **Live reload**: The Angular UI supports full hot-reload — file changes in `ui/meta-anime-ui/src` are reflected in the browser instantly without restarting the stack.

### 🐳 Quick Start with Docker (Alternative)

The fastest way to get up and running — **full stack with the Angular UI** — is a single command from the repo root:

1.  **Clone the repository**
    ```bash
    git clone https://github.com/phx1985/meta-anime.git
    cd meta-anime
    ```

2.  **Start the full stack (PostgreSQL + API + Angular UI) with hot-reload**
    ```bash
    docker compose -f docker-compose.dev.yml up --build
    ```
    > First run builds both images (~2–5 min). Subsequent starts are faster.

3.  **Access the services**
    | Service | URL |
    |---------|-----|
    | Angular UI | http://localhost:4200 |
    | API | http://localhost:8080 |
    | Swagger | http://localhost:8080/swagger |

4.  **Stop**
    ```bash
    docker compose -f docker-compose.dev.yml down
    ```

5.  **Configure (Optional)** — override API keys, Sonarr, Plex, MAL credentials:

    Copy the template from the `api/` directory to the repo root and edit it:
    ```bash
    cp api/docker-compose.override.yml.template docker-compose.override.yml
    # Edit docker-compose.override.yml — set Authentication__ApiKey, Sonarr__*, etc.
    ```
    The file is gitignored. Docker Compose will automatically merge it with `docker-compose.dev.yml`.

#### API-only (no UI)

If you only need the API + database without the Angular frontend:

```bash
cd api
docker compose up -d
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

**Full stack (API + UI) — run from the repo root:**

```bash
# Start full stack with hot-reload
docker compose -f docker-compose.dev.yml up --build

# View UI logs
docker compose -f docker-compose.dev.yml logs -f meta-anime-ui

# View API logs
docker compose -f docker-compose.dev.yml logs -f meta-anime-api

# Stop full stack
docker compose -f docker-compose.dev.yml down
```

**API-only — run from the `api` directory:**

```bash
cd api

# Start services
docker compose up -d

# View logs
docker compose logs -f meta-anime-api

# Stop services
docker compose down

# Rebuild and restart
docker compose up -d --build
```

For additional commands and Makefile usage, see [DOCKER.md](DOCKER.md).

## 🔒 Authentication

All API endpoints are protected. You must include the configured API Key in the request header:

`X-Api-Key: <Your-Configured-Api-Key>`

## 📄 License

[MIT](LICENSE)
