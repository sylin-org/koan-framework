using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Classification.Crypto;

/// <summary>
/// The local-first key floor: keys persist in a file under the application's own <c>.koan</c> directory, so
/// classified data written today is still readable after a restart.
///
/// <para>This exists because the alternative is worse. An in-memory key regenerates every process, which turns
/// the ordinary development loop — run, stop, run again — into permanent loss of everything written before the
/// restart. A capability that corrupts its own data on restart is not a usable default.</para>
///
/// <para>Local custody is not production custody. The key sits beside the data it protects, is never rotated on
/// a schedule, and inherits only the filesystem's protection. Koan says so loudly at startup and refuses it in
/// Production unless the application explicitly opts in; a real deployment supplies its own
/// <see cref="IClassificationKeyProvider"/> over whatever key service it already trusts.</para>
/// </summary>
public sealed class LocalFileClassificationKeyProvider : IClassificationKeyProvider, IDisposable
{
    /// <summary>Default keyring location, relative to the process working directory.</summary>
    public const string DefaultRelativePath = ".koan/keys/classification.json";

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _byKeyId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activeByScope = new(StringComparer.Ordinal);
    private bool _loaded;
    private int _disposed;

    public LocalFileClassificationKeyProvider() : this(DefaultRelativePath) { }

    public LocalFileClassificationKeyProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>Absolute keyring path, so startup reporting can name the custody rather than imply it.</summary>
    public string KeyringPath => _path;

    public ClassificationDataKey GetActiveKey(string scope)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        lock (_gate)
        {
            Load();
            if (_activeByScope.TryGetValue(scope, out var keyId) && _byKeyId.TryGetValue(keyId, out var existing))
                return new ClassificationDataKey(keyId, existing);

            var material = RandomNumberGenerator.GetBytes(AesGcmFieldCipher.KeySize);
            var created = Guid.NewGuid().ToString("N");
            _byKeyId[created] = material;
            _activeByScope[scope] = created;
            Save();
            return new ClassificationDataKey(created, material);
        }
    }

    public ClassificationDataKey GetForDecrypt(string keyId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        lock (_gate)
        {
            Load();
            // Every key ever issued is retained. Dropping a superseded key would silently strand every row
            // still encrypted under it, which is the failure this provider exists to avoid.
            if (_byKeyId.TryGetValue(keyId, out var material))
                return new ClassificationDataKey(keyId, material);
        }

        throw new ClassificationKeyUnavailableException(keyId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate)
        {
            foreach (var material in _byKeyId.Values) CryptographicOperations.ZeroMemory(material);
            _byKeyId.Clear();
            _activeByScope.Clear();
        }
    }

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_path)) return;

        Keyring? keyring;
        try
        {
            keyring = JsonSerializer.Deserialize(File.ReadAllText(_path), KeyringContext.Default.Keyring);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Refusing here is the honest outcome: silently starting a fresh keyring would make every existing
            // row undecryptable while appearing to work.
            throw new InvalidOperationException(
                $"Koan Classification could not read its keyring at '{_path}'. Repair or remove the file — " +
                "removing it discards custody of everything already encrypted under those keys.", ex);
        }

        if (keyring is null) return;
        foreach (var (keyId, encoded) in keyring.Keys ?? new Dictionary<string, string>())
        {
            var material = Convert.FromBase64String(encoded);
            if (material.Length != AesGcmFieldCipher.KeySize)
                throw new InvalidOperationException(
                    $"Keyring entry '{keyId}' in '{_path}' is {material.Length} bytes; AES-256 requires {AesGcmFieldCipher.KeySize}.");
            _byKeyId[keyId] = material;
        }

        foreach (var (scope, keyId) in keyring.Active ?? new Dictionary<string, string>())
            _activeByScope[scope] = keyId;
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var keyring = new Keyring
        {
            Keys = _byKeyId.ToDictionary(static pair => pair.Key, static pair => Convert.ToBase64String(pair.Value), StringComparer.Ordinal),
            Active = new Dictionary<string, string>(_activeByScope, StringComparer.Ordinal)
        };

        // Write through a temporary file so an interrupted write cannot truncate custody of live data.
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(keyring, KeyringContext.Default.Keyring));
        RestrictToOwner(temporary);
        File.Move(temporary, _path, overwrite: true);
        RestrictToOwner(_path);
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;   // NTFS inherits the directory ACL; no POSIX mode to set.
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort. A filesystem that cannot express owner-only permissions is a custody limitation to
            // report, not a reason to refuse to start.
        }
    }

}

/// <summary>On-disk shape of the local keyring. Key material is base64; nothing else is stored.</summary>
internal sealed class Keyring
{
    [JsonPropertyName("keys")] public Dictionary<string, string>? Keys { get; set; }
    [JsonPropertyName("active")] public Dictionary<string, string>? Active { get; set; }
}

// Top-level by necessity: the JSON source generator cannot emit a context nested in another type.
[JsonSerializable(typeof(Keyring))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class KeyringContext : JsonSerializerContext;
