using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.AI;

namespace Koan.AI.Pipeline;

internal static class ChatResponseMapper
{
    public static AiChatResponse ToAiChatResponse(ChatResponse response)
    {
        var toolCalls = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(c => new AiToolCall
            {
                Name = c.Name,
                ArgumentsJson = JsonSerializer.Serialize(c.Arguments ?? EmptyArguments),
                Id = c.CallId
            })
            .ToList();

        return new AiChatResponse
        {
            Text = response.Text,
            FinishReason = response.FinishReason?.ToString(),
            TokensIn = (int?)(response.Usage?.InputTokenCount),
            TokensOut = (int?)(response.Usage?.OutputTokenCount),
            Model = response.ModelId,
            AdapterId = response.ResponseId,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
        };
    }

    public static ChatResponse FromAiChatResponse(AiChatResponse response)
    {
        var message = new ChatMessage(ChatRole.Assistant, response.Text ?? "");
        if (response.ToolCalls is { Count: > 0 })
        {
            foreach (var call in response.ToolCalls)
            {
                message.Contents.Add(new FunctionCallContent(
                    call.Name,
                    call.Id ?? call.Name,
                    ParseArguments(call.ArgumentsJson)));
            }
        }

        return new ChatResponse(message)
        {
            FinishReason = MapFinishReason(response.FinishReason),
            ModelId = response.Model,
            Usage = CreateUsage(response.TokensIn, response.TokensOut),
        };
    }

    private static readonly IDictionary<string, object?> EmptyArguments =
        new Dictionary<string, object?>();

    private static IDictionary<string, object?> ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return EmptyArguments;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson) ?? EmptyArguments;
        }
        catch (JsonException)
        {
            return EmptyArguments;
        }
    }

    /// <summary>
    /// Maps a string finish reason to a ChatFinishReason struct instance.
    /// ChatFinishReason is a struct with static properties, not an enum.
    /// </summary>
    private static ChatFinishReason? MapFinishReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        // Map common finish reason values to ChatFinishReason static properties
        // ChatFinishReason is a struct, not an enum, so we use the static properties
        return reason.ToLowerInvariant() switch
        {
            "stop" => ChatFinishReason.Stop,
            "length" => ChatFinishReason.Length,
            "toolcalls" or "tool_calls" => ChatFinishReason.ToolCalls,
            "contentfilter" or "content_filter" => ChatFinishReason.ContentFilter,
            _ => new ChatFinishReason(reason) // Use constructor for unknown values
        };
    }

    public static AiChatChunk ToAiChatChunk(ChatResponseUpdate update, int index)
    {
        return new AiChatChunk
        {
            DeltaText = update.Text,
            Index = index,
            Model = update.ModelId,
            TokensOutInc = null,
            AdapterId = update.ResponseId,
        };
    }

    public static ChatResponseUpdate FromAiChatChunk(AiChatChunk chunk)
    {
        return new ChatResponseUpdate(ChatRole.Assistant, chunk.DeltaText ?? "")
        {
            ModelId = chunk.Model,
            ResponseId = chunk.AdapterId,
        };
    }

    public static AiEmbeddingsResponse ToAiEmbeddingsResponse(GeneratedEmbeddings<Embedding<float>> embeddings)
    {
        var vectors = embeddings.Select(e => e.Vector.ToArray()).ToList();
        var first = embeddings.FirstOrDefault();

        return new AiEmbeddingsResponse
        {
            Vectors = vectors,
            Model = first?.ModelId,
            Dimension = first?.Dimensions,
        };
    }

    public static GeneratedEmbeddings<Embedding<float>> FromAiEmbeddingsResponse(AiEmbeddingsResponse response)
    {
        var generated = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var vector in response.Vectors)
        {
            generated.Add(new Embedding<float>(vector.AsMemory())
            {
                ModelId = response.Model,
            });
        }

        return generated;
    }

    private static UsageDetails? CreateUsage(int? input, int? output)
    {
        if (input is null && output is null)
        {
            return null;
        }

        return new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = output,
        };
    }
}
