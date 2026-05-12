<div align="center">

# 🎌 MetaAnime

### The self-hosted anime command center

[![Docker Pulls](https://img.shields.io/docker/pulls/phx1985/meta-anime?style=flat-square&logo=docker&label=Docker%20Pulls&color=2496ED)](https://hub.docker.com/r/phx1985/meta-anime)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg?style=flat-square)](LICENSE)

*Lovingly overengineered and relentlessly hyper-optimized with the help of [GitHub Copilot](https://github.com/features/copilot). 🤖✨*

<!-- screenshot: dashboard.png -->

</div>

---

MetaAnime unifies **MyAnimeList**, **Plex**, and **Sonarr** into a single, gorgeous command center — all inside **one Docker container**. No multi-service Compose stacks. No manual credential wiring. No secrets to manage. Drop the container, open your browser, follow the Setup Wizard, and your entire anime library is synchronized within minutes.

Built for homelab enthusiasts who want a real tool, not a fragile demo — every feature works offline, and optional integrations degrade gracefully when not configured.

---

## ✨ Features at a Glance

| | Feature | Description |
|--|---------|-------------|
| 📊 | **Unified Dashboard** | Live view of download activity, library coverage, genre distribution, gap analysis, and provider health — all on one screen |
| 🕸️ | **Franchise Constellation** | Zoomable, draggable SVG relationship graph — explore every prequel, sequel, and spin-off in a franchise at a glance |
| ⚡ | **Lightning-Fast Sync** | PostgreSQL bulk inserts power library synchronization that takes seconds, not minutes, even at scale |
| 🗺️ | **Kometa ID Mapping** | Community-sourced cross-reference of AniDB ↔ MAL ↔ TVDB ↔ TMDB ↔ IMDB IDs, cached locally for pinpoint-accurate matching |
| 🌸 | **Seasonal Browser** | Browse any year/season, filter by genre/status/rating, and add titles to Sonarr and MAL simultaneously |
| 📦 | **Library Analysis** | Two-phase storage audit: aggregate series stats (Phase 1) + per-episode codec/resolution breakdown (Phase 2) |
| 📈 | **Download Stats** | Pre-cached Sonarr download activity (today/week/month/year) with a line chart and configurable background refresh |
| 🎲 | **Feeling Lucky** | Randomized anime picker filtered by your own preferences — plan-to-watch, local files, finished series |
| 🤖 | **Bot-kun AI** | Optional streaming AI chat assistant powered by any OpenAI-compatible provider (Ollama, vLLM, OpenAI, etc.) |
| 🩺 | **Provider Health** | Real-time Plex / Sonarr / MAL latency checks with online/degraded/offline status indicators |
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
| 1 · Welcome | Choose your display language (English / Français) and color theme |
| 2 · Security | Optionally set an admin password (bcrypt-hashed); skippable for LAN-only deployments |
| 3 · MyAnimeList | Connect via OAuth2 + PKCE — Client ID & Client Secret required |
| 4 · Plex | Enter your server URL; pick anime libraries from a live list (skippable) |
| 5 · Sonarr | Enter URL + API key; select or create anime tags (skippable) |
| 6 · Recap & Sync | Review all providers, then trigger the first sync with live progress |
| 7 · Done | Welcome to your command center 🎉 |

No YAML to learn. No config files to edit. Click through and you are live.

---

## 📊 Dashboard

The home screen is divided into purpose-built cards:

### 📈 Download Activity

Displays Sonarr download stats pre-cached by a background job — opening the dashboard is instant, no live API call required.

| Control | Purpose |
|---------|---------|
| **Period selector** | Switch between Today / This Week / This Month / This Year |
| **Downloads count** | Number of completed episode downloads in the selected period |
| **Storage used** | Total bytes (formatted as MB / GB / TB) consumed in the selected period |
| **Line chart** | Time-series breakdown — daily points for Week/Month, weekly points for Year (hidden for Today) |
| **Last updated** | Timestamp of the most recent cache refresh, shown in your local timezone |
| **Force Refresh** | Triggers an immediate background job and polls until the cache is updated (up to 30 s) |

The cache refresh interval is configurable in Settings (1 h / 3 h / 6 h / 12 h, default 1 h). The job also runs once on container startup to warm the cache.

### 🕳️ Gap Analysis

Two counters show your library coverage mismatches:

| Gap | Meaning |
|-----|---------|
| **Sonarr not in MAL** | Series tracked in Sonarr that have no corresponding entry on your MAL list |
| **Plex not in MAL** | Episodes on your Plex server with no matching MAL entry |

Clicking either counter opens a detail modal where you can review, cross-link, or dismiss each entry individually.

### 🎭 Genre Distribution

A doughnut chart breaks down your entire library by MAL genre. Hover a slice to see the count. Colors adapt to your active theme.

### 🩺 Provider Health

A status card showing the real-time health of each connected provider:

| Status | Meaning |
|--------|---------|
| 🟢 Online | Responding within normal latency |
| 🟡 Degraded | Responding but above the latency threshold (1 000 ms) |
| 🔴 Offline | Not reachable within the probe timeout (10 s) |

Health is probed every **5 minutes** in a dedicated background service. The card shows the latest snapshot — no poll on page load.

---

## 🌸 Seasonal Browser

Browse every anime season from 1963 to the current year. A rich filter panel lets you narrow down:

- **Season:** Winter / Spring / Summer / Fall
- **Airing status:** Finished / Currently Airing / Not Yet Aired
- **Media type:** TV / OVA / Movie / Special / ONA / Music
- **Age rating:** G / PG / PG-13 / R / R+
- **Genre / Theme / Demographic:** multi-select with AND/OR logic
- **MAL list status:** Already on your list / Not on your list

For any title, click **Add to Library** to launch a 3-step wizard:

1. Set your MAL watch status and score
2. Configure Sonarr: root folder, quality profile, monitor type, season folders
3. Confirm and add — both MAL and Sonarr are updated in one action

**Filter presets** let you save and recall any filter combination by name.

---

## 📦 Library Analysis

A two-phase storage audit for your Sonarr library:

**Phase 1 — Summary** pulls aggregate stats for every series in Sonarr:
- Total size on disk
- Episode count (total / downloaded)
- Average episode file size

**Phase 2 — Codec Enrichment** lets you select specific series and fetch full per-episode codec and resolution data from Sonarr. Results appear in an analytics panel:
- Top series by disk usage (bar chart)
- Codec distribution (pie chart: H.264 / HEVC / AV1 / MPEG-2)
- Per-series drill-down with file-level detail

Phase 2 is on-demand because it can be slow for large libraries — Phase 1 always loads instantly.

---

## 🕸️ Franchise Constellation

An interactive SVG graph that visualizes the full franchise tree for any anime:

- **Nodes** represent individual titles; their size reflects MAL score
- **Edges** are labeled with relationship type (Prequel / Sequel / Side Story / Spin-off / etc.)
- **Pan and zoom** with mouse or touch
- Nodes are colored by your MAL watch status (watching / completed / plan to watch / not on list)
- Click any node to open the anime detail panel

Open the constellation from any anime detail panel or directly from the anime list.

---

## 📚 Anime Library

The main library view with four display modes:

| Mode | Description |
|------|-------------|
| Grid (1–4 columns) | Cover art + title cards |
| List | Compact rows with metadata |
| Compact | Minimal rows for dense browsing |
| Table | Sortable columns with full metadata |

**Filtering:**

| Filter | Options |
|--------|---------|
| Genre / Theme / Demographic | Multi-select with AND / OR logic |
| MAL status | Watching / Completed / On Hold / Dropped / Plan to Watch |
| Sonarr status | In Sonarr / Not in Sonarr |
| Plex status | In Plex / Not in Plex |
| Video codec | H.264 / HEVC / AV1 / MPEG-2 |
| NSFW | Toggle (respects global setting) |

Full-text search, sort by title/score/date, and pagination are all supported. Saved filter presets work the same as on the Seasonal page.

---

## 🔗 Integrations

### 🔵 MyAnimeList (MAL)

Connects via **OAuth2 + PKCE** for secure, token-based access to your anime list.

- Read and write your anime list status, scores, and episode progress
- Power the Franchise Constellation relationship graph
- Feed the Seasonal Browser with upcoming and airing titles
- Tokens auto-refresh weekly via a scheduled Hangfire job — no manual re-authentication needed

**How to connect:** Settings → MyAnimeList → enter your MAL Client ID and Client Secret, then authorize via the OAuth flow.

### 🟠 Plex

Connects via **PIN-based OAuth** — no Plex credentials stored in MetaAnime.

- Queries your selected libraries for anime episode metadata (codec, resolution, thumbnails)
- Generates deep links to the Plex web/app UI per series
- Contributes to library coverage and gap analysis on the Dashboard

**How to connect:** Settings → Plex → enter your Plex Media Server URL, then click **Sign In with Plex** to complete PIN authorization.

### ⬛ Sonarr

Connects via **API key authentication**.

- Enumerate series, tags, quality profiles, and root folders
- Add or remove series directly from the Seasonal Browser and Anime List
- Feed the Download Activity card with episode download history
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

No configuration needed — Kometa sync runs automatically on the same schedule as the main library sync and can be triggered manually from Settings.

### 🤖 Bot-kun AI

An **optional**, production-quality AI chat assistant. Completely dormant if not configured — all core features work without it.

- Works with **any OpenAI-compatible API endpoint** — Ollama, vLLM, LM Studio, OpenAI, and more
- Responses stream token-by-token via **Server-Sent Events (SSE)** — real-time, not polling, not fake typing animation
- **Context-aware**: Bot-kun builds its system prompt from your actual anime library and preferences, so answers are relevant to *your* collection
- Auto-discovers available models from your provider's `/v1/models` endpoint — no model name guessing
- **Ask button** on every anime detail panel sends context about that specific title directly to Bot-kun

**How to connect:** Settings → Bot-kun AI → enter your API base URL (e.g. `http://localhost:11434/v1`) and optionally an API key, then select your preferred model.

---

## ⚙️ Configuration

### Volume

| Mount | Purpose |
|-------|---------|
| `/config` | Persists PostgreSQL data directory **and** generated `appsettings.json` across container restarts and upgrades |

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `TZ` | No | `UTC` | Container timezone — controls local-time display throughout the UI (e.g. `America/New_York`, `Europe/Paris`) |

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

### Settings Reference

All settings are managed through the **Settings UI** (`/settings`). They are grouped into the following categories:

<details>
<summary>🔵 MyAnimeList</summary>

| Setting | Description |
|---------|-------------|
| MAL Client ID | Your MAL OAuth application client ID |
| MAL Client Secret | Your MAL OAuth application client secret (masked) |
| Connected | Read-only — shows OAuth connection status |
| Token Last Refreshed | Read-only — timestamp of the last automatic token refresh |

</details>

<details>
<summary>🟠 Plex</summary>

| Setting | Description |
|---------|-------------|
| Plex Enabled | Toggle Plex integration on/off |
| Plex Host | Your Plex Media Server base URL |
| Anime Library Sections | Comma-separated section IDs to scan |
| Machine Identifier | Read-only — your Plex server's unique identifier |
| Connected | Read-only — shows PIN auth connection status |

</details>

<details>
<summary>⬛ Sonarr</summary>

| Setting | Description |
|---------|-------------|
| Sonarr Enabled | Toggle Sonarr integration on/off |
| Sonarr Host | Your Sonarr API base URL |
| API Key | Sonarr API key (masked) |
| Anime Tags | Comma-separated Sonarr tag IDs to filter by |
| Remote URL | External Sonarr URL used for deep links |
| Default Root Folder | Pre-selected root folder when adding series |
| Default Quality Profile | Pre-selected quality profile name |
| Default Season Folder | Default season-folder toggle for new series |
| Default Monitor | Default monitor type (`all` / `future` / `missing` / etc.) |
| Avg Episode Size (MB) | Used by Library Analysis for storage estimates |
| Avg GiB/Hour | Used for bandwidth calculation in Library Analysis |

</details>

<details>
<summary>⚙️ App Preferences</summary>

| Setting | Description |
|---------|-------------|
| Show NSFW | Toggle NSFW content visibility |
| Sync Interval | How often the library sync job runs: `hourly` / `every6h` / `every12h` / `daily` / `weekly` (default: daily) |
| Title Similarity Threshold | Fuzzy-match sensitivity (1–100%) for title matching via `pg_trgm` (default: 80) |
| Stats Refresh Interval | How often the download stats cache is refreshed: `1h` / `3h` / `6h` / `12h` (default: 1h) |
| Stats Last Refreshed | Read-only — timestamp of the most recent stats cache update |
| Kometa Mapping URL | URL to the Kometa anime IDs JSON file |

</details>

<details>
<summary>🤖 Bot-kun AI</summary>

| Setting | Description |
|---------|-------------|
| Bot-kun Enabled | Toggle the AI assistant on/off |
| API Base URL | Your OpenAI-compatible provider base URL |
| API Key | Provider API key (masked; optional for local providers) |
| Model | Model ID to use (e.g. `llama3`, `gpt-4o`) |

</details>

---

## ⏱️ Background Jobs

MetaAnime runs four background processes automatically:

| Job | Schedule | What it does |
|-----|----------|-------------|
| **Library Sync** | Configurable (hourly → weekly) | Refreshes Kometa IDs, pulls MAL list, scans Plex libraries, enumerates Sonarr series, runs fuzzy title matching, broadcasts progress via SignalR |
| **Download Stats Cache** | Configurable (1 h / 3 h / 6 h / 12 h) | Queries Sonarr for episode download history, serializes the result to the `PropertyBag` cache, stores a UTC timestamp; runs once on startup to warm the cache |
| **MAL Token Refresh** | Weekly (Sundays) | Silently refreshes the MAL OAuth access token so it never expires mid-session; also runs on startup |
| **Provider Health Monitor** | Every 5 minutes | Probes Plex, Sonarr, and MAL with a 10-second timeout; classifies each as Online / Degraded (>1 000 ms) / Offline; result is served instantly from an in-memory cache |

All Hangfire jobs can be inspected and manually triggered from the **Hangfire dashboard** at `<host>/hangfire`.

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

The Setup Wizard (step 2) lets you set an admin password. If set, a **bcrypt hash** is stored in the `PropertyBag` table — plaintext is never persisted anywhere. Session lifetime defaults to 7 days and is configurable (1–365 days).

Public (no-auth) mode is fully supported — leave the password blank in the wizard to skip authentication entirely.

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
| **Frontend** | Angular 21 (standalone components, signals, `@if`/`@for` control flow) |
| **Database** | PostgreSQL 17 + Entity Framework Core 10 |
| **Background Jobs** | Hangfire (PostgreSQL-backed, scheduled + on-demand) |
| **Real-Time** | SignalR WebSockets (live sync-progress updates) |
| **Streaming AI** | Server-Sent Events (SSE) for token-by-token Bot-kun responses |
| **Container Base** | linuxserver.io Alpine 3.21 + s6-overlay v3 |
| **Auth** | JWT HS256 (auto-generated secret) + optional bcrypt admin password |
| **API Docs** | Scalar UI at `/scalar/v1` |
| **i18n** | ngx-translate — English and French, locale-aware date formatting |

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

### API Design

The API follows **CQRS** with a railway-oriented `Result<T>` pattern — services never throw, controllers translate results to HTTP status codes. Every route is documented in the Scalar UI at `/scalar/v1`.

<details>
<summary>📋 Controller summary</summary>

| Controller | Prefix | Responsibility |
|------------|--------|---------------|
| `AnimeController` | `/api/v1/animes` | Library data, stats, sync trigger, coverage gap queries, Plex thumbnails |
| `SettingsController` | `/api/v1/settings` | Read/write all settings; sync status endpoint |
| `AuthController` | `/api/v1/auth` | Login, password management, JWT issuance |
| `MalController` | `/api/v1/mal` | MAL anime search, list updates, franchise graph, cross-links |
| `MalAuthController` | `/api/v1/mal/auth` | MAL OAuth2 + PKCE initiation, callback, disconnect |
| `SonarrController` | `/api/v1/sonarr` | Series enumeration, lookup, add/remove, tags, quality profiles |
| `PlexAuthController` | `/api/v1/plex/auth` | Plex PIN OAuth flow, library listing, disconnect |
| `SeasonalController` | `/api/v1/seasonal` | Seasonal anime browsing with MAL and Sonarr status |
| `BotKunController` | `/api/v1/botkun` | SSE chat stream, greeting, model discovery |
| `LibraryAnalysisController` | `/api/v1/library-analysis` | Phase 1 summary, Phase 2 codec enrichment |
| `PropertyBagController` | `/api/v1/property-bag` | Generic key-value store CRUD |
| `SystemHealthController` | `/api/v1/system/health` | Plex / Sonarr / MAL live health snapshot |
| `StatusController` | `/api/v1/status` | App initialization flag |

</details>

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
│   └── src/assets/i18n/              # en.json / fr.json translation files
├── root/                             # s6-overlay init scripts
│   └── etc/s6-overlay/s6-rc.d/      # Service definitions
├── Dockerfile                        # Multi-stage production build
└── docker-compose.yml
```

### Build Commands

```bash
# Run API tests
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

### Code Conventions

| Topic | Convention |
|-------|-----------|
| C# version | C# 14 — primary constructors throughout |
| Error handling | `Result<T>` railway pattern — services never `throw`; controllers translate to HTTP status codes |
| Angular components | Standalone only; signals (`input()`, `output()`, `signal()`, `computed()`); `@if`/`@for` control flow — no `*ngIf`/`*ngFor`/`@Input`/`@Output` |
| Database access | EF Core — bulk inserts via EFCore.BulkExtensions for sync jobs; repositories accessed through `AnimeMediatorService` |
| Error constants | Each operation has a dedicated constant in `Constants.Errors`, never a generic fallback |

---

## 📡 API Reference

Interactive API documentation is available at:

```
http://localhost:8080/scalar/v1
```

Scalar provides a clean, modern API explorer with live request execution. All endpoints require the `X-Api-Key` header (auto-generated on first boot and visible in Settings).

---

## 🌐 Internationalization

MetaAnime ships with **English** and **French** translations for every UI string, including all settings field names, option labels, wizard steps, and error messages. The language is selected during the Setup Wizard and can be changed at any time in Settings.

Date and time values are always displayed in the **browser's local timezone** — timestamps stored as UTC are converted client-side.

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

