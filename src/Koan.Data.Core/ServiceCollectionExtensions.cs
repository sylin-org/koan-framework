using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Initialize modules (adapters, etc.) that opt-in via IKoanInitializer
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

        // The data core is tenancy-agnostic: it exposes the generic Pipeline.IStorageGuard seam (DATA-0105 §0).
        // Tenancy registers its gate from the Koan.Tenancy module's auto-registrar (Reference = Intent); no
        // registered guard → the chokepoint loop is empty → no-op. A grep for "tenant" here returns nothing.

        // DATA-0106: the read-filter is uniformly contributor-driven. The data core ships ONE built-in default
        // contributor — the equality fold (the re-homed ManagedReadFilter) — so tenancy's equality read-filter is a
        // registered contributor too (golden, byte-identical). A predicate axis (moderation) adds its own
        // Pipeline.IReadFilterContributor; the facade AND-folds the union. Absent every axis ⇒ the registry is empty
        // ⇒ this contributor returns null ⇒ the read fold is a no-op (structural absence).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Pipeline.IReadFilterContributor, Pipeline.ManagedEqualityReadContributor>());

        // ARCH-0100: the durable ambient carrier. Aggregates the DI-enumerable IAmbientSliceCarrier set (each
        // cross-cutting module registers its own; none → an empty registry that captures null / restores nothing).
        // Koan.Jobs / Koan.Messaging resolve this to carry ambient slices across the async-hop, naming no axis.
        services.TryAddSingleton<AmbientCarrierRegistry>();

        // ARCH-0101 §7: the [DataAxis] premium authoring layer. Discover every IDataAxis, declare it, and EXPAND it to
        // the exact raw Phase A/B/C seams (managed field / read-filter / name particle / carrier / operation override)
        // — byte-identical to a hand-written registration. Runs after the built-in equality contributor + carrier
        // registry (it adds DI-enumerable read contributors + carriers) and before the IDataService add; discovery is
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

        services.AddSingleton<IDataService, DataService>();
        // Direct data access (DATA-0053 / ARCH-0090 §1): folded in from the former Koan.Data.Direct package.
        // Registered by default so DataService.Direct(...) works out-of-box — no separate AddKoanDataDirect().
        services.AddSingleton<Direct.IDirectDataService, Direct.DirectDataService>();
        // EntitySchemaGuard + ISchemaHealthContributor were removed in DATA-0095 Phase 1c.1.
        // Adapters now implement IDataRepository.EnsureReady directly; the facade calls it.
        services.AddSingleton<IDataDiagnostics, DataDiagnostics>();
        // Decorate repositories registered as IDataRepository<,>
        services.TryDecorate(typeof(IDataRepository<,>), typeof(RepositoryFacade<,>));
        // Relationship metadata scanning (ParentAttribute, etc.)
        services.TryAddSingleton<Koan.Data.Core.Relationships.IRelationshipMetadata, Koan.Data.Core.Relationships.RelationshipMetadataService>();
        services.TryAddSingleton<Koan.Data.Core.Relationships.IRelationshipQueryExecutor, Koan.Data.Core.Relationships.RelationshipQueryExecutor>();
        Koan.Data.Core.Model.EntityMetadataProvider.RelationshipMetadataAccessor = sp => sp.GetRequiredService<Koan.Data.Core.Relationships.IRelationshipMetadata>();

        // Auto-register transaction support (AI-0020) - "Reference = Intent" pattern
        Transactions.TransactionServiceCollectionExtensions.AddKoanTransactions(services);
    }

    /// <summary>
    /// Builds a provider and starts Koan runtime discovery for a synchronous, non-hosted process.
    /// </summary>
    /// <remarks>
    /// The caller owns the returned provider and must dispose it. This path does not run a generic
    /// host or its hosted-service lifecycle; web applications and workers should use <c>AddKoan()</c>
    /// with the generic host instead.
    /// </remarks>
    /// <returns>
    /// The active provider. The returned object implements both <see cref="IDisposable"/> and
    /// <see cref="IAsyncDisposable"/>.
    /// </returns>
    public static IServiceProvider StartKoan(this IServiceCollection services)
    {
        // Avoid duplicate registration if already configured
        if (!services.Any(d => d.ServiceType == typeof(Koan.Core.Hosting.Runtime.IAppRuntime)))
            services.AddKoan();

        // Provide a default IConfiguration only if the host hasn't already registered one
        if (!services.Any(d => d.ServiceType == typeof(IConfiguration)))
        {
            var cb = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();
            // If Koan.Secrets.Core is referenced, auto-add the secrets configuration wrapper
            TryInvokeSecretsBootstrap("AddSecretsReferenceConfiguration", cb, null);

            var cfg = cb.Build();
            services.AddSingleton<IConfiguration>(cfg);
        }

        var sp = services.BuildServiceProvider();
        var owner = new NonHostedServiceProvider(sp);
        owner.Own(Koan.Core.Hosting.App.AppHost.Attach(owner));

        try
        {
            // If secrets configuration is present, upgrade from bootstrap to DI-backed resolver and emit reload
            TryInvokeSecretsBootstrap("UpgradeSecretsConfiguration", owner);
            try { KoanEnv.TryInitialize(owner); } catch { }
            var rt = owner.GetService<Koan.Core.Hosting.Runtime.IAppRuntime>();
            rt?.Discover();
            rt?.Start();
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

    private sealed class NonHostedServiceProvider(ServiceProvider provider) : IServiceProvider, IDisposable, IAsyncDisposable
    {
        private ServiceProvider? _provider = provider;
        private IDisposable? _lease;

        public void Own(IDisposable lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            Interlocked.Exchange(ref _lease, lease)?.Dispose();
        }

        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            var current = Volatile.Read(ref _provider)
                ?? throw new ObjectDisposedException(nameof(NonHostedServiceProvider));
            return current.GetService(serviceType);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            Interlocked.Exchange(ref _provider, null)?.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            var current = Interlocked.Exchange(ref _provider, null);
            if (current is not null)
            {
                await current.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
