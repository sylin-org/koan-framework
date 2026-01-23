# Zen Garden Offerings Catalog - Day 1 Candidates

**Target Audience:** Hobbyists, self-hosting enthusiasts, homelabs, small businesses
**Status:** Proposal
**Date:** 2026-01-23

This document catalogs candidate offerings for Zen Garden Day 1 launch, organized by category with priority tiers.

---

## Current Offerings (Baseline)

| Category | Offerings |
|----------|-----------|
| **ai** | ollama |
| **data** | mongodb, postgresql, redis, elasticsearch, opensearch, couchbase, sqlserver |
| **vector** | milvus, weaviate |
| **messaging** | rabbitmq |
| **observability** | aspire |
| **secrets** | vault |

---

## Tier 1: High Priority (Core Homelab/Small Business)

These are the most requested, most used services that should be available at launch.

### Database (data)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **mariadb** | `mariadb:latest` | MySQL-compatible RDBMS | Drop-in MySQL replacement, widely used |
| **mysql** | `mysql:latest` | Oracle MySQL | Legacy support, specific compatibility needs |
| **cockroachdb** | `cockroachdb/cockroach` | Distributed SQL | Horizontal scaling, geo-distribution |

### Cache

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **memcached** | `memcached:latest` | High-performance caching | Simple key-value caching |
| **keydb** | `eqalpha/keydb` | Redis-compatible, multithreaded | Drop-in Redis replacement, better performance |
| **dragonfly** | `docker.dragonflydb.io/dragonflydb/dragonfly` | Modern Redis alternative | 25x faster than Redis, drop-in compatible |

### Search

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **meilisearch** | `getmeili/meilisearch` | Lightning-fast search engine | Simple API, typo-tolerant, instant results |
| **typesense** | `typesense/typesense` | Fast, typo-tolerant search | Alternative to Algolia |
| **manticoresearch** | `manticoresearch/manticore` | Full-text search engine | Sphinx fork, SQL-compatible |

### Vector (Extending Current)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **qdrant** | `qdrant/qdrant` | Vector similarity search | Excellent Rust performance, filtering |
| **chroma** | `chromadb/chroma` | AI-native embedding database | Simple Python API, LangChain integration |
| **pgvector** | `pgvector/pgvector` | PostgreSQL vector extension | Use existing Postgres, no new infra |

### Time Series (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **influxdb** | `influxdb:latest` | Time series database | IoT, metrics, sensor data |
| **timescaledb** | `timescale/timescaledb` | PostgreSQL for time series | SQL-native, familiar tooling |
| **questdb** | `questdb/questdb` | High-performance time series | Fastest ingestion, SQL support |
| **victoriametrics** | `victoriametrics/victoria-metrics` | Prometheus-compatible TSDB | Drop-in Prometheus replacement |

### Messaging (Extending Current)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **nats** | `nats:latest` | Cloud-native messaging | Lightweight, JetStream persistence |
| **kafka** | `confluentinc/cp-kafka` | Distributed streaming | Event sourcing, high throughput |
| **redpanda** | `redpandadata/redpanda` | Kafka-compatible streaming | No JVM, simpler operations |
| **mosquitto** | `eclipse-mosquitto` | MQTT broker | IoT, home automation |
| **emqx** | `emqx/emqx` | Scalable MQTT broker | Production MQTT, clustering |

### Observability (Extending Current)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **grafana** | `grafana/grafana` | Visualization & dashboards | De facto standard for metrics viz |
| **prometheus** | `prom/prometheus` | Metrics collection | Standard metrics pipeline |
| **loki** | `grafana/loki` | Log aggregation | "Prometheus for logs" |
| **jaeger** | `jaegertracing/all-in-one` | Distributed tracing | OpenTelemetry compatible |
| **uptimekuma** | `louislam/uptime-kuma` | Status monitoring | Beautiful UI, push notifications |
| **graylog** | `graylog/graylog` | Log management | Security-focused, compliance |

### AI (Extending Current)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **localai** | `localai/localai` | OpenAI-compatible API | Multiple model backends |
| **textgen-webui** | `atinoda/text-generation-webui` | LLM interface | Gradio UI, model management |
| **vllm** | `vllm/vllm-openai` | High-performance inference | Production LLM serving |
| **whisper** | `onerahmet/openai-whisper-asr-webservice` | Speech-to-text | Audio transcription |
| **comfyui** | `comfyanonymous/comfyui` | Image generation | Stable Diffusion workflows |
| **openwebui** | `ghcr.io/open-webui/open-webui` | ChatGPT-like interface | Ollama frontend, RAG support |

---

## Tier 2: Important (Productivity & Media)

### Media (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **jellyfin** | `jellyfin/jellyfin` | Media server | Open source Plex alternative |
| **plex** | `plexinc/pms-docker` | Media server | Most popular, proprietary |
| **audiobookshelf** | `ghcr.io/advplyr/audiobookshelf` | Audiobook/podcast server | Niche but beloved |
| **navidrome** | `deluan/navidrome` | Music streaming | Lightweight Subsonic server |
| **photoprism** | `photoprism/photoprism` | Photo management | AI-powered, Google Photos alt |
| **immich** | `ghcr.io/immich-app/immich-server` | Photo/video backup | Mobile-first, fast |

### Storage (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **minio** | `minio/minio` | S3-compatible storage | Object storage standard |
| **seaweedfs** | `chrislusf/seaweedfs` | Distributed file system | Scalable blob storage |
| **nextcloud** | `nextcloud:latest` | Cloud storage platform | Full-featured Dropbox alternative |
| **filebrowser** | `filebrowser/filebrowser` | Web file manager | Simple file access UI |
| **syncthing** | `syncthing/syncthing` | P2P file sync | Dropbox alternative, no server |

### Automation (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **n8n** | `n8nio/n8n` | Workflow automation | Zapier/Make alternative, 400+ integrations |
| **huginn** | `huginn/huginn` | Agent automation | IFTTT alternative, programmable |
| **activepieces** | `activepieces/activepieces` | No-code automation | Modern UI, growing fast |
| **automatisch** | `automatisch/automatisch` | Zapier alternative | Open source, self-hosted |

### Home Automation (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **homeassistant** | `ghcr.io/home-assistant/home-assistant` | Home automation | 3000+ integrations |
| **nodered** | `nodered/node-red` | Flow-based programming | Visual automation |
| **mosquitto** | `eclipse-mosquitto` | MQTT broker | IoT messaging |
| **zigbee2mqtt** | `koenkk/zigbee2mqtt` | Zigbee bridge | Smart home devices |

### DevOps (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **gitea** | `gitea/gitea` | Git server | Lightweight GitHub alternative |
| **forgejo** | `codeberg.org/forgejo/forgejo` | Git server | Gitea fork, community-driven |
| **woodpecker** | `woodpeckerci/woodpecker-server` | CI/CD | Drone fork, simple YAML |
| **drone** | `drone/drone` | CI/CD | Lightweight, Go-based |
| **jenkins** | `jenkins/jenkins` | CI/CD | Enterprise standard |
| **registry** | `registry:2` | Container registry | Private Docker images |
| **harbor** | `goharbor/harbor-core` | Enterprise registry | Scanning, replication |

### Reverse Proxy / Ingress (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **traefik** | `traefik:latest` | Cloud-native proxy | Auto-discovery, Let's Encrypt |
| **caddy** | `caddy:latest` | Modern web server | Automatic HTTPS, simple config |
| **nginx-proxy-manager** | `jc21/nginx-proxy-manager` | Nginx with UI | Easy management interface |

---

## Tier 3: Nice to Have (Specialized)

### Security (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **vaultwarden** | `vaultwarden/server` | Password manager | Bitwarden-compatible, lightweight |
| **keycloak** | `quay.io/keycloak/keycloak` | Identity management | SSO, OIDC, SAML |
| **authelia** | `authelia/authelia` | Auth proxy | 2FA, SSO for services |
| **authentik** | `ghcr.io/goauthentik/server` | Identity provider | Modern Keycloak alternative |

### Networking (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **pihole** | `pihole/pihole` | DNS sinkhole | Ad blocking, network-wide |
| **adguard** | `adguard/adguardhome` | DNS filtering | Pi-hole alternative |
| **wireguard** | `linuxserver/wireguard` | VPN | Fast, modern VPN |
| **tailscale** | `tailscale/tailscale` | Mesh VPN | Zero-config networking |
| **headscale** | `headscale/headscale` | Tailscale control server | Self-hosted Tailscale |
| **netbird** | `netbirdio/netbird` | WireGuard mesh | Open source Tailscale alt |

### Productivity (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **bookstack** | `linuxserver/bookstack` | Documentation wiki | Clean UI, easy to use |
| **outline** | `outlinewiki/outline` | Knowledge base | Notion-like, modern |
| **wiki.js** | `ghcr.io/requarks/wiki` | Wiki engine | Beautiful, powerful |
| **hedgedoc** | `quay.io/hedgedoc/hedgedoc` | Collaborative markdown | HackMD/CodiMD fork |
| **paperless-ngx** | `ghcr.io/paperless-ngx/paperless-ngx` | Document management | OCR, tagging, search |

### Communication (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **matrix-synapse** | `matrixdotorg/synapse` | Chat server | Federated, E2E encrypted |
| **mattermost** | `mattermost/mattermost-enterprise-edition` | Team chat | Slack alternative |
| **rocket.chat** | `rocket.chat` | Team chat | Full-featured, plugins |
| **gotify** | `gotify/server` | Push notifications | Self-hosted push service |
| **ntfy** | `binwiederhier/ntfy` | Push notifications | Simple, pub/sub |

### Dashboards (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **homepage** | `ghcr.io/gethomepage/homepage` | Application dashboard | Service integrations, widgets |
| **homarr** | `ghcr.io/ajnart/homarr` | Dashboard | *arr integration, Docker aware |
| **dashy** | `lissy93/dashy` | Dashboard | Highly customizable |
| **homer** | `b4bz/homer` | Dashboard | Simple YAML config |
| **heimdall** | `linuxserver/heimdall` | Dashboard | Application launcher |

### Backup (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **duplicati** | `linuxserver/duplicati` | Backup solution | Encrypted, cloud targets |
| **restic** | `restic/restic` | Backup program | Fast, deduplicating |
| **borgmatic** | `b3vis/borgmatic` | Borg wrapper | Scheduled backups |
| **kopia** | `kopia/kopia` | Backup tool | Modern, fast |

### Surveillance (NEW CATEGORY)

| Offering | Docker Image | Description | Why Include |
|----------|--------------|-------------|-------------|
| **frigate** | `ghcr.io/blakeblackshear/frigate` | NVR with AI | Coral TPU support, object detection |
| **zoneminder** | `zoneminderhq/zoneminder` | Video surveillance | Mature, feature-rich |
| **shinobi** | `shinobisystems/shinobi` | NVR | Modern alternative |

---

## Category Taxonomy Update

Based on this analysis, the recommended category taxonomy for Zen Garden:

```yaml
categories:
  # Data Layer
  - database        # SQL, NoSQL, document stores
  - cache           # In-memory data stores
  - search          # Full-text and semantic search
  - vector          # Embedding databases
  - timeseries      # Time series databases

  # Infrastructure
  - messaging       # Message queues, event streaming
  - storage         # Object storage, file systems
  - proxy           # Reverse proxies, load balancers
  - networking      # VPN, DNS, mesh

  # Operations
  - observability   # Metrics, logs, traces, dashboards
  - secrets         # Secret management, vaults
  - devops          # Git, CI/CD, registries
  - backup          # Backup solutions

  # AI & ML
  - ai              # LLMs, inference, ML serving

  # Applications
  - media           # Media servers, photo management
  - automation      # Workflow automation
  - home            # Home automation, IoT
  - productivity    # Wikis, docs, notes
  - communication   # Chat, notifications
  - security        # Auth, identity, passwords
  - dashboard       # Homepages, launchers
  - surveillance    # NVR, cameras
```

---

## Prioritized Launch List

### Phase 1: Core Infrastructure (Launch) - 23 Offerings
Essential services every homelab needs. Fully validated with manifests.

| Category | Offerings | Status |
|----------|-----------|--------|
| **data** | postgresql, mongodb, mariadb, redis | ✅ Complete |
| **messaging** | rabbitmq, nats | ✅ Complete |
| **search** | elasticsearch, opensearch | ✅ Complete |
| **vector** | milvus | ✅ Complete |
| **secrets** | vault | ✅ Complete |
| **ai** | ollama | ✅ Complete |
| **observability** | prometheus, grafana | ✅ Complete |
| **storage** | minio, nextcloud | ✅ Complete |
| **proxy** | traefik | ✅ Complete |
| **networking** | pihole, wireguard | ✅ Complete |
| **auth** | authelia | ✅ Complete |
| **dashboard** | homepage | ✅ Complete |
| **automation** | n8n | ✅ Complete |
| **devops** | registry | ✅ Complete |
| **timeseries** | influxdb | ✅ Complete |

### Phase 2: Productivity & Media (Month 2)
Services that make homelabs useful:

1. **media**: jellyfin, photoprism, immich, audiobookshelf, navidrome
2. **devops**: gitea, woodpecker, drone
3. **automation**: nodered, huginn
4. **home**: homeassistant, zigbee2mqtt
5. **productivity**: bookstack, paperless-ngx, outline
6. **dashboard**: homarr, dashy
7. **timeseries**: victoriametrics, timescaledb
8. **cache**: memcached, dragonfly

### Phase 3: Advanced (Month 3+)
Specialized and enterprise-adjacent:

1. **security**: keycloak, authentik, vaultwarden
2. **networking**: adguard, tailscale, headscale
3. **communication**: matrix-synapse, ntfy, gotify
4. **surveillance**: frigate, zoneminder
5. **backup**: duplicati, kopia, borgmatic
6. **search**: meilisearch, typesense

---

## Sources

Research compiled from:
- [awesome-selfhosted/awesome-selfhosted](https://github.com/awesome-selfhosted/awesome-selfhosted) - 227k+ stars
- [TechHut: Must Have Homelab Services 2025](https://techhut.tv/must-have-home-server-services-2025/)
- [Teqqy: Top 10 Selfhosted & Homelab Software 2025](https://teqqy.de/en/my-top-10-selfhosted-and-homelab-software-2025-favorite-tools-for-the-new-year/)
- [selfh.st: Favorite New Apps 2025](https://selfh.st/post/2025-favorite-new-apps/)
- [Perfect Media Server: Top 10 Self-Hosted Apps](https://perfectmediaserver.com/04-day-two/top10apps/)
- [Virtualization Howto: 15 Docker Containers](https://www.virtualizationhowto.com/2025/11/15-docker-containers-that-make-your-home-lab-instantly-better/)
- [Blog.elest.io: The 2026 Homelab Stack](https://blog.elest.io/the-2026-homelab-stack-what-self-hosters-are-actually-running-this-year/)

---

## Appendix: Docker Hub Stats (Popularity Reference)

| Image | Pulls |
|-------|-------|
| redis | 1B+ |
| postgres | 1B+ |
| nginx | 1B+ |
| mysql | 1B+ |
| mongo | 500M+ |
| mariadb | 500M+ |
| elasticsearch | 500M+ |
| rabbitmq | 500M+ |
| grafana | 500M+ |
| prometheus | 100M+ |
| influxdb | 100M+ |
| minio | 100M+ |
| traefik | 100M+ |
| jenkins | 100M+ |
| nextcloud | 100M+ |
| gitea | 50M+ |
| jellyfin | 50M+ |
| homeassistant | 50M+ |
| vaultwarden | 10M+ |
| n8n | 10M+ |
| meilisearch | 10M+ |
| qdrant | 5M+ |
| uptimekuma | 5M+ |

---

## Notes for Implementation

### Manifest Considerations

1. **GPU Detection**: AI offerings should auto-detect GPU availability and adjust configuration
2. **ARM Support**: Many homelabbers use Raspberry Pi - verify ARM64 image availability
3. **Resource Defaults**: Set sensible defaults for homelab scale (not enterprise)
4. **Volume Mounts**: Standardize data persistence patterns
5. **Health Checks**: Every offering needs a health check endpoint
6. **Connection Strings**: Auto-generate and expose connection info

### Naming Conventions

- Use lowercase, hyphenated names: `nginx-proxy-manager`, not `NginxProxyManager`
- Match Docker Hub naming where possible
- Keep names concise but recognizable

### Tags to Consider

```yaml
tags:
  - nosql, sql, relational, document, graph
  - gpu, cpu-only, arm64, x86
  - stateless, stateful
  - lightweight, enterprise
  - oss, proprietary
  - production-ready, experimental
```
