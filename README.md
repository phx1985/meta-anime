<div align="center">

# 🎌 MetaAnime

### A self-hosted, ultra-lightweight hub for synchronizing MyAnimeList, Plex, and Sonarr

[![Docker Pulls](https://img.shields.io/docker/pulls/phx1985/meta-anime?style=flat-square&logo=docker&label=Docker%20Pulls)](https://hub.docker.com/r/phx1985/meta-anime)
[![GitHub Stars](https://img.shields.io/github/stars/phx1985/meta-anime?style=flat-square&logo=github)](https://github.com/phx1985/meta-anime)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)

*Lovingly overengineered and relentlessly hyper-optimized with the help of [GitHub Copilot](https://github.com/features/copilot). 🤖✨*

</div>

---

## ✨ Features

| | Feature | Description |
|--|---------|-------------|
| 🕸️ | **Franchise Constellation** | Interactive graph of franchise relationships — explore prequels, sequels, and spin-offs with a zoomable, draggable canvas |
| ⚡ | **Lightning-Fast Sync** | PostgreSQL bulk inserts keep your entire library synchronized in seconds, not minutes |
| 🗺️ | **Kometa Mapping** | Cross-references AniDB, TVDB, TMDB, and IMDB IDs via Kometa's community mapping files for pinpoint-accurate matching across Plex, Sonarr, and MAL |
| 🎲 | **Feeling Lucky** | Randomized "pick me something to watch" picker, filtered by your own preferences — finished series, plan-to-watch, with local files |
| 🌸 | **Seasonal Browser** | Explore any season by year and add titles to Sonarr and MAL simultaneously, with quality profile and root-folder selection |
| 📊 | **Unified Dashboard** | Library coverage across all three platforms in one view, with genre-distribution charts and gap analysis |
| 🏗️ | **Ridiculously Lightweight** | The entire modern stack — .NET API, Angular UI, and PostgreSQL — ships as a **single container** managed by **s6-overlay**. No Compose file with five services. No orchestrator. One container, one volume, done. |

---

## 🚀 Getting Started

### Zero Configuration Required

MetaAnime is designed to just work out of the box.

> **You do not need to create or configure an API key.** On the very first boot, a background init script automatically generates a cryptographically secure API key and database password using `openssl rand`, then writes them to `/config/appsettings.json`. The Angular frontend picks up the key automatically at runtime — you never have to touch it.

Simply drop the container, open your browser, and you will be greeted by the Setup Wizard.

### 🧙 The Setup Wizard

On first run, MetaAnime guides you through a **beautiful, step-by-step configuration wizard**:

1. **Welcome** — choose your language
2. **Security** — optionally set an admin password to protect the app (skippable)
3. **MyAnimeList** — connect via OAuth2 (Client ID + Client Secret) — required to proceed
4. **Plex** — enter your Plex Media Server URL and token, then pick your anime libraries from a live list (skippable)
5. **Sonarr** — enter your Sonarr URL and API key, select or create the tags that identify anime series (skippable)
6. **Recap & Sync** — review your configured providers and kick off the first sync with live progress steps
7. **Done** — you are in 🎉

No config files to edit. No YAML to learn. Just click through the wizard and you are done.

> **Advanced users:** Environment variables are supported if you want to pre-seed provider credentials into the container (useful for automated deployments). This is completely optional — everything can be configured through the UI wizard instead.

---

## 🐳 Installation

### `docker run`

```bash
docker run -d \
  --name meta-anime \
  --restart unless-stopped \
  -p 8080:8080 \
  -e TZ=America/New_York \
  -v meta-anime-config:/config \
  ghcr.io/phx1985/meta-anime:latest
```

Open **http://localhost:8080** — the Setup Wizard will greet you on first launch.

### `docker-compose.yml`

```yaml
services:
  meta-anime:
    image: ghcr.io/phx1985/meta-anime:latest
    container_name: meta-anime
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      - TZ=America/New_York
    volumes:
      - meta-anime-config:/config

volumes:
  meta-anime-config:
```

> **Data persistence** — the named volume maps to `/config` inside the container, which holds both the PostgreSQL data directory and the generated `appsettings.json`. Everything survives container restarts and upgrades automatically.

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| **API** | C# 14 / ASP.NET Core (.NET 10) |
| **Frontend** | Angular 21+ (standalone components, signals, new control flow) |
| **Database** | PostgreSQL 17 + Entity Framework Core 10 |
| **Background Jobs** | Hangfire (scheduled + on-demand syncs) |
| **Real-Time** | SignalR (live sync-progress updates) |
| **Container** | linuxserver.io base image (Ubuntu Noble) + s6-overlay |

---

## 👩‍💻 Developer Quick Start

<details>
<summary>Expand for local development with .NET Aspire</summary>

### Prerequisites

| Tool | Purpose |
|------|---------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Build and run the API + AppHost |
| [Node.js 22+ / npm](https://nodejs.org/) | Run the Angular dev server |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Host the PostgreSQL container |

> **No Aspire workload install needed.** Aspire 13.x ships as NuGet packages.

### Steps

```bash
# 1. Clone
git clone https://github.com/phx1985/meta-anime.git
cd meta-anime

# 2. Install UI dependencies (first time only)
cd ui/meta-anime-ui && npm install && cd ../..

# 3. Configure credentials
cp api/MetaAnime.Presentation/appsettings.Development.json.template \
   api/MetaAnime.Presentation/appsettings.Development.json
# Edit the file and set at minimum Authentication__ApiKey

# 4. Run everything
dotnet run --project MetaAnime.AppHost --launch-profile https
```

Aspire starts PostgreSQL, the .NET API (auto-migrates on startup), and the Angular dev server in the correct order. The **Aspire Developer Dashboard** opens automatically.

| Service | URL |
|---------|-----|
| Aspire Dashboard | https://localhost:17042 |
| Angular UI | http://localhost:4200 |
| API / Swagger | shown in Dashboard |
| Hangfire | `<api-url>/hangfire` |

</details>

---

## 📄 License

[MIT](LICENSE)

---

## 🙏 Acknowledgments

* [**Kometa**](https://github.com/Kometa-Team/Kometa) — MetaAnime relies on Kometa's anime ID mapping files to cross-reference AniDB, MAL, Plex, and other platform identifiers. Thank you to the Kometa team for maintaining this invaluable community resource!

