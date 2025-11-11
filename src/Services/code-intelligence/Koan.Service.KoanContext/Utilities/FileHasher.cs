using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Context.Utilities;

/// <summary>
/// Provides shared hashing helpers for differential indexing and maintenance workflows.
/// </summary>
public static class FileHasher
{
    /// <summary>
    /// Computes a SHA256 hash for the supplied file path.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null or whitespace.</exception>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }
}
