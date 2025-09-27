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
    Task<TemplateDefinition> ResolveTemplateAsync(SemanticTypeProfile? profile, CancellationToken ct = default);
}

public sealed class TemplateGeneratorService : ITemplateGeneratorService
{
    private readonly ILogger<TemplateGeneratorService> _logger;

    public TemplateGeneratorService(ILogger<TemplateGeneratorService> logger)
    {
        _logger = logger;
    }

    public Task<TemplateDefinition> ResolveTemplateAsync(SemanticTypeProfile? profile, CancellationToken ct = default)
    {
        if (profile is null)
        {
            _logger.LogDebug("Falling back to default template definition because no semantic type profile was assigned.");
            return Task.FromResult(TemplateDefinition.Empty);
        }

        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Copy profile metadata
        foreach (var kvp in profile.Metadata)
        {
            metadata[kvp.Key] = kvp.Value;
        }

        // Add prompt information
        metadata["systemPrompt"] = profile.Prompt.SystemPrompt;
        metadata["userTemplate"] = profile.Prompt.UserTemplate;
        metadata["variables"] = profile.Prompt.Variables;

        var fields = new List<TemplateField>();

        // Extract fields from the extraction schema
        if (profile.ExtractionSchema?.Fields is { Count: > 0 })
        {
            foreach (var kvp in profile.ExtractionSchema.Fields)
            {
                var fieldDef = kvp.Value;
                fields.Add(new TemplateField(
                    kvp.Key,
                    fieldDef.Description ?? "Schema defined field",
                    fieldDef.Required,
                    fieldDef.Type));
            }
        }

        var template = new TemplateDefinition(
            Name: profile.Name,
            Description: profile.Description,
            Fields: fields,
            Metadata: metadata);

        return Task.FromResult(template);
    }
}
