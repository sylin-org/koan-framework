using Microsoft.Extensions.AI;

namespace Koan.AI.Pipeline;

internal static class PipelineMetadata
{
    public const string OriginalChatRequestKey = "koan:ai:original-chat-request";
    public const string OriginalEmbeddingRequestKey = "koan:ai:original-embedding-request";

    public static void AttachOriginalRequest(ChatOptions options, Contracts.Models.AiChatRequest request)
    {
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[OriginalChatRequestKey] = request;
    }

    public static void AttachOriginalRequest(EmbeddingGenerationOptions options, Contracts.Models.AiEmbeddingsRequest request)
    {
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[OriginalEmbeddingRequestKey] = request;
    }

    public static Contracts.Models.AiChatRequest? TryGetOriginalChatRequest(ChatOptions? options)
    {
        return options?.AdditionalProperties != null &&
               options.AdditionalProperties.TryGetValue(OriginalChatRequestKey, out var value)
            ? value as Contracts.Models.AiChatRequest
            : null;
    }

    public static Contracts.Models.AiEmbeddingsRequest? TryGetOriginalEmbeddingRequest(EmbeddingGenerationOptions? options)
    {
        return options?.AdditionalProperties != null &&
               options.AdditionalProperties.TryGetValue(OriginalEmbeddingRequestKey, out var value)
            ? value as Contracts.Models.AiEmbeddingsRequest
            : null;
    }
}
