using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Koan.Context.Services;

/// <summary>
/// Generates and validates opaque continuation tokens for stateless pagination
/// </summary>
public interface IContinuationTokenService
{
    /// <summary>
    /// Creates a continuation token encoding cursor state
    /// </summary>
    string CreateToken(ContinuationTokenData data);

    /// <summary>
    /// Decodes and validates a continuation token
    /// </summary>
    ContinuationTokenData? ParseToken(string token);
}

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
    int Page);

/// <summary>
/// Implementation using compressed JSON with expiration validation
/// </summary>
public sealed class ContinuationTokenService : IContinuationTokenService
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
