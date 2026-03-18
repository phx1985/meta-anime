<div align="center">

# 🎌 MetaAnime

### The self-hosted anime command center

[![Docker Pulls](https://img.shields.io/docker/pulls/phx1985/meta-anime?style=flat-square&logo=docker&label=Docker%20Pulls&color=2496ED)](https://hub.docker.com/r/phx1985/meta-anime)
[![GitHub Stars](https://img.shields.io/github/stars/phx1985/meta-anime?style=flat-square&logo=github&color=f0c040)](https://github.com/phx1985/meta-anime/stargazers)
[![Latest Tag](https://img.shields.io/github/v/tag/phx1985/meta-anime?style=flat-square&label=latest&color=7c3aed)](https://github.com/phx1985/meta-anime/releases)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg?style=flat-square)](LICENSE)

*Lovingly overengineered and relentlessly hyper-optimized with the help of [GitHub Copilot](https://github.com/features/copilot). 🤖✨*

<!-- screenshot: dashboard.png -->

</div>

---

MetaAnime unifies **MyAnimeList**, **Plex**, and **Sonarr** into a single, gorgeous command center — all inside **one Docker container**. No multi-service Compose stacks. No manual credential wiring. No secrets to manage. Drop the container, open your browser, follow the Setup Wizard, and your entire anime library is synchronized within minutes.

Built for homelab enthusiasts who want a real tool, not a fragile demo — every feature works offline, and optional integrations degrade gracefully when not configured.

---

## ✨ Features

| | Feature | Description |
|--|---------|-------------|
| 🕸️ | **Franchise Constellation** | Zoomable, draggable relationship graph — explore every prequel, sequel, and spin-off in a franchise at a glance |
| ⚡ | **Lightning-Fast Sync** | PostgreSQL bulk inserts power library synchronization that takes seconds, not minutes, even at scale |
| 🗺️ | **Kometa ID Mapping** | Community-sourced cross-reference of AniDB ↔ MAL ↔ TVDB ↔ TMDB ↔ IMDB IDs, cached locally for pinpoint-accurate matching |
| 🌸 | **Seasonal Browser** | Browse any year/season and add titles to both Sonarr and MAL simultaneously with quality-profile and root-folder selection |
| 🎲 | **Feeling Lucky** | Randomized anime picker filtered by your own preferences — plan-to-watch, local files, finished series |
| 📊 | **Unified Dashboard** | Single view of your library coverage across all three platforms, with genre-distribution charts and gap analysis |
| 🤖 | **Bot-kun AI** | Optional streaming AI chat assistant powered by any OpenAI-compatible provider (Ollama, vLLM, OpenAI, etc.) |
| 🧙 | **Setup Wizard** | Guided 7-step first-run configuration — from language selection to live first sync, no config files required |

---

## 🚀 Quick Start

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

Open **http://localhost:8080** — the Setup Wizard greets you on first launch.

> **Zero secrets required.** On first boot, an s6 init script auto-generates a cryptographically secure API key, database password, and JWT secret using `openssl rand -hex 32`. They are written to `/config/appsettings.json` automatically — you never touch them.

<details>
<summary>📄 docker-compose.yml</summary>

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

</details>

### 🧙 Setup Wizard Walkthrough

The wizard runs automatically on first launch and guides you through **7 steps**:

| Step | What happens |
|------|-------------|
| 1 · Welcome | Choose your display language |
| 2 · Security | Optionally set an admin password (bcrypt-hashed) |
| 3 · MyAnimeList | Connect via OAuth2 + PKCE — Client ID & Client Secret required |
| 4 · Plex | Enter your server URL; pick anime libraries from a live list (skippable) |
| 5 · Sonarr | Enter URL + API key; select or create anime tags (skippable) |
| 6 · Recap & Sync | Review all providers, then trigger the first sync with live progress |
| 7 · Done | Welcome to your command center 🎉 |

No YAML to learn. No config files to edit. Click through and you are live.

---

## ⚙️ Configuration

### Volume

| Mount | Purpose |
|-------|---------|
| `/config` | Persists PostgreSQL data directory **and** generated `appsettings.json` across container restarts and upgrades |

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `TZ` | No | `UTC` | Container timezone (e.g. `America/New_York`, `Europe/Paris`) |

> All provider credentials (MAL client ID/secret, Plex token, Sonarr API key) are configured through the **Settings UI** after the wizard completes — not via environment variables. This keeps your `docker-compose.yml` clean and secret-free.

<details>
<summary>🔍 What lives in /config?</summary>

```
/config/
├── appsettings.json        # Auto-generated on first boot (API key, DB password, JWT secret)
└── postgresql/             # PostgreSQL 17 data directory
```

Upgrading is safe: pull the new image and restart. The volume is preserved.

</details>

---

## 🔗 Integrations

### 🔵 MyAnimeList (MAL)

Connects via **OAuth2 + PKCE** for secure, token-based access to your anime list.

- Read and write your anime list status, scores, and progress
- Power the Franchise Constellation relationship graph
- Feed the Seasonal Browser with upcoming and airing titles
- Tokens auto-refresh — no manual re-authentication needed

**How to connect:** Settings → MyAnimeList → enter your MAL Client ID and Client Secret, then authorize via the OAuth flow.

### 🟠 Plex

Connects via **PIN-based OAuth** — no Plex credentials stored in MetaAnime.

- Queries your selected libraries for anime episode metadata
- Surfaces codec, resolution, and thumbnail information per episode
- Used by the Dashboard to compute library coverage and gap analysis

**How to connect:** Settings → Plex → enter your Plex Media Server URL, then click **Sign In with Plex** to complete PIN authorization.

### ⬛ Sonarr

Connects via **API key authentication**.

- Add or remove anime series by TVDB ID directly from the Seasonal Browser
- Respects your quality profiles and root folder configuration
- Supports tag-based filtering to scope MetaAnime to your anime libraries only

**How to connect:** Settings → Sonarr → enter your Sonarr base URL and API key, then select your anime tags and default quality profile.

### 🗺️ Kometa

MetaAnime fetches the [Kometa community anime ID mapping file](https://github.com/Kometa-Team/Kometa) from GitHub and caches it in PostgreSQL. This gives every title a cross-referenced set of IDs:

| Platform | ID Type |
|----------|---------|
| AniDB | AniDB series ID |
| MyAnimeList | MAL ID |
| TheTVDB | TVDB series ID |
| TheMovieDB | TMDB ID |
| IMDB | IMDB ID |

No configuration needed — Kometa sync runs automatically on a schedule and can be triggered manually from Settings.

### 🤖 Bot-kun AI

An **optional**, production-quality AI chat assistant built into MetaAnime. Completely dormant if not configured — all core features work without it.

- Works with **any OpenAI-compatible API endpoint** — Ollama, vLLM, LM Studio, OpenAI, and more
- Responses stream token-by-token via **Server-Sent Events (SSE)** — real-time, not polling, not fake typing animation
- **Context-aware**: Bot-kun builds its system prompt from your actual anime library and preferences, so answers are relevant to *your* collection
- Auto-discovers available models from your provider's `/v1/models` endpoint — no model name guessing

**How to connect:** Settings → Bot-kun AI → enter your API base URL (e.g. `http://localhost:11434/v1`) and optionally an API key, then select your preferred model.

---

## 🔐 Security

### Auto-Generated Secrets

On **first boot**, the s6 init script generates three secrets with `openssl rand -hex 32` (256-bit entropy each) and writes them to `/config/appsettings.json`:

| Secret | Where used |
|--------|-----------|
| API Key | `X-Api-Key` header for all API calls; written to `/assets/config.json` at boot so Angular can self-configure |
| Database password | PostgreSQL authentication (internal only — never exposed externally) |
| JWT secret | HS256 token signing for authenticated sessions |

You never see or set these — they are generated once and stored in the container volume.

### Optional Admin Password

The Setup Wizard (step 2) lets you set an admin password. If set, a **bcrypt hash** is stored in the `PropertyBag` table — plaintext is never persisted anywhere.

### Hangfire Dashboard

The Hangfire job dashboard is available at `<host>/hangfire`:

- **Development:** open, no authentication required
- **Production:** protected by `X-Api-Key` header validation — no external access without the auto-generated key

### PostgreSQL Isolation

PostgreSQL is bound to `127.0.0.1` inside the container — it is **never exposed externally**, regardless of port mappings. Only the .NET process running in the same container can reach it.

### Production Recommendations

| Recommendation | Why |
|----------------|-----|
| Put MetaAnime behind a **reverse proxy (Nginx, Caddy, Traefik)** with TLS | Port 8080 is plain HTTP — the API key travels in a header and must be encrypted in transit |
| Restrict host access to the `/config` named volume | The volume contains `appsettings.json` with all three auto-generated secrets |
| Keep the container on an isolated Docker network if co-hosted | Limits lateral movement if another container on the same host is compromised |

---

## 🏗️ Architecture

### Tech Stack

| Layer | Technology |
|-------|-----------|
| **API** | C# 14 / ASP.NET Core 10 |
| **Frontend** | Angular 21.1.4 (standalone components, signals, new control flow) |
| **Database** | PostgreSQL 17 + Entity Framework Core 10 |
| **Background Jobs** | Hangfire (scheduled + on-demand syncs) |
| **Real-Time** | SignalR (live sync-progress updates) |
| **Container Base** | linuxserver.io Alpine 3.21 + s6-overlay |
| **Auth** | JWT (auto-generated secret) + optional admin password |
| **API Docs** | Scalar UI at `/scalar/v1` |

### Everything in One Container

```
┌─────────────────────────────────────────────────────┐
│                  meta-anime container                │
│                                                      │
│  ┌─────────────┐   ┌──────────────────────────────┐ │
│  │  s6-overlay │   │     .NET 10 ASP.NET Core     │ │
│  │  (PID 1)    │──▶│  ┌──────────┐ ┌───────────┐  │ │
│  │             │   │  │  API     │ │  Angular  │  │ │
│  │  init-done  │   │  │ (port    │ │  (static  │  │ │
│  │  ↓          │   │  │  8080)   │ │  wwwroot) │  │ │
│  │  postgresql │   │  └──────────┘ └───────────┘  │ │
│  │  ↓          │   │  ┌──────────┐ ┌───────────┐  │ │
│  │  dotnet app │   │  │ Hangfire │ │  SignalR  │  │ │
│  └─────────────┘   │  └──────────┘ └───────────┘  │ │
│                    └──────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────┐ │
│  │  PostgreSQL 17 (127.0.0.1:5432, internal only)  │ │
│  └─────────────────────────────────────────────────┘ │
│                                                      │
│  📁 /config  ←  named volume (persisted)            │
└─────────────────────────────────────────────────────┘
         ↕ port 8080
     host / reverse proxy
```

### s6 Init Sequence

When the container starts, s6-overlay runs the following init stages in order:

1. **`init-postgresql`** — initialize the PostgreSQL data directory if it doesn't exist; start the PostgreSQL service
2. **`init-secrets`** — generate API key, DB password, and JWT secret via `openssl rand -hex 32`; write to `/config/appsettings.json` (idempotent — skipped on subsequent boots)
3. **`init-config`** — write `/config/assets/config.json` so the Angular frontend discovers the API key at runtime
4. **`dotnet`** — start the .NET 10 API, which auto-migrates the database and begins serving both the REST API and the Angular static files

s6 acts as PID 1, handling graceful shutdown signals and ensuring both PostgreSQL and the .NET process terminate cleanly.

---

## 👨‍💻 Developer Guide

### Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.x | Build and run the API + Aspire host |
| [Node.js](https://nodejs.org/) | 22+ | Angular dev server and build |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Latest | Hosts the PostgreSQL container via Aspire |

> **No Aspire workload install needed.** Aspire 9.x ships as NuGet packages — `dotnet run` is all you need.

### Local Development with .NET Aspire

```bash
# 1. Clone
git clone https://github.com/phx1985/meta-anime.git
cd meta-anime

# 2. Install UI dependencies (first time only)
cd ui/meta-anime-ui && npm install && cd ../..

# 3. Launch everything via Aspire
dotnet run --project MetaAnime.AppHost
```

Aspire orchestrates PostgreSQL (in Docker), the .NET API (with auto-migration on startup), and the Angular dev server — all in the correct startup order.

| Service | URL |
|---------|-----|
| Aspire Dashboard | https://localhost:17042 |
| Angular UI | http://localhost:4200 |
| .NET API | shown in Aspire Dashboard |
| Hangfire | `<api-url>/hangfire` |
| API Docs | `<api-url>/scalar/v1` |

### Project Structure

```
meta-anime/
├── api/                              # .NET 10 solution
│   ├── MetaAnime.Presentation/       # Controllers, middleware, SignalR hubs
│   ├── MetaAnime.Application/        # Services, business logic, Hangfire jobs
│   ├── MetaAnime.Core/               # Interfaces, domain models, enums
│   ├── MetaAnime.Infrastructure/     # EF Core DbContext, repositories, migrations
│   └── MetaAnime.AppHost/            # .NET Aspire orchestration host
├── ui/meta-anime-ui/                 # Angular 21 standalone app
├── root/                             # s6-overlay init scripts
│   └── etc/s6-overlay/s6-rc.d/      # Service definitions
├── Dockerfile                        # Multi-stage production build
└── docker-compose.yml
```

### Build Commands

```bash
# Run API tests (--no-restore assumes dependencies already installed)
dotnet test api/MetaAnime.sln --no-restore

# Build Angular for production
cd ui/meta-anime-ui
npm run build

# Build the production Docker image
docker build -t meta-anime:local .
```

<details>
<summary>🐳 Multi-Stage Dockerfile Overview</summary>

The production `Dockerfile` uses three stages:

| Stage | Base image | What it does |
|-------|-----------|-------------|
| `node-build` | `node:22-alpine` | `npm run build` → Angular output in `/dist` |
| `dotnet-build` | `mcr.microsoft.com/dotnet/sdk:10.0-alpine` | `dotnet publish -r linux-musl-x64 --self-contained true` |
| `final` | linuxserver.io Alpine 3.21 + s6-overlay | Copies binary + Angular `wwwroot`, adds s6 init scripts |

The result is a single self-contained Alpine image. No Nginx. No separate Node process. .NET serves Angular's static files natively.

</details>

---

## 📡 API Reference

Interactive API documentation is available at:

```
http://localhost:8080/scalar/v1
```

Scalar provides a clean, modern API explorer with live request execution. All endpoints require the `X-Api-Key` header (auto-generated on first boot and visible in Settings).

---

## 🤝 Contributing

Contributions are welcome and appreciated! Whether it's a bug fix, a new feature, or a documentation improvement — open an issue or send a PR.

1. Fork → create branch (`feat/your-feature-name`) → PR against `main`
2. Follow existing code style: C# 14 primary constructors, Angular signals, no legacy `@Input`/`@Output` decorators
3. Open an issue first for significant features before investing time building

---

## 📜 License

MetaAnime is released under the **[GNU Affero General Public License v3.0](LICENSE)** (AGPLv3).

**TL;DR for the homelab community:**

| ✅ Allowed | ⚠️ Conditions | ❌ Not allowed |
|-----------|--------------|---------------|
| Personal & homelab use | If you modify **and** deploy over a network, you must publish your modified source under AGPLv3 | Proprietary/closed-source forks |
| Fork and modify freely | Share improvements back — the community will thank you | Monetizing as a hosted SaaS without open-sourcing |
| Self-host for friends/family | | |

This keeps MetaAnime free and open for the self-hosting community — forever.

---

## 🙏 Acknowledgments

- [**Kometa Team**](https://github.com/Kometa-Team/Kometa) — MetaAnime's ID cross-referencing is powered by Kometa's community anime mapping files. An invaluable resource maintained with care for the entire self-hosting ecosystem.
- [**linuxserver.io**](https://www.linuxserver.io/) — The Alpine + s6-overlay base image that makes the monocontainer architecture clean, reliable, and production-grade.
- [**GitHub Copilot**](https://github.com/features/copilot) — The relentless AI co-pilot that helped make this project what it is. 🤖✨

