using Koan.Core.Providers;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.SourceIntegration.Runtime;

/// <summary>Host-owned source resolver. Pure resolution is separate from lazy native activation.</summary>
internal sealed class DataSourceIntegrationService : IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _services;
    private readonly DataSourceRegistry _registry;
    private readonly ProviderCatalog<IDataSourceIntegrationFactory> _factories;
    private readonly DataDiagnostics? _diagnostics;
    private bool _disposed;

    public DataSourceIntegrationService(
        IServiceProvider services,
        DataSourceRegistry registry,
        IEnumerable<IDataSourceIntegrationFactory> factories,
        DataDiagnostics? diagnostics = null)
    {
        _services = services;
        _registry = registry;
        _diagnostics = diagnostics;
        _factories = ProviderCatalog<IDataSourceIntegrationFactory>.Compile(
            factories,
            static factory => new ProviderCandidateDescriptor(
                factory.Provider,
                factory.Aliases.ToArray(),
                factory.ReferenceIdentities
                    .Concat(ProviderMetadata.References(factory.GetType()))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ProviderMetadata.Priority(factory.GetType())));
    }

    public ResolvedSource Resolve(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requested = source.Trim();
        var definition = _registry.GetSource(requested)
            ?? throw new SourceIntegrationException(
                requested,
                "Configure Koan:Data:Sources:{source} with an exact Adapter and connection before using Data.Source(...).".Replace("{source}", requested));
        if (string.IsNullOrWhiteSpace(definition.Adapter))
        {
            throw new SourceIntegrationException(
                definition.Name,
                "Source-only access requires an explicit Adapter; automatic Entity-provider selection does not apply.");
        }

        var factory = _factories.Find(definition.Adapter)
            ?? throw new SourceIntegrationException(
                definition.Name,
                $"Adapter '{definition.Adapter}' does not provide Source Integration. Available providers: {AvailableProviders()}.");
        var provider = _factories.Describe(factory).Id;
        var plan = _registry.GetPlan(definition.Name, provider);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sources.TryGetValue(definition.Name, out var cached))
            {
                if (cached.Plan.RouteIdentity != plan.RouteIdentity || !ReferenceEquals(cached.Factory, factory))
                    throw new InvalidOperationException(
                        $"Frozen data source '{definition.Name}' resolved to a different route. " +
                        "Source decisions cannot change after composition.");
                return cached.Resolved;
            }

            DataSourceIntegrationDescriptor descriptor;
            DataClaimSet claims;
            try
            {
                descriptor = factory.DescribeSource(definition.Name) ?? DataSourceIntegrationDescriptor.Empty;
                claims = DataClaimSet.Describe(factory);
            }
            catch (Exception error) when (error is not SourceIntegrationException)
            {
                throw new SourceIntegrationException(
                    definition.Name,
                    $"Adapter '{provider}' returned an invalid pure source or claim declaration.",
                    error);
            }

            var resolved = new ResolvedSource(
                plan,
                provider,
                descriptor,
                claims,
                () => factory.CreateSource(_services, definition.Name)
                    ?? throw new SourceIntegrationException(
                        definition.Name,
                        $"Adapter '{provider}' returned no Source Integration implementation."),
                _gate);
            _sources.Add(definition.Name, new CachedSource(plan, factory, resolved));
            _diagnostics?.ObserveSourcePlan(plan, claims);
            _diagnostics?.ObserveParticipation(provider, definition.Name);
            return resolved;
        }
    }

    public void Dispose()
    {
        IDataSourceIntegration[] integrations;
        lock (_gate)
        {
            if (_disposed) return;
            var sources = CurrentSources();
            if (sources.Any(static source => !source.CanDisposeSynchronously))
                throw new InvalidOperationException(
                    "An activated source integration requires asynchronous host disposal. " +
                    "Dispose the Koan host with DisposeAsync().");

            integrations = sources
                .Select(static source => source.CaptureForSynchronousDisposal())
                .OfType<IDataSourceIntegration>()
                .ToArray();
            _disposed = true;
            _sources.Clear();
        }

        DisposeSynchronously(integrations);
    }

    public async ValueTask DisposeAsync()
    {
        IDataSourceIntegration[] integrations;
        lock (_gate)
        {
            if (_disposed) return;
            integrations = CurrentSources()
                .Select(static source => source.CaptureForDisposal())
                .OfType<IDataSourceIntegration>()
                .ToArray();
            _disposed = true;
            _sources.Clear();
        }

        List<Exception>? failures = null;
        foreach (var integration in integrations)
        {
            try
            {
                if (integration is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (integration is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null) throw new AggregateException("One or more source integrations failed to dispose.", failures);
    }

    private ResolvedSource[] CurrentSources() => _sources.Values
        .Select(static cached => cached.Resolved)
        .Distinct()
        .ToArray();

    private static void DisposeSynchronously(IEnumerable<IDataSourceIntegration> integrations)
    {
        List<Exception>? failures = null;
        foreach (var integration in integrations)
        {
            try
            {
                if (integration is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null) throw new AggregateException("One or more source integrations failed to dispose.", failures);
    }

    private string AvailableProviders()
    {
        var names = _factories.Candidates.Select(static candidate => candidate.Id).ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }

    private sealed record CachedSource(
        DataSourcePlan Plan,
        IDataSourceIntegrationFactory Factory,
        ResolvedSource Resolved);
}

internal sealed class ResolvedSource : IDisposable, IAsyncDisposable
{
    private readonly object _gate;
    private readonly Func<IDataSourceIntegration> _create;
    private IDataSourceIntegration? _integration;
    private bool _disposed;

    public ResolvedSource(
        DataSourcePlan plan,
        string provider,
        DataSourceIntegrationDescriptor descriptor,
        DataClaimSet claims,
        Func<IDataSourceIntegration> create)
        : this(plan, provider, descriptor, claims, create, new object())
    {
    }

    internal ResolvedSource(
        DataSourcePlan plan,
        string provider,
        DataSourceIntegrationDescriptor descriptor,
        DataClaimSet claims,
        Func<IDataSourceIntegration> create,
        object lifetimeGate)
    {
        Plan = plan;
        Provider = provider;
        Descriptor = descriptor;
        Claims = claims;
        _create = create;
        _gate = lifetimeGate;
    }

    public DataSourcePlan Plan { get; }
    public string Provider { get; }
    public DataSourceIntegrationDescriptor Descriptor { get; }
    public DataClaimSet Claims { get; }
    public bool IsActivated
    {
        get { lock (_gate) return _integration is not null; }
    }

    public IDataSourceIntegration Integration
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                // Creation runs inside the owner lock: concurrent first use publishes one instance,
                // while a failed attempt leaves no poisoned Lazy and can be retried.
                return _integration ??= _create();
            }
        }
    }

    internal bool CanDisposeSynchronously
    {
        get
        {
            lock (_gate)
                return _disposed || _integration is null ||
                    _integration is IDisposable || _integration is not IAsyncDisposable;
        }
    }

    public void Dispose()
    {
        var integration = CaptureForSynchronousDisposal();
        if (integration is IDisposable disposable) disposable.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var integration = CaptureForDisposal();
        if (integration is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (integration is IDisposable disposable) disposable.Dispose();
    }

    internal IDataSourceIntegration? CaptureForSynchronousDisposal()
    {
        lock (_gate)
        {
            if (_disposed) return null;
            if (_integration is IAsyncDisposable and not IDisposable)
                throw new InvalidOperationException(
                    "This source integration requires asynchronous host disposal. Dispose the Koan host with DisposeAsync().");
            return CaptureForDisposalCore();
        }
    }

    internal IDataSourceIntegration? CaptureForDisposal()
    {
        lock (_gate) return CaptureForDisposalCore();
    }

    private IDataSourceIntegration? CaptureForDisposalCore()
    {
        if (_disposed) return null;
        _disposed = true;
        return _integration;
    }
}
