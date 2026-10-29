using System.Reflection;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Core;

/// <summary>Reads an entity's <c>[Lexical]</c> declaration once per type (the type-plane memoization of DATA-0105 §3).</summary>
public static class LexicalMetadata
{
    private static readonly ConditionalWeakTable<Type, Declaration> Cache = new();

    /// <summary>The entity's lexical declaration, or <see langword="null"/> when it declares no lexical property.</summary>
    public static LexicalSpec? Get(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Cache.GetValue(entityType, static type => new Declaration(Compile(type))).Spec;
    }

    /// <summary>
    /// Whether <paramref name="language"/> can name a store's text search configuration: lower-case letters and
    /// underscores only, so it is spelled into a statement as an identifier and never as text a store could misparse.
    /// </summary>
    public static bool IsLanguageName(string language) =>
        language.Length is > 0 and <= 32 && language.All(static character => character is >= 'a' and <= 'z' or '_');

    private static LexicalSpec? Compile(Type entityType)
    {
        var properties = new List<PropertyInfo>();
        string? language = null;
        foreach (var property in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var attribute = property.GetCustomAttribute<LexicalAttribute>(inherit: true);
            if (attribute is null) continue;
            if (property.PropertyType != typeof(string))
                throw new InvalidOperationException(
                    $"[Lexical] on {entityType.Name}.{property.Name} requires a string property; " +
                    $"{property.PropertyType.Name} cannot be tokenized.");
            if (property.GetMethod is null)
                throw new InvalidOperationException($"[Lexical] on {entityType.Name}.{property.Name} requires a readable property.");
            if (!string.IsNullOrWhiteSpace(attribute.Language))
            {
                var declared = attribute.Language.Trim().ToLowerInvariant();
                if (!IsLanguageName(declared))
                    throw new InvalidOperationException(
                        $"[Lexical(Language = \"{attribute.Language}\")] on {entityType.Name}.{property.Name} is not a text search " +
                        "configuration name: use lower-case letters and underscores, such as \"english\" or \"simple\".");
                if (language is not null && !string.Equals(language, declared, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"{entityType.Name} declares two lexical languages ('{language}' and '{declared}'); one entity tokenizes by one language.");
                language = declared;
            }
            properties.Add(property);
        }
        return properties.Count == 0 ? null : new LexicalSpec(properties, language ?? LexicalSpec.DefaultLanguage);
    }

    private sealed record Declaration(LexicalSpec? Spec);
}
