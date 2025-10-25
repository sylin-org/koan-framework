using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Koan.Samples.Meridian.Models;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace Koan.Samples.Meridian.Infrastructure;

/// <summary>
/// Description of an expected deliverable field derived from the analysis template/schema.
/// </summary>
public sealed record FieldExpectation(
	string FieldPath,
	string DisplayName,
	string DataType,
	bool IsRequired,
	string? Description,
	IReadOnlyList<string> ExampleValues,
	IReadOnlyList<string> Keywords);

/// <summary>
/// Summary of expectations for a specific analysis type.
/// </summary>
public sealed record AnalysisExpectationSummary(
	string AnalysisName,
	string? Description,
	IReadOnlyList<string> Tags,
	IReadOnlyList<string> Descriptors,
	IReadOnlyList<FieldExpectation> Fields);

/// <summary>
/// Builds expectation summaries for an analysis type based on its template schema.
/// </summary>
public static class FieldExpectationBuilder
{
	private static readonly Regex TokenBoundary = new("[\\s\\-_/]+", RegexOptions.Compiled);

	public static AnalysisExpectationSummary Build(AnalysisType analysisType, JSchema? schema)
	{
		schema ??= TryParseSchema(analysisType.JsonSchema);

		var fields = new List<FieldExpectation>();
		if (schema is not null)
		{
			Traverse(schema, "$", isRequired: false, fields);
		}

		return new AnalysisExpectationSummary(
			analysisType.Name,
			string.IsNullOrWhiteSpace(analysisType.Description) ? null : analysisType.Description,
			analysisType.Tags ?? new List<string>(),
			analysisType.Descriptors ?? new List<string>(),
			fields);
	}

	public static FieldExpectation CreateFallback(string fieldPath, string displayName, JSchema schema)
	{
		var keywords = BuildKeywords(displayName, schema.Description, Array.Empty<string>());
		return new FieldExpectation(
			FieldPathCanonicalizer.Canonicalize(fieldPath),
			displayName,
			DescribeType(schema),
			false,
			schema.Description,
			ReadExamples(schema),
			keywords);
	}

	public static FieldExpectation CreateFromOrganizationField(string fieldPath, OrganizationFieldDefinition field)
	{
		var displayName = string.IsNullOrWhiteSpace(field.FieldName)
			? FieldPathCanonicalizer.ToDisplayName(fieldPath)
			: field.FieldName;

		var keywords = BuildKeywords(displayName, field.Description, field.Examples ?? new List<string>());
		return new FieldExpectation(
			FieldPathCanonicalizer.Canonicalize(fieldPath),
			displayName,
			"string",
			false,
			field.Description,
			field.Examples ?? new List<string>(),
			keywords);
	}

	public static IReadOnlyList<FieldExpectation> MergeWithOrganizationFields(AnalysisExpectationSummary summary, OrganizationProfile? organizationProfile)
	{
		var combined = new Dictionary<string, FieldExpectation>(StringComparer.OrdinalIgnoreCase);
		foreach (var field in summary.Fields)
		{
			combined[field.FieldPath] = field;
		}

		if (organizationProfile?.Fields is { Count: > 0 })
		{
			foreach (var field in organizationProfile.Fields.OrderBy(f => f.DisplayOrder))
			{
				var canonicalPath = FieldPathCanonicalizer.Canonicalize($"$.{field.FieldName}");
				if (!combined.ContainsKey(canonicalPath))
				{
					combined[canonicalPath] = CreateFromOrganizationField(canonicalPath, field);
				}
			}
		}

		return combined.Values
			.OrderBy(f => f.FieldPath, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void Traverse(JSchema schema, string path, bool isRequired, List<FieldExpectation> fields)
	{
		switch (schema.Type)
		{
			case JSchemaType.Object:
				foreach (var property in schema.Properties)
				{
					var nextPath = path == "$"
						? $"$.{property.Key}"
						: $"{path}.{property.Key}";

					var childRequired = schema.Required?.Any(required => string.Equals(required, property.Key, StringComparison.OrdinalIgnoreCase)) ?? false;
					Traverse(property.Value, nextPath, childRequired, fields);
				}
				break;

			case JSchemaType.Array when schema.Items.Count > 0:
				var arrayPath = path.EndsWith("[]", StringComparison.Ordinal)
					? path
					: $"{path}[]";
				Traverse(schema.Items[0], arrayPath, isRequired, fields);
				break;

			default:
				var canonicalPath = FieldPathCanonicalizer.Canonicalize(path);
				var displayName = FieldPathCanonicalizer.ToDisplayName(canonicalPath);
				var examples = ReadExamples(schema);
				var keywords = BuildKeywords(displayName, schema.Description, examples);
				fields.Add(new FieldExpectation(
					canonicalPath,
					displayName,
					DescribeType(schema),
					isRequired,
					schema.Description,
					examples,
					keywords));
				break;
		}
	}

	private static IReadOnlyList<string> ReadExamples(JSchema schema)
	{
		if (schema.ExtensionData is { Count: > 0 } && schema.ExtensionData.TryGetValue("examples", out var examplesToken) && examplesToken is JArray examplesArray && examplesArray.Count > 0)
		{
			return examplesArray
				.Select(example => example?.ToString())
				.Where(example => !string.IsNullOrWhiteSpace(example))
				.Cast<string>()
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		if (schema.Enum is { Count: > 0 })
		{
			return schema.Enum
				.Select(option => option?.ToString())
				.Where(option => !string.IsNullOrWhiteSpace(option))
				.Cast<string>()
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		return Array.Empty<string>();
	}

	private static IReadOnlyList<string> BuildKeywords(string displayName, string? description, IEnumerable<string> examples)
	{
		var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var token in Tokenize(displayName))
		{
			tokens.Add(token);
		}

		foreach (var token in Tokenize(description))
		{
			tokens.Add(token);
		}

		foreach (var example in examples)
		{
			foreach (var token in Tokenize(example))
			{
				tokens.Add(token);
			}
		}

		return tokens.ToList();
	}

	private static IEnumerable<string> Tokenize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			yield break;
		}

		foreach (var part in TokenBoundary.Split(value))
		{
			var token = part.Trim().ToLowerInvariant();
			if (token.Length > 2)
			{
				yield return token;
			}
		}
	}

	private static string DescribeType(JSchema schema)
	{
		if (schema.Type is null)
		{
			return "unknown";
		}

		if (schema.Type == JSchemaType.Array && schema.Items.Count > 0)
		{
			return $"array<{DescribeType(schema.Items[0])}>";
		}

		if (!string.IsNullOrWhiteSpace(schema.Format))
		{
			return schema.Format;
		}

		return schema.Type.Value switch
		{
			JSchemaType.String => "string",
			JSchemaType.Boolean => "boolean",
			JSchemaType.Integer => "integer",
			JSchemaType.Number => "number",
			JSchemaType.Object => "object",
			JSchemaType.Array => "array",
			_ => schema.Type.Value.ToString().ToLowerInvariant()
		};
	}

	private static JSchema? TryParseSchema(string? jsonSchema)
	{
		if (string.IsNullOrWhiteSpace(jsonSchema))
		{
			return null;
		}

		try
		{
			return JSchema.Parse(jsonSchema);
		}
		catch (JSchemaException)
		{
			return null;
		}
	}
}

