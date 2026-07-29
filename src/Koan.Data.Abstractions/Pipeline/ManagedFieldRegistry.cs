using System.Runtime.CompilerServices;
using Koan.Core.Composition;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Abstractions.Pipeline;

/// <summary>Host-owned managed-field declarations behind Koan's terse composition facade.</summary>
public static class ManagedFieldRegistry
{
    public static bool IsEmpty => Current()?.IsEmpty ?? true;

    public static IReadOnlyList<ManagedFieldDescriptor> All => Current()?.All ?? [];

    public static void Register(ManagedFieldDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValidateStorageName(descriptor.StorageName);
        Composition().Register(descriptor);
    }

    public static IReadOnlyList<ManagedFieldDescriptor> ForType(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Current()?.ForType(entityType) ?? [];
    }

    public static IReadOnlyList<ManagedFieldDescriptor> EqualityFields(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Current()?.EqualityFields(entityType) ?? [];
    }

    public static void ValidateStorageName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A managed field StorageName must be a non-empty value.", nameof(name));
        EntityFamilyStorage.EnsureFieldAvailable(name, "A managed field");
        var stable = name[0] == '_' || !name.Any(char.IsUpper);
        if (!stable)
            throw new ArgumentException(
                $"Managed field StorageName '{name}' is not camel-case-stable. It must lead with '_' or contain no " +
                "uppercase letters, so the write literal and a camel-cased read leaf stay identical across adapters.",
                nameof(name));
    }

    /// <summary>Clears only the current composition or active host; it never changes another host.</summary>
    public static void Reset() => Current()?.Reset();

    private static ManagedFieldCatalog Composition()
    {
        var services = KoanCompositionScope.RequireServices("Managed-field registration");
        var catalog = services.Where(static descriptor => descriptor.ServiceType == typeof(ManagedFieldCatalog))
            .Select(static descriptor => descriptor.ImplementationInstance).OfType<ManagedFieldCatalog>().LastOrDefault();
        if (catalog is not null) return catalog;
        catalog = new ManagedFieldCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }

    private static ManagedFieldCatalog? Current()
    {
        if (KoanCompositionScope.TryGetServices(out var services))
            return services.Where(static descriptor => descriptor.ServiceType == typeof(ManagedFieldCatalog))
                .Select(static descriptor => descriptor.ImplementationInstance).OfType<ManagedFieldCatalog>().LastOrDefault();
        return AppHost.Current?.GetService(typeof(ManagedFieldCatalog)) as ManagedFieldCatalog;
    }

    internal sealed class ManagedFieldCatalog
    {
        private readonly object _gate = new();
        private readonly List<ManagedFieldDescriptor> _descriptors = [];
        private ConditionalWeakTable<Type, TypeFields> _byType = new();
        private volatile bool _isEmpty = true;

        public bool IsEmpty => _isEmpty;
        public IReadOnlyList<ManagedFieldDescriptor> All
        {
            get { lock (_gate) return _descriptors.OrderBy(static descriptor => descriptor.Priority).ToArray(); }
        }

        public void Register(ManagedFieldDescriptor descriptor)
        {
            lock (_gate)
            {
                if (_descriptors.Any(item => string.Equals(item.StorageName, descriptor.StorageName, StringComparison.Ordinal)))
                    return;
                _descriptors.Add(descriptor);
                _byType = new ConditionalWeakTable<Type, TypeFields>();
                _isEmpty = false;
            }
        }

        public IReadOnlyList<ManagedFieldDescriptor> ForType(Type type) =>
            _isEmpty ? [] : _byType.GetValue(type, Build).All;

        public IReadOnlyList<ManagedFieldDescriptor> EqualityFields(Type type) =>
            _isEmpty ? [] : _byType.GetValue(type, Build).Equality;

        public void Reset()
        {
            lock (_gate)
            {
                _descriptors.Clear();
                _byType = new ConditionalWeakTable<Type, TypeFields>();
                _isEmpty = true;
            }
        }

        private TypeFields Build(Type type)
        {
            ManagedFieldDescriptor[] snapshot;
            lock (_gate) snapshot = _descriptors.ToArray();
            var all = snapshot.Where(item => item.AppliesTo(type)).OrderBy(static item => item.Priority).ToArray();
            return new TypeFields(all, all.Where(static item => item.AutoReadFilter).ToArray());
        }

        private sealed record TypeFields(
            IReadOnlyList<ManagedFieldDescriptor> All,
            IReadOnlyList<ManagedFieldDescriptor> Equality);
    }
}
