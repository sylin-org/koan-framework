using Koan.Core.Composition;
using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Mapping.Composition;

internal sealed class MappingDeclarationCatalog
{
    private readonly object _gate = new();
    private Dictionary<(string Source, Type Entity), MappingDescriptor> _descriptors = new();
    private bool _frozen;

    public static void Declare(MappingDescriptor descriptor)
    {
        var services = KoanCompositionScope.RequireServices($"Data mapping for '{descriptor.EntityType.FullName}'");
        var catalog = services
            .Where(static item => item.ServiceType == typeof(MappingDeclarationCatalog))
            .Select(static item => item.ImplementationInstance)
            .OfType<MappingDeclarationCatalog>()
            .LastOrDefault();
        if (catalog is null)
        {
            catalog = new MappingDeclarationCatalog();
            services.AddSingleton(catalog);
        }
        catalog.Add(descriptor);
    }

    public IReadOnlyList<MappingDescriptor> Snapshot()
    {
        Freeze();
        return _descriptors.Values
            .OrderBy(static descriptor => descriptor.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static descriptor => descriptor.EntityType.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryGet(string source, Type entityType, out MappingDescriptor descriptor)
    {
        Freeze();
        return _descriptors.TryGetValue(Key(source, entityType), out descriptor!);
    }

    private void Add(MappingDescriptor descriptor)
    {
        lock (_gate)
        {
            if (_frozen) throw new InvalidOperationException("The Data mapping declaration catalog is already frozen.");
            if (!_descriptors.TryAdd(Key(descriptor.Source, descriptor.EntityType), descriptor))
                throw new MappingCompilationException(
                    descriptor.Source,
                    descriptor.EntityType,
                    "Declare one map per source/entity pair.");
        }
    }

    private void Freeze()
    {
        if (_frozen) return;
        lock (_gate)
        {
            if (_frozen) return;
            _descriptors = new Dictionary<(string Source, Type Entity), MappingDescriptor>(_descriptors);
            _frozen = true;
        }
    }

    private static (string Source, Type Entity) Key(string source, Type entityType) =>
        (source.Trim().ToUpperInvariant(), entityType);
}
