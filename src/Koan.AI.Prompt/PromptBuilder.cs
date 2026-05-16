using System.Collections.Immutable;
using System.Text.Json;

namespace Koan.AI.Prompt;

/// <summary>
/// Fluent builder for constructing structured prompts.
///
/// <code>
/// var prompt = Prompt.Create(p => p
///     .System("You are a {role}")
///     .Instruct("Answer questions about {product}")
///     .Constrain("Be concise", "Max 3 sentences")
///     .OutputAs&lt;SupportResponse&gt;()
///     .Example(input: "How do I reset?", output: new { Answer = "Go to Settings..." })
///     .Default("role", "support agent"));
/// </code>
/// </summary>
public sealed class PromptBuilder
{
    private string? _system;
    private string? _template;
    private readonly List<string> _constraints = [];
    private OutputSpec? _outputFormat;
    private readonly List<Example> _examples = [];
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _meta = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set the system directive (role definition, persona).</summary>
    public PromptBuilder System(string systemPrompt)
    {
        _system = systemPrompt;
        return this;
    }

    /// <summary>Set the user message template with {variable} placeholders.</summary>
    public PromptBuilder Instruct(string template)
    {
        _template = template;
        return this;
    }

    /// <summary>Add one or more constraints (rules, guardrails).</summary>
    public PromptBuilder Constrain(params string[] constraints)
    {
        _constraints.AddRange(constraints);
        return this;
    }

    /// <summary>Set expected output format from a type's JSON schema.</summary>
    public PromptBuilder OutputAs<T>()
    {
        _outputFormat = OutputSpec.FromType<T>();
        return this;
    }

    /// <summary>Set expected output format from an explicit spec.</summary>
    public PromptBuilder OutputAs(OutputSpec spec)
    {
        _outputFormat = spec;
        return this;
    }

    /// <summary>Set expected output format as a list of field names.</summary>
    public PromptBuilder OutputFields(params string[] fields)
    {
        _outputFormat = OutputSpec.WithFields(fields);
        return this;
    }

    /// <summary>Add a few-shot example.</summary>
    public PromptBuilder Example(string input, string output)
    {
        _examples.Add(new Example(input, output));
        return this;
    }

    /// <summary>Add a few-shot example with a typed output (serialized to JSON).</summary>
    public PromptBuilder Example<T>(string input, T output)
    {
        var json = JsonSerializer.Serialize(output, JsonOptions.Compact);
        _examples.Add(new Example(input, json));
        return this;
    }

    /// <summary>Add a pre-built example.</summary>
    public PromptBuilder Example(Example example)
    {
        _examples.Add(example);
        return this;
    }

    /// <summary>Set a default value for a variable.</summary>
    public PromptBuilder Default(string variable, string value)
    {
        _defaults[variable] = value;
        return this;
    }

    /// <summary>Add metadata (author, version, tags).</summary>
    public PromptBuilder Meta(string key, string value)
    {
        _meta[key] = value;
        return this;
    }

    /// <summary>Build the immutable Prompt.</summary>
    internal Prompt Build()
    {
        return Prompt.FromBuilder(
            _system,
            _template,
            _constraints.ToImmutableList(),
            _outputFormat,
            _examples.ToImmutableList(),
            _defaults.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            _meta.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase));
    }
}

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
