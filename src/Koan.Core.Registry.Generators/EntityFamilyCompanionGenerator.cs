using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Koan.Core.Registry.Generators;

/// <summary>
/// Emits the compile-time Entity-family bridge for a non-generic root:
/// <c>Media&lt;TVariant&gt; : Media</c>. The bridge keeps set-wide statics rooted at Media while giving
/// point reads an exact variant return type.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityFamilyCompanionGenerator : IIncrementalGenerator
{
    private const string StringEntityDefinitionMetadataName = "Koan.Data.Core.Model.Entity`1";
    private const string KeyedEntityDefinitionMetadataName = "Koan.Data.Core.Model.Entity`2";
    private const string FamilyMarkerMetadataName = "Koan.Data.Abstractions.IEntityFamilyVariant`3";
    private const string EntityContractMetadataName = "Koan.Data.Abstractions.IEntity";
    private const string RegistryMetadataName = "Koan.Core.Hosting.Registry.KoanRegistry";
    private const string EntityTypeCatalogMetadataName = "Koan.Data.Core.Polymorphism.EntityTypeCatalog";

    private static readonly DiagnosticDescriptor MalformedSelfClosure = new(
        id: "KOAN0004",
        title: "Entity family variants close over themselves",
        messageFormat: "Entity family variant '{0}' must inherit '{1}<{0}>', not '{1}<{2}>'",
        category: "Koan.Data",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A generated Entity-family companion must be closed with the concrete variant that inherits it.");

    private static readonly DiagnosticDescriptor InaccessibleVariant = new(
        id: "KOAN0005",
        title: "Entity family variants must be discoverable",
        messageFormat: "Entity family variant '{0}' is not accessible to generated discovery code; make it and every containing type public or internal",
        category: "Koan.Data",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generated family variants must be referenceable from the assembly-level discovery manifest for trimming and NativeAOT.");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classSymbols = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (syntaxContext, cancellationToken) =>
                    ResolveClassSymbol(syntaxContext, cancellationToken))
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => (INamedTypeSymbol)symbol!);

        var input = context.CompilationProvider.Combine(classSymbols.Collect());
        context.RegisterSourceOutput(input, static (productionContext, pair) =>
        {
            var model = FamilyModel.Create(pair.Left, pair.Right);

            foreach (var diagnostic in model.Diagnostics)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    diagnostic.Descriptor,
                    diagnostic.Location,
                    diagnostic.Arguments));
            }

            foreach (var root in model.Roots)
            {
                productionContext.AddSource(
                    CompanionEmitter.HintName(root),
                    CompanionEmitter.Emit(root));
            }

            if (!model.Variants.IsDefaultOrEmpty)
            {
                productionContext.AddSource(
                    CompanionEmitter.RegistryHintName(pair.Left.AssemblyName),
                    CompanionEmitter.EmitRegistry(pair.Left.AssemblyName, model.Variants));
            }
        });
    }

    private static INamedTypeSymbol? ResolveClassSymbol(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
        => context.Node is ClassDeclarationSyntax declaration
            ? context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken)
            : null;

    private readonly record struct FamilyRoot(
        INamedTypeSymbol Symbol,
        ITypeSymbol KeyType,
        string QualifiedName,
        string Namespace,
        string DeclarationName,
        string Accessibility);

    private readonly record struct FamilyVariant(string QualifiedName);

    private readonly record struct FamilyDiagnostic(
        DiagnosticDescriptor Descriptor,
        Location Location,
        object[] Arguments);

    private readonly record struct FamilyModel(
        ImmutableArray<FamilyRoot> Roots,
        ImmutableArray<FamilyVariant> Variants,
        ImmutableArray<FamilyDiagnostic> Diagnostics)
    {
        public static FamilyModel Create(
            Compilation compilation,
            ImmutableArray<INamedTypeSymbol> candidates)
        {
            var stringEntityDefinition = compilation.GetTypeByMetadataName(StringEntityDefinitionMetadataName);
            var keyedEntityDefinition = compilation.GetTypeByMetadataName(KeyedEntityDefinitionMetadataName);
            var familyMarker = compilation.GetTypeByMetadataName(FamilyMarkerMetadataName);
            var entityContract = compilation.GetTypeByMetadataName(EntityContractMetadataName);
            var registry = compilation.GetTypeByMetadataName(RegistryMetadataName);
            var entityTypeCatalog = compilation.GetTypeByMetadataName(EntityTypeCatalogMetadataName);
            if (familyMarker is null)
            {
                return new FamilyModel([], [], []);
            }

            var symbols = candidates
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .ToImmutableArray();
            var stringType = compilation.GetSpecialType(SpecialType.System_String);
            var roots = symbols
                .Select(symbol => TryCreateRoot(
                    symbol,
                    stringEntityDefinition,
                    keyedEntityDefinition,
                    stringType))
                .Where(static root => root.HasValue)
                .Select(static root => root!.Value)
                .Where(static root => !HasGenericCompanionCollision(root.Symbol))
                .OrderBy(static root => root.QualifiedName, StringComparer.Ordinal)
                .ToImmutableArray();

            var variants = ImmutableArray.CreateBuilder<FamilyVariant>();
            var diagnostics = ImmutableArray.CreateBuilder<FamilyDiagnostic>();
            foreach (var symbol in symbols)
            {
                InspectSemanticFamilyMarker(
                    symbol,
                    familyMarker,
                    variants,
                    diagnostics);
            }

            // A companion emitted in this same generator pass is not yet part of the semantic model. Inspect
            // those unresolved base clauses syntactically; imported companions were handled above through
            // their inherited marker contract.
            if (!roots.IsDefaultOrEmpty)
            {
                foreach (var symbol in symbols)
                {
                    InspectLocalFamilyClosure(
                        compilation,
                        symbol,
                        roots,
                        variants,
                        diagnostics);
                }
            }

            // The ordinary registry generator may see an error base before this generator's companion source is
            // added. Emit both discovery and direct catalog registration from the same compile-time fact so
            // NativeAOT and assemblies loaded after the catalog's initial fold never need reflection.
            if (entityContract is null || registry is null || entityTypeCatalog is null)
            {
                variants.Clear();
            }

            return new FamilyModel(
                roots,
                variants
                    .Distinct()
                    .OrderBy(static variant => variant.QualifiedName, StringComparer.Ordinal)
                    .ToImmutableArray(),
                diagnostics.ToImmutable());
        }
    }

    private static void InspectSemanticFamilyMarker(
        INamedTypeSymbol variant,
        INamedTypeSymbol familyMarker,
        ImmutableArray<FamilyVariant>.Builder variants,
        ImmutableArray<FamilyDiagnostic>.Builder diagnostics)
    {
        if (variant.TypeKind != TypeKind.Class
            || variant.IsStatic
            || variant.IsGenericType)
        {
            return;
        }

        var familyContracts = variant.AllInterfaces
            .Where(contract =>
                contract.IsGenericType
                && SymbolEqualityComparer.Default.Equals(
                    contract.OriginalDefinition,
                    familyMarker))
            .ToArray();
        if (familyContracts.Length == 0)
        {
            return;
        }

        var closesOverSelf = false;
        foreach (var contract in familyContracts)
        {
            var arguments = contract.TypeArguments;
            if (arguments.Length != 3)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(arguments[1], variant))
            {
                closesOverSelf = true;
                continue;
            }

            diagnostics.Add(new FamilyDiagnostic(
                MalformedSelfClosure,
                SourceLocation(variant),
                [
                    variant.ToDisplayString(),
                    arguments[0].ToDisplayString(),
                    arguments[1].ToDisplayString()
                ]));
        }

        if (!closesOverSelf || variant.IsAbstract)
        {
            return;
        }

        if (!IsAccessibleFromGeneratedCode(variant))
        {
            diagnostics.Add(new FamilyDiagnostic(
                InaccessibleVariant,
                SourceLocation(variant),
                [variant.ToDisplayString()]));
            return;
        }

        variants.Add(new FamilyVariant(
            variant.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private static FamilyRoot? TryCreateRoot(
        INamedTypeSymbol symbol,
        INamedTypeSymbol? stringEntityDefinition,
        INamedTypeSymbol? keyedEntityDefinition,
        ITypeSymbol stringType)
    {
        if (symbol.TypeKind != TypeKind.Class
            || symbol.IsRecord
            || symbol.IsStatic
            || symbol.IsSealed
            || symbol.Arity != 0
            || symbol.ContainingType is not null
            || !IsAccessibleFromGeneratedCode(symbol)
            || !HasAccessibleDefaultInvocationConstructor(symbol))
        {
            return null;
        }

        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType)
            {
                continue;
            }

            var arguments = current.TypeArguments;
            if (stringEntityDefinition is not null
                && SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    stringEntityDefinition))
            {
                return arguments.Length == 1
                    && SymbolEqualityComparer.Default.Equals(arguments[0], symbol)
                        ? CreateRoot(symbol, stringType)
                        : null;
            }

            if (keyedEntityDefinition is not null
                && SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    keyedEntityDefinition))
            {
                return arguments.Length == 2
                    && SymbolEqualityComparer.Default.Equals(arguments[0], symbol)
                        ? CreateRoot(symbol, arguments[1])
                        : null;
            }
        }

        return null;
    }

    private static FamilyRoot CreateRoot(INamedTypeSymbol symbol, ITypeSymbol keyType)
        => new(
            symbol,
            keyType,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            NamespaceName(symbol.ContainingNamespace),
            EscapeIdentifier(symbol.Name),
            symbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal");

    private static bool HasAccessibleDefaultInvocationConstructor(INamedTypeSymbol symbol)
        => symbol.InstanceConstructors.Any(static constructor =>
            constructor.DeclaredAccessibility != Accessibility.Private
            && constructor.Parameters.All(static parameter =>
                parameter.IsOptional || parameter.IsParams));

    private static bool HasGenericCompanionCollision(INamedTypeSymbol root)
        => root.ContainingNamespace
            .GetTypeMembers(root.Name, arity: 1)
            // An unresolved `Anime : Media<Anime>` contributes an error symbol with arity one before this
            // generator's output is added. It is the request for the companion, not a real declaration collision.
            .Any(static candidate =>
                candidate.TypeKind != TypeKind.Error
                && (candidate.DeclaringSyntaxReferences.Length > 0
                    || candidate.Locations.Any(static location => location.IsInMetadata)));

    private static void InspectLocalFamilyClosure(
        Compilation compilation,
        INamedTypeSymbol variant,
        ImmutableArray<FamilyRoot> roots,
        ImmutableArray<FamilyVariant>.Builder variants,
        ImmutableArray<FamilyDiagnostic>.Builder diagnostics)
    {
        if (variant.Arity != 0)
        {
            return;
        }

        foreach (var syntaxReference in variant.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not ClassDeclarationSyntax declaration
                || declaration.BaseList is null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var baseType in declaration.BaseList.Types)
            {
                var genericName = RightmostGenericName(baseType.Type);
                if (genericName is null || genericName.TypeArgumentList.Arguments.Count != 1)
                {
                    continue;
                }

                var root = ResolveRoot(
                    semanticModel,
                    genericName,
                    variant,
                    roots);
                if (root is null)
                {
                    continue;
                }

                var argumentSyntax = genericName.TypeArgumentList.Arguments[0];
                var argumentType = semanticModel.GetTypeInfo(argumentSyntax).Type as INamedTypeSymbol;
                var closesOverSelf = argumentType is not null
                    && SymbolEqualityComparer.Default.Equals(
                        argumentType.OriginalDefinition,
                        variant.OriginalDefinition);
                if (!closesOverSelf)
                {
                    diagnostics.Add(new FamilyDiagnostic(
                        MalformedSelfClosure,
                        argumentSyntax.GetLocation(),
                        [
                            variant.ToDisplayString(),
                            root.Value.Symbol.ToDisplayString(),
                            argumentType?.ToDisplayString() ?? argumentSyntax.ToString()
                        ]));
                    continue;
                }

                if (variant.IsAbstract)
                {
                    continue;
                }

                if (!IsAccessibleFromGeneratedCode(variant))
                {
                    diagnostics.Add(new FamilyDiagnostic(
                        InaccessibleVariant,
                        declaration.Identifier.GetLocation(),
                        [variant.ToDisplayString()]));
                    continue;
                }

                variants.Add(new FamilyVariant(
                    variant.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }
    }

    private static Location SourceLocation(INamedTypeSymbol symbol)
        => symbol.Locations.FirstOrDefault(static location => location.IsInSource)
            ?? Location.None;

    private static FamilyRoot? ResolveRoot(
        SemanticModel semanticModel,
        GenericNameSyntax genericName,
        INamedTypeSymbol variant,
        ImmutableArray<FamilyRoot> roots)
    {
        var name = genericName.Identifier.ValueText;
        var boundCandidates = new List<INamedTypeSymbol>();
        var symbolInfo = semanticModel.GetSymbolInfo(genericName);
        if (symbolInfo.Symbol is INamedTypeSymbol symbol)
        {
            boundCandidates.Add(symbol);
        }
        boundCandidates.AddRange(symbolInfo.CandidateSymbols.OfType<INamedTypeSymbol>());
        if (semanticModel.GetTypeInfo(genericName).Type is INamedTypeSymbol typeInfo)
        {
            boundCandidates.Add(typeInfo);
        }

        foreach (var candidate in boundCandidates)
        {
            var match = roots.FirstOrDefault(root =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    root.Symbol));
            if (match.Symbol is not null)
            {
                return match;
            }
        }

        // A successfully bound imported generic with the same simple name is not a request for one of this
        // compilation's generated companions. Only fall back to lexical lookup while the base itself is an
        // unresolved error symbol.
        if (boundCandidates.Any(static candidate => candidate.TypeKind != TypeKind.Error))
        {
            return null;
        }

        foreach (var candidate in semanticModel
                     .LookupNamespacesAndTypes(genericName.SpanStart, name: name)
                     .OfType<INamedTypeSymbol>())
        {
            var match = roots.FirstOrDefault(root =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    root.Symbol));
            if (match.Symbol is not null)
            {
                return match;
            }
        }

        var sameNamespace = roots
            .Where(root =>
                string.Equals(root.Symbol.Name, name, StringComparison.Ordinal)
                && SymbolEqualityComparer.Default.Equals(
                    root.Symbol.ContainingNamespace,
                    variant.ContainingNamespace))
            .ToArray();
        if (sameNamespace.Length == 1)
        {
            return sameNamespace[0];
        }

        var byUniqueName = roots
            .Where(root => string.Equals(root.Symbol.Name, name, StringComparison.Ordinal))
            .ToArray();
        return byUniqueName.Length == 1 ? byUniqueName[0] : null;
    }

    private static GenericNameSyntax? RightmostGenericName(TypeSyntax syntax)
        => syntax switch
        {
            GenericNameSyntax generic => generic,
            QualifiedNameSyntax { Right: GenericNameSyntax generic } => generic,
            AliasQualifiedNameSyntax { Name: GenericNameSyntax generic } => generic,
            _ => null
        };

    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal
                || current.DeclaredAccessibility is not (
                    Accessibility.Public
                    or Accessibility.Internal
                    or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    private static string EscapeIdentifier(string value)
        => SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None
            ? "@" + value
            : value;

    private static string NamespaceName(INamespaceSymbol symbol)
    {
        if (symbol.IsGlobalNamespace)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        for (var current = symbol; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            parts.Push(EscapeIdentifier(current.Name));
        }

        return string.Join(".", parts);
    }

    private static class CompanionEmitter
    {
        public static string HintName(FamilyRoot root)
            => $"KoanEntityFamily_{Sanitize(root.QualifiedName)}_{StableHash(root.QualifiedName):x8}.g.cs";

        public static string RegistryHintName(string? assemblyName)
            => $"KoanEntityFamilyRegistry_{Sanitize(assemblyName)}.g.cs";

        public static string Emit(FamilyRoot root)
        {
            var keyType = root.KeyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            if (root.Namespace.Length > 0)
            {
                sb.Append("namespace ").Append(root.Namespace).AppendLine(";");
                sb.AppendLine();
            }

            sb.Append(root.Accessibility)
                .Append(" abstract class ")
                .Append(root.DeclarationName)
                .Append("<TVariant> : ")
                .Append(root.QualifiedName)
                .AppendLine(",");
            sb.Append("    global::Koan.Data.Abstractions.IEntityFamilyVariant<")
                .Append(root.QualifiedName)
                .Append(", TVariant, ")
                .Append(keyType)
                .AppendLine(">");
            sb.Append("    where TVariant : ")
                .Append(root.DeclarationName)
                .AppendLine("<TVariant>");
            sb.AppendLine("{");
            EmitSingleGet(sb, keyType, partitioned: false);
            sb.AppendLine();
            EmitManyGet(sb, keyType, partitioned: false);
            sb.AppendLine();
            EmitSingleGet(sb, keyType, partitioned: true);
            sb.AppendLine();
            EmitManyGet(sb, keyType, partitioned: true);
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string EmitRegistry(
            string? assemblyName,
            ImmutableArray<FamilyVariant> variants)
        {
            var suffix = Sanitize(assemblyName);
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace Koan.Core.Hosting.Registry;");
            sb.AppendLine();
            sb.Append("file static class KoanEntityFamilyRegistry_")
                .Append(suffix)
                .AppendLine();
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.Append("    internal static void RegisterEntityFamilies_")
                .Append(suffix)
                .AppendLine("()");
            sb.AppendLine("    {");
            foreach (var variant in variants)
            {
                sb.Append("        global::Koan.Data.Core.Polymorphism.EntityTypeCatalog.Register(typeof(")
                    .Append(variant.QualifiedName)
                    .AppendLine("));");
            }
            sb.AppendLine();
            sb.AppendLine("        global::Koan.Core.Hosting.Registry.KoanRegistry.RegisterDiscoveredImplementors(");
            sb.AppendLine("            typeof(global::Koan.Data.Abstractions.IEntity),");
            sb.AppendLine("            new global::System.Type[]");
            sb.AppendLine("            {");
            foreach (var variant in variants)
            {
                sb.Append("                typeof(")
                    .Append(variant.QualifiedName)
                    .AppendLine("),");
            }
            sb.AppendLine("            });");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void EmitSingleGet(
            StringBuilder sb,
            string keyType,
            bool partitioned)
        {
            sb.Append("    public new static global::System.Threading.Tasks.Task<TVariant?> Get(")
                .Append(keyType)
                .Append(" id");
            if (partitioned)
            {
                sb.Append(", string partition");
            }
            sb.AppendLine(", global::System.Threading.CancellationToken ct = default)");
            sb.Append("        => global::Koan.Data.Core.Model.Entity<TVariant, ")
                .Append(keyType)
                .Append(">.Get(id");
            if (partitioned)
            {
                sb.Append(", partition");
            }
            sb.AppendLine(", ct);");
        }

        private static void EmitManyGet(
            StringBuilder sb,
            string keyType,
            bool partitioned)
        {
            sb.Append("    public new static global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<TVariant?>> Get(")
                .Append("global::System.Collections.Generic.IEnumerable<")
                .Append(keyType)
                .Append("> ids");
            if (partitioned)
            {
                sb.Append(", string partition");
            }
            sb.AppendLine(", global::System.Threading.CancellationToken ct = default)");
            sb.Append("        => global::Koan.Data.Core.Model.Entity<TVariant, ")
                .Append(keyType)
                .Append(">.Get(ids");
            if (partitioned)
            {
                sb.Append(", partition");
            }
            sb.AppendLine(", ct);");
        }

        private static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Assembly";
            }

            var sb = new StringBuilder(value!.Length);
            foreach (var ch in value)
            {
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }
            if (sb.Length == 0)
            {
                return "Assembly";
            }
            if (char.IsDigit(sb[0]))
            {
                sb.Insert(0, '_');
            }
            return sb.ToString();
        }

        private static uint StableHash(string value)
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= prime;
            }
            return hash;
        }
    }
}
