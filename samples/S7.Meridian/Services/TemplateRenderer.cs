using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stubble.Core.Builders;

namespace Koan.Samples.Meridian.Services;

public interface ITemplateRenderer
{
    Task<string> RenderMarkdownAsync(Deliverable deliverable, CancellationToken ct);
    Task<string> RenderJsonAsync(Deliverable deliverable, CancellationToken ct);
    Task<byte[]> RenderPdfAsync(Deliverable deliverable, CancellationToken ct);
}

public sealed class TemplateRenderer : ITemplateRenderer
{
    private readonly ILogger<TemplateRenderer> _logger;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly StubbleBuilder _stubbleBuilder = new();

    public TemplateRenderer(ILogger<TemplateRenderer> logger, IPdfRenderer pdfRenderer)
    {
        _logger = logger;
        _pdfRenderer = pdfRenderer;
    }

    public async Task<string> RenderMarkdownAsync(Deliverable deliverable, CancellationToken ct)
    {
        var pipeline = await DocumentPipeline.Get(deliverable.PipelineId, ct).ConfigureAwait(false);
        if (pipeline is null)
        {
            _logger.LogWarning("Pipeline {PipelineId} missing while rendering deliverable {DeliverableId}.", deliverable.PipelineId, deliverable.Id);
            return deliverable.RenderedMarkdown ?? string.Empty;
        }

        var template = string.IsNullOrWhiteSpace(pipeline.TemplateMarkdown)
            ? "# Meridian Deliverable\n"
            : pipeline.TemplateMarkdown;
        var templateHash = ComputeHash(template);

        if (!string.IsNullOrWhiteSpace(deliverable.RenderedMarkdown) &&
            string.Equals(deliverable.TemplateMdHash, templateHash, StringComparison.OrdinalIgnoreCase))
        {
            return deliverable.RenderedMarkdown!;
        }

        var data = ParseData(deliverable.DataJson);
        var context = BuildTemplateContext(data);

        var renderer = _stubbleBuilder.Build();
        var markdown = await renderer.RenderAsync(template, context).ConfigureAwait(false);

        if (data.Footnotes.Count > 0)
        {
            var builder = new StringBuilder(markdown.TrimEnd());
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("## Footnotes");

            foreach (var footnote in data.Footnotes.Select((token, index) => new { Token = token, Index = index + 1 }))
            {
                var label = footnote.Token["index"]?.Value<int?>() ?? footnote.Index;
                var content = footnote.Token["content"]?.Value<string>() ?? string.Empty;
                builder.AppendLine($"[^{label}]: {content}");
            }

            markdown = builder.ToString();
        }

        return markdown;
    }

    public Task<string> RenderJsonAsync(Deliverable deliverable, CancellationToken ct)
    {
        var data = ParseData(deliverable.DataJson);
        var resolvedFacts = BuildResolvedFacts(data);
        if (resolvedFacts.HasValues)
        {
            data.Root["resolvedFacts"] = resolvedFacts;
        }

        return Task.FromResult(data.Root.ToString(Formatting.Indented));
    }

    public async Task<byte[]> RenderPdfAsync(Deliverable deliverable, CancellationToken ct)
    {
        try
        {
            var markdown = await RenderMarkdownAsync(deliverable, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return Array.Empty<byte>();
            }

            return await _pdfRenderer.RenderAsync(markdown, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render PDF for deliverable {DeliverableId}.", deliverable.Id);
            return Array.Empty<byte>();
        }
    }

    private static TemplateData ParseData(string dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return new TemplateData(new JObject(), new JObject(), new JObject(), new JArray(), new JObject());
        }

        try
        {
            var root = JObject.Parse(dataJson);
            var fields = root["fields"] as JObject ?? new JObject();
            var formatted = root["formatted"] as JObject ?? new JObject();
            var evidence = root["evidence"] as JObject ?? new JObject();
            var footnotes = root["footnotes"] as JArray ?? new JArray();
            var metadata = root["metadata"] as JObject ?? new JObject();

            return new TemplateData(root, fields, formatted, footnotes, metadata, evidence);
        }
        catch (JsonException)
        {
            return new TemplateData(new JObject(), new JObject(), new JObject(), new JArray(), new JObject());
        }
    }

    private static Dictionary<string, object?> BuildTemplateContext(TemplateData data)
    {
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in data.Fields.Properties())
        {
            context[property.Name] = ConvertToken(property.Value);
        }

        context["_fields"] = ConvertToken(data.Fields);
        context["_formatted"] = ConvertToken(data.Formatted);
        context["_metadata"] = ConvertToken(data.Metadata);
        context["_evidence"] = ConvertToken(data.Evidence);
        context["_footnotes"] = ConvertToken(data.Footnotes);

        return context;
    }

    private static object? ConvertToken(JToken token)
    {
        return token.Type switch
        {
            JTokenType.Object => token.Children<JProperty>()
                .ToDictionary(prop => prop.Name, prop => ConvertToken(prop.Value), StringComparer.OrdinalIgnoreCase),
            JTokenType.Array => token.Values<JToken>().Select(ConvertToken).ToList(),
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => token.Value<double>(),
            JTokenType.Boolean => token.Value<bool>(),
            JTokenType.String => token.Value<string>(),
            JTokenType.Date => token.Value<DateTime>().ToString("O", CultureInfo.InvariantCulture),
            JTokenType.Guid => token.Value<Guid>().ToString(),
            JTokenType.Uri => token.Value<Uri>()?.ToString(),
            JTokenType.TimeSpan => token.Value<TimeSpan>().ToString(),
            JTokenType.Null => null,
            _ => token.ToString()
        };
    }

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static JObject BuildResolvedFacts(TemplateData data)
    {
        var resolved = new JObject();

        var footnoteLookup = BuildFootnoteLookup(data.Footnotes);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in data.Fields.Properties())
        {
            keys.Add(prop.Name);
        }

        foreach (var prop in data.Formatted.Properties())
        {
            keys.Add(prop.Name);
        }

        foreach (var prop in data.Evidence.Properties())
        {
            keys.Add(prop.Name);
        }

        foreach (var key in keys)
        {
            var fieldToken = data.Fields[key];
            var formattedToken = data.Formatted[key];
            var evidenceToken = data.Evidence[key];

            var formattedText = formattedToken?.Type switch
            {
                null => string.Empty,
                JTokenType.Null => string.Empty,
                _ => formattedToken!.ToString()
            };

            var primaryText = string.IsNullOrWhiteSpace(formattedText)
                ? fieldToken?.ToString() ?? string.Empty
                : formattedText;

            var displayText = BuildDisplayText(primaryText);
            var displayHtml = BuildDisplayHtml(primaryText, footnoteLookup);
            var footnotes = BuildFootnoteDetails(primaryText, footnoteLookup);

            var fact = new JObject
            {
                ["value"] = fieldToken?.DeepClone() ?? JValue.CreateNull(),
                ["formatted"] = string.IsNullOrWhiteSpace(formattedText) ? null : JToken.FromObject(formattedText),
                ["displayText"] = displayText,
                ["displayHtml"] = displayHtml
            };

            if (footnotes.Count > 0)
            {
                fact["footnotes"] = new JArray(footnotes);
            }

            if (evidenceToken is not null)
            {
                fact["evidence"] = evidenceToken.DeepClone();

                if (evidenceToken is JObject evidenceObj)
                {
                    var metadata = evidenceObj["metadata"] as JObject;
                    var confidence = metadata?["confidence"]?.ToString() ?? evidenceObj["confidence"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(confidence))
                    {
                        fact["confidence"] = confidence;
                    }

                    var summary = BuildEvidenceSummary(evidenceObj);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        fact["evidenceSummary"] = summary;
                    }
                }
            }

            resolved[key] = fact;
        }

        return resolved;
    }

    private static IDictionary<int, string> BuildFootnoteLookup(JArray footnotes)
    {
        var lookup = new Dictionary<int, string>();

        foreach (var token in footnotes.Children<JToken>())
        {
            if (token is not JObject obj)
            {
                continue;
            }

            var index = obj["index"]?.Value<int?>();
            if (index is null)
            {
                continue;
            }

            var content = obj["content"]?.Value<string>() ?? string.Empty;
            lookup[index.Value] = content;
        }

        return lookup;
    }

    private static readonly Regex FootnoteMarkerRegex = new(@"\[\^(?<index>\d+)\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string BuildDisplayText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return FootnoteMarkerRegex.Replace(value, match =>
        {
            var index = match.Groups["index"].Value;
            return string.IsNullOrEmpty(index) ? string.Empty : $" [{index}]";
        }).Trim();
    }

    private static string BuildDisplayHtml(string value, IDictionary<int, string> footnotes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in FootnoteMarkerRegex.Matches(value))
        {
            if (match.Index > lastIndex)
            {
                AppendEncodedSegment(builder, value.Substring(lastIndex, match.Index - lastIndex));
            }

            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                lastIndex = match.Index + match.Length;
                continue;
            }

            var encodedContent = footnotes.TryGetValue(index, out var content)
                ? WebUtility.HtmlEncode(content)
                : string.Empty;

            builder.AppendFormat(CultureInfo.InvariantCulture,
                "<sup class=\"fact-footnote\" data-footnote-index=\"{0}\" title=\"{1}\">[{0}]</sup>",
                index,
                encodedContent);

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < value.Length)
        {
            AppendEncodedSegment(builder, value.Substring(lastIndex));
        }

        return builder.ToString();
    }

    private static void AppendEncodedSegment(StringBuilder builder, string segment)
    {
        if (string.IsNullOrEmpty(segment))
        {
            return;
        }

        var encoded = WebUtility.HtmlEncode(segment);
        encoded = encoded.Replace("\r\n", "\n");
        encoded = encoded.Replace("\r", "\n");
        encoded = encoded.Replace("\n", "<br />");

        builder.Append(encoded);
    }

    private static List<JObject> BuildFootnoteDetails(string value, IDictionary<int, string> footnotes)
    {
        var details = new List<JObject>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return details;
        }

        var seen = new HashSet<int>();

        foreach (Match match in FootnoteMarkerRegex.Matches(value))
        {
            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            if (!seen.Add(index))
            {
                continue;
            }

            var content = footnotes.TryGetValue(index, out var valueContent)
                ? valueContent
                : string.Empty;

            details.Add(new JObject
            {
                ["index"] = index,
                ["content"] = content
            });
        }

        return details;
    }

    private static string BuildEvidenceSummary(JObject evidence)
    {
        var parts = new List<string>();

        var source = evidence.Value<string>("sourceFileName");
        if (!string.IsNullOrWhiteSpace(source))
        {
            var page = evidence.Value<int?>("page");
            var section = evidence.Value<string>("section");

            var builder = new StringBuilder();
            builder.Append(source);
            if (page is > 0)
            {
                builder.Append(" (p. ");
                builder.Append(page.Value.ToString(CultureInfo.InvariantCulture));
                builder.Append(')');
            }

            if (!string.IsNullOrWhiteSpace(section))
            {
                builder.Append(" - ");
                builder.Append(section);
            }

            parts.Add(builder.ToString());
        }

        var text = evidence.Value<string>("text");
        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add(text);
        }

        var reasoning = evidence.SelectToken("metadata.factReasoning")?.Value<string>();
        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            parts.Add(reasoning);
        }

        return string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private sealed record TemplateData(JObject Root, JObject Fields, JObject Formatted, JArray Footnotes, JObject Metadata, JObject Evidence)
    {
        public TemplateData(JObject root, JObject fields, JObject formatted, JArray footnotes, JObject metadata)
            : this(root, fields, formatted, footnotes, metadata, new JObject())
        {
        }
    }
}
