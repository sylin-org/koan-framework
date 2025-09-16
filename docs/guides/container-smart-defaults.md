# Container-smart defaults and discovery lists

Koan adapters and AI providers include discovery logic to make local and docker-compose setups work with minimal configuration.

Key behaviors:

- Env-list variables let you specify multiple candidates in order; the first reachable is selected:
  - Ollama: `Koan_AI_OLLAMA_URLS`
  - Weaviate: `Koan_DATA_WEAVIATE_URLS`
  - MongoDB: `Koan_DATA_MONGO_URLS`
  - Redis: `Koan_DATA_REDIS_URLS`
- The special value `auto` on the main option field opts into discovery explicitly:
  - `Koan:Data:Weaviate:Endpoint = "auto"`
  - `Koan:Data:Mongo:ConnectionString = "auto"`
  - `Koan:Data:Redis:ConnectionString = "auto"`
- Defaults reflect dev-compose reality and in-container vs host environments:
  - Weaviate: host.docker.internal:8080 → localhost:8080 → weaviate:8080 → localhost:8085
  - Ollama: localhost:11434 → 127.0.0.1:11434 → host.docker.internal:11434 → ollama:11434
  - Mongo: mongodb://mongodb:27017 (in-container) or mongodb://localhost:27017 (host)
  - Redis: redis:6379 (in-container) or localhost:6379 (host)
- Ollama discovery in non-dev is allowed by default (disable via `Koan:Ai:AllowDiscoveryInNonDev=false`).

Recommendation: In samples and dev compose, prefer env-lists to communicate intent while preserving quick local runs.
