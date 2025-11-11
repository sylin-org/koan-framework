using System;
using System.Security.Cryptography;
using System.Text;
using Koan.Context.Models;

namespace Koan.Context.Utilities;

/// <summary>
/// Produces deterministic chunk version hashes for vector synchronization.
/// </summary>
/// <remarks>
/// The hash incorporates project, file, provenance, and chunk content to ensure
/// that any materially different payload generates a new version identifier.
/// </remarks>
public static class ChunkVersionCalculator
{
    public static string Calculate(string projectId, Chunk chunk)
    {
        if (chunk is null) throw new ArgumentNullException(nameof(chunk));

        var builder = new StringBuilder(256)
            .Append(projectId)
            .Append('|')
            .Append(chunk.IndexedFileId)
            .Append('|')
            .Append(chunk.FilePath)
            .Append('|')
            .Append(chunk.StartLine)
            .Append('-')
            .Append(chunk.EndLine)
            .Append('|')
            .Append(chunk.CommitSha)
            .Append('|')
            .Append(chunk.FileHash)
            .Append('|')
            .Append(chunk.SearchText);

        var payload = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash);
    }
}
