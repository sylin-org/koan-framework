using System.Collections.Generic;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.AI;

namespace Koan.AI.Pipeline;

internal static class ChatOptionsMapper
{
    private const string ProfileKey = "koan:ai:profile";
    private const string ThinkKey = "koan:ai:think";
    private const string RouteAdapterKey = "koan:ai:route:adapter";
    private const string RoutePolicyKey = "koan:ai:route:policy";
    private const string RouteStickyKey = "koan:ai:route:sticky";
    private const string ContextTagsKey = "koan:ai:context:tags";
    private const string ContextGroundingKey = "koan:ai:context:grounding";

    public static ChatOptions CreateChatOptions(AiChatRequest request)
    {
        var options = new ChatOptions
        {
            ModelId = request.Model,
        };

        ApplyPromptOptions(options, request.Options);
        ApplyRouteHints(options, request.Route);
        ApplyContext(options, request.Context);

        PipelineMetadata.AttachOriginalRequest(options, request);
        return options;
    }

    public static EmbeddingGenerationOptions CreateEmbeddingOptions(AiEmbeddingsRequest request)
    {
        var options = new EmbeddingGenerationOptions
        {
            ModelId = request.Model,
        };

        PipelineMetadata.AttachOriginalRequest(options, request);
        return options;
    }

    public static AiChatRequest CreateChatRequest(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        if (PipelineMetadata.TryGetOriginalChatRequest(options) is { } original)
        {
            return original;
        }

        var mapped = new AiChatRequest
        {
            Messages = ChatMessageMapper.ToAiMessages(messages),
            Model = options?.ModelId,
            Options = ExtractPromptOptions(options),
            Route = ExtractRouteHints(options),
            Context = ExtractContext(options),
        };

        return mapped;
    }

    public static AiEmbeddingsRequest CreateEmbeddingRequest(IEnumerable<string> inputs, EmbeddingGenerationOptions? options)
    {
        if (PipelineMetadata.TryGetOriginalEmbeddingRequest(options) is { } original)
        {
            return original;
        }

        return new AiEmbeddingsRequest
        {
            Input = new List<string>(inputs),
            Model = options?.ModelId,
        };
    }

    private static void ApplyPromptOptions(ChatOptions options, AiPromptOptions? prompt)
    {
        if (prompt is null)
        {
            return;
        }

        if (prompt.Temperature is { } temperature)
        {
            options.Temperature = (float)temperature;
        }

        if (prompt.MaxOutputTokens is { } maxTokens)
        {
            options.MaxOutputTokens = maxTokens;
        }

        if (prompt.TopP is { } topP)
        {
            options.TopP = (float)topP;
        }

        if (prompt.Stop is { Length: > 0 })
        {
            options.StopSequences = new List<string>(prompt.Stop);
        }

        if (prompt.Seed is { } seed)
        {
            options.Seed = seed;
        }

        if (!string.IsNullOrWhiteSpace(prompt.ResponseFormat))
        {
            options.ResponseFormat = prompt.ResponseFormat.Equals("json", System.StringComparison.OrdinalIgnoreCase)
                ? ChatResponseFormat.Json
                : ChatResponseFormat.Text;
        }

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();

        if (!string.IsNullOrWhiteSpace(prompt.Profile))
        {
            options.AdditionalProperties[ProfileKey] = prompt.Profile;
        }

        if (prompt.Think is { } think)
        {
            options.AdditionalProperties[ThinkKey] = think;
        }

        if (prompt.VendorOptions is { Count: > 0 })
        {
            foreach (var pair in prompt.VendorOptions)
            {
                options.AdditionalProperties[pair.Key] = pair.Value;
            }
        }
    }

    private static void ApplyRouteHints(ChatOptions options, AiRouteHints? hints)
    {
        if (hints is null)
        {
            return;
        }

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();

        if (!string.IsNullOrWhiteSpace(hints.AdapterId))
        {
            options.AdditionalProperties[RouteAdapterKey] = hints.AdapterId;
        }

        if (!string.IsNullOrWhiteSpace(hints.Policy))
        {
            options.AdditionalProperties[RoutePolicyKey] = hints.Policy;
        }

        if (!string.IsNullOrWhiteSpace(hints.StickyKey))
        {
            options.AdditionalProperties[RouteStickyKey] = hints.StickyKey;
        }
    }

    private static void ApplyContext(ChatOptions options, AiConversationContext? context)
    {
        if (context is null)
        {
            return;
        }

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();

        if (!string.IsNullOrWhiteSpace(context.Profile))
        {
            options.AdditionalProperties[ProfileKey] = context.Profile;
        }

        if (context.Tags is { Count: > 0 })
        {
            options.AdditionalProperties[ContextTagsKey] = context.Tags;
        }

        if (context.GroundingReferences is { Count: > 0 })
        {
            options.AdditionalProperties[ContextGroundingKey] = context.GroundingReferences;
        }
    }

    private static AiPromptOptions? ExtractPromptOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        var profile = TryGetAdditional<string>(options, ProfileKey);
        var think = TryGetAdditional<bool?>(options, ThinkKey);

        IDictionary<string, Newtonsoft.Json.Linq.JToken>? vendor = null;
        if (options.AdditionalProperties is { Count: > 0 })
        {
            vendor = new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
            foreach (var pair in options.AdditionalProperties)
            {
                if (pair.Key is ProfileKey or ThinkKey or RouteAdapterKey or RoutePolicyKey or RouteStickyKey or ContextTagsKey or ContextGroundingKey)
                {
                    continue;
                }

                if (pair.Value is not null)
                {
                    vendor[pair.Key] = Newtonsoft.Json.Linq.JToken.FromObject(pair.Value);
                }
            }

            if (vendor.Count == 0)
            {
                vendor = null;
            }
        }

        return new AiPromptOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxOutputTokens,
            TopP = options.TopP,
            Stop = options.StopSequences?.Count > 0 ? options.StopSequences.ToArray() : null,
            Seed = options.Seed is long l ? (int?)l : null,
            ResponseFormat = options.ResponseFormat switch
            {
                ChatResponseFormatJson => "json",
                _ => null,
            },
            Profile = profile,
            Think = think,
            VendorOptions = vendor,
        };
    }

    private static AiRouteHints? ExtractRouteHints(ChatOptions? options)
    {
        if (options?.AdditionalProperties is not { Count: > 0 })
        {
            return null;
        }

        return new AiRouteHints
        {
            AdapterId = TryGetAdditional<string>(options, RouteAdapterKey),
            Policy = TryGetAdditional<string>(options, RoutePolicyKey),
            StickyKey = TryGetAdditional<string>(options, RouteStickyKey),
        };
    }

    private static AiConversationContext? ExtractContext(ChatOptions? options)
    {
        if (options?.AdditionalProperties is not { Count: > 0 })
        {
            return null;
        }

        return new AiConversationContext
        {
            Profile = TryGetAdditional<string>(options, ProfileKey),
            Tags = TryGetAdditional<IDictionary<string, string>>(options, ContextTagsKey),
            GroundingReferences = TryGetAdditional<IList<string>>(options, ContextGroundingKey),
        };
    }

    private static T? TryGetAdditional<T>(ChatOptions options, string key)
    {
        if (options.AdditionalProperties is null)
        {
            return default;
        }

        return options.AdditionalProperties.TryGetValue(key, out var value) && value is T typed
            ? typed
            : default;
    }
}
