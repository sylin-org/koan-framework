using System;
using Koan.Core.Naming;

namespace Koan.Storage.Keys;

/// <summary>
/// A sanitizing <see cref="IParticleFormatter"/> for leading segmentation particles on a blob key. It
/// REJECTS a value that could escape its own prefix (path traversal / cross-scope) — a leading dot, a path
/// separator (<c>/</c> or <c>\</c>), <c>..</c>, or a control character. Not <see cref="VerbatimParticleFormatter"/>:
/// isolation must not depend on each provider's per-segment guard. A null/empty value omits the particle (the
/// shared segmentation plan is the fail-closed authority when no required context is available.
/// </summary>
public sealed class StorageKeyParticleFormatter : IParticleFormatter
{
    public static readonly StorageKeyParticleFormatter Instance = new();
    private StorageKeyParticleFormatter() { }

    public string? Format(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value[0] == '.' || value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "A storage segmentation value is not a safe path segment (leading dot, '/', '\\' or '..').");
        foreach (var c in value)
            if (char.IsControl(c))
                throw new InvalidOperationException("A storage segmentation value contains a control character.");
        return value;
    }
}
