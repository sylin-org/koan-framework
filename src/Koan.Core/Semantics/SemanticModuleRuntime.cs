using System.Collections.Immutable;
using Koan.Core.Ordering;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Composition;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Core.Semantics;

/// <summary>Retains the exact module instances constructed, registered, and started for one host.</summary>
internal sealed class SemanticModuleRuntime
{
    private readonly ImmutableArray<ModuleEntry> _modules;
    private readonly HashSet<Type> _registeredTypes = [];
    private readonly object _registrationGate = new();

    private SemanticModuleRuntime(ImmutableArray<ModuleEntry> modules)
    {
        _modules = modules;
    }

    public static SemanticModuleRuntime Create(SemanticHostConstitution constitution)
    {
        ArgumentNullException.ThrowIfNull(constitution);
        if (constitution.Problems.Length > 0)
        {
            var problem = constitution.Problems[0];
            throw new SemanticRuntimeException(problem);
        }

        var modules = ImmutableArray.CreateBuilder<ModuleEntry>(constitution.ActiveDescriptors.Length);
        foreach (var descriptor in constitution.ActiveDescriptors)
        {
            KoanModule module;
            try
            {
                module = descriptor.Factory()
                    ?? throw new InvalidOperationException("The module factory returned null.");
            }
            catch (Exception exception)
            {
                var problem = new SemanticProblem(
                    descriptor.Id,
                    "module-factory-failed",
                    $"Fix the constructor for '{descriptor.ImplementationType.FullName}' or remove the capability reference.");
                throw new SemanticRuntimeException(problem, exception);
            }

            if (!descriptor.ImplementationType.IsInstanceOfType(module))
            {
                throw new SemanticRuntimeException(new SemanticProblem(
                    descriptor.Id,
                    "module-factory-type-mismatch",
                    $"Make the descriptor factory return '{descriptor.ImplementationType.FullName}'."));
            }

            module.BindSemanticIdentity(descriptor.Id);
            modules.Add(new ModuleEntry(descriptor, module));
        }

        return new SemanticModuleRuntime(modules.ToImmutable());
    }

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var type in OrderLifecycleTypes()) TryRegister(type, services);
    }

    public KoanModule GetModule(SemanticId id) =>
        _modules.FirstOrDefault(entry => entry.Descriptor.Id == id).Module
        ?? throw new KeyNotFoundException($"Semantic module '{id}' is not active in this host.");

    internal IReadOnlyList<KoanModule> Modules => _modules.Select(static entry => entry.Module).ToArray();

    internal IReadOnlyList<Type> ImplementationTypes =>
        _modules.Select(static entry => entry.Descriptor.ImplementationType).ToArray();

    internal void ReportComposition(KoanCompositionBuilder builder, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(services);

        foreach (var entry in _modules)
        {
            try
            {
                entry.Module.ReportComposition(builder, services);
            }
            catch (Exception exception)
            {
                var type = entry.Descriptor.ImplementationType;
                builder.AddFact(KoanFact.Create(
                    Constants.Diagnostics.Codes.CollectionFailed,
                    KoanFactKind.Degradation,
                    KoanFactState.CollectionFailed,
                    entry.Descriptor.Id.Value,
                    "An active Koan module could not report its runtime composition evidence.",
                    Constants.Diagnostics.Reasons.ReporterFailed,
                    "Inspect the named module and retry startup after correcting its evidence projection.",
                    type.Assembly.GetName().Name ?? "composition",
                    $"composition:{entry.Descriptor.Id.Value.ToLowerInvariant()}:{exception.GetType().Name}"));
            }
        }
    }

    internal Type[] OrderLifecycleTypes()
    {
        return ModuleOrdering.Sort(
            ImplementationTypes,
            type => _modules.FirstOrDefault(entry => entry.Descriptor.ImplementationType == type).Descriptor?.Id.Value
                ?? type.AssemblyQualifiedName
                ?? type.FullName
                ?? type.Name,
            Array.Empty<(Type From, Type To)>());
    }

    internal bool TryRegister(Type implementationType, IServiceCollection services)
    {
        var entry = _modules.FirstOrDefault(candidate => candidate.Descriptor.ImplementationType == implementationType);
        if (entry.Module is null) return false;
        lock (_registrationGate)
        {
            if (_registeredTypes.Contains(implementationType)) return true;
            try
            {
                entry.Module.Register(services);
            }
            catch (Exception exception)
            {
                throw new SemanticRuntimeException(
                    new SemanticProblem(
                        entry.Descriptor.Id,
                        "module-registration-failed",
                        $"Fix the registration performed by '{entry.Descriptor.ImplementationType.FullName}' or remove the capability reference."),
                    exception);
            }

            _registeredTypes.Add(implementationType);
        }

        return true;
    }

    internal bool TryGetModule(Type implementationType, out KoanModule module)
    {
        var entry = _modules.FirstOrDefault(candidate => candidate.Descriptor.ImplementationType == implementationType);
        module = entry.Module!;
        return module is not null;
    }

    private readonly record struct ModuleEntry(
        SemanticComponentDescriptor Descriptor,
        KoanModule Module);

    internal sealed class SemanticRuntimeException : InvalidOperationException
    {
        public SemanticRuntimeException(SemanticProblem problem, Exception? innerException = null)
            : base(
                $"Koan could not activate '{problem.Owner}': {problem.Reason}. {problem.Correction}" +
                (innerException is null ? string.Empty : $" {innerException.Message}"),
                innerException)
        {
            Problem = problem;
        }

        public SemanticProblem Problem { get; }
    }
}
