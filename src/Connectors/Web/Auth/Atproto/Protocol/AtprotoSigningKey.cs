using System.Numerics;
using System.Security.Cryptography;
using CarpaNet.Identity;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using BcInteger = Org.BouncyCastle.Math.BigInteger;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// A DID's current signing key, the one <c>#atproto</c> verification method its document controls: a Multikey over
/// secp256k1 or P-256, or a 2019 key type carrying the raw point. A token's claims are the caller's; this checks a compact
/// low-S ECDSA signature over SHA-256, as the AT Protocol's cryptography specification requires.
/// </summary>
internal sealed class AtprotoSigningKey
{
    private const string K256 = "secp256k1";
    private const string P256 = "secp256r1";
    private const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private readonly ECPublicKeyParameters publicKey;

    private AtprotoSigningKey(string curve, byte[] point)
    {
        Algorithm = curve == P256 ? "ES256" : "ES256K";
        var parameters = CustomNamedCurves.GetByName(curve);
        var domain = new ECDomainParameters(parameters.Curve, parameters.G, parameters.N, parameters.H);
        try { publicKey = new ECPublicKeyParameters(parameters.Curve.DecodePoint(point), domain); }
        catch (ArgumentException invalid) { throw new InvalidDataException("The signing key is not a point of its curve.", invalid); }
    }

    /// <summary>The JWT algorithm this key signs with: <c>ES256</c> for P-256, <c>ES256K</c> for secp256k1.</summary>
    public string Algorithm { get; }

    /// <summary>The signing key of <paramref name="did"/> in its <paramref name="document"/>.</summary>
    /// <exception cref="InvalidDataException">The document holds no one such key, or holds a form of one the protocol does not name.</exception>
    public static AtprotoSigningKey From(DidDocument document, string did)
    {
        var candidates = document.VerificationMethod?
            .Where(method => method is not null && (method.Id == "#atproto" || method.Id == did + "#atproto")).ToArray() ?? [];
        if (document.Id != did || candidates.Length != 1 || candidates[0].Controller != did)
            throw new InvalidDataException("The document does not control exactly one #atproto signing key.");

        var method = candidates[0];
        var encoded = DecodeBase58(method.PublicKeyMultibase);
        if (method.Type is "EcdsaSecp256k1VerificationKey2019" or "EcdsaSecp256r1VerificationKey2019")
        {
            // The 2019 key types carry the raw point in their multibase, not a multicodec envelope.
            if (!(encoded.Length == 33 && encoded[0] is 2 or 3) && !(encoded.Length == 65 && encoded[0] == 4))
                throw new InvalidDataException("A 2019 signing key is a compressed or uncompressed point.");
            return new AtprotoSigningKey(method.Type == "EcdsaSecp256k1VerificationKey2019" ? K256 : P256, encoded);
        }

        if (method.Type != "Multikey" || encoded.Length != 35 || encoded[2] is not (2 or 3))
            throw new InvalidDataException("A Multikey signing key is a multicodec prefix and a compressed point.");
        var curve = (encoded[0], encoded[1]) switch
        {
            (0xe7, 0x01) => K256,
            (0x80, 0x24) => P256,
            _ => throw new InvalidDataException("The signing key's multicodec names no curve the protocol uses."),
        };
        return new AtprotoSigningKey(curve, encoded[2..]);
    }

    /// <summary>Whether <paramref name="signature"/>, 64 bytes with S in its lower half, signs the SHA-256 of <paramref name="input"/>.</summary>
    public bool Verifies(ReadOnlySpan<byte> input, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 64) return false;
        var r = new BcInteger(1, signature[..32].ToArray());
        var s = new BcInteger(1, signature[32..].ToArray());
        var order = publicKey.Parameters.N;
        if (r.SignValue <= 0 || r.CompareTo(order) >= 0 || s.SignValue <= 0 || s.CompareTo(order.ShiftRight(1)) > 0) return false;
        var verifier = new ECDsaSigner();
        verifier.Init(false, publicKey);
        return verifier.VerifySignature(SHA256.HashData(input), r, s);
    }

    // The SDK's base58 helpers are private, and a key's encoding is bounded, so this reads only what a key can be.
    private static byte[] DecodeBase58(string? value)
    {
        if (value is null || value.Length is < 2 or > 128 || value[0] != 'z' || value[1] == '1')
            throw new InvalidDataException("A signing key is bounded base58btc multibase.");
        var number = BigInteger.Zero;
        foreach (var character in value.AsSpan(1))
        {
            var digit = Base58.IndexOf(character);
            if (digit < 0) throw new InvalidDataException("A signing key is bounded base58btc multibase.");
            number = number * 58 + digit;
        }

        var bytes = number.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length is < 33 or > 65) throw new InvalidDataException("A signing key is 33 to 65 bytes.");
        return bytes;
    }
}
