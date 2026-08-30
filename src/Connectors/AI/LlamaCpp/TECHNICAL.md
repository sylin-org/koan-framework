# Sylin.Koan.AI.Connector.LlamaCpp — technical contract

## Responsibility

This package owns llama-server's OpenAI-compatible wire protocol and provider-specific health validation.
`Sylin.Koan.Core` owns discovery order; `Sylin.Koan.AI` compiles the provider topology and owns source routing.

The provider contributes id `llamacpp`. Its adapter is a DI-owned singleton, and its memoized per-endpoint HTTP
clients are disposed with the host. llama.cpp is declared as an external service, so orchestration and inspection do
not promise a container Koan cannot responsibly provision (weights are user-supplied).

## Configuration and election

- Options bind only from `Koan:Ai:LlamaCpp`.
- `Endpoints` declares an ordered mesh; `ConnectionStrings:LlamaCpp` declares one endpoint.
- Declaring both is a startup error.
- Explicit placement is authoritative even when automatic discovery is disabled.
- With no explicit placement, the shared Core discovery pipeline evaluates composed candidates, conventional
  container topology (`ghcr.io/ggml-org/llama.cpp`, port 8080), Docker host gateway, local loopback, and Aspire binding.
- Health validation calls `GET /v1/models`, attaches `ApiKey` as a Bearer token, and can require `DefaultModel`.

The activator publishes one `llamacpp` source with deterministic `llamacpp::member-N` members. Chat and Embedding
capabilities remain routable even when no default model is configured; the request must then name its model.

## Protocol and readiness

- Chat posts `/v1/chat/completions`; streaming parses SSE `data:` frames through `[DONE]`, skipping a malformed
  single event (debug-logged) rather than failing the stream.
- Embeddings post `/v1/embeddings`. llama-server only serves embeddings when started with `--embedding`; `/health`
  stays green without the flag, so an embed failure surfaces loudly with the provider's status and body.
- Readiness probes `GET /health` — llama-server answers 503 while the model loads, which correctly reads as
  not-ready — and then decides Ready (default model listed in `/v1/models`) vs Degraded (health up, model absent).
  Missing default model availability is degraded rather than falsely reported ready.
- Trailing `/v1` in an endpoint is normalized before relative protocol calls.
- Request timeout comes from `RequestTimeoutSeconds`.

## Boundaries

The package does not build llama.cpp, start llama-server, download or select GGUF weights, manufacture an API key,
guarantee OpenAI compatibility beyond the operations implemented here, retry failed inference, or make an unavailable
automatic candidate fatal. Tool calling stays undeclared: llama-server's support is chat-template dependent
(`--jinja`) and cannot be claimed unconditionally. Vision, Pull, and ModelRemove are equally undeclared — there is
no model manager on llama-server.

## Proof posture

The behavioral suite (`tests/Suites/AI/Connector.LlamaCpp/`) proves the wire contract against a deterministic
Kestrel-hosted service speaking llama-server's REST contract (ARCH-0120 wire-contract-service posture): real
sockets, real SSE bytes, real status codes, recorded requests (auth header, payload shape, stream flag), and
protocol-failure cases (503 loading, unknown-model refusal, malformed SSE line, mid-stream cancellation). HuggingFace
is download-gated in the proof environment, so a real-weights run was not possible; model-inference behavior
(generation quality, tokenizer drift) is out of scope by nature and remains unproven here.
