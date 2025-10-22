using System;

namespace Koan.Canon.Web.Catalog;

/// <summary>
/// Describes a canonical model or value object exposed through the web surface.
/// </summary>
public sealed record CanonModelDescriptor
{
    public CanonModelDescriptor(Type modelType, string slug, string displayName, string route, bool isValueObject)
    {
        ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        Slug = string.IsNullOrWhiteSpace(slug) ? throw new ArgumentException("Slug must be provided.", nameof(slug)) : slug;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("Display name must be provided.", nameof(displayName)) : displayName;
        Route = string.IsNullOrWhiteSpace(route) ? throw new ArgumentException("Route must be provided.", nameof(route)) : route;
        IsValueObject = isValueObject;
    }

    public Type ModelType { get; }

    public string Slug { get; }

    public string DisplayName { get; }

    public string Route { get; }

    public bool IsValueObject { get; }
}
