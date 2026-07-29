using System.Collections.Concurrent;

namespace Koan.Data.Connector.Json.Runtime;

/// <summary>Owns the finite set of canonical files and their live snapshots for one Koan host.</summary>
internal sealed class JsonFileRegistry
{
    private readonly ConcurrentDictionary<string, JsonFileSlot> _files = new(PathComparer);
    private readonly object _admission = new();

    internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal bool TryGet(string canonicalPath, out JsonFileSlot slot) =>
        _files.TryGetValue(canonicalPath, out slot!);

    internal JsonFileSlot Get(string canonicalPath)
    {
        if (_files.TryGetValue(canonicalPath, out var existing)) return existing;

        lock (_admission)
        {
            if (_files.TryGetValue(canonicalPath, out existing)) return existing;
            if (_files.Count >= Infrastructure.Constants.Provider.MaximumFilesPerHost)
            {
                throw new InvalidOperationException(
                    $"JSON reached the host bound of {Infrastructure.Constants.Provider.MaximumFilesPerHost} " +
                    "canonical entity files. Use a database adapter for a larger or dynamically partitioned store.");
            }

            var admitted = new JsonFileSlot(canonicalPath);
            if (!_files.TryAdd(canonicalPath, admitted)) return _files[canonicalPath];
            return admitted;
        }
    }
}

internal sealed class JsonFileSlot(string path)
{
    private JsonFileSnapshot? _snapshot;

    internal string Path { get; } = path;
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal JsonFileSnapshot? Snapshot => Volatile.Read(ref _snapshot);
    internal void Publish(JsonFileSnapshot snapshot) => Volatile.Write(ref _snapshot, snapshot);
}

internal sealed record JsonFileSnapshot(Type RootType, Type KeyType, object Records);
