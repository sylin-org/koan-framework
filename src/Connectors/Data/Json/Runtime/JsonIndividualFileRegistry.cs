using System.Collections.Concurrent;

namespace Koan.Data.Connector.Json.Runtime;

/// <summary>
/// Coordinates individual-file mutations with fixed host memory and protects storage-independent templates from
/// being claimed by incompatible Entity roots.
/// </summary>
internal sealed class JsonIndividualFileRegistry
{
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(
            0,
            Infrastructure.Constants.Provider.IndividualFileLockStripes)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();
    private readonly ConcurrentDictionary<string, LayoutOwner> _unqualifiedLayouts = new(JsonFileRegistry.PathComparer);

    internal SemaphoreSlim Gate(string canonicalPath)
    {
        var hash = unchecked((uint)JsonFileRegistry.PathComparer.GetHashCode(canonicalPath));
        return _gates[hash % (uint)_gates.Length];
    }

    internal void ClaimUnqualifiedLayout(string directoryPath, string template, Type rootType, Type keyType)
    {
        var key = Path.GetFullPath(directoryPath) + "\0" + template;
        var requested = new LayoutOwner(rootType, keyType);
        var owner = _unqualifiedLayouts.GetOrAdd(key, requested);
        if (owner == requested) return;

        throw new InvalidOperationException(
            $"JSON IndividualFilePath '{template}' under '{directoryPath}' omits '{{storage}}' and is already owned " +
            $"by Entity root '{owner.RootType.FullName}' with key '{owner.KeyType.FullName}', not " +
            $"'{rootType.FullName}'/'{keyType.FullName}'. Use a dedicated source or include '{{storage}}' in the path.");
    }

    private sealed record LayoutOwner(Type RootType, Type KeyType);
}
