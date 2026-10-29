using Koan.Data.Abstractions;

namespace Koan.Data.Core.Mapping.Runtime;

internal static class MappingLexicalCompiler
{
    /// <summary>The prefix of a lexical index name, as <c>IX_</c> prefixes a declared index.</summary>
    internal const string NamePrefix = "LX_";

    public static MappingLexicalPlan? Compile(MappingPlan plan)
    {
        var spec = LexicalMetadata.Get(plan.EntityType);
        if (spec is null) return null;
        var bindings = spec.Properties
            .SelectMany(property => plan.Use(MappingPath.Of(property.Name), MappingConsumer.Index).Bindings)
            .ToArray();
        foreach (var binding in bindings)
            if (binding.PhysicalType != typeof(string))
                throw new InvalidOperationException(
                    $"[Lexical] on {plan.EntityType.Name}.{binding.LogicalPath.Leaf} is mapped to a {binding.PhysicalType.Name} value; a lexical index reads text.");
        return new MappingLexicalPlan(NamePrefix + plan.Container.Name, bindings, spec.Language, plan.Id);
    }
}
