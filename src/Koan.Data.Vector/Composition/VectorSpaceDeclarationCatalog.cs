using System.Collections.Concurrent;
using Koan.Core.Composition;
using Koan.Data.Core.Routing;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Vector;

internal sealed class VectorSpaceDeclarationCatalog
{
    private readonly object _gate = new();
    private Dictionary<(Type Entity, string Source), VectorSpacePlan> _plans = new();
    private readonly ConcurrentDictionary<(Type Entity, string Source), VectorSpacePlan> _axisPlans = new();
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

    public VectorSpacePlan Resolve(Type entityType, RoutedSource route)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        Freeze();
        var routedSource = route.Source;
        if (!string.IsNullOrWhiteSpace(routedSource))
        {
            var key = Key(entityType, routedSource);
            if (_plans.TryGetValue(key, out var exact)) return exact;
            if (route.Kind == RouteKind.DatabaseAxis)
            {
                if (_axisPlans.TryGetValue(key, out var routed)) return routed;
                var template = SinglePlan(entityType, routedSource);
                return _axisPlans.GetOrAdd(
                    key,
                    static (_, state) => new VectorSpacePlan(
                        state.Source,
                        state.Template.Name,
                        state.Template.Dimensions,
                        state.Template.Metric,
                        state.Template.Visibility,
                        state.Template.Model),
                    (Source: routedSource, Template: template));
            }

            throw new InvalidOperationException(
                $"Vector entity '{entityType.Name}' has no space declared for routed source '{routedSource}'. " +
                "Declare it with koan.Data.Source(...).Vector<TEntity>(...) or correct the source context.");
        }

        return SinglePlan(entityType, routedSource: null);
    }

    private VectorSpacePlan SinglePlan(Type entityType, string? routedSource)
    {
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
                routedSource is null
                    ? $"Vector entity '{entityType.Name}' has spaces on multiple sources: " +
                      $"{string.Join(", ", candidates.Select(static plan => plan.Source))}. " +
                      "Select one with EntityContext.Source(...)."
                    : $"Database-axis route '{routedSource}' cannot choose a vector shape for entity " +
                      $"'{entityType.Name}' because it has spaces on multiple sources: " +
                      $"{string.Join(", ", candidates.Select(static plan => plan.Source))}. " +
                      "Declare that routed source explicitly or keep one source-independent vector shape.")
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
