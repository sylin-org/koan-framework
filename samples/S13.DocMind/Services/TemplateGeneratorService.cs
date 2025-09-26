using System.Linq;
using Microsoft.Extensions.Logging;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public record TemplateField(string Name, string Description, bool Required, string DataType = "string");

public record TemplateDefinition(
    string Name,
    string? Description,
    IReadOnlyList<TemplateField> Fields,
    IDictionary<string, object> Metadata)
{
    public static TemplateDefinition Empty { get; } = new(
        Name: "default",
        Description: "Default free-form analysis template",
        Fields: Array.Empty<TemplateField>(),
        Metadata: new Dictionary<string, object>());
}

public interface ITemplateGeneratorService
{
    Task<TemplateDefinition> ResolveTemplateAsync(DocumentType? type, CancellationToken ct = default);
}

public sealed class TemplateGeneratorService : ITemplateGeneratorService
{
    private readonly ILogger<TemplateGeneratorService> _logger;

    public TemplateGeneratorService(ILogger<TemplateGeneratorService> logger)
    {
        _logger = logger;
    }

    public Task<TemplateDefinition> ResolveTemplateAsync(DocumentType? type, CancellationToken ct = default)
    {
        if (type is null)
        {
            _logger.LogDebug("Falling back to default template definition because no document type was assigned.");
            return Task.FromResult(TemplateDefinition.Empty);
        }

        var metadata = new Dictionary<string, object>(type.ModelSettings, StringComparer.OrdinalIgnoreCase)
        {
            ["prompt"] = type.AnalysisPrompt,
            ["requiredFields"] = type.RequiredFields,
            ["optionalFields"] = type.OptionalFields
        };

        var fields = new List<TemplateField>();
        foreach (var field in type.RequiredFields)
        {
            fields.Add(new TemplateField(field, "Required field identified in template", required: true));
        }

        foreach (var field in type.OptionalFields)
        {
            if (fields.All(f => !string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase)))
            {
                fields.Add(new TemplateField(field, "Optional field identified in template", required: false));
            }
        }

        if (type.ExtractionSchema is { Count: > 0 })
        {
            foreach (var kvp in type.ExtractionSchema)
            {
                var required = type.RequiredFields.Any(f => string.Equals(f, kvp.Key, StringComparison.OrdinalIgnoreCase));
                fields.Add(new TemplateField(kvp.Key, "Schema defined field", required, kvp.Value?.ToString() ?? "string"));
            }
        }

        var template = new TemplateDefinition(
            Name: type.Name,
            Description: type.Description,
            Fields: fields,
            Metadata: metadata);

        return Task.FromResult(template);
    }
}
