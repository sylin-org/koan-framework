using System.Runtime.CompilerServices;
using Koan.Cache.Abstractions;
using Koan.Communication;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.SoftDelete;
using Koan.Jobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Koan.Tests.EntityLanguage;

[Collection("Entity language consumer compilation")]
public sealed class EntityLanguageConsumerSpec
{
    [Fact]
    public void Base_entity_without_cache_module_rejects_the_cache_facet()
    {
        var result = Compile("CacheAccess", includeCache: false);

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Cache");
    }

    [Fact]
    public void Referencing_cache_adds_the_facet_with_only_the_normal_entity_namespace()
    {
        var result = Compile("CacheAccess", includeCache: true);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public void Invalid_receivers_do_not_gain_cache_or_runtime_delete_language()
    {
        var cache = Compile("InvalidCacheReceiver", includeCache: true);
        var delete = Compile("InvalidDeleteReceiver", includeCache: false);

        cache.Succeeded.Should().BeFalse();
        cache.Output.Should().Contain("Cache");
        delete.Succeeded.Should().BeFalse();
        delete.Output.Should().Contain("Delete");
    }

    [Fact]
    public void Current_entity_language_modules_do_not_collide_with_cache()
    {
        // This is the contracted C# 14 Entity language, not a census of every historical
        // extension method. New module-owned facets join this cell when they become public syntax.
        var result = Compile("AllModules", includeCache: true, includeAllModules: true);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public void Data_core_exposes_Lifecycle_directly_on_the_entity_type()
    {
        var result = Compile("LifecycleAccess", includeCache: false);
        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public void Persistence_Events_is_not_retained_as_a_second_canonical_name()
    {
        var result = Compile("LegacyEventsAccess", includeCache: false);
        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Events");
    }

    [Fact]
    public void Data_core_normalizes_each_entity_cardinality_without_a_flow_container()
    {
        var result = Compile("EntityCardinalityAccess", includeCache: false);
        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public void Cardinality_infrastructure_rejects_non_entity_receivers_at_compile_time()
    {
        var result = Compile("InvalidEntityCardinalityAccess", includeCache: false);
        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("IEntity");
    }

    [Fact]
    public void Removing_cache_makes_the_same_consumer_source_fail_at_the_facet()
    {
        var present = Compile("CacheAccess", includeCache: true);
        var removed = Compile("CacheAccess", includeCache: false);

        present.Succeeded.Should().BeTrue(present.Output);
        removed.Succeeded.Should().BeFalse();
        removed.Output.Should().Contain("Cache");
    }

    [Fact]
    public void Referencing_communication_adds_scalar_set_and_stream_transport_with_typed_receivers()
    {
        var result = Compile("TransportAccess", includeCache: false, includeCommunication: true);

        result.Succeeded.Should().BeTrue(result.Output);
    }

    [Fact]
    public void Removing_communication_removes_the_transport_language_from_the_same_source()
    {
        var result = Compile("TransportAccess", includeCache: false, includeCommunication: false);

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Communication");
    }

    [Fact]
    public void Transport_rejects_non_entity_receivers_at_compile_time()
    {
        var result = Compile("InvalidTransportReceiver", includeCache: false, includeCommunication: true);

        result.Succeeded.Should().BeFalse();
        result.Output.Should().Contain("Transport");
    }

    private static CompilationResult Compile(
        string cell,
        bool includeCache,
        bool includeAllModules = false,
        bool includeCommunication = false)
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "tests", "Fixtures", "EntityLanguage");
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTrees = new[]
        {
            "Shared/GlobalUsings.cs",
            "Shared/Todo.cs",
            $"Cells/{cell}.cs"
        }.Select(path => CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(fixture, path)),
            parseOptions,
            path)).ToArray();

        var assemblyPaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("Koan.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddAssembly(typeof(IEntity<>), assemblyPaths);
        AddAssembly(typeof(AggregateExtensions), assemblyPaths);
        AddAssembly(typeof(AppHost), assemblyPaths);
        AddAssembly(typeof(CacheConstants), assemblyPaths);

        if (includeCache)
        {
            AddAssembly(typeof(global::Koan.Cache.Cache), assemblyPaths);
        }

        if (includeAllModules)
        {
            AddAssembly(typeof(JobAccessorExtensions), assemblyPaths);
            AddAssembly(typeof(SoftDeleteExtensions), assemblyPaths);
            AddAssembly(typeof(EntityTransportFacetExtensions), assemblyPaths);
        }

        if (includeCommunication)
        {
            AddAssembly(typeof(EntityTransportFacetExtensions), assemblyPaths);
        }

        var compilation = CSharpCompilation.Create(
            $"Koan.EntityLanguage.Consumer.{cell}",
            syntaxTrees,
            assemblyPaths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        return new CompilationResult(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
    }

    private static void AddAssembly(Type type, ISet<string> paths)
    {
        var location = type.Assembly.Location;
        if (!string.IsNullOrWhiteSpace(location))
        {
            paths.Add(location);
        }
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile)! })
        {
            for (var directory = new DirectoryInfo(start);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Koan.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Koan repository root.");
    }

    private sealed record CompilationResult(bool Succeeded, string Output);
}

[CollectionDefinition("Entity language consumer compilation", DisableParallelization = true)]
public sealed class EntityLanguageConsumerCollection;
