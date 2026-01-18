# Zen Garden Service Manifests

Service and template definitions for `garden-rake offer` command.

## Structure

```
manifests/
  data/           - Database services
  vector/         - Vector database services
  cache/          - Cache services
  messaging/      - Message broker services
  ai/             - AI/LLM runtime services
  secrets/        - Secrets management services
  observability/  - Monitoring services
  templates/      - Multi-service templates
```

## Individual Services

### Data (7 services)
- `mongodb` - MongoDB 7.x document database
- `postgresql` - PostgreSQL 16 with pgvector
- `sqlserver` - SQL Server 2022 Express
- `redis` - Redis Stack (JSON, Search)
- `couchbase` - Couchbase Server Community
- `elasticsearch` - Elasticsearch 8.x
- `opensearch` - OpenSearch 2.x

### Vector (2 services)
- `weaviate` - Weaviate vector database
- `milvus` - Milvus vector database (standalone)

### Cache (1 service)
- `redis` - Redis 7.x in-memory cache

### Messaging (1 service)
- `rabbitmq` - RabbitMQ 3.x with management UI

### AI (1 service)
- `ollama` - Ollama local LLM runtime

### Secrets (1 service)
- `vault` - HashiCorp Vault (dev mode)

### Observability (1 service)
- `aspire` - .NET Aspire Dashboard

## Templates

Predefined service bundles:

- `database` - MongoDB + PostgreSQL + SQL Server
- `cache` - Redis
- `messaging` - RabbitMQ
- `search` - Elasticsearch + OpenSearch
- `vector` - Weaviate + Milvus
- `ai` - Ollama
- `secrets` - Vault
- `observability` - Aspire Dashboard
- `fullstack` - PostgreSQL + Redis + RabbitMQ + Ollama

## Usage

```bash
# Single service
garden-rake offer mongodb

# Multiple services
garden-rake offer mongodb redis

# Template
garden-rake offer --template fullstack

# List available
garden-rake catalog
```

## Manifest Schema

Each service manifest includes:

- `name` - Service identifier
- `description` - Human-readable description
- `category` - Service category (data, vector, cache, etc.)
- `image` - Docker image reference
- `port` - Primary port number
- `offering` - mDNS offering identifier
- `environment` - Environment variables with defaults
- `volumes` - Volume mappings
- `healthcheck` - Docker healthcheck configuration
- `restart` - Restart policy
- `labels` - Metadata labels

## Environment Variables

All secrets use `${VARIABLE:-default}` pattern for overrides:

```bash
# MongoDB
export MONGO_PASSWORD=secure123
garden-rake offer mongodb

# PostgreSQL
export POSTGRES_PASSWORD=secure123
export POSTGRES_DB=myapp
garden-rake offer postgresql
```

## Notes

- All services use named volumes for persistence
- Healthchecks ensure services are ready before announcing
- Default credentials are provided for development only
- Production deployments should override all passwords
- GPU support for Ollama is commented out (enable manually)
