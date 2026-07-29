using Koan.Core.Composition;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Vector;

internal sealed class VectorSpaceDeclarationCatalog
{
    private readonly object _gate = new();
    private Dictionary<(Type Entity, string Source), VectorSpacePlan> _plans = new();
    private bool _frozen;

    public static void Declare(Type entityType, VectorSpacePlan plan)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(plan);
        var services = KoanCompositionScope.RequireServices(
            $"Vector space '{plan.Name}' for '{entityType.FullName}'");
        var catalog = services
            .Where(static descriptor => descriptor.ServiceType == typeof(VectorSpaceDeclarationCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<VectorSpaceDeclarationCatalog>()
            .LastOrDefault();
        if (catalog is null)
        {
            catalog = new VectorSpaceDeclarationCatalog();
            services.AddSingleton(catalog);
        }
        catalog.Add(entityType, plan);
    }

    public VectorSpacePlan Resolve(Type entityType, string? routedSource)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        Freeze();
        if (!string.IsNullOrWhiteSpace(routedSource))
        {
            var key = Key(entityType, routedSource);
            return _plans.TryGetValue(key, out var exact)
                ? exact
                : throw new InvalidOperationException(
                    $"Vector entity '{entityType.Name}' has no space declared for routed source '{routedSource}'. " +
                    "Declare it with koan.Data.Source(...).Vector<TEntity>(...) or correct the source context.");
        }

        var candidates = _plans
            .Where(entry => entry.Key.Entity == entityType)
            .Select(static entry => entry.Value)
            .OrderBy(static plan => plan.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"Vector entity '{entityType.Name}' has no declared space. Declare one inside " +
                "AddKoan(koan => koan.Data.Source(...).Vector<TEntity>(...))."),
            _ => throw new InvalidOperationException(
                $"Vector entity '{entityType.Name}' has spaces on multiple sources: " +
                $"{string.Join(", ", candidates.Select(static plan => plan.Source))}. " +
                "Select one with EntityContext.Source(...).")
        };
    }

    private void Add(Type entityType, VectorSpacePlan plan)
    {
        lock (_gate)
        {
            if (_frozen) throw new InvalidOperationException("The Vector space declaration catalog is already frozen.");
            if (!_plans.TryAdd(Key(entityType, plan.Source), plan))
                throw new InvalidOperationException(
                    $"Vector entity '{entityType.Name}' already has a space declared on source '{plan.Source}'. " +
                    "Declare one space per source/entity pair.");
        }
    }

    private void Freeze()
    {
        if (_frozen) return;
        lock (_gate)
        {
            if (_frozen) return;
            _plans = new Dictionary<(Type Entity, string Source), VectorSpacePlan>(_plans);
            _frozen = true;
        }
    }

    private static (Type Entity, string Source) Key(Type entityType, string source) =>
        (entityType, source.Trim().ToUpperInvariant());
}
