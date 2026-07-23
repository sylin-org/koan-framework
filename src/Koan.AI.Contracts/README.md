# Sylin.Koan.AI.Contracts

Inert vocabulary for building on Koan AI: provider adapters, capability identifiers, model-selection seams, routing
registries, immutable prompt values/options, and multimodal request/result shapes. Referencing this package alone starts no runtime,
selects no provider, and performs no network or model operation.

The generated [product surface](../../docs/reference/product-surface.md) owns support maturity. This
page owns the inert provider-author contract and its limits.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Contracts
```

Application developers normally reference `Sylin.Koan.AI`, which brings these types with the AI runtime. Reference
this package directly when a library or provider needs the public AI boundary without activating that runtime.

## Smallest meaningful use

A module can describe a model recommendation without depending on Koan's AI engine:

```csharp
using Koan.AI.Contracts;

public sealed class AcmeModelAdvisor : IAiModelAdvisor
{
    public string? GetRecommendedModel(string category) =>
        category == AiCapability.Chat ? "acme/chat-small" : null;
}
```

The functional module registers the implementation. When `Sylin.Koan.AI` is also active, its model-resolution
pipeline consumes the advisor; otherwise the contract remains dormant.

Provider authors implement the narrow interfaces under `Koan.AI.Contracts.Adapters` and declare supported operations
with `AiCapability` values. Custom capability strings remain valid so providers are not limited to the in-box catalog.

Libraries can also compose a prompt without activating AI or Data:

```csharp
using Koan.AI.Prompt;

Prompt prompt = Prompt.Create(p => p
    .Instruct("Classify {text}")
    .OutputAs<Classification>());
```

Persisted prompt catalogs are optional functionality in `Sylin.Koan.AI.Prompt`; they are not part of this inert
boundary.

## Guarantees and boundaries

- This package owns shapes and SPIs only. Provider discovery, adapter election, retries, health, and execution belong
  to `Sylin.Koan.AI` and the referenced provider packages.
- Prompt parsing, interpolation, and structured-output description are deterministic in-memory operations. Prompt
  storage, rollout assignment, authorization, and content policy are outside this package.
- An `AiCapability` value describes support; it does not guarantee that a model is installed, reachable, or healthy.
- Adapter implementations are expected to be concurrency-safe and to honor cancellation, but those guarantees belong
  to the implementation selected at runtime.
- Vendor option bags are intentionally provider-specific and currently use Newtonsoft JSON tokens on the legacy
  prompt surface. Do not treat them as portable across providers.
- `Sylin.Koan.AI.Contracts.Shared` is a separate, dependency-free lifecycle vocabulary. It is not required for the
  core inference adapter boundary.

See [TECHNICAL.md](./TECHNICAL.md) for namespace ownership and the runtime handoff.
