using Koan.Communication.Adapters;
using Koan.Communication.Infrastructure;
using Koan.Communication.Signals;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Context;
using Koan.Core.Hosting.App;
using Koan.Core.Providers;
using Koan.Communication.Semantics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

internal sealed class CommunicationRouter
{
    private static readonly CommunicationAdapterCapabilities TransportRequirements =
        CommunicationAdapterCapabilities.ContractIdentity
        | CommunicationAdapterCapabilities.SnapshotCopy
        | CommunicationAdapterCapabilities.ContextCarriage
        | CommunicationAdapterCapabilities.TypedGroups
        | CommunicationAdapterCapabilities.GroupFanOut
        | CommunicationAdapterCapabilities.MessageIdentity
        | CommunicationAdapterCapabilities.BoundedAcceptance;

    private static readonly CommunicationAdapterCapabilities EventRequirements =
        TransportRequirements | CommunicationAdapterCapabilities.ZeroTargetEvents;

    private static readonly CommunicationAdapterCapabilities FrameworkSignalRequirements =
        CommunicationAdapterCapabilities.ContractIdentity
        | CommunicationAdapterCapabilities.SnapshotCopy
        | CommunicationAdapterCapabilities.TypedGroups
        | CommunicationAdapterCapabilities.GroupFanOut
        | CommunicationAdapterCapabilities.MessageIdentity
        | CommunicationAdapterCapabilities.BoundedAcceptance;

    private static readonly CommunicationAdapterCapabilities FrameworkBroadcastRequirements =
        CommunicationAdapterCapabilities.ContractIdentity
        | CommunicationAdapterCapabilities.SnapshotCopy
        | CommunicationAdapterCapabilities.NodeFanOut
        | CommunicationAdapterCapabilities.MessageIdentity
        | CommunicationAdapterCapabilities.BoundedAcceptance;

    private readonly IReadOnlyDictionary<(CommunicationLane Lane, string Channel), CommunicationRouteDecision>
        _routes;
    private readonly CommunicationRouteDecision[] _orderedRoutes;
    private readonly IReadOnlyDictionary<string, BoundTarget> _bindings;
    private readonly IReadOnlyDictionary<ICommunicationAdapter, CommunicationAdapterHost> _adapterHosts;
    private readonly CommunicationIngress _ingress;
    private readonly ILogger<CommunicationRouter> _logger;
    private readonly ICommunicationAdapter[] _selectedAdapters;
    private readonly string _meshId;
    private readonly ProviderCatalog<ICommunicationAdapter> _providers;
    private int _state;

    public CommunicationRouter(
        IEnumerable<ICommunicationAdapter> adapters,
        KoanApplicationReferenceManifest references,
        ApplicationIdentitySnapshot application,
        CommunicationHandlerCatalog handlers,
        IEnumerable<FrameworkMessageTargetBinding> frameworkSignals,
        CommunicationIngress ingress,
        CommunicationContextPlan contextPlan,
        IOptions<CommunicationOptions> options,
        ILogger<CommunicationRouter> logger)
    {
        _ingress = ingress;
        _logger = logger;
        _providers = ProviderCatalog<ICommunicationAdapter>.Compile(adapters, DescribeAdapter);
        var candidates = _providers.Candidates
            .Select(static candidate => candidate.Value)
            .ToArray();
        IReadOnlySet<ICommunicationAdapter> directCandidates = references.IsPresent
            ? new HashSet<ICommunicationAdapter>(
                _providers.Direct(references),
                ReferenceEqualityComparer.Instance)
            : [];

        var routeList = new List<CommunicationRouteDecision>();
        foreach (var channel in PublicChannels(options.Value))
        {
            routeList.Add(Elect(
                CommunicationLane.Transport,
                channel.Name,
                channel.TransportProvider,
                TransportRequirements,
                contextPlan.MinimumIngressTrust,
                candidates,
                directCandidates,
                _providers));
            routeList.Add(Elect(
                CommunicationLane.Events,
                channel.Name,
                channel.EventsProvider,
                EventRequirements,
                contextPlan.MinimumIngressTrust,
                candidates,
                directCandidates,
                _providers));
        }
        routeList.Add(Elect(
            CommunicationLane.FrameworkSignals,
            Constants.Channels.Default,
            options.Value.FrameworkSignalsProvider,
            FrameworkSignalRequirements,
            ContextIngressTrust.Unverified,
            candidates,
            directCandidates,
            _providers));
        routeList.Add(Elect(
            CommunicationLane.FrameworkBroadcasts,
            Constants.Channels.Default,
            options.Value.FrameworkBroadcastsProvider,
            FrameworkBroadcastRequirements,
            ContextIngressTrust.Unverified,
            candidates,
            directCandidates,
            _providers));

        _orderedRoutes = routeList
            .OrderBy(static route => route.Lane)
            .ThenBy(static route => route.Channel, StringComparer.Ordinal)
            .ToArray();
        _routes = _orderedRoutes.ToDictionary(static route => (route.Lane, route.Channel));
        _bindings = BuildBindings(handlers, frameworkSignals, _orderedRoutes);
        _meshId = application.Code;
        _selectedAdapters = _orderedRoutes
            .Select(static route => route.Adapter)
            .Distinct()
            .OrderBy(static adapter => adapter.Descriptor.Id, StringComparer.Ordinal)
            .ToArray();
        _adapterHosts = _selectedAdapters.ToDictionary(
            static adapter => adapter,
            adapter => new CommunicationAdapterHost(
                _meshId,
                _bindings.Values
                    .Where(binding => ReferenceEquals(
                        _routes[(binding.Declaration.Lane, binding.Declaration.Channel)].Adapter,
                        adapter))
                    .Select(static binding => binding.Declaration)
                    .OrderBy(static binding => binding.Id, StringComparer.Ordinal)
                    .ToArray(),
                (bindingId, payload, ct) => Dispatch(
                    bindingId,
                    payload,
                    adapter.Descriptor.IngressTrust,
                    ct)));
    }

    public IReadOnlyList<CommunicationRouteDecision> Routes => _orderedRoutes;
    public IReadOnlyList<CommunicationAdapterBinding> Bindings => _bindings.Values
        .Select(static binding => binding.Declaration)
        .OrderBy(static binding => binding.Id, StringComparer.Ordinal)
        .ToArray();
    public string MeshId => _meshId;

    public CommunicationRouteDecision For(CommunicationLane lane, string? channel = null)
    {
        var normalized = channel is null
            ? Constants.Channels.Default
            : NormalizeChannel(channel);
        if (_routes.TryGetValue((lane, normalized), out var route)) return route;

        var configured = string.Join(", ", _orderedRoutes
            .Where(route => route.Lane == lane)
            .Select(static route => route.Channel));
        throw new InvalidOperationException(
            $"Communication channel '{normalized}' is not configured for {lane}. " +
            $"Declare '{Constants.Configuration.Channels}:{normalized}' before host startup or use '{Constants.Channels.Default}'. " +
            $"Configured {lane} channels: {configured}.");
    }

    public int? KnownTargetGroups(CommunicationRouteDecision route, string contractId)
        => route.Adapter.Descriptor.IsBuiltIn
            ? _bindings.Values.Count(binding =>
                binding.Declaration.Lane == route.Lane
                && string.Equals(binding.Declaration.Channel, route.Channel, StringComparison.Ordinal)
                && string.Equals(binding.Declaration.ContractId, contractId, StringComparison.Ordinal))
            : null;

    public async Task Start(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("The Communication router cannot be started more than once.");

        var started = new List<ICommunicationAdapter>(_selectedAdapters.Length);
        try
        {
            foreach (var adapter in _selectedAdapters)
            {
                await adapter.Start(_adapterHosts[adapter], ct).ConfigureAwait(false);
                started.Add(adapter);
            }

            foreach (var route in _orderedRoutes)
            {
                _logger.LogInformation(
                    "Koan Communication {Lane}/{Channel}: provider={Provider}, assurance={Assurance}, " +
                    "directIntent={DirectIntent}, priority={Priority}, reason={Reason}",
                    route.Lane,
                    route.Channel,
                    route.AdapterId,
                    route.Assurance,
                    route.DirectIntent,
                    route.Priority,
                    route.Reason);
            }
        }
        catch (Exception error)
        {
            Interlocked.Exchange(ref _state, 2);
            for (var i = started.Count - 1; i >= 0; i--)
            {
                try { await started[i].Stop(CancellationToken.None).ConfigureAwait(false); }
                catch { /* preserve the startup failure */ }
            }

            throw new InvalidOperationException(
                "Koan Communication could not start an elected provider. Direct provider intent never falls back " +
                "to process-local reach; correct the provider configuration or remove the direct reference.",
                error);
        }
    }

    public async ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationRouteDecision route,
        string contractId,
        string messageId,
        ReadOnlyMemory<byte> payload,
        CommunicationOperation operation,
        CancellationToken ct)
    {
        if (Volatile.Read(ref _state) != 1 || !route.Adapter.IsReady)
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                $"Communication provider '{route.AdapterId}' is not accepting {route.Lane} publications.");

        var publication = new CommunicationAdapterPublication(
            route.Lane,
            route.Channel,
            contractId,
            messageId,
            payload,
            operation);
        var acceptance = await route.Adapter.Publish(publication, ct).ConfigureAwait(false);
        if (acceptance.SettlementObservable != route.Adapter.Descriptor.SettlementObservable
            || acceptance.TargetGroups is < 0
            || acceptance.SettlementObservable && !acceptance.TargetGroups.HasValue)
        {
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                $"Communication provider '{route.AdapterId}' returned an invalid publication acceptance.");
        }

        if (route.Lane != CommunicationLane.Events && acceptance.TargetGroups == 0)
        {
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.NoRoute,
                $"Communication provider '{route.AdapterId}' reported no {route.Lane} receiver group for '{contractId}'.");
        }

        return acceptance;
    }

    public async Task Stop(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _state, 2) != 1) return;

        List<Exception>? failures = null;
        for (var i = _selectedAdapters.Length - 1; i >= 0; i--)
        {
            try { await _selectedAdapters[i].Stop(ct).ConfigureAwait(false); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }

        if (failures is not null) throw new AggregateException("Communication provider shutdown failed.", failures);
    }

    private async Task<CommunicationDeliveryOutcome> Dispatch(
        string bindingId,
        ReadOnlyMemory<byte> payload,
        ContextIngressTrust ingressTrust,
        CancellationToken ct)
    {
        if (!_bindings.TryGetValue(bindingId, out var binding))
        {
            _logger.LogError("Communication ingress rejected unknown binding {BindingId}.", bindingId);
            return CommunicationDeliveryOutcome.Failed;
        }

        try
        {
            var wire = CommunicationWireCodec.Decode(payload);
            ValidateWire(binding.Declaration, wire, _meshId);
            CommunicationEnvelope envelope = binding.Target switch
            {
                EventHandlerBinding eventBinding => new EventEnvelope(
                    wire.OperationId,
                    wire.Ordinal,
                    eventBinding.EntityType,
                    wire.Payload,
                    wire.Context,
                    eventBinding.EventType,
                    wire.OccurrenceId ?? throw new InvalidDataException("An Event envelope has no occurrence identity."),
                    wire.OccurredAt ?? throw new InvalidDataException("An Event envelope has no occurrence timestamp."),
                    wire.HasDetails,
                    wire.DetailsPayload),
                TransportReceiverBinding transportBinding => new TransportEnvelope(
                    wire.OperationId,
                    wire.Ordinal,
                    transportBinding.EntityType,
                    wire.Payload,
                    wire.Context),
                FrameworkMessageTargetBinding signalBinding => new FrameworkSignalEnvelope(
                    wire.OperationId,
                    wire.Lane,
                    signalBinding.SignalType,
                    wire.Payload),
                _ => throw new InvalidDataException("The Communication binding has an unsupported target type.")
            };

            var outcome = await _ingress.Dispatch(binding.Target, envelope, ingressTrust, ct).ConfigureAwait(false);
            return outcome == CommunicationTargetOutcome.Filtered
                ? CommunicationDeliveryOutcome.Filtered
                : CommunicationDeliveryOutcome.Delivered;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CommunicationDeliveryOutcome.Failed;
        }
        catch (Exception error)
        {
            _logger.LogError(
                error,
                "Communication ingress failed binding {BindingId}; the provider will apply its declared failure policy.",
                bindingId);
            return CommunicationDeliveryOutcome.Failed;
        }
    }

    private static CommunicationRouteDecision Elect(
        CommunicationLane lane,
        string channel,
        string? explicitProvider,
        CommunicationAdapterCapabilities requirements,
        ContextIngressTrust minimumIngressTrust,
        IReadOnlyList<ICommunicationAdapter> candidates,
        IReadOnlySet<ICommunicationAdapter> directCandidates,
        ProviderCatalog<ICommunicationAdapter> providers)
    {
        var laneCandidates = candidates
            .Where(candidate => candidate.Descriptor.Lanes.Contains(lane))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(explicitProvider))
        {
            var pinned = providers.Find(explicitProvider);
            if (pinned is not null && !laneCandidates.Contains(pinned)) pinned = null;
            if (pinned is null)
                throw ElectionFailure(
                    lane,
                    channel,
                    $"explicit provider '{explicitProvider}' is not registered",
                    laneCandidates);
            EnsureEligible(lane, pinned, requirements, minimumIngressTrust);
            return Decision(
                lane,
                channel,
                pinned,
                "explicit-binding",
                directIntent: directCandidates.Contains(pinned),
                intent: ProviderIntentPosture.Required,
                providers: providers);
        }

        var direct = directCandidates.Where(laneCandidates.Contains).ToArray();
        var automatic = direct.Length > 0
            ? direct
            : laneCandidates
                .Where(static candidate => candidate.Descriptor.IsBuiltIn || candidate.Descriptor.IsLayered)
                .ToArray();
        var eligible = automatic
            .Where(candidate => IsEligible(candidate, requirements, minimumIngressTrust))
            .Select(providers.Describe)
            .ToArray();

        if (eligible.Length == 0)
        {
            var reason = direct.Length > 0
                ? "directly intended providers do not satisfy the lane's semantic requirements"
                : "the built-in provider floor is unavailable or ineligible";
            throw ElectionFailure(lane, channel, reason, automatic);
        }

        var selected = providers.Best(eligible, CompareCandidates)
            ?? throw ElectionFailure(lane, channel, "no eligible provider remained after ranking", automatic);
        return Decision(
            lane,
            channel,
            selected,
            direct.Length > 0
                ? "direct-reference-intent"
                : selected.Descriptor.IsLayered
                    ? "layered-capability"
                    : "built-in-floor",
            directIntent: direct.Length > 0,
            intent: ProviderIntentPosture.Automatic,
            providers: providers);
    }

    private static CommunicationRouteDecision Decision(
        CommunicationLane lane,
        string channel,
        ICommunicationAdapter adapter,
        string reason,
        bool directIntent,
        ProviderIntentPosture intent,
        ProviderCatalog<ICommunicationAdapter> providers)
    {
        var candidate = providers.Describe(adapter);
        return new CommunicationRouteDecision(
            lane,
            channel,
            adapter,
            new ProviderSelectionReceipt(
                RouteSubject(lane, channel),
                candidate.Id,
                intent,
                candidate.Priority,
                reason,
                directIntent));
    }

    private static string RouteSubject(CommunicationLane lane, string channel) => lane switch
    {
        CommunicationLane.Transport => Constants.Diagnostics.Subjects.TransportFor(channel),
        CommunicationLane.Events => Constants.Diagnostics.Subjects.EventsFor(channel),
        CommunicationLane.FrameworkSignals => Constants.Diagnostics.Subjects.FrameworkSignals,
        CommunicationLane.FrameworkBroadcasts => Constants.Diagnostics.Subjects.FrameworkBroadcasts,
        _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
    };

    private static ProviderCandidateDescriptor DescribeAdapter(ICommunicationAdapter adapter)
        => new(
            adapter.Descriptor.Id,
            adapter.Descriptor.Aliases,
            adapter.Descriptor.DirectReferenceIdentities
                .Concat(ProviderMetadata.References(adapter.GetType()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ProviderMetadata.Priority(adapter.GetType()));

    private static int CompareCandidates(
        ProviderCandidate<ICommunicationAdapter> left,
        ProviderCandidate<ICommunicationAdapter> right)
    {
        var assurance = right.Value.Descriptor.Assurance.CompareTo(left.Value.Descriptor.Assurance);
        return assurance != 0 ? assurance : right.Priority.CompareTo(left.Priority);
    }

    private static void EnsureEligible(
        CommunicationLane lane,
        ICommunicationAdapter adapter,
        CommunicationAdapterCapabilities requirements,
        ContextIngressTrust minimumIngressTrust)
    {
        if (!Enum.IsDefined(adapter.Descriptor.IngressTrust))
            throw new InvalidOperationException(
                $"Communication provider '{adapter.Descriptor.Id}' cannot carry {lane}: its declared ingress trust " +
                "is unknown. Declare one of Koan's supported context-ingress trust levels.");

        var missing = requirements & ~adapter.Descriptor.Capabilities;
        if (missing != CommunicationAdapterCapabilities.None)
            throw new InvalidOperationException(
                $"Communication provider '{adapter.Descriptor.Id}' cannot carry {lane}: missing {missing}. " +
                "Choose a provider that preserves the lane's semantic contract.");
        if (!Meets(adapter.Descriptor.IngressTrust, minimumIngressTrust))
            throw new InvalidOperationException(
                $"Communication provider '{adapter.Descriptor.Id}' cannot carry {lane}: its declared ingress trust " +
                $"is {adapter.Descriptor.IngressTrust}, but the composed context requires {minimumIngressTrust}. " +
                "Choose a provider that authenticates the required application context.");
    }

    private static bool IsEligible(
        ICommunicationAdapter adapter,
        CommunicationAdapterCapabilities requirements,
        ContextIngressTrust minimumIngressTrust)
        => (adapter.Descriptor.Capabilities & requirements) == requirements
           && Enum.IsDefined(adapter.Descriptor.IngressTrust)
           && Meets(adapter.Descriptor.IngressTrust, minimumIngressTrust);

    private static bool Meets(ContextIngressTrust provided, ContextIngressTrust required)
        => required switch
        {
            ContextIngressTrust.Unverified => true,
            ContextIngressTrust.Authenticated => provided is ContextIngressTrust.Authenticated or ContextIngressTrust.HostTrusted,
            ContextIngressTrust.HostTrusted => provided is ContextIngressTrust.HostTrusted,
            _ => false
        };

    private static InvalidOperationException ElectionFailure(
        CommunicationLane lane,
        string channel,
        string reason,
        IReadOnlyCollection<ICommunicationAdapter> candidates)
    {
        var choices = candidates.Count == 0
            ? "none"
            : string.Join(", ", candidates.Select(static candidate => candidate.Descriptor.Id));
        return new InvalidOperationException(
            $"Koan Communication could not elect {lane}/{channel} because {reason}. Candidates: {choices}. " +
            "Correct the explicit binding or direct connector references; Koan will not weaken the route silently.");
    }

    private static IReadOnlyList<ChannelProviders> PublicChannels(CommunicationOptions options)
    {
        var result = new List<ChannelProviders>
        {
            new(Constants.Channels.Default, options.TransportProvider, options.EventsProvider)
        };
        var names = new HashSet<string>(StringComparer.Ordinal) { Constants.Channels.Default };
        foreach (var (rawName, binding) in (options.Channels
                     ?? throw new InvalidOperationException(
                         $"{nameof(CommunicationOptions.Channels)} cannot be null."))
                 .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (binding is null)
            {
                throw new InvalidOperationException(
                    $"Communication channel '{rawName}' has no options value. Configure an object at " +
                    $"'{Constants.Configuration.Channels}:{rawName}'.");
            }

            var name = NormalizeChannel(rawName);
            if (string.Equals(name, Constants.Channels.Default, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{Constants.Channels.Default}' is the inferred Communication channel and cannot be redeclared under " +
                    $"'{Constants.Configuration.Channels}'. Use the top-level provider options instead.");
            }

            if (!names.Add(name))
            {
                throw new InvalidOperationException(
                    $"Communication channel '{name}' is declared more than once after case normalization.");
            }

            result.Add(new ChannelProviders(name, binding.TransportProvider, binding.EventsProvider));
        }

        return result;
    }

    private static string NormalizeChannel(string rawName)
    {
        var name = rawName.Trim().ToLowerInvariant();
        var valid = name.Length is > 0 and <= Constants.Channels.MaximumNameLength
                    && char.IsAsciiLetterOrDigit(name[0])
                    && name.All(static character =>
                        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Communication channel '{rawName}' is invalid. Channel names must start with a letter or digit, " +
                $"contain only letters, digits, '.', '_', or '-', and be at most " +
                $"{Constants.Channels.MaximumNameLength} characters.");
        }

        return name;
    }

    private static IReadOnlyDictionary<string, BoundTarget> BuildBindings(
        CommunicationHandlerCatalog handlers,
        IEnumerable<FrameworkMessageTargetBinding> frameworkSignals,
        IReadOnlyList<CommunicationRouteDecision> routes)
    {
        var result = new Dictionary<string, BoundTarget>(StringComparer.Ordinal);
        var transportChannels = routes
            .Where(static route => route.Lane == CommunicationLane.Transport)
            .Select(static route => route.Channel)
            .ToArray();
        var eventChannels = routes
            .Where(static route => route.Lane == CommunicationLane.Events)
            .Select(static route => route.Channel)
            .ToArray();
        foreach (var target in handlers.TransportReceivers)
        {
            var contract = CommunicationContractIdentity.Transport(target.EntityType);
            foreach (var channel in transportChannels)
                Add(result, target, CommunicationLane.Transport, channel, contract);
        }

        foreach (var target in handlers.EventSubscriptions)
        {
            var contract = CommunicationContractIdentity.Events(target.EntityType, target.EventType);
            foreach (var channel in eventChannels)
                Add(result, target, CommunicationLane.Events, channel, contract);
        }

        foreach (var target in frameworkSignals
                     .OrderBy(static target => target.Lane)
                     .ThenBy(static target => target.ContractId, StringComparer.Ordinal))
        {
            Add(result, target, target.Lane, Constants.Channels.Default, target.ContractId, target.Scope);
        }

        return result;
    }

    private static void Add(
        IDictionary<string, BoundTarget> result,
        CommunicationTargetBinding target,
        CommunicationLane lane,
        string channel,
        string contract,
        CommunicationBindingScope scope = CommunicationBindingScope.ConsumerGroup)
    {
        var id = $"{lane.ToString().ToLowerInvariant()}|{channel}|{contract}|{target.GroupIdentity}";
        var declaration = new CommunicationAdapterBinding(
            id,
            lane,
            channel,
            contract,
            target.GroupIdentity,
            scope);
        result.Add(id, new BoundTarget(declaration, target));
    }

    private static void ValidateWire(
        CommunicationAdapterBinding binding,
        CommunicationWireEnvelope wire,
        string meshId)
    {
        if (wire.Schema != CommunicationWireCodec.SchemaVersion
            || !string.Equals(wire.Mesh, meshId, StringComparison.Ordinal)
            || wire.Lane != binding.Lane
            || !string.Equals(wire.Channel, binding.Channel, StringComparison.Ordinal)
            || !string.Equals(wire.Contract, binding.ContractId, StringComparison.Ordinal)
            || wire.OperationId == Guid.Empty
            || wire.Ordinal < 0
            || wire.Payload is null)
        {
            throw new InvalidDataException(
                "The Communication envelope does not match the elected mesh, lane, channel, contract, or schema.");
        }
    }

    private sealed record BoundTarget(
        CommunicationAdapterBinding Declaration,
        CommunicationTargetBinding Target);

    private sealed record ChannelProviders(
        string Name,
        string? TransportProvider,
        string? EventsProvider);
}
