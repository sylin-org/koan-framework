using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions;

namespace Koan.Data.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKoanDataCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Compile and activate referenced Koan modules (adapters, etc.).
        AppBootstrapper.InitializeModules(services);
        RegisterKoanDataCoreServices(services);
        return services;
    }

    internal static void RegisterKoanDataCoreServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(d => d.ServiceType == typeof(IDataService)))
        {
            return;
        }

        services.TryAddSingleton<Configuration.IDataConnectionResolver, Configuration.DefaultDataConnectionResolver>();
        // Provide a default storage name resolver so naming works even without adapter-specific registration (e.g., JSON adapter)
        services.TryAddSingleton<Koan.Data.Abstractions.Naming.IStorageNameResolver, Koan.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        // Note: Partition context provided by EntityContext (static, no DI needed) - see DATA-0077
        services.AddKoanOptions<Options.DirectOptions>(Infrastructure.Constants.Configuration.Direct.Section);
        // Vector defaults now live in Koan.Data.Vector; apps should call AddKoanDataVector() to enable vector features.
        services.AddKoanOptions<DataRuntimeOptions>();
        services.AddSingleton<IAggregateIdentityManager, AggregateIdentityManager>();
        services.TryAddSingleton(Koan.Core.Semantics.Segmentation.SegmentationPlan.Empty);

        // The data core is tenancy-agnostic: it exposes the generic Pipeline.IStorageGuard seam (DATA-0105 §0).
        // Tenancy registers its gate from the Koan.Tenancy module's auto-registrar (Reference = Intent); no
        // registered guard → the chokepoint loop is empty → no-op. A grep for "tenant" here returns nothing.

        // DATA-0106: the read-filter is uniformly contributor-driven. The data core ships ONE built-in default
        // contributor — the equality fold (the re-homed ManagedReadFilter) — so tenancy's equality read-filter is a
        // registered contributor too (golden, byte-identical). A predicate axis (moderation) adds its own
        // Pipeline.IReadFilterContributor; the facade AND-folds the union. Absent every axis ⇒ the registry is empty
        // ⇒ this contributor returns null ⇒ the read fold is a no-op (structural absence).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Pipeline.IReadFilterContributor, Pipeline.ManagedEqualityReadContributor>());
        services.TryAddSingleton<Pipeline.StorageFieldTransformPlan>();
        services.TryAddSingleton<Koan.Data.Abstractions.Pipeline.IFieldTransformInspector>(sp =>
            sp.GetRequiredService<Pipeline.StorageFieldTransformPlan>());

        // ARCH-0101 §7: the [DataAxis] premium authoring layer. Discover every IDataAxis, declare it, and EXPAND it to
        // the exact raw Phase A/B/C seams (managed field / read-filter / name particle / operation override)
        // — byte-identical to a hand-written registration. Runs after the built-in equality contributor and adds
        // DI-enumerable read contributors before the IDataService add; discovery is
        // already populated (manifest loader runs before this Register). No axis ⇒ no-op (off = structurally absent).
        // Runs once per ServiceCollection (the IDataService guard above), so its DI registrations never double-add.
        Axes.DataAxisExpander.ExpandDiscovered(services);

        // Data source registry for source/adapter routing (DATA-0077)
        services.AddSingleton<DataSourceRegistry>(sp =>
        {
            var registry = new DataSourceRegistry();
            var config = sp.GetRequiredService<IConfiguration>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<DataSourceRegistry>>();
            registry.DiscoverFromConfiguration(config, logger);
            return registry;
        });

        services.AddSingleton<Routing.DataProviderCatalog>(sp => new Routing.DataProviderCatalog(
            sp.GetServices<IDataAdapterFactory>(),
            sp.GetService<Koan.Core.Composition.KoanApplicationReferenceManifest>()));
        services.AddSingleton<Routing.DataDefaultProviderPlan>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            Koan.Core.Semantics.Segmentation.ISegmentationRealization,
            Semantics.DataSegmentationPlan>());
        services.TryAddSingleton(sp => sp
            .GetServices<Koan.Core.Semantics.Segmentation.ISegmentationRealization>()
            .OfType<Semantics.DataSegmentationPlan>()
            .Single());
        services.AddSingleton<IDataService, DataService>();
        // Direct data access (DATA-0053 / ARCH-0090 §1): folded in from the former Koan.Data.Direct package.
        // Registered by default so DataService.Direct(...) works out-of-box — no separate AddKoanDataDirect().
        services.AddSingleton<Direct.IDirectDataService, Direct.DirectDataService>();
        // EntitySchemaGuard + ISchemaHealthContributor were removed in DATA-0095 Phase 1c.1.
        // Adapters now implement IDataRepository.EnsureReady directly; the facade calls it.
        services.AddSingleton<DataDiagnostics>();
        services.AddSingleton<IDataDiagnostics>(sp => sp.GetRequiredService<DataDiagnostics>());
        // Decorate repositories registered as IDataRepository<,>
        services.TryDecorate(typeof(IDataRepository<,>), typeof(RepositoryFacade<,>));
        // Relationship metadata scanning (ParentAttribute, etc.)
        services.TryAddSingleton<Koan.Data.Core.Relationships.IRelationshipMetadata, Koan.Data.Core.Relationships.RelationshipMetadataService>();
        services.TryAddSingleton<Koan.Data.Core.Relationships.IRelationshipQueryExecutor, Koan.Data.Core.Relationships.RelationshipQueryExecutor>();
        services.TryAddSingleton<Koan.Data.Core.Relationships.RelationshipGraphLoader>();
        Koan.Data.Core.Model.EntityMetadataProvider.RelationshipMetadataAccessor = sp => sp.GetRequiredService<Koan.Data.Core.Relationships.IRelationshipMetadata>();

        // Auto-register transaction support (AI-0020) - "Reference = Intent" pattern
        Transactions.TransactionServiceCollectionExtensions.AddKoanTransactions(services);
    }

    /// <summary>
    /// Builds and starts a Koan generic host for a synchronous console process.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned host and must dispose it. The synchronous facade starts the
    /// same standard hosted-service lifecycle used by worker and web hosts; it does not block waiting
    /// for application shutdown.
    /// </remarks>
    /// <returns>The active standard host. Dispose it to stop hosted capabilities and release the ambient owner.</returns>
    public static IHost StartKoan(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var suppliedConfiguration = services.Any(d => d.ServiceType == typeof(IConfiguration));
        var builder = Host.CreateApplicationBuilder();
        foreach (var descriptor in services)
        {
            builder.Services.Add(descriptor);
        }

        // The standard host already owns appsettings, environment configuration, logging,
        // IHostEnvironment, IHostApplicationLifetime, and hosted-service shutdown. Preserve an
        // explicitly supplied IConfiguration as the last DI registration; otherwise let optional
        // Secrets extend the standard configuration pipeline before Koan composes.
        if (!suppliedConfiguration)
        {
            TryInvokeSecretsBootstrap("AddSecretsReferenceConfiguration", builder.Configuration, null);
        }

        if (!builder.Services.Any(d => d.ServiceType == typeof(Koan.Core.Hosting.Runtime.IAppRuntime)))
        {
            builder.Services.AddKoan();
        }

        var owner = new StartedKoanHost(builder.Build());

        try
        {
            // Complete the optional DI-backed configuration upgrade before IHost starts services.
            // Koan's host binder owns the inner provider during startup; after startup the facade
            // returned to the caller deliberately becomes the ambient owner.
            TryInvokeSecretsBootstrap("UpgradeSecretsConfiguration", owner);
            owner.StartOwnedHost();

            // Custom runtimes supplied by a caller may not have the standard hosted bridge. The
            // built-in runtime is idempotent, so the common host path remains one lifecycle.
            var rt = owner.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
            rt?.Discover();
            rt?.Start();
            owner.Own(Koan.Core.Hosting.App.AppHost.Attach(owner));
            return owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, "Koan.Secrets.Core.Configuration.SecretResolvingConfigurationExtensions", "Koan.Secrets.Core")]
    private static void TryInvokeSecretsBootstrap(string methodName, params object?[]? args)
    {
        try
        {
            var secretsType = Type.GetType("Koan.Secrets.Core.Configuration.SecretResolvingConfigurationExtensions, Koan.Secrets.Core", throwOnError: false, ignoreCase: false);
            var method = secretsType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            method?.Invoke(null, args ?? []);
        }
        catch { /* optional */ }
    }

    private sealed class StartedKoanHost(IHost host) : IHost, IServiceProvider, IAsyncDisposable
    {
        private IHost? _host = host;
        private IDisposable? _lease;

        public IServiceProvider Services => this;

        public void Own(IDisposable lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            Interlocked.Exchange(ref _lease, lease)?.Dispose();
        }

        public void StartOwnedHost()
        {
            var current = Volatile.Read(ref _host)
                ?? throw new ObjectDisposedException(nameof(StartedKoanHost));
            current.Start();
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            var current = Volatile.Read(ref _host)
                ?? throw new ObjectDisposedException(nameof(StartedKoanHost));
            return current.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            var current = Volatile.Read(ref _host)
                ?? throw new ObjectDisposedException(nameof(StartedKoanHost));
            return current.StopAsync(cancellationToken);
        }

        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            var current = Volatile.Read(ref _host)
                ?? throw new ObjectDisposedException(nameof(StartedKoanHost));
            return current.Services.GetService(serviceType);
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            var current = Interlocked.Exchange(ref _host, null);
            if (current is null) return;

            try
            {
                await current.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                if (current is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    current.Dispose();
                }
            }
        }
    }
}
