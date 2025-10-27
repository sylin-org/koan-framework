using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Services;

public interface IFactCatalogBuilder
{
    Task<FactCatalog> BuildAsync(
        DocumentPipeline pipeline,
        AnalysisType analysisType,
        CancellationToken ct);
}

/// <summary>
/// Stage 1: Enumerate all facts to extract from organization profile and analysis type.
/// Combines global organizational fields with analysis-specific template fields.
/// </summary>
public sealed class FactCatalogBuilder : IFactCatalogBuilder
{
    private readonly ILogger<FactCatalogBuilder> _logger;

    public FactCatalogBuilder(ILogger<FactCatalogBuilder> logger)
    {
        _logger = logger;
    }

    public async Task<FactCatalog> BuildAsync(
        DocumentPipeline pipeline,
        AnalysisType analysisType,
        CancellationToken ct)
    {
        var catalog = new FactCatalog();

        // Get active organization profile
        var orgProfile = await OrganizationProfile.GetActiveAsync(ct).ConfigureAwait(false);

        // Add organization-wide facts
        if (orgProfile != null)
        {
            _logger.LogDebug("Adding {Count} facts from OrganizationProfile '{Name}'",
                orgProfile.Fields.Count, orgProfile.Name);

            foreach (var field in orgProfile.Fields.OrderBy(f => f.DisplayOrder))
            {
                catalog.Facts.Add(new FactDefinition
                {
                    FieldPath = $"$.{field.FieldName}",
                    FieldName = field.FieldName,
                    Description = field.Description ?? string.Empty,
                    Examples = field.Examples ?? new List<string>(),
                    Source = "OrgProfile",
                    DataType = "string"
                });
            }
        }
        else
        {
            _logger.LogWarning("No active OrganizationProfile found - only using AnalysisType fields");
        }

        // Add analysis-specific facts from JSON schema
        var schema = pipeline.TryParseSchema();
        if (schema != null)
        {
            var analysisFields = ExtractFieldsFromSchema(schema);
            _logger.LogDebug("Adding {Count} facts from AnalysisType '{Name}' schema",
                analysisFields.Count, analysisType.Name);

            catalog.Facts.AddRange(analysisFields);
        }
        else
        {
            _logger.LogWarning("Pipeline {PipelineId} has invalid schema - only using OrgProfile fields",
                pipeline.Id);
        }

        _logger.LogInformation("Built FactCatalog with {TotalFacts} facts ({OrgFacts} org + {AnalysisFacts} analysis)",
            catalog.Facts.Count,
            catalog.Facts.Count(f => f.Source == "OrgProfile"),
            catalog.Facts.Count(f => f.Source == "AnalysisType"));

        return catalog;
    }

    private List<FactDefinition> ExtractFieldsFromSchema(JSchema schema)
    {
        var facts = new List<FactDefinition>();

        foreach (var (fieldPath, fieldSchema) in SchemaFieldEnumerator.EnumerateLeaves(schema))
        {
            var canonicalPath = FieldPathCanonicalizer.Canonicalize(fieldPath);
            var fieldName = canonicalPath.TrimStart('$').Trim('.');

            if (string.IsNullOrWhiteSpace(fieldName))
            {
                continue;
            }

            facts.Add(new FactDefinition
            {
                FieldPath = canonicalPath,
                FieldName = fieldName,
                Description = fieldSchema.Description ?? string.Empty,
                Examples = ExtractExamplesFromSchema(fieldSchema),
                Source = "AnalysisType",
                DataType = DescribeDataType(fieldSchema)
            });
        }

        return facts;
    }

    private static List<string> ExtractExamplesFromSchema(JSchema schema)
    {
        var examples = new List<string>();

        // Extract from enum values
        if (schema.Enum != null && schema.Enum.Count > 0)
        {
            examples.AddRange(schema.Enum.Select(e => e.ToString()));
        }

        // Extract from examples array if present
        if (schema.ExtensionData != null && schema.ExtensionData.TryGetValue("examples", out var examplesToken))
        {
            if (examplesToken is Newtonsoft.Json.Linq.JArray examplesArray)
            {
                examples.AddRange(examplesArray.Select(e => e.ToString()));
            }
        }

        return examples;
    }

    private static string DescribeDataType(JSchema schema)
    {
        if (schema.Type == null)
        {
            return "string";
        }

        if (schema.Type == JSchemaType.Array && schema.Items.Count > 0)
        {
            return "array<" + DescribeDataType(schema.Items[0]) + ">";
        }

        if (!string.IsNullOrWhiteSpace(schema.Format))
        {
            return schema.Format;
        }

        return schema.Type.Value switch
        {
            JSchemaType.Integer => "integer",
            JSchemaType.Number => "number",
            JSchemaType.Boolean => "boolean",
            JSchemaType.Array => "array",
            JSchemaType.Object => "object",
            _ => "string"
        };
    }
}
