# Sylin.Koan.AI.Contracts — technical contract

## Responsibility

This assembly is the inert compile-time boundary between the Koan AI runtime, optional providers, projections, and
libraries. It owns no `KoanModule`, service registration, hosted service, client, transport, or provider election.
Its only external dependency is Newtonsoft.Json for the existing `AiPromptOptions` extension-data contract.

## Contract groups

- `Koan.AI.Contracts`: `IAiPipeline`, `AiCapability`, `IAiRecipeProvider`, and `IAiModelAdvisor`.
- `.Adapters`: the base adapter identity, operation-specific adapter interfaces, model management, contributor SPI,
  and adapter election metadata.
- `.Routing` and `.Sources`: registry, source/member, health-state, and capability-configuration vocabulary.
- `.Models`: prompt, conversation, request/response, streaming, provenance, route-hint, and result shapes.
- `.Options` and `.Categories`: typed operation and category configuration.

The AI-specific capability catalog and model-selection SPIs live here rather than in `Sylin.Koan.Core`; applications
that do not reference AI therefore do not carry AI vocabulary in their mandatory framework substrate.

## Runtime handoff

Functional provider modules register `IAiAdapterContributor` implementations. `Sylin.Koan.AI` owns when those
contributors execute, compiles the adapter/source registries, and consumes optional `IAiRecipeProvider` and
`IAiModelAdvisor` implementations. A module may reference and register one of these contracts without activating AI;
the behavior appears only when the AI runtime resolves it.

`IAiAdapter.Capabilities` is string-based by design. `AiCapability` supplies interoperable in-box identifiers while
allowing providers to declare additional capabilities without changing this package.

## Failure and compatibility boundaries

- Contracts do not convert provider failures into a universal error taxonomy; functional layers own corrective
  exceptions and observable health.
- Request and option shapes do not imply universal provider support. A provider must reject or explicitly ignore
  unsupported options according to its documented contract.
- Streaming implementations must propagate cancellation and may produce partial output before a provider failure.
- Prompts, media, tool arguments, and model output can contain sensitive data; contracts provide no automatic
  redaction or policy enforcement.
- `AiPromptOptions.VendorOptions` exposes Newtonsoft `JToken` values. New operation-specific options use ordinary
  object dictionaries; consumers must not assume the two bags are interchangeable.
