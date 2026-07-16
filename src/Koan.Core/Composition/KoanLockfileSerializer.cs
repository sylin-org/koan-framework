using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Koan.Core.Composition;

/// <summary>
/// The single serialization authority for <see cref="KoanLockfile"/>. Deterministic output
/// (camelCase property names, verbatim dictionary keys, 2-space indent, null sections omitted) so a
/// regenerated file is byte-stable and <c>git diff</c> surfaces only real composition drift.
/// </summary>
/// <remarks>
/// The build-time emitter (<c>build/Sylin.Koan.Core.targets</c>) hand-writes the same SCHEMA
/// (schema/app/modules/directReferences) from MSBuild — it does not call this serializer (it runs before the
/// assembly is loaded). The two are kept in lockstep by <c>KoanLockfileSchemaSpec</c>, which
/// round-trips the target's golden output through <see cref="Deserialize"/>.
/// </remarks>
internal static class KoanLockfileSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // DictionaryKeyPolicy is intentionally left null: election/capability/config keys
        // ("data:default", "Koan:Data:Postgres:ConnectionString") must serialize verbatim.
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(KoanLockfile lockfile)
    {
        ArgumentNullException.ThrowIfNull(lockfile);
        return JsonSerializer.Serialize(lockfile, Options);
    }

    public static KoanLockfile? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<KoanLockfile>(json, Options);
    }

    /// <summary>Read and parse a lockfile from disk, or <c>null</c> if absent/unreadable/malformed.</summary>
    public static KoanLockfile? TryReadFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return Deserialize(File.ReadAllText(path));
        }
        catch
        {
            // A missing or malformed lockfile is a "no comparison available" signal, not a fault.
            return null;
        }
    }
}
