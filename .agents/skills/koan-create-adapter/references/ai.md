# AI-runtime-adapter playbook

Derived from the assessed exemplars **LMStudio** (`src/Connectors/AI/LMStudio/` — the OpenAI-style
wire, the closest template for llama.cpp/llama-server, vLLM, and friends) and **Ollama**
(`src/Connectors/AI/Ollama/` — the richer native surface), proven by **LlamaCpp**
(`src/Connectors/AI/LlamaCpp/`). General rules (package mechanics, truth gates, AOT, staging) live
in the data playbook; this file covers the AI seam only.

## The oracle question (ARCH-0127)

There is **no shared AI conformance kit** — and per ARCH-0127 a missing kit is a STOP for
kit-building, not for the adapter. You construct the strongest available behavioral proof from the
exemplar's test shape and *say which posture you proved under*:

1. **Real provider** (preferred): a container or local runtime serving a real model. Existing AI
   suites do NOT do this — `tests/Suites/AI/Unit/Koan.Tests.AI.Unit/Specs/Adapters/*.Spec.cs` run
   the adapter against an in-process `RecordingHandler : HttpMessageHandler` (a fake, which proves
   serialization and routing only).
2. **Wire-contract service** (ARCH-0120 §"wire-contract service"): when a real model is impractical
   (download-gated, huge, or license-blocked), host a deterministic HTTP server that speaks the
   provider's exact wire contract — a real socket server (Kestrel `WebApplication` in the test
   host, the pattern of `tests/Suites/Auth/*IntegrationTests` fixtures), NOT an injected
   `HttpMessageHandler`. This proves the full wire path: real HttpClient behavior, SSE streaming,
   status-code mapping, auth headers, JSON round-trips. Model-inference behavior (generation
   quality, tokenizer drift) is out of scope by nature and must be reported as such.

LlamaCpp shipped under posture 2: HuggingFace is download-gated in the proof environment (401), so
no GGUF could be fetched for a real `llama-server`. The fixture is a deterministic Kestrel server
speaking llama-server's OpenAI-compatible contract; the report and TECHNICAL say so.

## The seam

- Core contracts (`Koan.AI.Contracts`): `IChatAdapter` (`Chat`, `Stream`, `CanServe`),
  `IEmbedAdapter` (`Embed`), optional `IAiModelManager` (pull/remove/list), `IAiSourceInspector`
  (`InspectAsync` for endpoint candidates), and the readiness family `IAdapterReadiness`,
  `IAdapterReadinessConfiguration`, `IAsyncAdapterInitializer` with `ReadinessStateManager` +
  `AdapterNotReadyException`. Capabilities are strings from `AiCapability` — claim only what you
  prove: Chat, Embed, Streaming, ModelList, ServeGGUF are the local-runtime set; decline Vision,
  Tools, Pull, ModelRemove unless the provider truly serves them.
- Wire models: requests/responses are your own Newtonsoft DTOs (`NullValueHandling.Ignore`);
  **Newtonsoft is the canonical serializer** on this seam (no System.Text.Json).
- `AiChatResponse` must carry `AdapterId` (routing/explanation read it); `AiChatChunk` carries
  `DeltaText` + `AdapterId`; `AiEmbeddingsResponse` normalizes to `Vectors` + `Model` +
  `Dimension = first vector length`.

## Registration (copy LMStudio's shape)

- `KoanModule` implementing `IContributeTo<AiProviderContributionTarget>`: registers
  `AddKoanOptions<TOptions>(Section)`, the discovery adapter, and the adapter singleton (an
  `HttpClient` with the configured request timeout + logger + readiness defaults + options), then
  `Contribute` adds `<AdapterContributor>(Constants.Adapter.Type)`.
- `AdapterContributor : IAiProviderActivator` resolves placement in strict order: existing source
  in `IAiSourceRegistry` → explicit `Koan:Ai:<Provider>:Endpoints` → `ConnectionStrings:<Provider>`
  (reject configuring BOTH with `InvalidOperationException` — the "configured twice" rule) →
  auto-discovery through `IServiceDiscoveryCoordinator`, gated by `KoanEnv.Gate.Allows(new
  KoanMagic(...))` (production refuses silently-adopted local runtimes unless
  `AllowDiscoveryInNonDev`). Inactive discovery returns `new AiProviderActivation { Adapter }` (the
  adapter exists, no source — it must not answer). Ready discovery returns
  `AiProviderActivation { Adapter, Sources = [AiProviderSources.Create(type, endpoints,
  capabilityConfig, origin, autoDiscovered)] }` with the `Chat`/`Embedding` capability→model map.
- `ServiceDescriptor`: an `internal sealed class` carrying `[KoanService(ServiceKind.Ai,
  shortCode: "<type>", ...)]` with `DeploymentKind.External`, default port, `HealthEndpoint`, and
  URI patterns — this is what the discovery adapter's `GetFactoryType()` returns and what
  orchestrators explain.
- `ServiceDiscoveryAdapterBase` subclass: health-validate a candidate endpoint (GET the models or
  health path, honor an `apiKey` parameter, optionally require the configured model to be listed).
- `InternalsVisibleTo` the unit-test assembly if exemplar-parity unit specs are wanted.

## Readiness is a first-class claim

The adapter owns its boot state: probe the provider's health endpoint (llama-server `GET /health`
answers 503 while the model loads — better than probing `/v1/models`), then verify the configured
`DefaultModel` is actually listed (`Ready` vs `Degraded`). `Chat`/`Stream`/`Embed` wait through
`WaitForReadiness` and throw `AdapterNotReadyException` on timeout/failure — a business operation
is never a missing-shape probe. Report readiness transitions via `ReadinessStateChanged`.

## Wire-contract notes for OpenAI-style runtimes

- Endpoints normalize by stripping a trailing `/v1` (callers may configure either root or root+v1);
  paths are constants (`/v1/chat/completions`, `/v1/embeddings`, `/v1/models`, `/health`).
- Chat payload: `model`, `stream:false`, `messages[{role,content}]` (multi-part content as
  `[{type,text}]`), then optional `temperature`/`top_p`/`max_tokens`/`stop`/`seed` and vendor
  pass-through from `Options.VendorOptions`. Auth is `Authorization: Bearer` only when configured.
- Streaming: accept `text/event-stream`, parse `data:` lines, `[DONE]` terminates, `choices[0].
  delta.content` yields `AiChatChunk`s; skip empty/undecodable chunks (log-debug), never throw for
  a malformed single event.
- Embeddings: `{model, input:[...]}` → `data[i].embedding` float arrays; dimension from the first
  vector. llama-server serves embeddings only when started with `--embedding` — an adapter cannot
  detect the flag; readiness probes `/health` (which stays green) so the embed path must map the
  provider's error loudly (log + `EnsureSuccessStatusCode`, exemplar parity).
- Provider errors surface with their status codes after a warn-log; a 200 with an unusable body is
  an `InvalidOperationException("... returned an empty response.")`, never a silent empty answer.

## LlamaCpp specifics (llama-server wire, probed against its documented contract 2026-08-29)

- `GET /health` → `{"status":"ok"}` (503 while loading); `GET /v1/models` → OpenAI list of the
  loaded model(s); chat/embeddings as above; `POST /v1/completions` and native `/completion` exist
  but are NOT claimed — the portable claim is the OpenAI-compatible surface.
- Single loaded model: a request naming another model is refused by the server; the adapter passes
  the caller's model through and lets the provider refuse (no silent model substitution).
- Capabilities claimed: Chat, Embed, Streaming, ModelList, ServeGGUF. Declined: Vision, Tools
  (`--jinja` template-dependent), Pull/ModelRemove (no model manager on llama-server).
- Test fixture: deterministic Kestrel wire-contract service (see the oracle question above); the
  fixture asserts against RECORDED requests (auth header, payload shape, stream flag) and returns
  canned OpenAI-shaped responses/SSE streams, plus protocol-failure cases (503 loading,
  unknown-model error, malformed SSE line, mid-stream cancellation).
