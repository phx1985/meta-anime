# Meta Anime

Meta Anime is a full-stack application (Angular UI + ASP.NET Core API) that aggregates, cross-references, and manages anime metadata from multiple sources — Sonarr, Plex, MyAnimeList (MAL), and Kometa.

## 🚀 Features

* **Multi-Source Aggregation** — fetches data from Sonarr, Plex, and MyAnimeList
* **Intelligent Matching** — cross-references anime using external IDs (Tvdb, Imdb, Tmdb) and Kometa mapping files
* **Advanced Filtering** — filter by genre, theme, and demographic with an elegant popup picker; selected filters displayed as pills on each card
* **Genre / Theme / Demographic Pills** — at-a-glance tags on every anime card, with NSFW content visually distinguished
* **Background Sync** — scheduled Hangfire jobs keep metadata up to date
* **Aspire Orchestration** — one command starts PostgreSQL, the API, and the Angular UI with live reload
* **Developer Dashboard** — real-time logs, traces, and metrics via the Aspire Dashboard
* **Secure** — API Key authentication (`X-Api-Key` header)
* **Swagger / OpenAPI** — interactive API docs at `<api-url>/swagger`

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| API Framework | ASP.NET Core 10 (C# 14) |
| Architecture | Clean Architecture (Domain / Core / Application / Infrastructure / Presentation) |
| Database | PostgreSQL 17 + Entity Framework Core 10 |
| Background Jobs | Hangfire |
| Orchestration | .NET Aspire 13.x |
| Frontend | Angular 21 |
| Observability | OpenTelemetry (traces, metrics, logs) |

## 🏗️ Architecture (Aspire Resources)

When you run the AppHost, Aspire manages three resources:

```
MetaAnime.AppHost
├── postgres          PostgreSQL 17 container  ──► volume-mounted, auto-migrated
│   └── metaanime     database injected as ConnectionStrings__metaanime
├── api               .NET API (MetaAnime.Presentation)
│   ├── waits for postgres
│   ├── auto-migrates the DB on startup
│   └── exposes /health, /alive, /swagger, /hangfire
└── ui                Angular dev server (npm run start)
    ├── waits for api
    ├── hot-reload enabled
    └── proxies /api/* → api  (via proxy.conf.js)
```

`MetaAnime.ServiceDefaults` is a shared library injected into the API that wires up:
* OpenTelemetry traces, metrics, and logs → forwarded to the Aspire Dashboard
* Health checks at `/health` (all checks) and `/alive` (liveness probe)
* HTTP client resilience and service discovery

## ⚙️ Configuration

Local settings live in `api/MetaAnime.Presentation/appsettings.Development.json` (gitignored — see [Quick Start](#-quick-start) below). The database connection string is injected automatically by Aspire; you do **not** need any `Postgres__*` keys for local development.

| Category | Key | Description |
|----------|-----|-------------|
| **Authentication** | `Authentication__ApiKey` | **Required.** API Key sent as `X-Api-Key` header. |
| **Sonarr** | `Sonarr__Host` | URL of your Sonarr instance (e.g. `http://sonarr:8989`). |
| | `Sonarr__ApiKey` | Sonarr API key. |
| | `Sonarr__AnimeTags` | Comma-separated Sonarr tags that identify anime (e.g. `anime`). |
| | `Sonarr__RemoteUrl` | Optional public-facing Sonarr URL (e.g. `https://sonarr.example.com`) used for series links in the UI. Falls back to `Sonarr__Host` when not set. |
| **Plex** | `Plex__Host` | URL of your Plex Media Server (e.g. `http://192.168.1.5:32400`). |
| | `Plex__Token` | Plex authentication token (`X-Plex-Token`). |
| | `Plex__AnimeSections` | Comma-separated Plex library section IDs containing anime. |
| **MyAnimeList** | `MyAnimeList__ApiToken` | MAL API token / client ID. |
| | `MyAnimeList__ClientId` | OAuth2 client ID (for automatic token refresh). |
| | `MyAnimeList__RefreshToken` | Initial refresh token to bootstrap OAuth2. |
| **Kometa** | `Kometa__AnimeIdsJsonUrl` | URL to the Kometa anime IDs JSON mapping file. |
| **CORS** | `AllowedCorsOrigins` | JSON array of allowed origins in Development (default: `["http://localhost:4200"]`). |

## 📦 Quick Start

### Prerequisites

| Tool | Purpose |
|------|---------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Build and run the API + AppHost |
| [Node.js 22+ and npm](https://nodejs.org/) | Run the Angular dev server |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Host the PostgreSQL container |

> **No Aspire workload install needed.** Aspire 13.x ships as NuGet packages — `dotnet run` is all you need.

### 1 · Clone the repository

```bash
git clone https://github.com/phx1985/meta-anime.git
cd meta-anime
```

### 2 · Install npm dependencies *(first time only)*

```bash
cd ui/meta-anime-ui && npm install && cd ../..
```

### 3 · Configure local credentials

Copy the settings template and fill in your values:

```bash
cp api/MetaAnime.Presentation/appsettings.Development.json.template \
   api/MetaAnime.Presentation/appsettings.Development.json
```

Then edit `appsettings.Development.json` — at minimum set `Authentication__ApiKey`. Sonarr, Plex, and MAL settings are optional and can be added later. The file is gitignored and will never be committed.

> **Prefer .NET User Secrets?** Store values outside the repo:
> ```bash
> dotnet user-secrets set "Authentication__ApiKey" "your-key" \
>     --project api/MetaAnime.Presentation
> ```

### 4 · Run the full stack

```bash
dotnet run --project MetaAnime.AppHost --launch-profile https
```

Aspire starts everything in the correct order:

* Pulls and starts the **PostgreSQL 17** container
* Starts the **.NET API** — waits for Postgres, then auto-applies EF Core migrations
* Starts the **Angular dev server** — waits for the API, then opens a browser

The **Aspire Developer Dashboard** opens automatically in your browser.

> **Visual Studio / Rider**: open `api/MetaAnime.sln`, set **MetaAnime.AppHost** as the startup project, and press **F5**. The `https` profile is the default.

### 5 · Access the services

| Service | URL | Notes |
|---------|-----|-------|
| **Aspire Dashboard** | https://localhost:17042 | Logs, traces, metrics for all resources |
| **Angular UI** | http://localhost:4200 | Hot-reload enabled |
| **API** | shown in Dashboard | Dynamic port; click the resource link |
| **Swagger** | `<api-url>/swagger` | Interactive API docs |
| **Hangfire** | `<api-url>/hangfire` | Background job monitor *(Development only — requires authentication in other environments)* |
| **Health (all)** | `<api-url>/health` | Aggregated health check |
| **Liveness** | `<api-url>/alive` | Liveness probe |

### 6 · Stop the stack

Press **Ctrl+C** in the terminal (or click **Stop** in Visual Studio). Aspire stops all processes and containers cleanly.

---

## 🖥️ Aspire Dashboard

The dashboard gives you full observability across all resources without any extra tooling:

* **Resources** tab — live status, endpoint URLs, and environment variables for each resource
* **Console** tab — combined stdout/stderr from the API and Angular dev server
* **Structured Logs** tab — searchable, filterable log entries with trace context
* **Traces** tab — distributed traces showing request flow across services
* **Metrics** tab — ASP.NET Core, HTTP client, and .NET runtime metrics

The Angular dev server (`ui`) forwards all `/api/*` requests to the API through `proxy.conf.js`, which reads the API endpoint from the `services__api__http__0` environment variable injected by Aspire.

---

## 🔒 Authentication

All API endpoints require an API key in the request header:

```
X-Api-Key: <your-configured-api-key>
```

The key is set via `Authentication__ApiKey` in `appsettings.Development.json` or User Secrets.

---

## 🩺 Troubleshooting

**`docker: command not found` or container won't start**
Make sure Docker Desktop is running before starting the AppHost.

**`ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` not defined**
You are running the AppHost without a launch profile. Always use:
```bash
dotnet run --project MetaAnime.AppHost --launch-profile https
```

**Angular UI shows CORS errors**
Ensure the API is running (check the Dashboard Resources tab) and that `AllowedCorsOrigins` in your `appsettings.Development.json` includes `http://localhost:4200`.

**`npm: command not found` when Aspire starts the UI**
Install Node.js 22+ and run `npm install` in `ui/meta-anime-ui` before starting the AppHost.

**Database migration errors on first run**
The API automatically creates the migrations history table and runs all EF Core migrations on startup. If the API exits, check the Console tab in the Dashboard for details.

---

## 📄 License

[MIT](LICENSE)

---

## 🙏 Acknowledgments

* [**Kometa**](https://github.com/Kometa-Team/Kometa) — Meta Anime uses Kometa's anime ID mapping files to cross-reference AniDB, MAL, Plex, and other platform identifiers. Thank you to the Kometa team for maintaining this invaluable community resource!

