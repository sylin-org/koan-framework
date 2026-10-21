namespace Koan.Data.Abstractions;

/// <summary>
/// The reserved store marker (DATA-0148): one random id each store holds, so two endpoints can be compared by what they
/// read rather than by how their connections are spelled. KoanRS reserves the same name.
/// </summary>
public static class StoreMarker
{
    /// <summary>The reserved table, collection, key or file name that holds the marker.</summary>
    public const string Name = "__koan_store";

    /// <summary>A new marker id: 32 lowercase hex characters from a random GUID.</summary>
    public static string NewId() => Guid.NewGuid().ToString("n");

    /// <summary>Whether a container name is the marker, which no listing reports as an entity set.</summary>
    public static bool Is(string? name) => string.Equals(name, Name, StringComparison.OrdinalIgnoreCase);
}
