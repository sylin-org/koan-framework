using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Koan.Samples.Meridian.Models;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Infrastructure;

/// <summary>
/// Builds analysis-level fact taxonomies used by the fact extraction and field alignment pipeline.
/// </summary>
public static class FactBlueprint
{
    public sealed class FactAttribute
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string DataType { get; set; } = "string";
        public bool Required { get; set; }
            = false;
        public List<string> Synonyms { get; set; } = new();
    }

    public sealed class FactCategory
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Description { get; set; }
            = null;
        public List<string> Synonyms { get; set; } = new();
        public List<string> SampleValues { get; set; } = new();
        public List<FactAttribute> Attributes { get; set; } = new();
    }

    public sealed class FieldMapping
    {
        public string FieldPath { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string AttributeId { get; set; } = string.Empty;
        public bool AllowSynthesis { get; set; }
            = false;
        public double MinimumConfidence { get; set; }
            = 0.4;
        public string? Aggregation { get; set; }
            = null;
        public string? Notes { get; set; }
            = null;
    }

    public sealed class Taxonomy
    {
        public List<FactCategory> Categories { get; } = new();
        public List<FieldMapping> FieldMappings { get; } = new();

        public IReadOnlyList<FieldMapping> FindMappings(string fieldPath)
        {
            var canonical = FieldPathCanonicalizer.Canonicalize(fieldPath);
            return FieldMappings
                .Where(mapping => string.Equals(
                    FieldPathCanonicalizer.Canonicalize(mapping.FieldPath),
                    canonical,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public FactCategory? FindCategory(string categoryId)
            => Categories.FirstOrDefault(category =>
                string.Equals(category.Id, categoryId, StringComparison.OrdinalIgnoreCase));
    }

    public static Taxonomy Build(AnalysisType analysisType, JSchema? schema)
    {
        if (analysisType is null)
        {
            throw new ArgumentNullException(nameof(analysisType));
        }

        var taxonomy = new Taxonomy();

        if (analysisType.FactCategories is { Count: > 0 })
        {
            taxonomy.Categories.AddRange(analysisType.FactCategories.Select(CloneCategory));
        }

        if (analysisType.FieldMappings is { Count: > 0 })
        {
            taxonomy.FieldMappings.AddRange(analysisType.FieldMappings.Select(CloneMapping));
        }

        var generated = GenerateFromSchema(schema);

        if (taxonomy.Categories.Count == 0 && generated.Categories.Count > 0)
        {
            taxonomy.Categories.AddRange(generated.Categories);
        }

        if (taxonomy.FieldMappings.Count == 0 && generated.FieldMappings.Count > 0)
        {
            taxonomy.FieldMappings.AddRange(generated.FieldMappings);
        }

        return taxonomy;
    }

    private static Taxonomy GenerateFromSchema(JSchema? schema)
    {
        var taxonomy = new Taxonomy();
        if (schema is null)
        {
            return taxonomy;
        }

        var seenCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fieldPath, fieldSchema) in SchemaFieldEnumerator.EnumerateLeaves(schema))
        {
            var canonicalPath = FieldPathCanonicalizer.Canonicalize(fieldPath);
            var key = canonicalPath.TrimStart('$').Trim('.');
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var categoryId = "field::" + key.Replace('.', '_')
                .Replace("[]", "_items", StringComparison.Ordinal);

            if (!seenCategories.Contains(categoryId))
            {
                taxonomy.Categories.Add(new FactCategory
                {
                    Id = categoryId,
                    Label = FieldPathCanonicalizer.ToDisplayName(canonicalPath),
                    Description = fieldSchema.Description,
                    Attributes =
                    {
                        new FactAttribute
                        {
                            Id = "value",
                            Label = FieldPathCanonicalizer.ToDisplayName(canonicalPath),
                            DataType = DescribeDataType(fieldSchema),
                            Required = false
                        }
                    }
                });
                seenCategories.Add(categoryId);
            }

            taxonomy.FieldMappings.Add(new FieldMapping
            {
                FieldPath = canonicalPath,
                CategoryId = categoryId,
                AttributeId = "value",
                MinimumConfidence = 0.4,
                AllowSynthesis = true,
                Aggregation = fieldSchema.Type == JSchemaType.Array ? "collection" : null
            });
        }

        return taxonomy;
    }

    private static FactCategory CloneCategory(FactCategory category)
    {
        return new FactCategory
        {
            Id = category.Id,
            Label = category.Label,
            Description = category.Description,
            Synonyms = new List<string>(category.Synonyms),
            SampleValues = new List<string>(category.SampleValues),
            Attributes = category.Attributes
                .Select(attribute => new FactAttribute
                {
                    Id = attribute.Id,
                    Label = attribute.Label,
                    DataType = attribute.DataType,
                    Required = attribute.Required,
                    Synonyms = new List<string>(attribute.Synonyms)
                })
                .ToList()
        };
    }

    private static FieldMapping CloneMapping(FieldMapping mapping)
    {
        return new FieldMapping
        {
            FieldPath = FieldPathCanonicalizer.Canonicalize(mapping.FieldPath),
            CategoryId = mapping.CategoryId,
            AttributeId = mapping.AttributeId,
            AllowSynthesis = mapping.AllowSynthesis,
            MinimumConfidence = mapping.MinimumConfidence,
            Aggregation = mapping.Aggregation,
            Notes = mapping.Notes
        };
    }

    private static string DescribeDataType(JSchema schema)
    {
        if (schema.Type is null)
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
            _ => schema.Type.Value.ToString().ToLower(CultureInfo.InvariantCulture)
        };
    }
}
