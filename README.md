# Meta Anime API

Meta-Anime-API is a robust RESTful API designed to aggregate, cross-reference, and manage anime metadata from multiple sources including Sonarr, Plex, MyAnimeList (MAL), and Kometa. Built with modern .NET technologies, it provides a centralized way to synchronize and match anime entries across your media ecosystem.

## 🚀 Features

*   **Multi-Source Aggregation**: Fetches anime data from Sonarr, Plex, and MyAnimeList.
*   **Intelligent Matching**: Cross-references anime using external IDs (Tvdb, Imdb, Tmdb) and Kometa mapping files.
*   **Metadata Synchronization**: Keeps your anime metadata consistent across platforms.
*   **Aspire Orchestration**: One-command local dev stack (PostgreSQL + API + Angular UI) with live reload.
*   **Secure**: API Key based authentication.
*   **Swagger/OpenAPI Support**: Interactive API documentation.

## 🛠️ Tech Stack

*   **Framework**: .NET 10 (C# 14)
*   **Architecture**: Clean Architecture (Core, Application, Infrastructure, Presentation)
*   **Database**: PostgreSQL / Entity Framework Core
*   **API**: ASP.NET Core Web API
*   **Orchestration**: .NET Aspire 13.x

## ⚙️ Configuration

The application uses the .NET Options pattern. Configure it via `appsettings.Development.json` or .NET User Secrets.

| Category | Key | Description |
|----------|-----|-------------|
| **Authentication** | `Authentication__ApiKey` | **Required**. API Key sent as `X-Api-Key` header. |
| **Sonarr** | `Sonarr__Host` | URL of your Sonarr instance. |
| | `Sonarr__ApiKey` | API Key for Sonarr. |
| | `Sonarr__AnimeTags` | Comma-separated tags identifying anime in Sonarr (e.g., `anime`). |
| **Plex** | `Plex__Host` | URL of your Plex Media Server (e.g., `http://192.168.1.5:32400`). |
| | `Plex__Token` | Plex Authentication Token (X-Plex-Token). |
| | `Plex__AnimeSections` | Comma-separated Plex library section IDs containing anime. |
| **MyAnimeList** | `MyAnimeList__ApiToken` | MAL Client ID / Token. |
| **Kometa** | `Kometa__AnimeIdsJsonUrl` | URL to the Kometa anime IDs JSON mapping file. |

> **Database connection** is injected automatically by Aspire — no `Postgres__*` keys needed for local development.

## 📦 Getting Started

### Prerequisites

*   [.NET 10 SDK](https://dotnet.microsoft.com/download)
*   [Node.js and npm](https://nodejs.org/) (for the Angular UI)
*   [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the PostgreSQL container managed by Aspire)

### 🚀 Quick Start with .NET Aspire

.NET Aspire orchestrates all services (PostgreSQL, API, and Angular UI) with a single command, includes a developer dashboard, and supports live reload for the Angular UI.

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

5.  **Configure local credentials**

    Copy the template and fill in your values:
    ```bash
    cp api/MetaAnime.Presentation/appsettings.Development.json.template \
       api/MetaAnime.Presentation/appsettings.Development.json
    # Edit appsettings.Development.json with your local API keys and service URLs
    ```
    `appsettings.Development.json` is gitignored and will never be committed.

    > **Prefer User Secrets?** Store sensitive values outside the repo instead:
    > ```bash
    > dotnet user-secrets set "Authentication__ApiKey" "your-key" \
    >     --project api/MetaAnime.Presentation
    > ```

> **Live reload**: The Angular UI supports full hot-reload — file changes in `ui/meta-anime-ui/src` are reflected in the browser instantly without restarting the stack.

## 🔒 Authentication

All API endpoints are protected. Include the configured API Key in the request header:

`X-Api-Key: <Your-Configured-Api-Key>`

## 📄 License

[MIT](LICENSE)

