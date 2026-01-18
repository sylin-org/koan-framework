using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Koan.Core.Registry.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class RegistrySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax, ResolveSymbol)
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => (INamedTypeSymbol)symbol!);

        var compilationAndTypes = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndTypes, static (spc, pair) =>
        {
            var (compilation, candidates) = pair;
            if (candidates.IsDefaultOrEmpty) return;

            var model = RegistryModel.Create(compilation, candidates.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default).ToImmutableArray());
            if (!model.HasRegistryInfrastructure)
            {
                return;
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

    private readonly record struct RegistryModel(
        ImmutableArray<string> Initializers,
        ImmutableArray<string> AutoRegistrars,
        ImmutableArray<BackgroundServiceInfo> BackgroundServices,
        ImmutableArray<string> ServiceDiscoveryAdapters,
        ImmutableArray<string> EmbeddingEntities,
        bool HasEmbeddingRegistry,
        bool HasRegistryInfrastructure)
    {
        public bool HasEntries =>
            Initializers.Length > 0 ||
            AutoRegistrars.Length > 0 ||
            BackgroundServices.Length > 0 ||
            ServiceDiscoveryAdapters.Length > 0 ||
            EmbeddingEntities.Length > 0;

        internal static RegistryModel Create(Compilation compilation, ImmutableArray<INamedTypeSymbol> symbols)
        {
            var initializers = ImmutableArray.CreateBuilder<string>();
            var autoRegistrars = ImmutableArray.CreateBuilder<string>();
            var backgroundServices = ImmutableArray.CreateBuilder<BackgroundServiceInfo>();
            var serviceDiscoveryAdapters = ImmutableArray.CreateBuilder<string>();
            var embeddingEntities = ImmutableArray.CreateBuilder<string>();

            var initializerInterface = compilation.GetTypeByMetadataName("Koan.Core.IKoanInitializer");
            var autoRegistrarInterface = compilation.GetTypeByMetadataName("Koan.Core.IKoanAutoRegistrar");
            var backgroundServiceInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanBackgroundService");
            var pokableInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanPokableService");
            var periodicInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanPeriodicService");
            var startupInterface = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.IKoanStartupService");
            var healthContributorInterface = compilation.GetTypeByMetadataName("Koan.Core.IHealthContributor");
            var serviceDiscoveryInterface = compilation.GetTypeByMetadataName("Koan.Core.Orchestration.Abstractions.IServiceDiscoveryAdapter");
            var backgroundAttribute = compilation.GetTypeByMetadataName("Koan.Core.BackgroundServices.KoanBackgroundServiceAttribute");
            var embeddingAttribute = compilation.GetTypeByMetadataName("Koan.Data.AI.Attributes.EmbeddingAttribute");
            var embeddingRegistry = compilation.GetTypeByMetadataName("Koan.Data.AI.EmbeddingRegistry");
            var registryInfrastructure = compilation.GetTypeByMetadataName("Koan.Core.Hosting.Registry.KoanRegistry");

            if (registryInfrastructure is null)
            {
                return new RegistryModel(
                    ImmutableArray<string>.Empty,
                    ImmutableArray<string>.Empty,
                    ImmutableArray<BackgroundServiceInfo>.Empty,
                    ImmutableArray<string>.Empty,
                    ImmutableArray<string>.Empty,
                    embeddingRegistry is not null,
                    HasRegistryInfrastructure: false);
            }

            foreach (var symbol in symbols)
            {
                if (symbol is null) continue;
                if (symbol.IsAbstract || symbol.TypeKind != TypeKind.Class) continue;

                var displayName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (initializerInterface is not null && Implements(symbol, initializerInterface))
                {
                    initializers.Add(displayName);
                }

                if (autoRegistrarInterface is not null && Implements(symbol, autoRegistrarInterface))
                {
                    autoRegistrars.Add(displayName);
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
            }

            return new RegistryModel(
                initializers.ToImmutable(),
                autoRegistrars.ToImmutable(),
                backgroundServices.ToImmutable(),
                serviceDiscoveryAdapters.ToImmutable(),
                embeddingEntities.ToImmutable(),
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

            if (model.Initializers.Length > 0)
            {
                sb.AppendLine("        global::Koan.Core.Hosting.Registry.KoanRegistry.RegisterInitializers(new global::System.Type[]");
                sb.AppendLine("        {");
                foreach (var typeName in model.Initializers)
                {
                    sb.Append("            typeof(").Append(typeName).AppendLine("),");
                }
                sb.AppendLine("        });");
            }

            if (model.AutoRegistrars.Length > 0)
            {
                sb.AppendLine("        global::Koan.Core.Hosting.Registry.KoanRegistry.RegisterAutoRegistrars(new global::System.Type[]");
                sb.AppendLine("        {");
                foreach (var typeName in model.AutoRegistrars)
                {
                    sb.Append("            typeof(").Append(typeName).AppendLine("),");
                }
                sb.AppendLine("        });");
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
    }
}
