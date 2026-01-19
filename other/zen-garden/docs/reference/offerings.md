---
audience: [developer, operator, contributor]
doc_type: reference
status: current
last_verified: 2026-01-18
canonical: true
related:
  - ../ops/roadmap.md
---

# Zen Garden Service Inventory

Comprehensive inventory of all services that can be offered by stones, based on Koan framework adapters.

## Data Services

### MongoDB
- **Adapter**: `Koan.Data.Connector.Mongo`
- **Default Port**: 27017
- **Image**: `mongo:7` (official)
- **Category**: Document Database
- **Volumes**: `/data/db` (data), `/data/configdb` (config)
- **Healthcheck**: `mongosh --eval "db.adminCommand('ping')"`
- **Environment Variables**:
  - `MONGO_INITDB_ROOT_USERNAME`
  - `MONGO_INITDB_ROOT_PASSWORD`
  - `MONGO_INITDB_DATABASE`
- **Connection String**: `mongodb://[user:pass@]host:port[/database]`

### PostgreSQL
- **Adapter**: `Koan.Data.Connector.Postgres`
- **Default Port**: 5432
- **Image**: `postgres:16` (official)
- **Category**: Relational Database
- **Volumes**: `/var/lib/postgresql/data`
- **Healthcheck**: `pg_isready -U postgres`
- **Environment Variables**:
  - `POSTGRES_USER`
  - `POSTGRES_PASSWORD`
  - `POSTGRES_DB`
- **Connection String**: `Host=host;Port=port;Database=db;Username=user;Password=pass`
- **Extensions**: pgvector for semantic workloads

### SQLite
- **Adapter**: `Koan.Data.Connector.Sqlite`
- **Default Port**: N/A (embedded)
- **Image**: N/A (file-based)
- **Category**: Embedded Database
- **Notes**: Not suitable for stone offerings (single-process file database)
- **Skip**: Yes (embedded only)

### SQL Server
- **Adapter**: `Koan.Data.Connector.SqlServer`
- **Default Port**: 1433
- **Image**: `mcr.microsoft.com/mssql/server:2022-latest`
- **Category**: Relational Database
- **Volumes**: `/var/opt/mssql`
- **Healthcheck**: `/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P $SA_PASSWORD -Q "SELECT 1"`
- **Environment Variables**:
  - `ACCEPT_EULA=Y` (required)
  - `SA_PASSWORD` (strong password required)
  - `MSSQL_PID=Express` (edition)
- **Connection String**: `Server=host,port;Database=db;User Id=sa;Password=pass;TrustServerCertificate=true`

### Redis (Data)
- **Adapter**: `Koan.Data.Connector.Redis` (RedisJSON)
- **Default Port**: 6379
- **Image**: `redis/redis-stack:latest` (includes RedisJSON, RediSearch)
- **Category**: Key-Value Store / Document
- **Volumes**: `/data`
- **Healthcheck**: `redis-cli ping`
- **Environment Variables**:
  - `REDIS_PASSWORD`
- **Connection String**: `host:port[,password=pass]`

### Couchbase
- **Adapter**: `Koan.Data.Connector.Couchbase`
- **Default Port**: 8091 (web console), 11210 (data)
- **Image**: `couchbase:latest`
- **Category**: Document Database
- **Volumes**: `/opt/couchbase/var`
- **Healthcheck**: `curl -f http://localhost:8091/ui/index.html`
- **Environment Variables**:
  - `COUCHBASE_ADMINISTRATOR_USERNAME`
  - `COUCHBASE_ADMINISTRATOR_PASSWORD`
- **Connection String**: `couchbase://host`

### Elasticsearch
- **Adapter**: `Koan.Data.Connector.ElasticSearch`
- **Default Port**: 9200 (HTTP), 9300 (transport)
- **Image**: `elasticsearch:8.11.0`
- **Category**: Search Engine / Document Store
- **Volumes**: `/usr/share/elasticsearch/data`
- **Healthcheck**: `curl -f http://localhost:9200/_cluster/health`
- **Environment Variables**:
  - `discovery.type=single-node`
  - `xpack.security.enabled=false` (dev only)
  - `ELASTIC_PASSWORD`
- **Connection String**: `http://host:port`

### OpenSearch
- **Adapter**: `Koan.Data.Connector.OpenSearch`
- **Default Port**: 9200
- **Image**: `opensearchproject/opensearch:latest`
- **Category**: Search Engine / Document Store
- **Volumes**: `/usr/share/opensearch/data`
- **Healthcheck**: `curl -f http://localhost:9200/_cluster/health`
- **Environment Variables**:
  - `discovery.type=single-node`
  - `OPENSEARCH_INITIAL_ADMIN_PASSWORD`
  - `plugins.security.disabled=true` (dev only)
- **Connection String**: `http://host:port`

---

## Vector Services

### Weaviate
- **Adapter**: `Koan.Data.Connector.Vector.Weaviate`
- **Default Port**: 8080
- **Image**: `semitechnologies/weaviate:latest`
- **Category**: Vector Database
- **Volumes**: `/var/lib/weaviate`
- **Healthcheck**: `curl -f http://localhost:8080/v1/.well-known/ready`
- **Environment Variables**:
  - `AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED=true` (dev)
  - `PERSISTENCE_DATA_PATH=/var/lib/weaviate`
  - `QUERY_DEFAULTS_LIMIT=25`
- **Connection String**: `http://host:port`

### Milvus
- **Adapter**: `Koan.Data.Connector.Vector.Milvus`
- **Default Port**: 19530 (gRPC), 9091 (HTTP)
- **Image**: `milvusdb/milvus:latest`
- **Category**: Vector Database
- **Volumes**: `/var/lib/milvus`
- **Healthcheck**: `curl -f http://localhost:9091/healthz`
- **Environment Variables**:
  - `ETCD_USE_EMBED=true`
  - `COMMON_STORAGETYPE=local`
- **Connection String**: `host:port`
- **Note**: Requires etcd and MinIO for production; standalone mode for dev

---

## Cache Services

### Redis (Cache)
- **Adapter**: `Koan.Cache.Adapter.Redis`
- **Default Port**: 6379
- **Image**: `redis:7-alpine` (minimal)
- **Category**: Cache
- **Volumes**: `/data`
- **Healthcheck**: `redis-cli ping`
- **Environment Variables**:
  - `REDIS_PASSWORD`
- **Connection String**: `host:port[,password=pass]`

### Memcached
- **Adapter**: N/A (future)
- **Default Port**: 11211
- **Image**: `memcached:alpine`
- **Category**: Cache
- **Skip**: Yes (no adapter yet)

---

## Messaging Services

### RabbitMQ
- **Adapter**: `Koan.Messaging.Connector.RabbitMq`
- **Default Port**: 5672 (AMQP), 15672 (management UI)
- **Image**: `rabbitmq:3-management-alpine`
- **Category**: Message Broker
- **Volumes**: `/var/lib/rabbitmq`
- **Healthcheck**: `rabbitmq-diagnostics ping`
- **Environment Variables**:
  - `RABBITMQ_DEFAULT_USER`
  - `RABBITMQ_DEFAULT_PASS`
- **Connection String**: `amqp://user:pass@host:port/`

---

## AI Services

### Ollama
- **Adapter**: `Koan.AI.Connector.Ollama`
- **Default Port**: 11434
- **Image**: `ollama/ollama:latest`
- **Category**: LLM Runtime
- **Volumes**: `/root/.ollama` (models)
- **Healthcheck**: `curl -f http://localhost:11434/api/tags`
- **Environment Variables**:
  - `OLLAMA_MODELS=/root/.ollama/models` (model storage)
- **Connection String**: `http://host:port`
- **GPU**: Supports NVIDIA GPU passthrough via `--gpus all`

### LM Studio
- **Adapter**: `Koan.AI.Connector.LMStudio`
- **Default Port**: 1234
- **Image**: N/A (desktop app, not containerized)
- **Category**: LLM Runtime
- **Skip**: Yes (not containerizable - desktop GUI app)

---

## Secrets Services

### HashiCorp Vault
- **Adapter**: `Koan.Secrets.Connector.Vault`
- **Default Port**: 8200
- **Image**: `hashicorp/vault:latest`
- **Category**: Secrets Management
- **Volumes**: `/vault/data` (storage), `/vault/logs`
- **Healthcheck**: `vault status`
- **Environment Variables**:
  - `VAULT_DEV_ROOT_TOKEN_ID` (dev mode)
  - `VAULT_ADDR=http://0.0.0.0:8200`
- **Connection String**: `http://host:port`
- **Note**: Dev mode only for stones; production requires unsealing

---

## Storage Services

### Local Filesystem
- **Adapter**: `Koan.Storage.Connector.Local`
- **Skip**: Yes (local filesystem, not a service)

---

## Orchestration Services

### Aspire Dashboard
- **Adapter**: `Koan.Orchestration.Aspire`
- **Default Port**: 18888 (dashboard), 4317 (OTLP)
- **Image**: `mcr.microsoft.com/dotnet/aspire-dashboard:latest`
- **Category**: Observability
- **Healthcheck**: `curl -f http://localhost:18888/health`
- **Environment Variables**:
  - `ASPIRE_DASHBOARD__OTLP__ENDPOINTURL=http://0.0.0.0:4317`
- **Connection String**: `http://host:port` (dashboard)

---

## Summary

**Total Offerings**: 18 containerizable services

### By Category

| Category | Count | Services |
|----------|-------|----------|
| Data | 8 | MongoDB, PostgreSQL, SQL Server, Redis, Couchbase, Elasticsearch, OpenSearch, JSON Files (skip) |
| Vector | 2 | Weaviate, Milvus |
| Cache | 1 | Redis |
| Messaging | 1 | RabbitMQ |
| AI | 1 | Ollama |
| Secrets | 1 | Vault |
| Orchestration | 1 | Aspire Dashboard |
| **Total** | **15** | (3 skipped: SQLite, LM Studio, Local) |

---

## Template Groupings

Based on adapter inventory, suggest these template categories:

### 1. Database (`database.yml`)
- MongoDB
- PostgreSQL
- SQL Server

### 2. Cache (`cache.yml`)
- Redis

### 3. Messaging (`messaging.yml`)
- RabbitMQ

### 4. Search (`search.yml`)
- Elasticsearch
- OpenSearch

### 5. Vector (`vector.yml`)
- Weaviate
- Milvus

### 6. AI (`ai.yml`)
- Ollama

### 7. Secrets (`secrets.yml`)
- HashiCorp Vault

### 8. Fullstack (`fullstack.yml`)
- PostgreSQL (data)
- Redis (cache)
- RabbitMQ (messaging)
- Ollama (AI)

### 9. Observability (`observability.yml`)
- Aspire Dashboard

---

## Next Steps

1. Create YAML manifests for each service (15 files)
2. Organize in `other/zen-garden/manifests/` by category
3. Define template compositions in `templates/` folder
4. Implement `garden-rake catalog` to list available offerings
5. Implement `garden-rake offer <service>` to deploy manifests
