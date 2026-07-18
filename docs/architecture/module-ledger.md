# Module Inventory Ledger

### Koan.AI
- Depends on: Koan.AI.Contracts, Koan.Core
- Depended by: Koan.AI.Agents, Koan.AI.Eval, Koan.AI.Models, Koan.AI.Orchestration, Koan.AI.Web, Koan.Data.AI
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Agents
- Depends on: Koan.AI, Koan.AI.Contracts, Koan.AI.Contracts.Shared, Koan.AI.Orchestration, Koan.Core, Koan.Data.Core, Koan.Data.Vector
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.AI.Compute
- Depends on: Koan.AI.Contracts.Shared, Koan.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.AI.Connector.HuggingFace
- Depends on: Koan.AI.Contracts, Koan.AI.Contracts.Shared, Koan.AI.Models, Koan.Core
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.AI.Connector.LMStudio
- Depends on: Koan.AI.Contracts, Koan.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Connector.Ollama
- Depends on: Koan.AI.Contracts, Koan.Core, Koan.ZenGarden.Contracts
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Connector.Onnx
- Depends on: Koan.AI.Contracts, Koan.Core
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.AI.Contracts
- Depends on: –
- Depended by: Koan.AI, Koan.AI.Agents, Koan.AI.Connector.HuggingFace, Koan.AI.Connector.LMStudio, Koan.AI.Connector.Ollama, Koan.AI.Connector.Onnx, Koan.AI.Eval, Koan.AI.Models, Koan.AI.Orchestration, Koan.AI.Prompt, Koan.AI.Web, Koan.Data.AI, Koan.ZenGarden
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Contracts.Shared
- Depends on: –
- Depended by: Koan.AI.Agents, Koan.AI.Compute, Koan.AI.Connector.HuggingFace, Koan.AI.Eval, Koan.AI.Models, Koan.AI.Orchestration
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Eval
- Depends on: Koan.AI, Koan.AI.Contracts, Koan.AI.Contracts.Shared, Koan.Core, Koan.Data.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.AI.Models
- Depends on: Koan.AI, Koan.AI.Contracts, Koan.AI.Contracts.Shared, Koan.Core, Koan.Data.Core
- Depended by: Koan.AI.Connector.HuggingFace
- Documentation: README ✅ · TECHNICAL ❌

### Koan.AI.Orchestration
- Depends on: Koan.AI, Koan.AI.Contracts, Koan.AI.Contracts.Shared, Koan.AI.Prompt, Koan.Core, Koan.Data.Core, Koan.Data.Vector
- Depended by: Koan.AI.Agents
- Documentation: README ✅ · TECHNICAL ❌

### Koan.AI.Prompt
- Depends on: Koan.AI.Contracts, Koan.Core, Koan.Data.Core
- Depended by: Koan.AI.Orchestration
- Documentation: README ✅ · TECHNICAL ✅

### Koan.AI.Review
- Depends on: Koan.Core, Koan.Data.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.AI.Web
- Depends on: Koan.AI, Koan.AI.Contracts, Koan.Web, Koan.Web.Sse
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Cache
- Depends on: Koan.Cache.Abstractions, Koan.Communication, Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Cache.Adapter.Redis, Koan.Cache.Adapter.Sqlite, Koan.Mcp.Operations
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Cache.Abstractions
- Depends on: Koan.Core
- Depended by: Koan.Cache, Koan.Cache.Adapter.Redis, Koan.Cache.Adapter.Sqlite, Koan.Data.Core, Koan.Web.OpenGraph
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Cache.Adapter.Redis
- Depends on: Koan.Cache, Koan.Cache.Abstractions, Koan.Communication, Koan.Data.Connector.Redis
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Cache.Adapter.Sqlite
- Depends on: Koan.Cache, Koan.Cache.Abstractions, Koan.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Canon
- Depends on: Koan.Canon.Contracts, Koan.Core, Koan.Data.Core
- Depended by: Koan.Canon.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Canon.Contracts
- Depends on: Koan.Core, Koan.Data.Core
- Depended by: Koan.Canon, Koan.Canon.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Canon.Web
- Depends on: Koan.Canon, Koan.Canon.Contracts, Koan.Core, Koan.Web, Koan.Web.Extensions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Classification
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Communication
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Cache, Koan.Cache.Adapter.Redis, Koan.Communication.Connector.RabbitMq, Koan.Jobs
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Communication.Connector.RabbitMq
- Depends on: Koan.Communication, Koan.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Core
- Depends on: –
- Depended by: Koan.AI, Koan.AI.Agents, Koan.AI.Compute, Koan.AI.Connector.HuggingFace, Koan.AI.Connector.LMStudio, Koan.AI.Connector.Ollama, Koan.AI.Connector.Onnx, Koan.AI.Eval, Koan.AI.Models, Koan.AI.Orchestration, Koan.AI.Prompt, Koan.AI.Review, Koan.Cache, Koan.Cache.Adapter.Sqlite, Koan.Canon, Koan.Canon.Contracts, Koan.Canon.Web, Koan.Classification, Koan.Communication, Koan.Communication.Connector.RabbitMq, Koan.Data.Abstractions, Koan.Data.Access, Koan.Data.AI, Koan.Data.Backup, Koan.Data.Connector.Cockroach, Koan.Data.Connector.Couchbase, Koan.Data.Connector.ElasticSearch, Koan.Data.Connector.InMemory, Koan.Data.Connector.Json, Koan.Data.Connector.Mongo, Koan.Data.Connector.OpenSearch, Koan.Data.Connector.Postgres, Koan.Data.Connector.Redis, Koan.Data.Core, Koan.Data.SearchEngine, Koan.Data.SoftDelete, Koan.Data.Vector.Connector.InMemory, Koan.Data.Vector.Connector.Milvus, Koan.Data.Vector.Connector.Qdrant, Koan.Data.Vector.Connector.SqliteVec, Koan.Data.Vector.Connector.Weaviate, Koan.Identity, Koan.Jobs, Koan.Mcp, Koan.Media.Abstractions, Koan.Observability, Koan.Orchestration.Aspire, Koan.Orchestration.Connector.Docker, Koan.Orchestration.Connector.Podman, Koan.Orchestration.Renderers.Connector.Compose, Koan.Security.Trust, Koan.Storage, Koan.Tenancy, Koan.Testing, Koan.Testing.Containers, Koan.Web, Koan.Web.Auth, Koan.Web.Auth.Connector.Discord, Koan.Web.Auth.Connector.Google, Koan.Web.Auth.Connector.Microsoft, Koan.Web.Auth.Connector.Oidc, Koan.Web.Auth.Connector.Test, Koan.Web.Auth.Roles, Koan.Web.Auth.Server, Koan.Web.Auth.Services, Koan.Web.Backup, Koan.Web.Extensions, Koan.Web.OpenApi, Koan.Web.OpenGraph, Koan.Web.Sse, Koan.ZenGarden
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Core.Registry.Generators
- Depends on: –
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Data.Abstractions
- Depends on: Koan.Core
- Depended by: Koan.Cache, Koan.Classification, Koan.Communication, Koan.Data.Access, Koan.Data.AI, Koan.Data.Backup, Koan.Data.Connector.Cockroach, Koan.Data.Connector.Couchbase, Koan.Data.Connector.ElasticSearch, Koan.Data.Connector.InMemory, Koan.Data.Connector.Json, Koan.Data.Connector.Mongo, Koan.Data.Connector.OpenSearch, Koan.Data.Connector.Postgres, Koan.Data.Connector.Redis, Koan.Data.Connector.Sqlite, Koan.Data.Connector.SqlServer, Koan.Data.Core, Koan.Data.Relational, Koan.Data.SearchEngine, Koan.Data.SoftDelete, Koan.Data.Vector.Abstractions, Koan.Data.Vector.Connector.InMemory, Koan.Data.Vector.Connector.Milvus, Koan.Data.Vector.Connector.Qdrant, Koan.Data.Vector.Connector.SqliteVec, Koan.Data.Vector.Connector.Weaviate, Koan.Jobs, Koan.Media.Abstractions, Koan.Storage, Koan.Storage.Abstractions, Koan.Tenancy, Koan.Testing, Koan.Web, Koan.Web.Backup, Koan.Web.Extensions
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Access
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Data.AI
- Depends on: Koan.AI, Koan.AI.Contracts, Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Vector
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Backup
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Storage, Koan.Storage.Abstractions
- Depended by: Koan.Web.Backup
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Cockroach
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational, Koan.Data.Relational.Npgsql
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Couchbase
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.ElasticSearch
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.SearchEngine, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.InMemory
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Json
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Mongo
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.ZenGarden.Contracts
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.OpenSearch
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.SearchEngine, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Postgres
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational, Koan.Data.Relational.Npgsql, Koan.Orchestration.Aspire.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Redis
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Orchestration.Aspire
- Depended by: Koan.Cache.Adapter.Redis
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.Sqlite
- Depends on: Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Connector.SqlServer
- Depends on: Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Core
- Depends on: Koan.Cache.Abstractions, Koan.Core, Koan.Data.Abstractions
- Depended by: Koan.AI.Agents, Koan.AI.Eval, Koan.AI.Models, Koan.AI.Orchestration, Koan.AI.Prompt, Koan.AI.Review, Koan.Cache, Koan.Canon, Koan.Canon.Contracts, Koan.Classification, Koan.Communication, Koan.Data.Access, Koan.Data.AI, Koan.Data.Backup, Koan.Data.Connector.Cockroach, Koan.Data.Connector.Couchbase, Koan.Data.Connector.ElasticSearch, Koan.Data.Connector.InMemory, Koan.Data.Connector.Json, Koan.Data.Connector.Mongo, Koan.Data.Connector.OpenSearch, Koan.Data.Connector.Postgres, Koan.Data.Connector.Redis, Koan.Data.Connector.Sqlite, Koan.Data.Connector.SqlServer, Koan.Data.Relational, Koan.Data.SoftDelete, Koan.Data.Vector, Koan.Data.Vector.Connector.InMemory, Koan.Data.Vector.Connector.Milvus, Koan.Data.Vector.Connector.Qdrant, Koan.Data.Vector.Connector.SqliteVec, Koan.Data.Vector.Connector.Weaviate, Koan.Identity, Koan.Jobs, Koan.Mcp, Koan.Media.Core, Koan.Media.Web, Koan.Storage, Koan.Tenancy, Koan.Testing, Koan.Testing.Containers, Koan.Web, Koan.Web.Auth.Roles, Koan.Web.Auth.Server, Koan.Web.Backup, Koan.Web.Extensions, Koan.Web.OpenGraph
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Relational
- Depends on: Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational.Abstractions
- Depended by: Koan.Data.Connector.Cockroach, Koan.Data.Connector.Postgres, Koan.Data.Connector.Sqlite, Koan.Data.Connector.SqlServer, Koan.Data.Relational.Npgsql
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Relational.Abstractions
- Depends on: Koan.Data.Abstractions
- Depended by: Koan.Data.Relational
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Relational.Npgsql
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Relational
- Depended by: Koan.Data.Connector.Cockroach, Koan.Data.Connector.Postgres
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.SearchEngine
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Vector, Koan.Data.Vector.Abstractions
- Depended by: Koan.Data.Connector.ElasticSearch, Koan.Data.Connector.OpenSearch
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.SoftDelete
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Data.Vector
- Depends on: Koan.Data.Core, Koan.Data.Vector.Abstractions
- Depended by: Koan.AI.Agents, Koan.AI.Orchestration, Koan.Data.AI, Koan.Data.SearchEngine, Koan.Data.Vector.Connector.Milvus, Koan.Data.Vector.Connector.Qdrant, Koan.Data.Vector.Connector.Weaviate
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector.Abstractions
- Depends on: Koan.Data.Abstractions
- Depended by: Koan.Data.Connector.ElasticSearch, Koan.Data.Connector.OpenSearch, Koan.Data.SearchEngine, Koan.Data.Vector, Koan.Data.Vector.Connector.InMemory, Koan.Data.Vector.Connector.Milvus, Koan.Data.Vector.Connector.Qdrant, Koan.Data.Vector.Connector.SqliteVec, Koan.Data.Vector.Connector.Weaviate
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector.Connector.InMemory
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Data.Vector.Connector.Milvus
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Vector, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Data.Vector.Connector.Qdrant
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Vector, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Data.Vector.Connector.SqliteVec
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Vector.Abstractions
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Data.Vector.Connector.Weaviate
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Data.Vector, Koan.Data.Vector.Abstractions, Koan.ZenGarden.Contracts
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Identity
- Depends on: Koan.Core, Koan.Data.Core, Koan.Web.Auth
- Depended by: Koan.Identity.Credentials, Koan.Identity.Tenancy, Koan.Identity.Web
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Identity.Credentials
- Depends on: Koan.Identity
- Depended by: Koan.Identity.Mfa, Koan.Identity.Passwords
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Identity.Mfa
- Depends on: Koan.Identity.Credentials
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Identity.Passwords
- Depends on: Koan.Identity.Credentials
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Identity.Tenancy
- Depends on: Koan.Identity, Koan.Tenancy, Koan.Web
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Identity.Web
- Depends on: Koan.Identity, Koan.Web
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Jobs
- Depends on: Koan.Communication, Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Mcp.Operations, Koan.Tenancy.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Mcp
- Depends on: Koan.Core, Koan.Data.Core, Koan.Security.Trust, Koan.Web, Koan.Web.Sse
- Depended by: Koan.Mcp.Explorer, Koan.Mcp.Operations
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Mcp.Explorer
- Depends on: Koan.Mcp
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Mcp.Operations
- Depends on: Koan.Cache, Koan.Jobs, Koan.Mcp
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Media.Abstractions
- Depends on: Koan.Data.Abstractions, Koan.Storage.Abstractions
- Depended by: Koan.Media.Core, Koan.Media.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Media.Core
- Depends on: Koan.Data.Core, Koan.Media.Abstractions, Koan.Storage, Koan.Storage.Abstractions
- Depended by: Koan.Media.Web
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Media.Web
- Depends on: Koan.Data.Core, Koan.Media.Abstractions, Koan.Media.Core, Koan.Storage, Koan.Storage.Abstractions, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Observability
- Depends on: Koan.Core
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Orchestration.Abstractions
- Depends on: –
- Depended by: Koan.Orchestration.Cli, Koan.Orchestration.Connector.Docker, Koan.Orchestration.Connector.Podman, Koan.Orchestration.Renderers.Connector.Compose
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Aspire
- Depends on: Koan.Core
- Depended by: Koan.Data.Connector.Postgres, Koan.Data.Connector.Redis
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Cli
- Depends on: Koan.Orchestration.Abstractions, Koan.Orchestration.Connector.Docker, Koan.Orchestration.Connector.Podman, Koan.Orchestration.Generators, Koan.Orchestration.Renderers.Connector.Compose
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Connector.Docker
- Depends on: Koan.Core, Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Connector.Podman
- Depends on: Koan.Core, Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Generators
- Depends on: –
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Orchestration.Renderers.Connector.Compose
- Depends on: Koan.Core, Koan.Orchestration.Abstractions
- Depended by: Koan.Orchestration.Cli
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Security.Trust
- Depends on: Koan.Core
- Depended by: Koan.Mcp, Koan.Web.Auth, Koan.Web.Auth.Server
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Storage
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Storage.Abstractions
- Depended by: Koan.Data.Backup, Koan.Media.Core, Koan.Media.Web, Koan.Storage.Connector.Local, Koan.Storage.Connector.S3
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Storage.Abstractions
- Depends on: Koan.Data.Abstractions
- Depended by: Koan.Data.Backup, Koan.Media.Abstractions, Koan.Media.Core, Koan.Media.Web, Koan.Storage, Koan.Storage.Connector.Local, Koan.Storage.Connector.S3
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Storage.Connector.Local
- Depends on: Koan.Storage, Koan.Storage.Abstractions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Storage.Connector.S3
- Depends on: Koan.Storage, Koan.Storage.Abstractions, Koan.ZenGarden.Contracts
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Tenancy
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Identity.Tenancy, Koan.Tenancy.Web
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Tenancy.Web
- Depends on: Koan.Jobs, Koan.Tenancy, Koan.Web
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Testing
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Testing.Hosting
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Testing.Containers
- Depends on: Koan.Core, Koan.Data.Core, Koan.Testing.Hosting
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Testing.Hosting
- Depends on: –
- Depended by: Koan.Testing, Koan.Testing.Containers
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core
- Depended by: Koan.Canon.Web, Koan.Identity.Tenancy, Koan.Identity.Web, Koan.Mcp, Koan.Media.Web, Koan.Tenancy.Web, Koan.Web.Admin, Koan.Web.Auth, Koan.Web.Auth.Server, Koan.Web.Backup, Koan.Web.Extensions, Koan.Web.OpenApi, Koan.Web.OpenGraph, Koan.Web.Sse
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Admin
- Depends on: Koan.Web
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Web.Auth
- Depends on: Koan.Core, Koan.Security.Trust, Koan.Web
- Depended by: Koan.Identity, Koan.Web.Auth.Connector.Discord, Koan.Web.Auth.Connector.Google, Koan.Web.Auth.Connector.Microsoft, Koan.Web.Auth.Connector.Oidc, Koan.Web.Auth.Connector.Test, Koan.Web.Auth.Roles, Koan.Web.Auth.Server, Koan.Web.Auth.Services
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Discord
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Google
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Microsoft
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Oidc
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Connector.Test
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Roles
- Depends on: Koan.Core, Koan.Data.Core, Koan.Web.Auth, Koan.Web.Extensions
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Auth.Server
- Depends on: Koan.Core, Koan.Data.Core, Koan.Security.Trust, Koan.Web, Koan.Web.Auth
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Web.Auth.Services
- Depends on: Koan.Core, Koan.Web.Auth
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.Web.Backup
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Backup, Koan.Data.Core, Koan.Web
- Depended by: –
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Web.Extensions
- Depends on: Koan.Core, Koan.Data.Abstractions, Koan.Data.Core, Koan.Web
- Depended by: Koan.Canon.Web, Koan.Web.Auth.Roles
- Documentation: README ✅ · TECHNICAL ❌

### Koan.Web.OpenApi
- Depends on: Koan.Core, Koan.Web
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Web.OpenGraph
- Depends on: Koan.Cache.Abstractions, Koan.Core, Koan.Data.Core, Koan.Web
- Depended by: –
- Documentation: README ❌ · TECHNICAL ❌

### Koan.Web.Sse
- Depends on: Koan.Core, Koan.Web
- Depended by: Koan.AI.Web, Koan.Mcp
- Documentation: README ❌ · TECHNICAL ❌

### Koan.ZenGarden
- Depends on: Koan.AI.Contracts, Koan.Core, Koan.ZenGarden.Contracts
- Depended by: –
- Documentation: README ✅ · TECHNICAL ✅

### Koan.ZenGarden.Contracts
- Depends on: –
- Depended by: Koan.AI.Connector.Ollama, Koan.Data.Connector.Mongo, Koan.Data.Vector.Connector.Weaviate, Koan.Storage.Connector.S3, Koan.ZenGarden
- Documentation: README ❌ · TECHNICAL ❌

