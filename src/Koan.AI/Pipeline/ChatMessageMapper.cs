using System.Collections.Generic;
using System.Linq;
using Koan.AI.Contracts.Models;
using Microsoft.Extensions.AI;

namespace Koan.AI.Pipeline;

internal static class ChatMessageMapper
{
    public static List<ChatMessage> ToChatMessages(IEnumerable<AiMessage> source)
    {
        List<ChatMessage> result = new();
        foreach (var message in source)
        {
            var role = string.IsNullOrWhiteSpace(message.Role) ? ChatRole.User : new ChatRole(message.Role!);
            var chatMessage = new ChatMessage(role, message.Content ?? "")
            {
                AuthorName = message.Name,
                MessageId = message.ToolCallId,
            };

            if (message.Metadata is { Count: > 0 })
            {
                chatMessage.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                foreach (var pair in message.Metadata)
                {
                    chatMessage.AdditionalProperties[pair.Key] = pair.Value;
                }
            }

            if (message.Parts is { Count: > 0 })
            {
                chatMessage.Contents.Clear();
                foreach (var part in message.Parts)
                {
                    if (part.Type.Equals("text", System.StringComparison.OrdinalIgnoreCase) && part.Text is not null)
                    {
                        chatMessage.Contents.Add(new TextContent(part.Text));
                    }
                }

                if (chatMessage.Contents.Count == 0)
                {
                    chatMessage.Contents.Add(new TextContent(message.Content ?? ""));
                }
            }

            result.Add(chatMessage);
        }

        return result;
    }

    public static List<AiMessage> ToAiMessages(IEnumerable<ChatMessage> source)
    {
        List<AiMessage> result = new();
        foreach (var message in source)
        {
            var text = message.Text ?? "";
            var aiMessage = new AiMessage(message.Role.Value, text)
            {
                Name = message.AuthorName,
                ToolCallId = message.MessageId,
            };

            if (message.AdditionalProperties is { Count: > 0 })
            {
                var metadata = message.AdditionalProperties
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value?.ToString() ?? "");
                aiMessage = aiMessage with { Metadata = metadata };
            }

            if (message.Contents is { Count: > 0 })
            {
                var parts = new List<AiMessagePart>();
                foreach (var content in message.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        parts.Add(new AiMessagePart
                        {
                            Type = "text",
                            Text = textContent.Text,
                        });
                    }
                }

                if (parts.Count > 0)
                {
                    aiMessage = aiMessage with { Parts = parts };
                }
            }

            result.Add(aiMessage);
        }

        return result;
    }
}
