using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Koan.Canon.Web.Catalog;

/// <summary>
/// Provides discovery metadata for canonical models exposed via HTTP surfaces.
/// </summary>
public interface ICanonModelCatalog
{
    /// <summary>
    /// Gets all registered canonical descriptors.
    /// </summary>
    IReadOnlyList<CanonModelDescriptor> All { get; }

    /// <summary>
    /// Attempts to resolve a descriptor by slug.
    /// </summary>
    bool TryGetBySlug(string slug, [MaybeNullWhen(false)] out CanonModelDescriptor descriptor);

    /// <summary>
    /// Attempts to resolve a descriptor by CLR type.
    /// </summary>
    bool TryGetByType(Type modelType, [MaybeNullWhen(false)] out CanonModelDescriptor descriptor);
}
