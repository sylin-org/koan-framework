using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Koan.Core.Registry.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class RegistrySourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidContributionOwner = new(
        id: "KOAN0002",
        title: "Semantic contributions require a KoanModule",
        messageFormat: "Type '{0}' implements IContributeTo<TTarget> but is not a concrete KoanModule",
        category: "Koan.Semantics",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Only retained modules in the compiled host constitution may contribute to semantic targets.");

    private static readonly DiagnosticDescriptor MultipleModules = new(
        id: "KOAN0003",
        title: "A Koan assembly has one activation module",
        messageFormat: "Assembly '{0}' declares {1} concrete KoanModule types ({2}). Keep one module as the assembly's activation and lifecycle owner.",
        category: "Koan.Semantics",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The assembly/package is Koan's activation and identity boundary, so it must have one concrete lifecycle owner.");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax, ResolveSymbol)
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => (INamedTypeSymbol)symbol!);

        var projectSettings = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => new ProjectSettings(
                provider.GlobalOptions.TryGetValue("build_property.PackageId", out var packageId)
                    && !string.IsNullOrWhiteSpace(packageId)
                        ? packageId.Trim()
                        : null,
                provider.GlobalOptions.TryGetValue("build_property.IsTestProject", out var isTestProject)
                    && string.Equals(isTestProject, "true", StringComparison.OrdinalIgnoreCase)));
        var compilationTypesAndIdentity = context.CompilationProvider
            .Combine(classDeclarations.Collect())
            .Combine(projectSettings);

        context.RegisterSourceOutput(compilationTypesAndIdentity, static (spc, pair) =>
        {
            var ((compilation, candidates), settings) = pair;
            if (candidates.IsDefaultOrEmpty) return;

            var identity = settings.Identity ?? compilation.AssemblyName ?? "Koan.Generated";
            var model = RegistryModel.Create(
                compilation,
                identity,
                emitSemanticModules: !settings.IsTestProject,
                candidates.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToImmutableArray());
            if (!model.HasRegistryInfrastructure)
            {
                return;
            }

            foreach (var invalidOwner in model.InvalidContributionOwners)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    InvalidContributionOwner,
                    invalidOwner.Location,
                    invalidOwner.TypeName));
            }
            if (model.SemanticModules.Length > 1)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    MultipleModules,
                    model.SemanticModules[1].Location,
                    compilation.AssemblyName ?? identity,
                    model.SemanticModules.Length,
                    string.Join(", ", model.SemanticModules.Select(static module => module.TypeName))));
            }
            if (!model.HasEntries) return;

            var assemblyName = compilation.AssemblyName ?? "Koan.Generated";
            var hintName = $"KoanRegistry_{RegistryEmitter.Sanitize(assemblyName)}.g.cs";
            var source = RegistryEmitter.Emit(assemblyName, model);
            spc.AddSource(hintName, source);
        });
    }

    private static INamedTypeSymbol? ResolveSymbol(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        return context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
    }

    private readonly record struct ProjectSettings(string? Identity, bool IsTestProject);

    private readonly record struct RegistryModel(
        ImmutableArray<SemanticModuleInfo> SemanticModules,
        ImmutableArray<BackgroundServiceInfo> BackgroundServices,
        ImmutableArray<string> ServiceDiscoveryAdapters,
        ImmutableArray<string> EmbeddingEntities,
        ImmutableArray<DiscoverableInfo> DiscoveredImplementors,
        ImmutableArray<InvalidContributionOwnerInfo> InvalidContributionOwners,
        bool HasEmbeddingRegistry,
        bool HasRegistryInfrastructure)
    {
        public bool HasEntries =>
            SemanticModules.Length > 0 ||
            BackgroundServices.Length > 0 ||
            ServiceDiscoveryAdapters.Length > 0 ||
            EmbeddingEntities.Length > 0 ||
            DiscoveredImplementors.Length > 0;

        internal static RegistryModel Create(
            Compilation compilation,
            string moduleIdentity,
            bool emitSemanticModules,
            ImmutableArray<INamedTypeSymbol> symbols)
        {
            var semanticModules = ImmutableArray.CreateBuilder<SemanticModuleInfo>();
            var backgroundServices = ImmutableArray.CreateBuilder<BackgroundServiceInfo>();
            var serviceDiscoveryAdapters = ImmutableArray.CreateBuilder<string>();
            var embeddingEntities = ImmutableArray.CreateBuilder<string>();
            var discoveredImplementors = ImmutableArray.CreateBuilder<DiscoverableInfo>();
            var invalidContributionOwners = ImmutableArray.CreateBuilder<InvalidContributionOwnerInfo>();

            var koanModuleType = compilation.GetTypeByMetadataName("Koan.Core.KoanModule");
            var contributionInterface = compilation.GetTypeByMetadataName("Koan.Core.Semantics.Contributions.IContributeTo`1");
            var backgroundServiceInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanBackgroundService");
            var pokableInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanPokableService");
            var periodicInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanPeriodicService");
            var startupInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanStartupService");
            var healthContributorInterface = compilation.GetTypeByMetadataName("Koan.Core.IHealthContributor");
            var serviceDiscoveryInterface = compilation.GetTypeByMetadataName("Koan.Core.Orchestration.Abstractions.IServiceDiscoveryAdapter");
            var discoverableAttribute = compilation.GetTypeByMetadataName("Koan.Core.KoanDiscoverableAttribute");
            var backgroundAttribute = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.KoanBackgroundServiceAttribute");
            var embeddingAttribute = compilation.GetTypeByMetadataName("Koan.Data.AI.Attributes.EmbeddingAttribute");
            var embeddingRegistry = compilation.GetTypeByMetadataName("Koan.Data.AI.EmbeddingRegistry");
            var registryInfrastructure = compilation.GetTypeByMetadataName("Koan.Core.Hosting.Registry.KoanRegistry");

            if (registryInfrastructure is null)
            {
                return new RegistryModel(
                    ImmutableArray<SemanticModuleInfo>.Empty,
                    ImmutableArray<BackgroundServiceInfo>.Empty,
                    ImmutableArray<string>.Empty,
                    ImmutableArray<string>.Empty,
                    ImmutableArray<DiscoverableInfo>.Empty,
                    ImmutableArray<InvalidContributionOwnerInfo>.Empty,
                    embeddingRegistry is not null,
                    HasRegistryInfrastructure: false);
            }

            foreach (var symbol in symbols)
            {
                if (symbol is null) continue;
                var displayName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var contributionTargets = GetContributionTargets(symbol, contributionInterface);
                var accessibleFromGeneratedCode = IsAccessibleFromGeneratedCode(symbol);
                var isConcreteModule = symbol.TypeKind == TypeKind.Class
                    && !symbol.IsAbstract
                    && !symbol.IsGenericType
                    && accessibleFromGeneratedCode
                    && koanModuleType is not null
                    && InheritsFrom(symbol, koanModuleType);
                var validContributionOwner = contributionTargets.IsDefaultOrEmpty
                    || isConcreteModule;
                if (!validContributionOwner)
                {
                    invalidContributionOwners.Add(new InvalidContributionOwnerInfo(
                        displayName,
                        symbol.Locations.FirstOrDefault() ?? Location.None));
                }

                if (symbol.IsAbstract
                    || symbol.IsGenericType
                    || symbol.TypeKind != TypeKind.Class
                    || !accessibleFromGeneratedCode)
                {
                    continue;
                }

                if (isConcreteModule && emitSemanticModules)
                {
                    semanticModules.Add(new SemanticModuleInfo(
                        moduleIdentity,
                        displayName,
                        validContributionOwner
                            ? contributionTargets
                            : ImmutableArray<string>.Empty,
                        symbol.Locations.FirstOrDefault() ?? Location.None));
                }

                if (backgroundServiceInterface is not null && Implements(symbol, backgroundServiceInterface))
                {
                    backgroundServices.Add(BuildBackgroundServiceInfo(symbol, displayName, backgroundAttribute, periodicInterface, startupInterface, pokableInterface, healthContributorInterface));
                }

                if (serviceDiscoveryInterface is not null && Implements(symbol, serviceDiscoveryInterface))
                {
                    serviceDiscoveryAdapters.Add(displayName);
                }

                if (embeddingAttribute is not null && FindAttribute(symbol, embeddingAttribute) is not null)
                {
                    embeddingEntities.Add(displayName);
                }

                if (discoverableAttribute is not null)
                {
                    foreach (var iface in symbol.AllInterfaces)
                    {
                        if (FindAttribute(iface, discoverableAttribute) is null) continue;
                        discoveredImplementors.Add(new DiscoverableInfo(
                            iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            displayName));
                    }
                }
            }

            return new RegistryModel(
                semanticModules.ToImmutable(),
                backgroundServices.ToImmutable(),
                serviceDiscoveryAdapters.ToImmutable(),
                embeddingEntities.ToImmutable(),
                discoveredImplementors.ToImmutable(),
                invalidContributionOwners.ToImmutable(),
                embeddingRegistry is not null,
                HasRegistryInfrastructure: true);
        }

        private static BackgroundServiceInfo BuildBackgroundServiceInfo(
            INamedTypeSymbol symbol,
            string displayName,
            INamedTypeSymbol? backgroundAttribute,
            INamedTypeSymbol? periodicInterface,
            INamedTypeSymbol? startupInterface,
            INamedTypeSymbol? pokableInterface,
            INamedTypeSymbol? healthContributorInterface)
        {
            var enabled = true;
            string? configurationSection = null;
            var lifetime = 0;
            var priority = 100;
            var runDev = true;
            var runProd = true;
            var runTest = false;

            if (backgroundAttribute is not null)
            {
                var attribute = FindAttribute(symbol, backgroundAttribute);
                if (attribute is not null)
                {
                    enabled = GetNamedArgument(attribute, "Enabled", true);
                    configurationSection = GetNamedArgument(attribute, "ConfigurationSection", (string?)null);
                    lifetime = GetNamedArgument(attribute, "Lifetime", lifetime);
                    priority = GetNamedArgument(attribute, "Priority", priority);
                    runDev = GetNamedArgument(attribute, "RunInDevelopment", runDev);
                    runProd = GetNamedArgument(attribute, "RunInProduction", runProd);
                    runTest = GetNamedArgument(attribute, "RunInTesting", runTest);
                }
            }

            var info = new BackgroundServiceInfo(
                displayName,
                enabled,
                configurationSection,
                lifetime,
                priority,
                runDev,
                runProd,
                runTest,
                periodicInterface is not null && Implements(symbol, periodicInterface),
                startupInterface is not null && Implements(symbol, startupInterface),
                pokableInterface is not null && Implements(symbol, pokableInterface),
                healthContributorInterface is not null && Implements(symbol, healthContributorInterface));

            return info;
        }

        private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, interfaceSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility is not Accessibility.Public
                    and not Accessibility.Internal)
                {
                    return false;
                }
            }

            return true;
        }

        private static ImmutableArray<string> GetContributionTargets(
            INamedTypeSymbol type,
            INamedTypeSymbol? contributionInterface)
        {
            if (contributionInterface is null) return ImmutableArray<string>.Empty;

            return type.AllInterfaces
                .Where(contract =>
                    contract.IsGenericType
                    && SymbolEqualityComparer.Default.Equals(
                        contract.OriginalDefinition,
                        contributionInterface))
                .Select(static contract =>
                    contract.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static target => target, StringComparer.Ordinal)
                .ToImmutableArray();
        }

        private static AttributeData? FindAttribute(INamedTypeSymbol symbol, INamedTypeSymbol attributeSymbol)
        {
            foreach (var attributeData in symbol.GetAttributes())
            {
                if (attributeData.AttributeClass is null) continue;
                if (InheritsFrom(attributeData.AttributeClass, attributeSymbol))
                {
                    return attributeData;
                }
            }

            return null;
        }

        private static bool InheritsFrom(INamedTypeSymbol candidate, INamedTypeSymbol attributeSymbol)
        {
            var current = candidate;
            while (current is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, attributeSymbol))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static T GetNamedArgument<T>(AttributeData attribute, string name, T fallback)
        {
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (!string.Equals(namedArgument.Key, name, StringComparison.Ordinal))
                {
                    continue;
                }

                var typedConstant = namedArgument.Value;

                if (typedConstant.Value is null)
                {
                    return fallback;
                }

                try
                {
                    if (typedConstant.Value is T exact)
                    {
                        return exact;
                    }

                    var targetType = typeof(T);
                    if (targetType.IsEnum && typedConstant.Value is int enumValue)
                    {
                        return (T)Enum.ToObject(targetType, enumValue);
                    }

                    if (targetType == typeof(bool) && typedConstant.Value is bool boolValue)
                    {
                        return (T)(object)boolValue;
                    }

                    if (targetType == typeof(string) && typedConstant.Value is string strValue)
                    {
                        return (T)(object)strValue;
                    }

                    if (targetType == typeof(int))
                    {
                        if (typedConstant.Value is int iv)
                        {
                            return (T)(object)iv;
                        }

                        if (typedConstant.Value is IConvertible convertible)
                        {
                            return (T)(object)convertible.ToInt32(CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch
                {
                    return fallback;
                }
            }

            return fallback;
        }

    }

    private readonly record struct SemanticModuleInfo(
        string Id,
        string TypeName,
        ImmutableArray<string> ContributionTargets,
        Location Location);

    private readonly record struct InvalidContributionOwnerInfo(string TypeName, Location Location);

    private readonly record struct DiscoverableInfo(string Contract, string Implementer);

    private readonly record struct BackgroundServiceInfo(
        string TypeName,
        bool Enabled,
        string? ConfigurationSection,
        int Lifetime,
        int Priority,
        bool RunInDevelopment,
        bool RunInProduction,
        bool RunInTesting,
        bool IsPeriodic,
        bool IsStartup,
        bool IsPokable,
        bool ImplementsHealthContributor);

    private static class RegistryEmitter
    {
        public static string Emit(string assemblyName, RegistryModel model)
        {
            var sb = new StringBuilder();
            var sanitizedAssemblyName = Sanitize(assemblyName);
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace Koan.Core.Hosting.Registry;");
            sb.AppendLine();
            sb.Append("file static class KoanRegistryModule_").Append(sanitizedAssemblyName).AppendLine();
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.Append("    internal static void RegisterAssembly_").Append(sanitizedAssemblyName).AppendLine("()");
            sb.AppendLine("    {");

            foreach (var module in model.SemanticModules)
            {
                sb.AppendLine("        global::Koan.Core.Hosting.Registry.KoanRegistry.RegisterSemanticModule(");
                sb.Append("            ").Append(ToLiteral(module.Id)).AppendLine(",");
                sb.Append("            typeof(").Append(module.TypeName).AppendLine("),");
                sb.Append("            static () => new ").Append(module.TypeName).AppendLine("(),");
                sb.Append("            ").Append(ToContributionBindings(module.TypeName, module.ContributionTargets)).AppendLine(");");
            }

            if (model.BackgroundServices.Length > 0)
            {
                sb.AppendLine("        global::Koan.Core.Hosting.Registry.KoanRegistry.RegisterBackgroundServices(new global::Koan.Core.Hosting.Registry.KoanRegistry.BackgroundServiceDescriptor[]");
                sb.AppendLine("        {");
                foreach (var service in model.BackgroundServices)
                {
                    sb.AppendLine("            new global::Koan.Core.Hosting.Registry.KoanRegistry.BackgroundServiceDescriptor(");
                    sb.Append("                ServiceType: typeof(").Append(service.TypeName).AppendLine("),");
                    sb.Append("                Enabled: ").Append(service.Enabled ? "true" : "false").AppendLine(",");
                    sb.Append("                ConfigurationSection: ").Append(ToLiteral(service.ConfigurationSection)).AppendLine(",");
                    sb.Append("                Lifetime: (global::Microsoft.Extensions.DependencyInjection.ServiceLifetime)").Append(service.Lifetime).AppendLine(",");
                    sb.Append("                Priority: ").Append(service.Priority).AppendLine(",");
                    sb.Append("                RunInDevelopment: ").Append(service.RunInDevelopment ? "true" : "false").AppendLine(",");
                    sb.Append("                RunInProduction: ").Append(service.RunInProduction ? "true" : "false").AppendLine(",");
                    sb.Append("                RunInTesting: ").Append(service.RunInTesting ? "true" : "false").AppendLine(",");
                    sb.Append("                IsPeriodic: ").Append(service.IsPeriodic ? "true" : "false").AppendLine(",");
                    sb.Append("                IsStartup: ").Append(service.IsStartup ? "true" : "false").AppendLine(",");
                    sb.Append("                IsPokable: ").Append(service.IsPokable ? "true" : "false").AppendLine(",");
                    sb.Append("                ImplementsHealthContributor: ").Append(service.ImplementsHealthContributor ? "true" : "false").AppendLine("),");
                }
                sb.AppendLine("        });");
            }

            if (model.ServiceDiscoveryAdapters.Length > 0)
            {
                sb.AppendLine("        global::Koan.Core.Hosting.Registry.KoanRegistry.RegisterServiceDiscoveryAdapters(new global::Koan.Core.Hosting.Registry.KoanRegistry.ServiceDiscoveryAdapterDescriptor[]");
                sb.AppendLine("        {");
                foreach (var typeName in model.ServiceDiscoveryAdapters)
                {
                    sb.Append("            new global::Koan.Core.Hosting.Registry.KoanRegistry.ServiceDiscoveryAdapterDescriptor(typeof(").Append(typeName).AppendLine(")),");
                }
                sb.AppendLine("        });");
            }

            if (model.DiscoveredImplementors.Length > 0)
            {
                foreach (var group in model.DiscoveredImplementors.GroupBy(static d => d.Contract, StringComparer.Ordinal))
                {
                    sb.Append("        global::Koan.Core.Hosting.Registry.KoanRegistry.RegisterDiscoveredImplementors(typeof(").Append(group.Key).AppendLine("), new global::System.Type[]");
                    sb.AppendLine("        {");
                    foreach (var info in group)
                    {
                        sb.Append("            typeof(").Append(info.Implementer).AppendLine("),");
                    }
                    sb.AppendLine("        });");
                }
            }

            if (model.EmbeddingEntities.Length > 0 && model.HasEmbeddingRegistry)
            {
                sb.AppendLine("        global::Koan.Data.AI.EmbeddingRegistry.RegisterTypes(new global::System.Type[]");
                sb.AppendLine("        {");
                foreach (var typeName in model.EmbeddingEntities)
                {
                    sb.Append("            typeof(").Append(typeName).AppendLine("),");
                }
                sb.AppendLine("        });");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        public static string Sanitize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Assembly";
            }

            var builder = new StringBuilder(value!.Length);
            foreach (var ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }

            var result = builder.ToString();
            if (result.Length == 0)
            {
                return "Assembly";
            }

            if (char.IsDigit(result[0]))
            {
                return "_" + result;
            }

            return result;
        }

        private static string ToLiteral(string? value) => value is null ? "null" : SymbolDisplay.FormatLiteral(value, true);

        private static string ToStringArray(ImmutableArray<string> values)
        {
            if (values.IsDefaultOrEmpty) return "global::System.Array.Empty<string>()";
            return "new string[] { " + string.Join(", ", values.Select(ToLiteral)) + " }";
        }

        private static string ToContributionBindings(
            string moduleType,
            ImmutableArray<string> targetTypes)
        {
            if (targetTypes.IsDefaultOrEmpty)
            {
                return "global::System.Array.Empty<global::Koan.Core.Semantics.Contributions.SemanticContributionBinding>()";
            }

            return "new global::Koan.Core.Semantics.Contributions.SemanticContributionBinding[] { "
                + string.Join(", ", targetTypes.Select(targetType =>
                    "new global::Koan.Core.Semantics.Contributions.SemanticContributionBinding("
                    + "typeof(" + targetType + "), "
                    + "static (module, target) => ((global::Koan.Core.Semantics.Contributions.IContributeTo<"
                    + targetType + ">)(" + moduleType + ")module).Contribute((" + targetType + ")target))"))
                + " }";
        }
    }
}
