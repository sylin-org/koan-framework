using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Storage.Replication;

/// <summary>
/// Append-only journal of pending sync operations.
/// Stored at <c>.Koan/storage-sync/{container}/journal.jsonl</c>.
/// Thread-safe for concurrent appends and drain.
/// </summary>
public sealed class SyncJournal
{
    private readonly object _lock = new();
    private readonly List<SyncJournalEntry> _entries = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Number of pending entries.</summary>
    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>
    /// Appends a sync operation to the journal.
    /// Also persists the entry to the backing JSONL file if <paramref name="journalPath"/> is provided.
    /// </summary>
    public void Append(SyncJournalEntry entry, string? journalPath = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            _entries.Add(entry);
        }

        if (!string.IsNullOrEmpty(journalPath))
        {
            var dir = Path.GetDirectoryName(journalPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (_lock)
            {
                File.AppendAllText(journalPath, line + Environment.NewLine);
            }
        }
    }

    /// <summary>
    /// Atomically drains all entries from the journal and returns them.
    /// Clears the in-memory list. The caller is responsible for truncating the file after processing.
    /// </summary>
    public IReadOnlyList<SyncJournalEntry> Drain()
    {
        lock (_lock)
        {
            if (_entries.Count == 0)
                return [];

            var snapshot = _entries.ToList();
            _entries.Clear();
            return snapshot;
        }
    }

    /// <summary>
    /// Loads journal entries from a JSONL file into memory.
    /// </summary>
    public async Task LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return;

        using var reader = new StreamReader(path);
        lock (_lock)
        {
            // Read synchronously under lock to avoid interleaving with appends
        }

        var loaded = new List<SyncJournalEntry>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var entry = JsonSerializer.Deserialize<SyncJournalEntry>(line, JsonOptions);
                if (entry is not null)
                    loaded.Add(entry);
            }
            catch (JsonException)
            {
                // Skip corrupted lines
            }
        }

        lock (_lock)
        {
            _entries.AddRange(loaded);
        }
    }

    /// <summary>
    /// Truncates the backing file (called after successful drain + processing).
    /// </summary>
    public static void Truncate(string journalPath)
    {
        if (File.Exists(journalPath))
            File.WriteAllText(journalPath, string.Empty);
    }
}

/// <summary>
/// The type of sync operation pending in the journal.
/// </summary>
public enum SyncOperationType
{
    /// <summary>File written to cache, needs push to durable.</summary>
    Put,

    /// <summary>File deleted from cache, needs delete on durable.</summary>
    Delete
}

/// <summary>
/// A single entry in the sync journal.
/// </summary>
public sealed record SyncJournalEntry
{
    public required SyncOperationType Op { get; init; }
    public required string Container { get; init; }
    public required string Key { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
