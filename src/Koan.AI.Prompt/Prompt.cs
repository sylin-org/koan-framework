using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Koan.AI.Prompt;

/// <summary>
/// A Uri-inspired prompt primitive. Parses a string into a rich, inspectable,
/// immutable object with variable extraction and resolution.
///
/// <code>
/// var prompt = Prompt("Summarize {topic}: {content}");
/// prompt.Variables   // ["topic", "content"]
/// prompt.Raw         // "Summarize {topic}: {content}"
///
/// var text = prompt.Resolve(new { topic = "AI", content = article });
/// </code>
/// </summary>
public sealed class Prompt
{
    private static readonly Regex VariablePattern = new(
        @"\{([a-zA-Z_][a-zA-Z0-9_]*)\}",
        RegexOptions.Compiled);

    private readonly ImmutableDictionary<string, string> _defaults;

    private Prompt(
        string raw,
        string? system,
        string? template,
        ImmutableList<string> variables,
        ImmutableList<string> constraints,
        OutputSpec? outputFormat,
        ImmutableList<Example> examples,
        ImmutableDictionary<string, string> defaults,
        ImmutableDictionary<string, string> meta)
    {
        Raw = raw;
        System = system;
        Template = template;
        Variables = variables;
        Constraints = constraints;
        OutputFormat = outputFormat;
        Examples = examples;
        _defaults = defaults;
        Meta = meta;
    }

    // ── Core parts (like Uri.Scheme, Uri.Host, etc.) ──

    /// <summary>The original string used to construct this prompt.</summary>
    public string Raw { get; }

    /// <summary>System directive (role definition, persona).</summary>
    public string? System { get; }

    /// <summary>User message template with {variable} placeholders.</summary>
    public string? Template { get; }

    /// <summary>Variable names extracted from all text parts.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>Rules and guardrails (e.g., "Be concise", "Max 3 sentences").</summary>
    public IReadOnlyList<string> Constraints { get; }

    /// <summary>Expected output structure (JSON schema from type, format instructions).</summary>
    public OutputSpec? OutputFormat { get; }

    /// <summary>Few-shot examples for in-context learning.</summary>
    public IReadOnlyList<Example> Examples { get; }

    /// <summary>Arbitrary metadata (author, version, tags).</summary>
    public IReadOnlyDictionary<string, string> Meta { get; }

    // ── Construction: from string (shallow parse) ──

    /// <summary>
    /// Parse a string into a Prompt. Extracts {variable} placeholders.
    /// System/Template/Constraints come from the builder, not inferred.
    /// </summary>
    public static Prompt Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var variables = ExtractVariables(text);
        return new Prompt(
            raw: text,
            system: null,
            template: text,
            variables: variables,
            constraints: ImmutableList<string>.Empty,
            outputFormat: null,
            examples: ImmutableList<Example>.Empty,
            defaults: ImmutableDictionary<string, string>.Empty,
            meta: ImmutableDictionary<string, string>.Empty);
    }

    /// <summary>Implicit conversion from string — enables <c>Prompt p = "text";</c></summary>
    public static implicit operator Prompt(string text) => Parse(text);

    /// <summary>Implicit conversion to string — backward compatible with string APIs.</summary>
    public static implicit operator string(Prompt prompt) => prompt.ToString();

    // ── Construction: from builder ──

    /// <summary>
    /// Build a structured prompt with system directive, constraints, examples, and output format.
    /// </summary>
    public static Prompt Create(Action<PromptBuilder> configure)
    {
        var builder = new PromptBuilder();
        configure(builder);
        return builder.Build();
    }

    // ── Construction: from entity catalog ──

    /// <summary>Load the active prompt with the given name from the PromptEntry catalog.</summary>
    public static async Task<Prompt> Load(string name, CancellationToken ct = default)
    {
        var entry = await PromptEntry.FindActive(name, ct);
        return entry?.ToPrompt() ?? throw new PromptNotFoundException(name);
    }

    /// <summary>Load a specific version of a prompt from the catalog.</summary>
    public static async Task<Prompt> Load(string name, int version, CancellationToken ct = default)
    {
        var entry = await PromptEntry.FindVersion(name, version, ct);
        return entry?.ToPrompt() ?? throw new PromptNotFoundException(name, version);
    }

    /// <summary>Load a prompt using a strategy (A/B test, canary, pinned).</summary>
    public static async Task<Prompt> Load(
        string name, PromptStrategy strategy, CancellationToken ct = default)
    {
        var entry = await strategy.Resolve(name, ct);
        return entry?.ToPrompt() ?? throw new PromptNotFoundException(name);
    }

    // ── Resolution ──

    /// <summary>
    /// Resolve all {variable} placeholders using values from the provided object.
    /// Properties are matched by name (case-insensitive).
    /// </summary>
    public string Resolve(object? variables = null)
    {
        var text = BuildFullText();
        if (variables is null && _defaults.IsEmpty)
            return text;

        var values = variables is not null
            ? ObjectToDictionary(variables)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Apply defaults for missing values
        foreach (var (key, defaultValue) in _defaults)
        {
            if (!values.ContainsKey(key))
                values[key] = defaultValue;
        }

        return VariablePattern.Replace(text, match =>
        {
            var varName = match.Groups[1].Value;
            return values.TryGetValue(varName, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Resolve variables from a typed entity. Property names match variable names.
    /// </summary>
    public string Resolve<T>(T context) => Resolve((object?)context);

    /// <summary>
    /// Returns variable names that would remain unresolved given the provided context.
    /// </summary>
    public IReadOnlyList<string> UnresolvedVariables(object? context = null)
    {
        var provided = context is not null
            ? ObjectToDictionary(context)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _defaults.Keys)
            provided.TryAdd(key, string.Empty);

        return Variables
            .Where(v => !provided.ContainsKey(v))
            .ToList();
    }

    // ── Immutable modification ──

    /// <summary>
    /// Returns a new Prompt with modifications applied. Original is unchanged.
    /// </summary>
    public Prompt With(Action<PromptBuilder> modify)
    {
        var builder = ToBuilder();
        modify(builder);
        return builder.Build();
    }

    // ── Conversion ──

    public override string ToString() => Raw;

    // ── Internal construction (used by PromptBuilder) ──

    internal static Prompt FromBuilder(
        string? system,
        string? template,
        ImmutableList<string> constraints,
        OutputSpec? outputFormat,
        ImmutableList<Example> examples,
        ImmutableDictionary<string, string> defaults,
        ImmutableDictionary<string, string> meta)
    {
        var raw = BuildRawText(system, template, constraints, outputFormat, examples);
        var variables = ExtractVariables(raw);

        return new Prompt(raw, system, template, variables, constraints,
            outputFormat, examples, defaults, meta);
    }

    // ── Private helpers ──

    private string BuildFullText()
    {
        if (System is null && Constraints.Count == 0 && Examples.Count == 0 && OutputFormat is null)
            return Template ?? Raw;

        return BuildRawText(System, Template, Constraints as ImmutableList<string> ?? Constraints.ToImmutableList(),
            OutputFormat, Examples as ImmutableList<Example> ?? Examples.ToImmutableList());
    }

    private static string BuildRawText(
        string? system,
        string? template,
        IReadOnlyList<string> constraints,
        OutputSpec? outputFormat,
        IReadOnlyList<Example> examples)
    {
        var parts = new List<string>();

        if (system is not null)
            parts.Add(system);

        if (constraints.Count > 0)
            parts.Add(string.Join(" ", constraints.Select(c => c.TrimEnd('.') + ".")));

        if (outputFormat is not null)
            parts.Add(outputFormat.ToInstructionText());

        foreach (var example in examples)
            parts.Add(example.ToText());

        if (template is not null)
            parts.Add(template);

        return string.Join("\n\n", parts);
    }

    private static ImmutableList<string> ExtractVariables(string text)
    {
        return VariablePattern.Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableList();
    }

    private static Dictionary<string, string> ObjectToDictionary(object obj)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (obj is IDictionary<string, string> stringDict)
        {
            foreach (var (key, value) in stringDict)
                dict[key] = value;
            return dict;
        }

        if (obj is IDictionary<string, object?> objDict)
        {
            foreach (var (key, value) in objDict)
                dict[key] = value?.ToString() ?? string.Empty;
            return dict;
        }

        foreach (var prop in obj.GetType().GetProperties())
        {
            if (prop.CanRead)
                dict[prop.Name] = prop.GetValue(obj)?.ToString() ?? string.Empty;
        }

        return dict;
    }

    private PromptBuilder ToBuilder()
    {
        var builder = new PromptBuilder();
        if (System is not null) builder.System(System);
        if (Template is not null) builder.Instruct(Template);
        foreach (var c in Constraints) builder.Constrain(c);
        if (OutputFormat is not null) builder.OutputAs(OutputFormat);
        foreach (var e in Examples) builder.Example(e);
        foreach (var (key, value) in _defaults) builder.Default(key, value);
        foreach (var (key, value) in Meta) builder.Meta(key, value);
        return builder;
    }
}
