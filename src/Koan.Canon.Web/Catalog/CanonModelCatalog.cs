using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Koan.Canon.Web.Catalog;

public sealed class CanonModelCatalog : ICanonModelCatalog
{
    private readonly IReadOnlyList<CanonModelDescriptor> _all;
    private readonly ConcurrentDictionary<string, CanonModelDescriptor> _bySlug;
    private readonly ConcurrentDictionary<Type, CanonModelDescriptor> _byType;

    public CanonModelCatalog(IEnumerable<CanonModelDescriptor> descriptors)
    {
        if (descriptors is null)
        {
            throw new ArgumentNullException(nameof(descriptors));
        }

        _all = descriptors.OfType<CanonModelDescriptor>().ToArray();

        _bySlug = new ConcurrentDictionary<string, CanonModelDescriptor>(StringComparer.OrdinalIgnoreCase);
        _byType = new ConcurrentDictionary<Type, CanonModelDescriptor>();

        foreach (var descriptor in _all)
        {
            var slug = descriptor.Slug;
            if (!string.IsNullOrWhiteSpace(slug))
            {
                _bySlug[slug!] = descriptor;
            }

            var modelType = descriptor.ModelType;
            ArgumentNullException.ThrowIfNull(modelType);
            _byType[modelType] = descriptor;
        }
    }

    public IReadOnlyList<CanonModelDescriptor> All => _all;

    public bool TryGetBySlug(string slug, [MaybeNullWhen(false)] out CanonModelDescriptor descriptor)
        => _bySlug.TryGetValue(slug, out descriptor);

    public bool TryGetByType(Type modelType, [MaybeNullWhen(false)] out CanonModelDescriptor descriptor)
        => _byType.TryGetValue(modelType, out descriptor);
}
