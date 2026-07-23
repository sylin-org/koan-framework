# Sylin.Koan.AI.Connector.Ollama — technical contract

## Responsibility

This package owns Ollama protocol translation and its thin activation description. `Sylin.Koan.Core` owns endpoint
discovery order and health qualification; `Sylin.Koan.AI` owns provider-plan compilation, source construction,
routing, and registry publication.

Referencing the package contributes one provider id, `ollama`, to the host's immutable AI provider plan. The adapter
is a DI-owned singleton and is disposed with the host. Per-endpoint HTTP clients are memoized and disposed by that
singleton.

## Configuration and election

- Options bind only from `Koan:Ai:Ollama`.
- `Endpoints` declares an ordered mesh; `ConnectionStrings:Ollama` declares one endpoint.
- Declaring both is invalid.
- Explicit endpoints win over automatic discovery and do not require discovery to be enabled.
- With no explicit placement, Core discovery evaluates the composed host plan, conventional container topology,
  Docker host gateway, local loopback, and Aspire bindings through one shared election pipeline.
- Discovery health checks `GET /api/tags`.

The activator publishes one source named `ollama`. Its members are named `ollama::member-N`, use the shared
`Fallback` policy, and advertise Chat and Embedding with `DefaultModel`. Source routing—not adapter registration—owns
endpoint election.

## Protocol behavior

- Chat and streaming use Ollama's generate API and preserve cancellation.
- Embeddings use the Ollama embedding API.
- `AiPromptOptions.Think` is sent as Ollama's top-level `think` value.
- Standard prompt controls map into Ollama's options object; `VendorOptions` are provider-specific passthrough values.
- `MaxConcurrentRequests` bounds concurrent calls for this adapter when greater than zero.
- Endpoint inspection independently calls `/api/version`, `/api/tags`, and `/api/ps`. The adapter maps those
  responses to provider-neutral version, installed-model, and resident-model facets. Overall reachability succeeds
  when any facet answers; per-facet availability and detail preserve partial failures.

## Boundaries

Koan does not promise delivery through a missing runtime, model availability, model compatibility with every declared
operation, TLS termination, authentication, retries, or automatic model installation. Those concerns must be
provided by the deployment or invoked explicitly through supported model-management operations.
