using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Koan.Context.Services;

/// <summary>
/// Data encoded in continuation tokens
/// </summary>
public record ContinuationTokenData(
    string ProjectId,
    string Query,
    float Alpha,
    int TokensRemaining,
    string LastChunkId,
    DateTime CreatedAt,
    int Page,
    List<string>? ProjectIds = null,  // For multi-project searches
    int ChunkOffset = 0,               // For multi-project pagination
    string? ProviderHint = null);      // Opaque provider-specific continuation token (Weaviate cursor, Qdrant offset, etc.)

/// <summary>
/// Pagination service - generates and validates opaque continuation tokens using compressed JSON
/// </summary>
public sealed class Pagination 
{
    private static readonly TimeSpan TokenExpiration = TimeSpan.FromHours(1);

    public string CreateToken(ContinuationTokenData data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
        {
            gzipStream.Write(bytes, 0, bytes.Length);
        }

        return Convert.ToBase64String(outputStream.ToArray());
    }

    public ContinuationTokenData? ParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var compressedBytes = Convert.FromBase64String(token);

            using var inputStream = new MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();

            gzipStream.CopyTo(outputStream);
            var json = Encoding.UTF8.GetString(outputStream.ToArray());

            var data = JsonSerializer.Deserialize<ContinuationTokenData>(json);

            if (data == null)
            {
                return null;
            }

            // Validate expiration
            if (DateTime.UtcNow - data.CreatedAt > TokenExpiration)
            {
                return null; // Token expired
            }

            return data;
        }
        catch
        {
            return null; // Invalid token
        }
    }
}
