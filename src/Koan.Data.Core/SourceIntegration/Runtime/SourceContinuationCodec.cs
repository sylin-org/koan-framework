using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core.SourceIntegration.Runtime;

internal sealed class SourceContinuationCodec
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Encode(DataSourcePlan plan, string providerContinuation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerContinuation);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Envelope(
            plan.Source,
            plan.RouteIdentity,
            providerContinuation));
        var signature = HMACSHA256.HashData(_key, payload);
        var encoded = new byte[payload.Length + signature.Length];
        Buffer.BlockCopy(payload, 0, encoded, 0, payload.Length);
        Buffer.BlockCopy(signature, 0, encoded, payload.Length, signature.Length);
        return Infrastructure.Constants.Defaults.SourceContinuationPrefix + Base64UrlEncode(encoded);
    }

    public string Decode(DataSourcePlan plan, string continuation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(continuation);
        try
        {
            if (!continuation.StartsWith(
                    Infrastructure.Constants.Defaults.SourceContinuationPrefix,
                    StringComparison.Ordinal))
                throw new FormatException();
            var encoded = Base64UrlDecode(
                continuation[Infrastructure.Constants.Defaults.SourceContinuationPrefix.Length..]);
            if (encoded.Length <= 32) throw new FormatException();
            var payloadLength = encoded.Length - 32;
            var payload = encoded.AsSpan(0, payloadLength);
            var supplied = encoded.AsSpan(payloadLength, 32);
            var expected = HMACSHA256.HashData(_key, payload);
            if (!CryptographicOperations.FixedTimeEquals(supplied, expected)) throw new FormatException();
            var envelope = JsonSerializer.Deserialize<Envelope>(payload)
                ?? throw new FormatException();
            if (!string.Equals(envelope.Source, plan.Source, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(envelope.Route, plan.RouteIdentity, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(envelope.Token))
                throw new FormatException();
            return envelope.Token;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            throw new StorageContinuationSourceMismatchException(plan.Source);
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException()
        };
        return Convert.FromBase64String(normalized);
    }

    private sealed record Envelope(string Source, string Route, string Token);
}
