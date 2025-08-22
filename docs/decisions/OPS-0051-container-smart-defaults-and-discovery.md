---
title: OPS-0051 Container-smart defaults and discovery lists
status: accepted
date: 2025-08-22
---

Context

Samples (e.g., S5.Recs) ship with docker-compose that defines well-known service names and mapped ports for Ollama, Weaviate, MongoDB, and Redis. Developers often run the API either on the host (mapping to host.docker.internal) or inside the compose network (using service names). Configuration friction comes from having to set endpoints even when the defaults are predictable and discoverable.

Decision

- Provide container-smart defaults across adapters/providers with a unified pattern:
  - Support a multi-endpoint environment variable list to preserve caller order and pick the first reachable candidate.
    - Ollama: SORA_AI_OLLAMA_URLS (existing)
    - Weaviate: SORA_DATA_WEAVIATE_URLS (new)
    - Mongo: SORA_DATA_MONGO_URLS (new)
    - Redis: SORA_DATA_REDIS_URLS (new)
  - Recognize a sentinel value "auto" for option fields to explicitly opt-in to discovery/default resolution.
  - Maintain a host-first default order that reflects dev compose reality:
    - Weaviate: host.docker.internal:8080 → localhost:8080 → weaviate:8080 → localhost:8085
    - Ollama: localhost:11434 → 127.0.0.1:11434 → host.docker.internal:11434 → ollama:11434 (unchanged)
    - MongoDB: mongodb://mongodb:27017 (in-container) or mongodb://localhost:27017 (host); env-list precedes
    - Redis: redis:6379 (in-container) or localhost:6379 (host); env-list precedes
  - Ollama discovery in non-dev is allowed by default (can be disabled via AiOptions.AllowDiscoveryInNonDev=false).
  - Weaviate adopts the discovered endpoint when Endpoint is unset, default, or set to "auto".

Consequences

- Fewer required appsettings for common dev/test setups. Sensible defaults help first-run success.
- Env lists allow flexible routing across multiple nodes while keeping deterministic selection.
- The "auto" sentinel allows explicit opt-in without hardcoding endpoints.

Implementation Notes

- Ollama: Changed default for AllowDiscoveryInNonDev to true unless explicitly set in AiOptions.
- Weaviate: Added SORA_DATA_WEAVIATE_URLS list, 'auto' recognition, and retained probing with readiness fallback.
- Mongo: Added SORA_DATA_MONGO_URLS list with quick ping, 'auto' recognition, and existing compose-aware defaults.
- Redis: Added SORA_DATA_REDIS_URLS list with quick connect ping, 'auto' recognition, and existing compose-aware defaults.

Testing

- Unit tests should cover precedence: explicit config > env single > env list > host-first defaults.
- Non-dev Ollama discovery can be toggled via AiOptions and verified by presence/absence of registered adapter.
