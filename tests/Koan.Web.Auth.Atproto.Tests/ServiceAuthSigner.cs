using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using BcInteger = Org.BouncyCastle.Math.BigInteger;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>An atproto account's signing key, as a PDS holds one: it signs service-auth tokens as a PDS mints them, compact
/// and low-S, and states itself as the verification method a DID document carries.</summary>
internal sealed class ServiceAuthSigner
{
    private const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private readonly string curve;
    private readonly ECDomainParameters domain;
    private readonly ECPrivateKeyParameters secret;
    private readonly ECPublicKeyParameters published;

    public ServiceAuthSigner(string curve)
    {
        this.curve = curve;
        var named = CustomNamedCurves.GetByName(curve);
        domain = new ECDomainParameters(named.Curve, named.G, named.N, named.H);
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        secret = (ECPrivateKeyParameters)pair.Private;
        published = (ECPublicKeyParameters)pair.Public;
    }

    /// <summary>The verification method a document of <paramref name="did"/> carries for this key.</summary>
    public object Method(string did)
    {
        byte[] prefix = curve == "secp256r1" ? [0x80, 0x24] : [0xe7, 0x01];
        return new
        {
            id = "#atproto",
            type = "Multikey",
            controller = did,
            publicKeyMultibase = "z" + EncodeBase58([.. prefix, .. published.Q.GetEncoded(compressed: true)]),
        };
    }

    /// <summary>A token as a PDS mints one, a minute's life and a fresh <c>jti</c>, with what a spec changes.</summary>
    public string Token(string issuer, string audience, string? method, string? jti = null,
        Action<Dictionary<string, object?>>? header = null, Action<Dictionary<string, object?>>? claims = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var headerValues = new Dictionary<string, object?> { ["typ"] = "JWT", ["alg"] = curve == "secp256r1" ? "ES256" : "ES256K" };
        var claimValues = new Dictionary<string, object?>
        {
            ["iss"] = issuer, ["aud"] = audience, ["exp"] = now + 60, ["iat"] = now, ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
        };
        if (method is not null) claimValues["lxm"] = method;
        header?.Invoke(headerValues);
        claims?.Invoke(claimValues);
        return Signed(Encode(JsonSerializer.Serialize(headerValues)), Encode(JsonSerializer.Serialize(claimValues)));
    }

    /// <summary>The two parts given, signed as they stand.</summary>
    public string Signed(string header, string payload)
    {
        var input = header + "." + payload;
        var signer = new ECDsaSigner(new HMacDsaKCalculator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest()));
        signer.Init(true, secret);
        var parts = signer.GenerateSignature(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(input)));
        var low = parts[1].CompareTo(domain.N.ShiftRight(1)) > 0 ? domain.N.Subtract(parts[1]) : parts[1];
        return input + "." + WebEncoders.Base64UrlEncode(Compact(parts[0], low));
    }

    /// <summary><paramref name="token"/> with the other S of its signature: as valid to plain ECDSA, and refused here.</summary>
    public string HighS(string token)
    {
        var parts = token.Split('.');
        var signature = WebEncoders.Base64UrlDecode(parts[2]);
        var s = new BcInteger(1, signature[32..]);
        return parts[0] + "." + parts[1] + "." + WebEncoders.Base64UrlEncode(Compact(new BcInteger(1, signature[..32]), domain.N.Subtract(s)));
    }

    public static string Encode(string json) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));

    public static JsonElement Header(string token) => JsonDocument.Parse(WebEncoders.Base64UrlDecode(token.Split('.')[0])).RootElement;

    private static byte[] Compact(BcInteger r, BcInteger s)
    {
        var compact = new byte[64];
        var (first, second) = (r.ToByteArrayUnsigned(), s.ToByteArrayUnsigned());
        first.CopyTo(compact, 32 - first.Length);
        second.CopyTo(compact, 64 - second.Length);
        return compact;
    }

    private static string EncodeBase58(byte[] bytes)
    {
        var number = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var encoded = new StringBuilder();
        while (number > 0)
        {
            encoded.Insert(0, Base58[(int)(number % 58)]);
            number /= 58;
        }

        return encoded.ToString();
    }
}
