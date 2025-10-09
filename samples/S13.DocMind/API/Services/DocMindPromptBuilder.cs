
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using S13.DocMind.Contracts;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocMindPromptBuilder
{
    TemplateGenerationPromptEnvelope BuildTemplateGenerationPrompt(TemplateGenerationRequest request);

    TemplateGenerationParseResult ParseTemplateGenerationResponse(string? response, TemplateGenerationRequest request);

    ManualPromptEnvelope BuildManualSessionPrompt(
        ManualAnalysisSession session,
        SemanticTypeProfile? profile,
        IReadOnlyList<ManualDocumentSnapshot> snapshots,
        DocMindOptions options);

    DelimitedContentResult ExtractDelimitedContent(string? text, string startDelimiter, string endDelimiter);
}

public sealed class DocMindPromptBuilder : IDocMindPromptBuilder
{
    public const string DocumentTypeJsonStart = "---DOCUMENT_TYPE_JSON_START---";
    public const string DocumentTypeJsonEnd = "---DOCUMENT_TYPE_JSON_END---";
    public const string FilledDocumentTypeStart = "---FILLED_DOCUMENT_TYPE_START---";
    public const string FilledDocumentTypeEnd = "---FILLED_DOCUMENT_TYPE_END---";
    public const string ContextUnderstandingStart = "---CONTEXT_UNDERSTANDING_START---";
    public const string ContextUnderstandingEnd = "---CONTEXT_UNDERSTANDING_END---";

    public TemplateGenerationPromptEnvelope BuildTemplateGenerationPrompt(TemplateGenerationRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var systemPrompt = string.Join(Environment.NewLine, new[]
        {
            "You are a strict API that outputs ONLY well-formed JSON for a new document type configuration.",
            "Return output wrapped EXACTLY between the delimiters on their own lines:",
            DocumentTypeJsonStart,
            "...JSON OBJECT...",
            DocumentTypeJsonEnd,
            "Rules:",
            "1. Output nothing before or after the delimiters.",
            "2. No markdown fences, no comments.",
            "3. Values MUST be concise; escape inner quotes.",
            "4. Code: 2-8 uppercase letters/numbers, no spaces (derive from name).",
            "5. Tags: 1-6 short kebab-case strings (a-z, numbers, hyphen).",
            "6. Template: markdown containing placeholders like {{FIELD_NAME}} (uppercase snake case).",
            "7. Always include all fields even if user prompt omits them (use placeholder text).",
            "8. Never hallucinate proprietary info; keep generic if uncertain.",
            "9. Avoid backticks anywhere.",
            "10. Prefer minimal essential sections in Template."
        });
        var user = new StringBuilder();
        user.AppendLine("SYSTEM");
        user.AppendLine("Design a structured extraction template using the provided description and sample text.");
        user.AppendLine("Follow the delimiter contract and return JSON only.");
        user.AppendLine();

        user.AppendLine("META");
        user.AppendLine($"name: {request.Name}");
        user.AppendLine($"delimiters: {DocumentTypeJsonStart},{DocumentTypeJsonEnd}");
        user.AppendLine($"sample_text: {!string.IsNullOrWhiteSpace(request.SampleText)}");
        if (request.Metadata is { Count: > 0 })
        {
            foreach (var (key, value) in request.Metadata)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                user.AppendLine($"meta.{key.Trim()}: {value}");
            }
        }
        user.AppendLine();

        user.AppendLine("CONTEXT");
        user.AppendLine(request.Description);
        if (!string.IsNullOrWhiteSpace(request.SampleText))
        {
            user.AppendLine();
            user.AppendLine("SAMPLE_TEXT");
            user.AppendLine(request.SampleText.Length > 2000
                ? request.SampleText.Substring(0, 2000)
                : request.SampleText);
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt.delimiters"] = $"{DocumentTypeJsonStart}|{DocumentTypeJsonEnd}",
            ["prompt.sample"] = (!string.IsNullOrWhiteSpace(request.SampleText)).ToString()
        };

        return new TemplateGenerationPromptEnvelope(systemPrompt, user.ToString(), metadata);
    }

    public TemplateGenerationParseResult ParseTemplateGenerationResponse(string? response, TemplateGenerationRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["prompt.delimiters"] = $"{DocumentTypeJsonStart}|{DocumentTypeJsonEnd}",
            ["prompt.sample"] = (!string.IsNullOrWhiteSpace(request.SampleText)).ToString()
        };

        if (string.IsNullOrWhiteSpace(response))
        {
            metadata["prompt.parse"] = "empty";
            return TemplateGenerationParseResult.Fallback(metadata, BuildFallbackPrompt(request), GenerateCode(request.Name), request.Description);
        }

        var delimited = ExtractDelimitedContent(response, DocumentTypeJsonStart, DocumentTypeJsonEnd);
        if (!delimited.Success)
        {
            metadata["prompt.parse"] = "missing_delimiters";
            return TemplateGenerationParseResult.Fallback(metadata, BuildFallbackPrompt(request), GenerateCode(request.Name), request.Description);
        }

        metadata["prompt.parse"] = "ok";
        metadata["prompt.payloadLength"] = delimited.Content.Length.ToString(CultureInfo.InvariantCulture);

        try
        {
            using var document = JsonDocument.Parse(delimited.Content);
            var root = document.RootElement;

            var systemPrompt = GetString(root, "systemPrompt") ?? GetString(root, "SystemPrompt") ?? "Extract structured information from the supplied document.";
            var instructions = GetString(root, "instructions") ?? GetString(root, "Instructions");
            if (!string.IsNullOrWhiteSpace(instructions))
            {
                systemPrompt = string.Join(Environment.NewLine, new[] { systemPrompt.Trim(), instructions.Trim() });
            }

            var userTemplate = GetString(root, "userTemplate") ?? GetString(root, "Template") ?? "Summarise key highlights from the document: {{text}}";

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("variables", out var variablesElement) && variablesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in variablesElement.EnumerateObject())
                {
                    variables[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
            else if (root.TryGetProperty("placeholders", out var placeholdersElement) && placeholdersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in placeholdersElement.EnumerateObject())
                {
                    variables[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            if (!variables.ContainsKey("sample") && !string.IsNullOrWhiteSpace(request.SampleText))
            {
                variables["sample"] = request.SampleText;
            }

            var prompt = new PromptTemplate
            {
                SystemPrompt = systemPrompt.Trim(),
                UserTemplate = userTemplate.Trim(),
                Variables = variables
            };

            var code = GetString(root, "code") ?? GenerateCode(request.Name);
            var description = GetString(root, "description") ?? request.Description;
            var tags = ExtractTags(root);

            if (!string.IsNullOrWhiteSpace(instructions))
            {
                metadata["prompt.instructions"] = instructions.Trim();
            }

            if (tags.Count > 0)
            {
                metadata["prompt.tags"] = string.Join(',', tags);
            }

            return new TemplateGenerationParseResult(true, prompt, code, description, tags, metadata);
        }
        catch (JsonException ex)
        {
            metadata["prompt.parse"] = "invalid_json";
            metadata["prompt.error"] = ex.GetType().Name;
            return TemplateGenerationParseResult.Fallback(metadata, BuildFallbackPrompt(request), GenerateCode(request.Name), request.Description);
        }
    }

    public ManualPromptEnvelope BuildManualSessionPrompt(
        ManualAnalysisSession session,
        SemanticTypeProfile? profile,
        IReadOnlyList<ManualDocumentSnapshot> snapshots,
        DocMindOptions options)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (snapshots is null) throw new ArgumentNullException(nameof(snapshots));
        if (options is null) throw new ArgumentNullException(nameof(options));

        var systemBuilder = new StringBuilder();
        systemBuilder.AppendLine("You are DocMind, consolidating structured findings across multiple documents.");
        systemBuilder.AppendLine("Respect the lean prompt contract and avoid commentary outside requested sections.");

        var userBuilder = new StringBuilder();
        userBuilder.AppendLine("SYSTEM");
        userBuilder.AppendLine("fill the template using ALL documents. cite sources as DOC_## (e.g. DOC_01).");
        userBuilder.AppendLine("operator notes override conflicting content. prefer UNKNOWN when data is missing.");
        userBuilder.AppendLine("return ONLY the required output blocks with no additional prose.");
        if (!string.IsNullOrWhiteSpace(profile?.Prompt.SystemPrompt))
        {
            userBuilder.AppendLine(profile!.Prompt.SystemPrompt.Trim());
        }
        if (!string.IsNullOrWhiteSpace(session.Prompt?.Instructions))
        {
            userBuilder.AppendLine(session.Prompt!.Instructions!.Trim());
        }
        userBuilder.AppendLine();

        var selectedProfile = profile is null ? "ad-hoc" : profile.Name;
        userBuilder.AppendLine("META");
        userBuilder.AppendLine($"session_id: {session.Id}");
        userBuilder.AppendLine($"profile: {selectedProfile}");
        userBuilder.AppendLine($"docs: {snapshots.Count}");
        userBuilder.AppendLine($"delimiters: {FilledDocumentTypeStart},{FilledDocumentTypeEnd};{ContextUnderstandingStart},{ContextUnderstandingEnd}");
        userBuilder.AppendLine("citation_format: DOC_##");
        userBuilder.AppendLine("unknown_token: UNKNOWN");
        userBuilder.AppendLine();

        userBuilder.AppendLine("DOCUMENTS");
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var alias = $"DOC_{index + 1:00}";
            userBuilder.AppendLine($"{alias}.name: {snapshot.Document.DisplayName ?? snapshot.Document.FileName}");
            userBuilder.AppendLine($"{alias}.status: {snapshot.Document.Status}");
            userBuilder.AppendLine($"{alias}.confidence: {snapshot.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(snapshot.SessionDocument?.Notes))
            {
                userBuilder.AppendLine($"{alias}.notes: {snapshot.SessionDocument!.Notes!.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.Summary))
            {
                userBuilder.AppendLine($"{alias}.summary: {snapshot.Summary.Trim()}");
            }
            if (snapshot.Topics.Count > 0)
            {
                userBuilder.AppendLine($"{alias}.topics: {string.Join(", ", snapshot.Topics)}");
            }
            userBuilder.AppendLine();
        }

        if (profile is not null && !string.IsNullOrWhiteSpace(profile.Prompt.UserTemplate))
        {
            userBuilder.AppendLine("TEMPLATE");
            userBuilder.AppendLine(profile.Prompt.UserTemplate.Trim());
            userBuilder.AppendLine();
        }

        if (session.Prompt?.Variables is { Count: > 0 })
        {
            userBuilder.AppendLine("VARIABLE_OVERRIDES");
            foreach (var (key, value) in session.Prompt.Variables)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    userBuilder.AppendLine($"{key}: {value}");
                }
            }
            userBuilder.AppendLine();
        }

        userBuilder.AppendLine("OUTPUT");
        userBuilder.AppendLine(FilledDocumentTypeStart);
        userBuilder.AppendLine("(filled template using consolidated findings with DOC_## citations)");
        userBuilder.AppendLine(FilledDocumentTypeEnd);
        userBuilder.AppendLine(ContextUnderstandingStart);
        userBuilder.AppendLine("(brief synthesis of approach, sources used, and confidence)");
        userBuilder.AppendLine(ContextUnderstandingEnd);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["manual.docs"] = snapshots.Count.ToString(CultureInfo.InvariantCulture),
            ["manual.profile"] = profile?.Id ?? string.Empty,
            ["manual.delimiters"] = $"{FilledDocumentTypeStart}|{FilledDocumentTypeEnd};{ContextUnderstandingStart}|{ContextUnderstandingEnd}"
        };

        return new ManualPromptEnvelope(systemBuilder.ToString().Trim(), userBuilder.ToString(), metadata);
    }

    public DelimitedContentResult ExtractDelimitedContent(string? text, string startDelimiter, string endDelimiter)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DelimitedContentResult.Missing;
        }

        var startIndex = text.IndexOf(startDelimiter, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return DelimitedContentResult.Missing;
        }

        startIndex += startDelimiter.Length;
        var endIndex = text.IndexOf(endDelimiter, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return DelimitedContentResult.Missing;
        }

        var content = text[startIndex..endIndex].Trim();
        return new DelimitedContentResult(true, content);
    }

    private static PromptTemplate BuildFallbackPrompt(TemplateGenerationRequest request)
        => new()
        {
            SystemPrompt = "Extract structured information from the supplied document.",
            UserTemplate = "Summarise key highlights from the document: {{text}}",
            Variables = string.IsNullOrWhiteSpace(request.SampleText)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sample"] = request.SampleText
                }
        };

    private static string GenerateCode(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"PROFILE-{Guid.NewGuid():N}";
        }

        var span = name.Trim().ToUpperInvariant();
        Span<char> buffer = stackalloc char[Math.Min(span.Length, 8)];
        var index = 0;
        foreach (var ch in span)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = ch;
            }
            if (index >= buffer.Length)
            {
                break;
            }
        }

        if (index == 0)
        {
            return $"PROFILE-{Guid.NewGuid():N}";
        }

        return new string(buffer[..index]);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;

    private static List<string> ExtractTags(JsonElement root)
    {
        if (root.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
        {
            return tagsElement
                .EnumerateArray()
                .Select(tag => tag.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return new List<string>();
    }
}

public sealed record TemplateGenerationPromptEnvelope(
    string SystemPrompt,
    string UserPrompt,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record TemplateGenerationParseResult(
    bool Success,
    PromptTemplate Prompt,
    string Code,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static TemplateGenerationParseResult Fallback(
        IReadOnlyDictionary<string, string> metadata,
        PromptTemplate prompt,
        string code,
        string description)
        => new(false, prompt, code, description, Array.Empty<string>(), metadata);
}

public sealed record ManualPromptEnvelope(
    string SystemPrompt,
    string UserPrompt,
    IReadOnlyDictionary<string, string> Metadata);

public readonly record struct DelimitedContentResult(bool Success, string Content)
{
    public static readonly DelimitedContentResult Missing = new(false, string.Empty);
}

public sealed record ManualDocumentSnapshot(
    SourceDocument Document,
    ManualAnalysisDocument? SessionDocument,
    IReadOnlyList<DocumentInsight> Insights,
    double Confidence,
    string Summary,
    IReadOnlyList<string> Topics);
