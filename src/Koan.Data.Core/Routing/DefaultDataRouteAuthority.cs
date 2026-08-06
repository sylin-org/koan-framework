using System.Text.Json;
using Koan.Core.Providers;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Routing;

/// <summary>
/// Owns the durable active-default decision. Physical source composition stays immutable; only this redacted pointer
/// and its route generations can change at runtime.
/// </summary>
public sealed class DefaultDataRouteAuthority
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly DataSourceRegistry _sources;
    private readonly DataProviderCatalog _providers;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly string _statePath;
    private readonly int _trackedRoutes;
    private DefaultDataRouteSnapshot _current;

    public DefaultDataRouteAuthority(
        DataDefaultProviderPlan initial,
        DataSourceRegistry sources,
        DataProviderCatalog providers,
        IOptions<DataRouteOptions> options,
        IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.Value;
        if (configured.TrackedRoutes <= 0)
            throw new InvalidOperationException("Koan:Data:Route:TrackedRoutes must be positive.");
        if (configured.DrainTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Koan:Data:Route:DrainTimeout must be positive.");
        _trackedRoutes = configured.TrackedRoutes;
        _statePath = ResolveStatePath(
            configured.StatePath,
            environment?.ContentRootPath ?? Directory.GetCurrentDirectory());

        var decision = initial.Decision;
        var initialPlan = sources.GetPlan(decision.Source, decision.Adapter);
        _current = Load(initialPlan, initial.Receipt);
    }

    public DefaultDataRouteSnapshot Current => Volatile.Read(ref _current);

    internal string StatePath => _statePath;

    internal static bool HasDurableState(
        IOptions<DataRouteOptions> options,
        IHostEnvironment? environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = ResolveStatePath(
            options.Value.StatePath,
            environment?.ContentRootPath ?? Directory.GetCurrentDirectory());
        return File.Exists(path);
    }

    internal AdapterResolutionDecision ResolveDefault()
    {
        var current = Current;
        var factory = _providers.Find(current.Plan.Adapter)
            ?? throw new AdapterResolutionException(
                current.Plan.Adapter,
                Infrastructure.Constants.Diagnostics.Reasons.AdapterUnavailable,
                "Restore the configured adapter for the durable active Data route.");
        return new AdapterResolutionDecision(
            factory,
            current.Plan.Source,
            current.SelectionReceipt,
            Bind(current.Plan, DataRouteOrigin.Default));
    }

    internal DataRouteBinding Bind(DataSourcePlan plan, DataRouteOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var current = Current;
        return new DataRouteBinding(
            plan,
            origin,
            current.AuthorityRevision,
            GenerationFor(current, plan.RouteIdentity));
    }

    internal void Validate(DataRouteBinding binding, bool allowQuarantined = false)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var current = Current;
        if (!allowQuarantined && current.QuarantinedRouteIdentities.Contains(binding.Plan.RouteIdentity))
            throw new DataRouteUnavailableException(
                binding.Plan.Source,
                DataRouteUnavailableException.QuarantinedCode,
                "Inspect and empty the quarantined source, then run a new verified promotion before using it.");

        var generation = GenerationFor(current, binding.Plan.RouteIdentity);
        if (generation != binding.ContentGeneration ||
            binding.IsDefaultDerived &&
            (binding.AuthorityRevision != current.AuthorityRevision ||
             !string.Equals(binding.Plan.RouteIdentity, current.Plan.RouteIdentity, StringComparison.Ordinal)))
        {
            throw new StaleDataRouteException(
                binding.Plan.Source,
                binding.Plan.RouteIdentity,
                binding.ContentGeneration,
                current.Plan.Source,
                current.Plan.RouteIdentity,
                current.ContentGeneration);
        }
    }

    internal async Task<DefaultDataRouteChange> BeginChange(
        string operationId,
        DataSourcePlan target,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(target);
        await _transition.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = Current;
            if (string.Equals(current.Plan.RouteIdentity, target.RouteIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Data source '{target.Source}' is already the active default route. Select a distinct configured source.");
            if (string.Equals(current.Plan.ConnectionIdentity, target.ConnectionIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Data source '{target.Source}' resolves to the active default's physical connection. " +
                    "Configure a physically distinct target.");
            return new DefaultDataRouteChange(this, operationId.Trim(), current, target);
        }
        catch
        {
            _transition.Release();
            throw;
        }
    }

    internal async Task MarkPending(DefaultDataRouteChange change, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureOwner(change);
        var current = Current;
        EnsureExpected(change, current);
        var quarantined = current.QuarantinedRouteIdentities
            .Where(route => !string.Equals(route, change.Target.RouteIdentity, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        await Save(ToRecord(current, quarantined, new TransitionRecord(
            change.OperationId,
            change.Expected.AuthorityRevision,
            change.Target.Source,
            change.Target.Adapter,
            change.Target.RouteIdentity,
            Phase: "pending",
            TargetMayContainData: false))).ConfigureAwait(false);
        change.Pending = true;
    }

    internal async Task MarkTargetMutated(DefaultDataRouteChange change, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureOwner(change);
        if (!change.Pending) throw new InvalidOperationException("Persist cutover intent before mutating the target.");
        var current = Current;
        EnsureExpected(change, current);
        await Save(ToRecord(current, current.QuarantinedRouteIdentities, new TransitionRecord(
            change.OperationId,
            change.Expected.AuthorityRevision,
            change.Target.Source,
            change.Target.Adapter,
            change.Target.RouteIdentity,
            Phase: "target-mutated",
            TargetMayContainData: true))).ConfigureAwait(false);
        change.TargetMayContainData = true;
    }

    internal async Task<DefaultDataRouteSnapshot> Commit(DefaultDataRouteChange change)
    {
        EnsureOwner(change);
        if (!change.Pending) throw new InvalidOperationException("Persist cutover intent before activation.");
        var current = Current;
        EnsureExpected(change, current);

        var generations = new Dictionary<string, long>(current.ContentGenerations, StringComparer.Ordinal);
        var nextGeneration = checked(generations.GetValueOrDefault(change.Target.RouteIdentity) + 1L);
        generations[change.Target.RouteIdentity] = nextGeneration;
        Trim(generations, change.Target.RouteIdentity, current.Plan.RouteIdentity);
        var quarantined = current.QuarantinedRouteIdentities
            .Where(route => !string.Equals(route, change.Target.RouteIdentity, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var next = new DefaultDataRouteSnapshot(
            change.Target,
            checked(current.AuthorityRevision + 1L),
            nextGeneration,
            DateTimeOffset.UtcNow,
            SelectionReceipt(change.Target.Adapter),
            quarantined,
            generations);
        await Save(ToRecord(next, quarantined, null, generations)).ConfigureAwait(false);
        Volatile.Write(ref _current, next);
        change.Committed = true;
        return next;
    }

    internal async Task Fail(DefaultDataRouteChange change)
    {
        EnsureOwner(change);
        if (!change.Pending || change.Committed) return;
        var current = Current;
        EnsureExpected(change, current);
        var quarantined = current.QuarantinedRouteIdentities.ToHashSet(StringComparer.Ordinal);
        if (change.TargetMayContainData) quarantined.Add(change.Target.RouteIdentity);
        var next = current with { QuarantinedRouteIdentities = quarantined };
        await Save(ToRecord(next, quarantined, new TransitionRecord(
            change.OperationId,
            change.Expected.AuthorityRevision,
            change.Target.Source,
            change.Target.Adapter,
            change.Target.RouteIdentity,
            Phase: "failed",
            TargetMayContainData: change.TargetMayContainData))).ConfigureAwait(false);
        Volatile.Write(ref _current, next);
    }

    internal void Release(DefaultDataRouteChange change)
    {
        EnsureOwner(change);
        _transition.Release();
    }

    private DefaultDataRouteSnapshot Load(
        DataSourcePlan initial,
        Koan.Core.Providers.ProviderSelectionReceipt initialReceipt)
    {
        if (!File.Exists(_statePath))
            return new DefaultDataRouteSnapshot(
                initial,
                0,
                1,
                DateTimeOffset.UtcNow,
                initialReceipt,
                new HashSet<string>(StringComparer.Ordinal),
                new Dictionary<string, long>(StringComparer.Ordinal) { [initial.RouteIdentity] = 1 });

        RouteControlRecord record;
        try
        {
            record = JsonSerializer.Deserialize<RouteControlRecord>(File.ReadAllText(_statePath), Json)
                ?? throw new InvalidDataException("The route control record was empty.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"Koan could not read the durable Data route control record '{_statePath}'. " +
                "Restore a valid record or explicitly remove it only after proving the intended active database.", error);
        }

        if (record.SchemaVersion != Infrastructure.Constants.Defaults.DataRouteStateSchema ||
            record.Revision < 0 || string.IsNullOrWhiteSpace(record.ActiveSource) ||
            string.IsNullOrWhiteSpace(record.Adapter) || string.IsNullOrWhiteSpace(record.RouteIdentity))
            throw InvalidRecord("The record schema or active-route fields are invalid.");

        var source = _sources.GetSource(record.ActiveSource)
            ?? throw InvalidRecord($"Configured source '{record.ActiveSource}' no longer exists.");
        var selected = _providers.Find(record.Adapter)
            ?? throw InvalidRecord($"Adapter '{record.Adapter}' is no longer referenced.");
        var candidate = _providers.Describe(selected);
        var plan = _sources.GetPlan(source.Name, candidate.Id);
        if (!string.Equals(plan.RouteIdentity, record.RouteIdentity, StringComparison.Ordinal) ||
            !string.Equals(plan.ConnectionIdentity, record.ConnectionIdentity, StringComparison.Ordinal))
            throw InvalidRecord(
                $"Configured source '{source.Name}' no longer matches its saved physical identity.");

        var generations = record.Generations ?? new Dictionary<string, long>(StringComparer.Ordinal);
        var generation = generations.GetValueOrDefault(plan.RouteIdentity);
        if (generation <= 0) throw InvalidRecord("The active route has no positive content generation.");
        var quarantined = new HashSet<string>(record.QuarantinedRoutes ?? [], StringComparer.Ordinal);
        if (record.Transition is { } transition)
        {
            if (string.IsNullOrWhiteSpace(transition.OperationId) || transition.ExpectedRevision < 0 ||
                string.IsNullOrWhiteSpace(transition.TargetSource) ||
                string.IsNullOrWhiteSpace(transition.TargetAdapter) ||
                string.IsNullOrWhiteSpace(transition.TargetRouteIdentity) ||
                transition.Phase is not ("pending" or "target-mutated" or "failed"))
                throw InvalidRecord("The saved transition evidence is invalid.");
            if (transition.Phase is "pending" or "target-mutated" || transition.TargetMayContainData)
                quarantined.Add(transition.TargetRouteIdentity);
        }
        return new DefaultDataRouteSnapshot(
            plan,
            record.Revision,
            generation,
            record.ActivatedAt,
            SelectionReceipt(plan.Adapter),
            quarantined,
            new Dictionary<string, long>(generations, StringComparer.Ordinal));
    }

    private async Task Save(RouteControlRecord record)
    {
        var directory = Path.GetDirectoryName(_statePath)
            ?? throw new InvalidOperationException("The Data route state path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_statePath)}.{Guid.CreateVersion7():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, Json, CancellationToken.None).ConfigureAwait(false);
                await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_statePath))
                File.Replace(temporary, _statePath, destinationBackupFileName: null);
            else
                File.Move(temporary, _statePath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private RouteControlRecord ToRecord(
        DefaultDataRouteSnapshot snapshot,
        IReadOnlySet<string> quarantined,
        TransitionRecord? transition,
        Dictionary<string, long>? generations = null)
        => new(
            Infrastructure.Constants.Defaults.DataRouteStateSchema,
            snapshot.Plan.Source,
            snapshot.Plan.Adapter,
            snapshot.Plan.RouteIdentity,
            snapshot.Plan.ConnectionIdentity,
            snapshot.AuthorityRevision,
            snapshot.ActivatedAt,
            generations ?? new Dictionary<string, long>(snapshot.ContentGenerations, StringComparer.Ordinal),
            quarantined.Order(StringComparer.Ordinal).ToArray(),
            transition);

    private static long GenerationFor(DefaultDataRouteSnapshot snapshot, string routeIdentity)
        => snapshot.ContentGenerations.GetValueOrDefault(routeIdentity);

    private void Trim(Dictionary<string, long> generations, params string[] protectedRoutes)
    {
        if (generations.Count <= _trackedRoutes) return;
        var protectedSet = protectedRoutes.ToHashSet(StringComparer.Ordinal);
        foreach (var route in generations.Keys.Where(route => !protectedSet.Contains(route)).Order(StringComparer.Ordinal).ToArray())
        {
            generations.Remove(route);
            if (generations.Count <= _trackedRoutes) return;
        }
    }

    private static string ResolveStatePath(string? configured, string contentRoot)
    {
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Infrastructure.Constants.Defaults.DataRouteStateDirectory,
                Infrastructure.Constants.Defaults.DataRouteStateSubdirectory,
                Infrastructure.Constants.Defaults.DataRouteStateFile)
            : configured.Trim();
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));
    }

    private static InvalidOperationException InvalidRecord(string reason) => new(
        $"The durable Data route control record is inconsistent: {reason} " +
        "Restore the matching source configuration or repair the control record from verified operator evidence.");

    private Koan.Core.Providers.ProviderSelectionReceipt SelectionReceipt(string adapter)
        => _providers.Require(
            adapter,
            "data:default",
            Infrastructure.Constants.Diagnostics.Reasons.DefaultSource,
            "Restore the adapter selected by the durable active Data route.").Receipt;

    private static void EnsureExpected(DefaultDataRouteChange change, DefaultDataRouteSnapshot current)
    {
        if (change.Expected.AuthorityRevision != current.AuthorityRevision ||
            !string.Equals(change.Expected.Plan.RouteIdentity, current.Plan.RouteIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The active default route changed after this cutover was planned. Re-plan against the current route.");
    }

    private void EnsureOwner(DefaultDataRouteChange change)
    {
        if (!ReferenceEquals(change.Owner, this)) throw new InvalidOperationException("The route change belongs to another host.");
    }

    private sealed record RouteControlRecord(
        int SchemaVersion,
        string ActiveSource,
        string Adapter,
        string RouteIdentity,
        string ConnectionIdentity,
        long Revision,
        DateTimeOffset ActivatedAt,
        Dictionary<string, long> Generations,
        string[] QuarantinedRoutes,
        TransitionRecord? Transition);

    private sealed record TransitionRecord(
        string OperationId,
        long ExpectedRevision,
        string TargetSource,
        string TargetAdapter,
        string TargetRouteIdentity,
        string Phase,
        bool TargetMayContainData);
}

internal sealed class DefaultDataRouteChange : IAsyncDisposable
{
    private int _disposed;

    internal DefaultDataRouteChange(
        DefaultDataRouteAuthority owner,
        string operationId,
        DefaultDataRouteSnapshot expected,
        DataSourcePlan target)
    {
        Owner = owner;
        OperationId = operationId;
        Expected = expected;
        Target = target;
    }

    internal DefaultDataRouteAuthority Owner { get; }
    internal string OperationId { get; }
    internal DefaultDataRouteSnapshot Expected { get; }
    internal DataSourcePlan Target { get; }
    internal bool Pending { get; set; }
    internal bool TargetMayContainData { get; set; }
    internal bool Committed { get; set; }

    internal Task MarkPending(CancellationToken ct) => Owner.MarkPending(this, ct);
    internal Task MarkTargetMutated(CancellationToken ct) => Owner.MarkTargetMutated(this, ct);
    internal Task<DefaultDataRouteSnapshot> Commit() => Owner.Commit(this);
    internal Task Fail() => Owner.Fail(this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (!Committed) await Owner.Fail(this).ConfigureAwait(false);
        }
        finally
        {
            Owner.Release(this);
        }
    }
}
