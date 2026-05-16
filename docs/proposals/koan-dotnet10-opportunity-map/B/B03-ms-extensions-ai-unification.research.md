# B03 Research Log — Microsoft.Extensions.AI Unification

**Last updated:** 2025-11-12

## Discovery Notes

- **`IChatClient` contract expectations** — The interface is thread-safe, streams responses via `GetStreamingResponseAsync`, and may mutate supplied `ChatOptions`; consumers should avoid sharing mutable option instances across calls. Source: [learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient).
- **Streaming payload shape** — Streaming updates arrive as `ChatResponseUpdate` instances that carry partial content, metadata (role, message/model ids), optional continuation tokens, and can be lossily converted back into `ChatResponse`. Source: [raw.githubusercontent.com/dotnet/extensions/.../ChatResponseUpdate.cs](https://raw.githubusercontent.com/dotnet/extensions/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatResponseUpdate.cs).
- **Response aggregation semantics** — `ChatResponse` represents final responses (often multi-message), exposes concatenated `Text`, usage stats, conversation ids, and continuation tokens for background responses. Source: [raw.githubusercontent.com/dotnet/extensions/.../ChatResponse.cs](https://raw.githubusercontent.com/dotnet/extensions/main/src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/ChatResponse.cs).
- **Chat payload modeling** — `ChatMessage` holds role, author name, structured `AIContent` parts, and optional raw representation, enabling multimodal payloads beyond plain text. Source: [learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatmessage](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatmessage).
- **Role vocabulary** — `ChatRole` wraps role strings (`System`, `User`, `Assistant`, `Tool`) with case-insensitive equality, aligning Koan message roles to ME.AI values. Source: [learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatrole](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatrole).
- **Options surface** — `ChatOptions` defines request-level dials (model id, temperature, penalties, tool set, conversation id). Implementations may clone or mutate instances, so Koan will issue per-call copies. Source: [learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatoptions](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatoptions).
- **Embeddings pipeline contract** — `IEmbeddingGenerator<TInput,TEmbedding>` produces `GeneratedEmbeddings<TEmbedding>` with usage data, and may mutate `EmbeddingGenerationOptions`; the primary `Embedding` type carries model id, dimensions, and additional properties. Sources: [learn.microsoft.com/dotnet/api/microsoft.extensions.ai.iembeddinggenerator-2](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.iembeddinggenerator-2), [learn.microsoft.com/dotnet/api/microsoft.extensions.ai.embeddinggenerationoptions](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.embeddinggenerationoptions), [learn.microsoft.com/dotnet/api/microsoft.extensions.ai.embedding](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.embedding).
- **Package composition** — `Microsoft.Extensions.AI` pulls in abstractions plus middleware, telemetry hooks, and builder extensions; the NuGet feed documents the recommended package reference for consumers. Source: [nuget.org/packages/Microsoft.Extensions.AI](https://www.nuget.org/packages/Microsoft.Extensions.AI/).

## Follow-ups

- Map Koan `AiMessage`/`AiChatChunk` fields to `ChatMessage`/`ChatResponseUpdate` invariants.
- Decide on cloning strategy for `ChatOptions` and `EmbeddingGenerationOptions` to avoid concurrent mutation.
- Evaluate handling for `ContinuationToken` to support deferred responses in Koan pipelines.
