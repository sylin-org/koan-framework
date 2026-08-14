using System.Collections.Concurrent;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Options;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Routing;

/// <summary>
/// Enforces route-generation coherence and maintenance admission around complete logical Data operations.
/// </summary>
public sealed class DataOperationHorizon
{
    private static readonly AsyncLocal<ScopeNode?> Ambient = new();
    private readonly ConcurrentDictionary<string, RouteGate> _gates = new(StringComparer.Ordinal);
    private readonly DefaultDataRouteAuthority _authority;
    private readonly TimeSpan _drainTimeout;

    public DataOperationHorizon(
        DefaultDataRouteAuthority authority,
        IOptions<DataRouteOptions> options)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        ArgumentNullException.ThrowIfNull(options);
        _drainTimeout = options.Value.DrainTimeout;
    }

    internal ValueTask<DataOperationLease> Enter(
        DataRouteBinding binding,
        DataOperationEffect effect,
        string operation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        _authority.Validate(binding);
        ct.ThrowIfCancellationRequested();

        var mutation = effect != DataOperationEffect.Read;
        var parent = Ambient.Value;
        if (parent is not null && mutation && parent.Mutation &&
            !HasMutationFor(parent, binding.Plan.ConnectionIdentity))
            throw new InvalidOperationException(
                $"Data operation '{operation}' attempted to expand an active mutation from source " +
                $"'{parent.Binding.Plan.Source}' to '{binding.Plan.Source}'. Declare a multi-route operation horizon " +
                "so physical routes can be acquired in stable order.");

        var gate = _gates.GetOrAdd(binding.Plan.ConnectionIdentity, static _ => new RouteGate());
        var counted = false;
        lock (gate.Sync)
        {
            if (effect == DataOperationEffect.Read && gate.ReadsClosed)
                throw new DataSwitchInProgressException(binding.Plan.Source, DataOperationEffect.Read);
            if (mutation && gate.WritesClosed)
                throw new DataSwitchInProgressException(binding.Plan.Source, effect);

            var reentrantMutation = mutation && HasMutationFor(parent, binding.Plan.ConnectionIdentity);
            if (mutation && !reentrantMutation)
            {
                checked { gate.ActiveMutations++; }
                gate.Drained = null;
                counted = true;
            }
        }

        var node = new ScopeNode(binding, mutation, parent, null);
        Ambient.Value = node;
        return ValueTask.FromResult(new DataOperationLease(this, gate, node, counted));
    }

    internal ValueTask<DataMultiOperationLease> EnterMany(
        IReadOnlyList<DataRouteBinding> bindings,
        string operation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var routes = bindings
            .GroupBy(static binding => binding.Plan.ConnectionIdentity, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static binding => binding.Plan.ConnectionIdentity, StringComparer.Ordinal)
            .ToArray();
        if (routes.Length == 0)
            return ValueTask.FromResult(DataMultiOperationLease.Empty);
        foreach (var binding in routes) _authority.Validate(binding);
        ct.ThrowIfCancellationRequested();

        var parent = Ambient.Value;
        if (parent is not null && parent.Mutation)
            throw new InvalidOperationException(
                $"Data operation '{operation}' attempted to declare a multi-route horizon inside another mutation horizon.");

        var admitted = new List<RouteGate>(routes.Length);
        try
        {
            foreach (var binding in routes)
            {
                var gate = _gates.GetOrAdd(binding.Plan.ConnectionIdentity, static _ => new RouteGate());
                lock (gate.Sync)
                {
                    if (gate.WritesClosed)
                        throw new DataSwitchInProgressException(binding.Plan.Source, DataOperationEffect.Write);
                    checked { gate.ActiveMutations++; }
                    gate.Drained = null;
                }
                admitted.Add(gate);
            }
        }
        catch
        {
            ReleaseCounts(admitted);
            throw;
        }

        var allowed = routes.Select(static binding => binding.Plan.ConnectionIdentity).ToHashSet(StringComparer.Ordinal);
        var node = new ScopeNode(routes[0], true, parent, allowed);
        Ambient.Value = node;
        return ValueTask.FromResult(new DataMultiOperationLease(this, admitted, node));
    }

    internal async Task<DataRouteMaintenanceWindow> CloseAndDrain(
        IReadOnlyList<DataRouteMaintenanceRequest> requests,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) throw new ArgumentException("At least one route is required.", nameof(requests));

        var normalized = requests
            .GroupBy(static request => request.Binding.Plan.ConnectionIdentity, StringComparer.Ordinal)
            .Select(static group => new DataRouteMaintenanceRequest(
                group.First().Binding,
                group.Any(static request => request.BlockReads),
                group.Any(static request => request.BlockWrites),
                group.Any(static request => request.AllowQuarantined)))
            .OrderBy(static request => request.Binding.Plan.ConnectionIdentity, StringComparer.Ordinal)
            .ToArray();
        foreach (var request in normalized) _authority.Validate(request.Binding, request.AllowQuarantined);

        var closed = new List<(DataRouteMaintenanceRequest Request, RouteGate Gate)>(normalized.Length);
        try
        {
            foreach (var request in normalized)
            {
                var gate = _gates.GetOrAdd(request.Binding.Plan.ConnectionIdentity, static _ => new RouteGate());
                lock (gate.Sync)
                {
                    if (request.BlockReads && gate.ReadsClosed || request.BlockWrites && gate.WritesClosed)
                        throw new InvalidOperationException(
                            $"Data route '{request.Binding.Plan.Source}' already has an incompatible maintenance window.");
                    if (request.BlockReads) gate.ReadsClosed = true;
                    if (request.BlockWrites) gate.WritesClosed = true;
                }
                closed.Add((request, gate));
            }

            foreach (var (_, gate) in closed)
            {
                Task drained;
                lock (gate.Sync)
                {
                    if (gate.ActiveMutations == 0) continue;
                    gate.Drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    drained = gate.Drained.Task;
                }
                await drained.WaitAsync(_drainTimeout, ct).ConfigureAwait(false);
            }

            return new DataRouteMaintenanceWindow(this, closed);
        }
        catch
        {
            Reopen(closed);
            throw;
        }
    }

    internal static string? CurrentNamespace => Ambient.Value?.Binding.Namespace;

    internal void Activate(ScopeNode node)
    {
        var current = Ambient.Value;
        if (ReferenceEquals(current, node)) return;
        if (!ReferenceEquals(current, node.Parent))
            throw new InvalidOperationException(
                "A Data operation horizon cannot be activated outside the async context that admitted it.");
        Ambient.Value = node;
    }

    internal void Exit(RouteGate gate, ScopeNode node, bool counted)
    {
        var current = Ambient.Value;
        if (ReferenceEquals(current, node))
            Ambient.Value = node.Parent;
        else if (!ReferenceEquals(current, node.Parent))
            throw new InvalidOperationException("Data operation horizons must be disposed in reverse acquisition order.");
        if (!counted) return;

        TaskCompletionSource? drained = null;
        lock (gate.Sync)
        {
            gate.ActiveMutations--;
            if (gate.ActiveMutations < 0) throw new InvalidOperationException("Data mutation admission count underflowed.");
            if (gate.ActiveMutations == 0)
            {
                drained = gate.Drained;
                gate.Drained = null;
            }
        }
        drained?.TrySetResult();
    }

    internal void ExitMany(IReadOnlyList<RouteGate> gates, ScopeNode node)
    {
        var current = Ambient.Value;
        if (ReferenceEquals(current, node))
            Ambient.Value = node.Parent;
        else if (!ReferenceEquals(current, node.Parent))
            throw new InvalidOperationException("Data operation horizons must be disposed in reverse acquisition order.");
        ReleaseCounts(gates);
    }

    internal void Reopen(IReadOnlyList<(DataRouteMaintenanceRequest Request, RouteGate Gate)> closed)
    {
        foreach (var (request, gate) in closed.Reverse())
        {
            lock (gate.Sync)
            {
                if (request.BlockReads) gate.ReadsClosed = false;
                if (request.BlockWrites) gate.WritesClosed = false;
            }
        }
    }

    private static bool HasMutationFor(ScopeNode? node, string connectionIdentity)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (current.Mutation &&
                (string.Equals(current.Binding.Plan.ConnectionIdentity, connectionIdentity, StringComparison.Ordinal) ||
                 current.AllowedConnections?.Contains(connectionIdentity) == true))
                return true;
        return false;
    }

    private static void ReleaseCounts(IReadOnlyList<RouteGate> gates)
    {
        foreach (var gate in gates.Reverse())
        {
            TaskCompletionSource? drained = null;
            lock (gate.Sync)
            {
                gate.ActiveMutations--;
                if (gate.ActiveMutations < 0) throw new InvalidOperationException("Data mutation admission count underflowed.");
                if (gate.ActiveMutations == 0)
                {
                    drained = gate.Drained;
                    gate.Drained = null;
                }
            }
            drained?.TrySetResult();
        }
    }

    internal sealed class RouteGate
    {
        internal object Sync { get; } = new();
        internal bool ReadsClosed { get; set; }
        internal bool WritesClosed { get; set; }
        internal int ActiveMutations { get; set; }
        internal TaskCompletionSource? Drained { get; set; }
    }

    internal sealed record ScopeNode(
        DataRouteBinding Binding,
        bool Mutation,
        ScopeNode? Parent,
        IReadOnlySet<string>? AllowedConnections);
}

internal readonly record struct DataRouteMaintenanceRequest(
    DataRouteBinding Binding,
    bool BlockReads,
    bool BlockWrites,
    bool AllowQuarantined = false);

internal sealed class DataOperationLease : IAsyncDisposable
{
    private DataOperationHorizon? _owner;
    private readonly DataOperationHorizon.RouteGate _gate;
    private readonly DataOperationHorizon.ScopeNode _node;
    private readonly bool _counted;
    private int _activated;

    internal DataOperationLease(
        DataOperationHorizon owner,
        DataOperationHorizon.RouteGate gate,
        DataOperationHorizon.ScopeNode node,
        bool counted)
    {
        _owner = owner;
        _gate = gate;
        _node = node;
        _counted = counted;
    }

    internal DataOperationLease Activate()
    {
        if (Interlocked.Exchange(ref _activated, 1) != 0) return this;
        var owner = Volatile.Read(ref _owner)
            ?? throw new ObjectDisposedException(nameof(DataOperationLease));
        owner.Activate(_node);
        return this;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _owner, null)?.Exit(_gate, _node, _counted);
        return ValueTask.CompletedTask;
    }
}

internal sealed class DataMultiOperationLease : IAsyncDisposable
{
    internal static DataMultiOperationLease Empty { get; } = new();

    private DataOperationHorizon? _owner;
    private readonly IReadOnlyList<DataOperationHorizon.RouteGate> _gates = [];
    private readonly DataOperationHorizon.ScopeNode? _node;

    private DataMultiOperationLease() { }

    internal DataMultiOperationLease(
        DataOperationHorizon owner,
        IReadOnlyList<DataOperationHorizon.RouteGate> gates,
        DataOperationHorizon.ScopeNode node)
    {
        _owner = owner;
        _gates = gates;
        _node = node;
    }

    public ValueTask DisposeAsync()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        if (owner is not null) owner.ExitMany(_gates, _node!);
        return ValueTask.CompletedTask;
    }
}

internal sealed class DataRouteMaintenanceWindow : IAsyncDisposable
{
    private DataOperationHorizon? _owner;
    private readonly IReadOnlyList<(DataRouteMaintenanceRequest Request, DataOperationHorizon.RouteGate Gate)> _closed;

    internal DataRouteMaintenanceWindow(
        DataOperationHorizon owner,
        IReadOnlyList<(DataRouteMaintenanceRequest Request, DataOperationHorizon.RouteGate Gate)> closed)
    {
        _owner = owner;
        _closed = closed;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _owner, null)?.Reopen(_closed);
        return ValueTask.CompletedTask;
    }
}
