using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
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

    private sealed record TemplateData(JObject Root, JObject Fields, JObject Formatted, JArray Footnotes, JObject Metadata, JObject Evidence)
    {
        public TemplateData(JObject root, JObject fields, JObject formatted, JArray footnotes, JObject metadata)
            : this(root, fields, formatted, footnotes, metadata, new JObject())
        {
        }
    }
}
