using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Cache.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Koan.Tests.Cache.Analyzers.Specs;

/// <summary>
/// Verifies <see cref="CacheRegistrationAnalyzer"/> (KOAN0001) fires on the indistinguishable-
/// descriptor pattern and stays silent on the correct two-generic shape. Per ARCH-0081.
/// </summary>
/// <remarks>
/// Drives the analyzer directly via <c>Microsoft.CodeAnalysis</c> instead of the obsolete
/// <c>XUnitVerifier</c> testing helpers — those break against xunit 2.9.x because they expect
/// older <c>Xunit.Sdk.EqualException</c> constructor signatures. The direct approach has fewer
/// moving parts and works with any test framework.
/// </remarks>
public sealed class CacheRegistrationAnalyzerSpec
{
    [Fact]
    public async Task Bare_TryAddEnumerable_with_ICacheStore_fires_KOAN0001()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Koan.Cache.Abstractions.Stores;
            using Koan.Cache.Abstractions.Primitives;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            internal sealed class BadStore : ICacheStore
            {
                public string Name => "bad";
                public CacheStorePlacement Placement => default;
                public CacheStoreCapabilities Capabilities => default;
                public ValueTask<CacheFetchResult> Fetch(CacheKey k, CacheReadOptions o, CancellationToken ct) => default;
                public ValueTask Set(CacheKey k, CacheValue v, CacheWriteOptions o, CancellationToken ct) => default;
                public ValueTask<bool> Remove(CacheKey k, CancellationToken ct) => default;
                public ValueTask<bool> Exists(CacheKey k, CancellationToken ct) => default;
                public ValueTask Touch(CacheKey k, TimeSpan? ttl, CancellationToken ct) => default;
                public IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, CancellationToken ct) => null!;
            }

            public static class BadRegistration
            {
                public static void Register(IServiceCollection services)
                {
                    services.TryAddSingleton<BadStore>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore>(sp => sp.GetRequiredService<BadStore>()));
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics.Where(d => d.Id == CacheRegistrationAnalyzer.DiagnosticId)
            .Should().ContainSingle()
            .Which.GetMessage().Should().Contain("ICacheStore").And.Contain("AddCacheStore");
    }

    [Fact]
    public async Task Bare_TryAddEnumerable_with_ICacheCoherenceChannel_fires_KOAN0001()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Koan.Cache.Abstractions.Coherence;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            internal sealed class BadChannel : ICacheCoherenceChannel
            {
                public string TransportName => "bad";
                public CoherenceCapabilities Capabilities => default;
                public ValueTask Publish(CacheInvalidation m, CancellationToken ct) => default;
                public ValueTask Subscribe(Func<CacheInvalidation, CancellationToken, ValueTask> h, CancellationToken ct) => default;
                public ValueTask<string?> CatchUp(string? c, Func<CacheInvalidation, CancellationToken, ValueTask> h, CancellationToken ct) => default;
            }

            public static class BadRegistration
            {
                public static void Register(IServiceCollection services)
                {
                    services.TryAddSingleton<BadChannel>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheCoherenceChannel>(sp => sp.GetRequiredService<BadChannel>()));
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics.Where(d => d.Id == CacheRegistrationAnalyzer.DiagnosticId)
            .Should().ContainSingle()
            .Which.GetMessage().Should().Contain("ICacheCoherenceChannel").And.Contain("AddCoherenceChannel");
    }

    [Fact]
    public async Task Two_generic_form_does_not_fire()
    {
        // The CORRECT shape — exactly what the typed helpers emit internally. Must stay silent.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Koan.Cache.Abstractions.Stores;
            using Koan.Cache.Abstractions.Primitives;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            internal sealed class GoodStore : ICacheStore
            {
                public string Name => "good";
                public CacheStorePlacement Placement => default;
                public CacheStoreCapabilities Capabilities => default;
                public ValueTask<CacheFetchResult> Fetch(CacheKey k, CacheReadOptions o, CancellationToken ct) => default;
                public ValueTask Set(CacheKey k, CacheValue v, CacheWriteOptions o, CancellationToken ct) => default;
                public ValueTask<bool> Remove(CacheKey k, CancellationToken ct) => default;
                public ValueTask<bool> Exists(CacheKey k, CancellationToken ct) => default;
                public ValueTask Touch(CacheKey k, TimeSpan? ttl, CancellationToken ct) => default;
                public IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, CancellationToken ct) => null!;
            }

            public static class GoodRegistration
            {
                public static void Register(IServiceCollection services)
                {
                    services.TryAddSingleton<GoodStore>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore, GoodStore>(sp => sp.GetRequiredService<GoodStore>()));
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics.Should().NotContain(d => d.Id == CacheRegistrationAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task Singleton_instance_overload_does_not_fire()
    {
        // The single-generic INSTANCE overload Singleton<T>(T instance) is runtime-safe — the
        // descriptor's GetImplementationType() returns instance.GetType() (concrete), not the
        // service type. Only the factory overload produces the indistinguishable shape. The
        // analyzer must not over-fire on the instance form.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Koan.Cache.Abstractions.Stores;
            using Koan.Cache.Abstractions.Primitives;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            internal sealed class InstanceStore : ICacheStore
            {
                public string Name => "instance";
                public CacheStorePlacement Placement => default;
                public CacheStoreCapabilities Capabilities => default;
                public ValueTask<CacheFetchResult> Fetch(CacheKey k, CacheReadOptions o, CancellationToken ct) => default;
                public ValueTask Set(CacheKey k, CacheValue v, CacheWriteOptions o, CancellationToken ct) => default;
                public ValueTask<bool> Remove(CacheKey k, CancellationToken ct) => default;
                public ValueTask<bool> Exists(CacheKey k, CancellationToken ct) => default;
                public ValueTask Touch(CacheKey k, TimeSpan? ttl, CancellationToken ct) => default;
                public IAsyncEnumerable<TaggedCacheKey> EnumerateByTag(string tag, CancellationToken ct) => null!;
            }

            public static class InstanceRegistration
            {
                public static void Register(IServiceCollection services)
                {
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheStore>(new InstanceStore()));
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics.Should().NotContain(d => d.Id == CacheRegistrationAnalyzer.DiagnosticId);
    }

    [Fact]
    public async Task TryAddEnumerable_against_foreign_interface_does_not_fire()
    {
        // The analyzer only cares about framework-managed interfaces. Anything else compiles silently.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;

            public interface IFooService { }
            internal sealed class FooImpl : IFooService { }

            public static class ForeignRegistration
            {
                public static void Register(IServiceCollection services)
                {
                    services.TryAddSingleton<FooImpl>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IFooService>(sp => sp.GetRequiredService<FooImpl>()));
                }
            }
            """;

        var diagnostics = await RunAnalyzer(source);

        diagnostics.Should().NotContain(d => d.Id == CacheRegistrationAnalyzer.DiagnosticId);
    }

    /// <summary>
    /// Every FQN in <see cref="CacheRegistrationAnalyzer.KnownInterfaces"/> must resolve to a real
    /// type in <c>Koan.Cache.Abstractions</c>. Without this guard a silent typo would disable
    /// detection for that interface — every positive spec would still pass (because the spec uses
    /// the actual interface, which already happens to match the FQN). This is the only test that
    /// catches "the FQN in the analyzer doesn't match what the abstractions assembly exposes."
    /// </summary>
    [Fact]
    public void Every_KnownInterfaces_entry_resolves_to_a_real_type()
    {
        var abstractionsAssembly = typeof(Koan.Cache.Abstractions.Stores.ICacheStore).Assembly;

        foreach (var (fqn, helperName) in CacheRegistrationAnalyzer.KnownInterfaces)
        {
            var type = abstractionsAssembly.GetType(fqn, throwOnError: false);
            type.Should().NotBeNull($"FQN '{fqn}' (mapped to helper '{helperName}') in CacheRegistrationAnalyzer.KnownInterfaces must resolve to a real type in {abstractionsAssembly.GetName().Name}");
            type!.IsInterface.Should().BeTrue($"FQN '{fqn}' must point at an interface (the analyzer logic assumes interface types)");
        }
    }

    /// <summary>
    /// Parse the source, build a compilation with the necessary references, run
    /// <see cref="CacheRegistrationAnalyzer"/>, and return the analyzer's diagnostics.
    /// Compiler-side diagnostics (CS-prefixed) are filtered out — the analyzer's behavior is
    /// what we're asserting.
    /// </summary>
    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzer(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new[]
        {
            // BCL
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Func<>).Assembly.Location),
            // Cache contracts
            MetadataReference.CreateFromFile(typeof(Koan.Cache.Abstractions.Stores.ICacheStore).Assembly.Location),
            // Microsoft DI
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceDescriptor).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions).Assembly.Location),
            // netstandard / System.Runtime shims that some BCL types live in
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "netstandard.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
            // IServiceProvider lives in System.ComponentModel for .NET Core+ via type forwarding
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.ComponentModel.dll")),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Surface unexpected compilation errors so tests don't silently mask them.
        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (!compileErrors.IsEmpty)
        {
            throw new Xunit.Sdk.XunitException(
                "Test source failed to compile:\n" +
                string.Join("\n", compileErrors.Select(d => d.ToString())));
        }

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new CacheRegistrationAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
    }
}
