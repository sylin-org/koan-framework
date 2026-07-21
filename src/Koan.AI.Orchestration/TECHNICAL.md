# Sylin.Koan.AI.Orchestration technical contract

## Activation and ownership

Generated module activation registers one singleton `IChainExecutor`. `Chain.Create()` produces immutable builders;
`Run`/`Stream` resolve the executor from active `AppHost`. AI owns provider routing, Prompt owns stored prompts, Data
Vector owns retrieval, and applications own tool implementations and authorization.

## Execution

A `ChainDefinition` is the ordered step list plus system message, provider scopes, and memory choice. The executor
maintains one in-process context, routes chat/embed operations, invokes retrieval and tools, applies parse/classify/
branch/parallel transformations, accumulates citations/metrics, and streams chunks where supported.

## Failure and limits

Missing host/executor/provider is corrective. Filter lowering, structured parsing, moderation, tool, provider, and
cancellation failures are not hidden. Parallel steps do not create a transaction. Memory does not imply durable state.
Use Jobs for durable orchestration and application policy for authorization, retries, compensation, and cost controls.
