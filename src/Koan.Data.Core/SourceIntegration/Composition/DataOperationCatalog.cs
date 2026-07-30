using Koan.Core.Composition;
using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core;

internal sealed class DataOperationCatalog
{
    private readonly object _gate = new();
    private Dictionary<(string Source, string Name), OperationPlan> _plans = new();
    private HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);
    private bool _frozen;

    internal static RecordSetLimits DefaultLimits { get; } = new(
        Infrastructure.Constants.Defaults.SourceMaxRecords,
        Infrastructure.Constants.Defaults.SourceMaxBytes,
        Infrastructure.Constants.Defaults.SourceMaxValueBytes,
        TimeSpan.FromSeconds(Infrastructure.Constants.Defaults.SourceMaxDurationSeconds));

    public static void Declare(OperationPlan plan)
    {
        var services = KoanCompositionScope.RequireServices($"Data source operation '{plan.Name}'");
        var catalog = services
            .Where(descriptor => descriptor.ServiceType == typeof(DataOperationCatalog))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DataOperationCatalog>()
            .LastOrDefault();
        if (catalog is null)
        {
            catalog = new DataOperationCatalog();
            services.AddSingleton(catalog);
        }
        catalog.Add(plan);
    }

    public static void DeclareSource(string source)
    {
        var services = KoanCompositionScope.RequireServices($"Data source '{source}'");
        var catalog = services
            .Where(descriptor => descriptor.ServiceType == typeof(DataOperationCatalog))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DataOperationCatalog>()
            .LastOrDefault();
        if (catalog is null)
        {
            catalog = new DataOperationCatalog();
            services.AddSingleton(catalog);
        }
        catalog.AddSource(source);
    }

    public OperationPlan Require(string source, string name)
    {
        Freeze();
        return _plans.TryGetValue(Key(source, name), out var plan)
            ? plan
            : throw new RegisteredOperationException(
                source,
                name,
                "Declare it once inside AddKoan(koan => ...) for this source.");
    }

    public IReadOnlyList<OperationPlan> Snapshot()
    {
        Freeze();
        return _plans.Values.OrderBy(static plan => plan.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void Add(OperationPlan plan)
    {
        lock (_gate)
        {
            if (_frozen) throw new InvalidOperationException("The registered Data operation catalog is already frozen.");
            if (!_plans.TryAdd(Key(plan.Source, plan.Name), plan))
                throw new InvalidOperationException(
                    $"Registered operation '{plan.Name}' is already declared for source '{plan.Source}'.");
        }
    }

    private void AddSource(string source)
    {
        lock (_gate)
        {
            if (_frozen) throw new InvalidOperationException("The registered Data operation catalog is already frozen.");
            _sources.Add(source.Trim());
        }
    }

    private void Freeze()
    {
        if (_frozen) return;
        lock (_gate)
        {
            if (_frozen) return;
            _plans = new Dictionary<(string Source, string Name), OperationPlan>(_plans);
            _sources = new HashSet<string>(_sources, StringComparer.OrdinalIgnoreCase);
            _frozen = true;
        }
    }

    private static (string Source, string Name) Key(string source, string name)
        => (source.Trim().ToUpperInvariant(), name.Trim().ToUpperInvariant());
}
