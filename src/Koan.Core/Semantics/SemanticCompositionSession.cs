using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Composition;
using Koan.Core.Semantics.Contributions;

namespace Koan.Core.Semantics;

/// <summary>Owns semantic composition state for exactly one service collection until it freezes.</summary>
internal sealed class SemanticCompositionSession
{
    private readonly IServiceCollection _services;
    private readonly object _gate = new();
    private int _compositionDepth;
    private int _coreConfigured;
    private int _moduleInitialization;
    private Exception? _moduleInitializationFailure;
    private Exception? _compositionFailure;
    private readonly Dictionary<Type, IScheduledContributionTarget> _scheduledTargets = [];
    private int _contributionFinalization;
    private bool _frozen;

    private SemanticCompositionSession(IServiceCollection services)
    {
        _services = services;
    }

    public SemanticHostConstitution? Constitution { get; private set; }

    public SemanticModuleRuntime? Modules { get; private set; }

    public bool IsFrozen
    {
        get
        {
            lock (_gate) return _frozen;
        }
    }

    public static SemanticCompositionSession GetOrCreate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        lock (services)
        {
            var existing = services
                .Where(static descriptor => descriptor.ServiceType == typeof(SemanticCompositionSession))
                .Select(static descriptor => descriptor.ImplementationInstance)
                .OfType<SemanticCompositionSession>()
                .SingleOrDefault();
            if (existing is not null) return existing;

            var created = new SemanticCompositionSession(services);
            services.AddSingleton(created);
            return created;
        }
    }

    public CompositionLease Enter()
    {
        lock (_gate)
        {
            ThrowIfCompositionFaulted();
            if (_frozen)
            {
                throw new InvalidOperationException(
                    "This Koan application composition is already frozen. Put business declarations in the initial AddKoan(() => ...) call.");
            }

            _compositionDepth++;
            return new CompositionLease(this);
        }
    }

    public bool TryConfigureCore() => Interlocked.CompareExchange(ref _coreConfigured, 1, 0) == 0;

    public void ScheduleContributions<TTarget, TPlan>(
        Func<SemanticId, TTarget> targetForOwner,
        Func<TPlan> freeze,
        Action<IServiceCollection, TPlan> commit)
    {
        ArgumentNullException.ThrowIfNull(targetForOwner);
        ArgumentNullException.ThrowIfNull(freeze);
        ArgumentNullException.ThrowIfNull(commit);

        lock (_gate)
        {
            ThrowIfCompositionFaulted();
            if (_frozen || _contributionFinalization != 0)
            {
                throw new InvalidOperationException(
                    "Semantic contribution targets must be scheduled before the successful outermost AddKoan declaration completes.");
            }

            if (!_scheduledTargets.TryAdd(
                    typeof(TTarget),
                    new ScheduledContributionTarget<TTarget, TPlan>(targetForOwner, freeze, commit)))
            {
                throw new InvalidOperationException(
                    $"Semantic contribution target '{typeof(TTarget).FullName}' already has a compiler for this host. Keep one concern-owned target compiler.");
            }
        }
    }

    public bool TryBeginModuleInitialization()
    {
        if (Volatile.Read(ref _moduleInitialization) == 3)
        {
            var failure = _moduleInitializationFailure;
            throw new InvalidOperationException(
                "The previous Koan module initialization failed for this service collection. " +
                "Fix the reported cause and start with a new service collection; partial registration cannot be retried safely." +
                (failure is null ? string.Empty : $" Original failure: {failure.Message}"),
                failure);
        }

        return Interlocked.CompareExchange(ref _moduleInitialization, 1, 0) == 0;
    }

    public void FailModuleInitialization(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _moduleInitializationFailure = failure;
        Volatile.Write(ref _moduleInitialization, 3);
        FailComposition(failure);
    }

    public void FailComposition(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        lock (_gate) _compositionFailure ??= failure;
    }

    public void CompleteModuleInitialization(
        KoanApplicationReferenceManifest manifest,
        SemanticHostConstitution constitution,
        SemanticModuleRuntime modules)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(constitution);
        ArgumentNullException.ThrowIfNull(modules);

        Constitution = constitution;
        Modules = modules;
        _services.AddSingleton(manifest);
        _services.AddSingleton(constitution);
        _services.AddSingleton(modules);
        Volatile.Write(ref _moduleInitialization, 2);
    }

    private void Exit(bool complete)
    {
        var finalize = false;
        lock (_gate)
        {
            if (_compositionDepth <= 0) return;
            finalize = complete && _compositionDepth == 1 && _contributionFinalization == 0;
        }

        if (finalize) FinalizeContributions();

        lock (_gate)
        {
            _compositionDepth--;
            if (complete && _compositionDepth == 0) _frozen = true;
        }
    }

    private void PrepareCompletion()
    {
        var finalize = false;
        lock (_gate)
        {
            ThrowIfCompositionFaulted();
            finalize = _compositionDepth == 1 && _contributionFinalization == 0;
        }

        if (finalize) FinalizeContributions();
    }

    private void FinalizeContributions()
    {
        IScheduledContributionTarget[] scheduled;
        SemanticHostConstitution? constitution = null;
        SemanticModuleRuntime? modules = null;
        lock (_gate)
        {
            ThrowIfCompositionFaulted();
            if (_contributionFinalization == 2) return;
            if (_contributionFinalization != 0)
            {
                throw new InvalidOperationException("Semantic contribution targets are already being finalized for this host.");
            }

            scheduled = _scheduledTargets.Values
                .OrderBy(static target => target.TargetType.AssemblyQualifiedName, StringComparer.Ordinal)
                .ToArray();
            if (scheduled.Length > 0)
            {
                if (Constitution is null || Modules is null)
                {
                    var failure = new InvalidOperationException(
                        "Koan cannot compile semantic contributions before the host constitution and active retained modules exist.");
                    _contributionFinalization = 3;
                    _compositionFailure ??= failure;
                    throw failure;
                }

                constitution = Constitution;
                modules = Modules;
            }
            _contributionFinalization = 1;
        }

        try
        {
            var pending = scheduled
                .Select(target => target.Compile(constitution!, modules!))
                .ToArray();
            var snapshot = new SemanticContributionCompilationSnapshot(
                pending.Select(static result => result.Snapshot));

            foreach (var result in pending) result.Commit(_services);
            _services.AddSingleton(snapshot);

            lock (_gate) _contributionFinalization = 2;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _contributionFinalization = 3;
                _compositionFailure ??= exception;
            }

            throw;
        }
    }

    private void ThrowIfCompositionFaulted()
    {
        if (_compositionFailure is null) return;
        throw new InvalidOperationException(
            "Koan application composition failed after the service collection may have changed. " +
            "Fix the reported cause and start with a new service collection; partial composition cannot be retried safely. " +
            $"Original failure: {_compositionFailure.Message}",
            _compositionFailure);
    }

    internal sealed class CompositionLease : IDisposable
    {
        private SemanticCompositionSession? _owner;
        private bool _complete;

        internal CompositionLease(SemanticCompositionSession owner)
        {
            _owner = owner;
        }

        public void Complete()
        {
            _owner?.PrepareCompletion();
            _complete = true;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Exit(_complete);
        }
    }

    private interface IScheduledContributionTarget
    {
        Type TargetType { get; }

        SemanticPendingContributionResult Compile(
            SemanticHostConstitution constitution,
            SemanticModuleRuntime modules);
    }

    private sealed class ScheduledContributionTarget<TTarget, TPlan>(
        Func<SemanticId, TTarget> targetForOwner,
        Func<TPlan> freeze,
        Action<IServiceCollection, TPlan> commit) : IScheduledContributionTarget
    {
        public Type TargetType => typeof(TTarget);

        public SemanticPendingContributionResult Compile(
            SemanticHostConstitution constitution,
            SemanticModuleRuntime modules)
        {
            var snapshot = SemanticContributionCompiler.Compile(
                constitution,
                modules,
                targetForOwner);
            var plan = freeze();
            return new SemanticPendingContributionResult(
                snapshot,
                services => commit(services, plan));
        }
    }
}
