# Sylin.Koan.AI.Connector.LMStudio — technical contract

## Responsibility

This package owns LM Studio's OpenAI-compatible wire protocol and provider-specific health validation.
`Sylin.Koan.Core` owns discovery order; `Sylin.Koan.AI` compiles the provider topology and owns source routing.

The provider contributes id `lmstudio`. Its adapter is a DI-owned singleton, and its memoized per-endpoint HTTP
clients are disposed with the host. LM Studio is declared as an external service, so orchestration and inspection do
not promise a container Koan cannot responsibly provision.

## Configuration and election

- Options bind only from `Koan:Ai:LMStudio`.
- `Endpoints` declares an ordered mesh; `ConnectionStrings:LMStudio` declares one endpoint.
- Declaring both is a startup error.
- Explicit placement is authoritative even when automatic discovery is disabled.
- With no explicit placement, the shared Core discovery pipeline evaluates composed candidates, conventional
  container topology, Docker host gateway, local loopback, and Aspire binding.
- Health validation calls `GET /v1/models`, attaches `ApiKey` as a Bearer token, and can require `DefaultModel`.

The activator publishes one `lmstudio` source with deterministic `lmstudio::member-N` members. Chat and Embedding
capabilities remain routable even when no default model is configured; the request must then name its model.

## Protocol and readiness

- Chat posts `/v1/chat/completions`; streaming parses SSE frames through `[DONE]`.
- Embeddings post `/v1/embeddings`; model listing and readiness use `/v1/models`.
- Trailing `/v1` in an endpoint is normalized before relative protocol calls.
- Request timeout comes from `RequestTimeoutSeconds`.
- Readiness verifies endpoint reachability and the configured default model. Missing default model availability is
  degraded rather than falsely reported ready.

## Boundaries

The package does not start LM Studio, load a model, manufacture an API key, guarantee OpenAI compatibility beyond
the operations implemented here, retry failed inference, or make an unavailable automatic candidate fatal.
