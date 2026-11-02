using System.Numerics;
using System.Text;
using System.Text.Json;
using CarpaNet.Identity;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Crypto.EC;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>
/// An inbound service-auth token (SEC-0034) proves its issuer only when its shape, algorithm, audience, method and times
/// hold and the signing key in the issuer's verified document signed it. Tokens are signed with real keys and read against
/// documents a loopback PLC directory serves; nothing a stranger sends reaches the network before it could be genuine.
/// </summary>
public sealed class ServiceAuth
{
    private const string Audience = "did:web:app.example#service";
    private const string Method = "org.example.exchange";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AtprotoServiceAuth Verifier(Node node)
        => new(node.Sessions, TimeProvider.System, NullLogger<AtprotoServiceAuth>.Instance);

    /// <summary>One account of the world, its document holding <paramref name="signer"/>'s key.</summary>
    private static string Holding(SessionWorld world, ServiceAuthSigner signer)
    {
        world.Pds.SigningKey(world.Dids[0], signer.Method(world.Dids[0]));
        return world.Dids[0];
    }

    [Theory]
    [InlineData("secp256r1", "ES256")]
    [InlineData("secp256k1", "ES256K")]
    public async Task A_token_signed_by_the_key_in_the_issuers_document_proves_its_issuer_with_its_handle(string curve, string algorithm)
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var signer = new ServiceAuthSigner(curve);
        var did = Holding(world, signer);
        var token = signer.Token(did, Audience, Method);
        Assert.Equal(algorithm, ServiceAuthSigner.Header(token).GetProperty("alg").GetString());

        var first = await Verifier(world.Node()).Verify(token, Audience, Method, Ct);

        Assert.Equal((AtprotoServiceAuthOutcome.Verified, did, SessionWorld.HandleOf(did)), (first.Outcome, first.Did, first.Handle));
    }

    [Fact]
    public async Task A_verified_token_signs_its_issuer_in_with_the_cookie_a_browser_gets_and_a_refused_one_signs_no_one_in()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var signer = new ServiceAuthSigner("secp256r1");
        var did = Holding(world, signer);
        var verifier = Verifier(world.Node());
        var proof = await verifier.Verify(signer.Token(did, Audience, Method), Audience, Method, Ct);
        using var scope = world.Store.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        var person = await verifier.SignIn(context, proof);

        Assert.Equal(did, person.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Contains(context.Response.Headers.SetCookie, cookie => cookie!.StartsWith(".AspNetCore.Koan.cookie=", StringComparison.Ordinal));
        await Assert.ThrowsAsync<ArgumentException>(() => verifier.SignIn(new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            new AtprotoServiceAuthResult(AtprotoServiceAuthOutcome.BadSignature)));
    }

    public static TheoryData<string, AtprotoServiceAuthOutcome> Refusals => new()
    {
        { "another audience", AtprotoServiceAuthOutcome.WrongAudience },
        { "the bare did of the audience", AtprotoServiceAuthOutcome.WrongAudience },
        { "another method", AtprotoServiceAuthOutcome.WrongMethod },
        { "no method", AtprotoServiceAuthOutcome.Malformed },
        { "expired", AtprotoServiceAuthOutcome.Expired },
        { "too long a life", AtprotoServiceAuthOutcome.Expired },
        { "issued in the future", AtprotoServiceAuthOutcome.Expired },
        { "not valid yet", AtprotoServiceAuthOutcome.Expired },
        { "HS256", AtprotoServiceAuthOutcome.UnsupportedAlgorithm },
        { "none", AtprotoServiceAuthOutcome.UnsupportedAlgorithm },
        { "another type", AtprotoServiceAuthOutcome.Malformed },
        { "critical header", AtprotoServiceAuthOutcome.Malformed },
        { "another key id", AtprotoServiceAuthOutcome.Malformed },
        { "duplicate claim", AtprotoServiceAuthOutcome.Malformed },
        { "not a did", AtprotoServiceAuthOutcome.Malformed },
        { "two parts", AtprotoServiceAuthOutcome.Malformed },
        { "not base64", AtprotoServiceAuthOutcome.Malformed },
        { "not json", AtprotoServiceAuthOutcome.Malformed },
        { "too long a token", AtprotoServiceAuthOutcome.Malformed },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_token_that_could_not_be_genuine_is_refused_before_anything_resolves_its_issuer(string flaw, AtprotoServiceAuthOutcome outcome)
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var signer = new ServiceAuthSigner("secp256r1");
        var did = Holding(world, signer);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = flaw switch
        {
            "another audience" => signer.Token(did, "did:web:elsewhere.example#service", Method),
            "the bare did of the audience" => signer.Token(did, "did:web:app.example", Method),
            "another method" => signer.Token(did, Audience, "org.example.other"),
            "no method" => signer.Token(did, Audience, null),
            "expired" => signer.Token(did, Audience, Method, claims: c => { c["exp"] = now - 1; c["iat"] = now - 61; }),
            "too long a life" => signer.Token(did, Audience, Method, claims: c => c["exp"] = now + 3600),
            "issued in the future" => signer.Token(did, Audience, Method, claims: c => { c["iat"] = now + 60; c["exp"] = now + 120; }),
            "not valid yet" => signer.Token(did, Audience, Method, claims: c => c["nbf"] = now + 30),
            "HS256" => signer.Token(did, Audience, Method, header: h => h["alg"] = "HS256"),
            "none" => signer.Token(did, Audience, Method, header: h => h["alg"] = "none"),
            "another type" => signer.Token(did, Audience, Method, header: h => h["typ"] = "dpop+jwt"),
            "critical header" => signer.Token(did, Audience, Method, header: h => h["crit"] = new[] { "b64" }),
            "another key id" => signer.Token(did, Audience, Method, header: h => h["kid"] = "#elsewhere"),
            "duplicate claim" => signer.Signed(ServiceAuthSigner.Encode("""{"typ":"JWT","alg":"ES256"}"""),
                ServiceAuthSigner.Encode($$"""{"iss":"{{did}}","iss":"did:plc:someoneelse","aud":"{{Audience}}","lxm":"{{Method}}","exp":{{now + 60}},"iat":{{now}},"jti":"1"}""")),
            "not a did" => signer.Token("someone", Audience, Method),
            "two parts" => "aGVhZGVy.cGF5bG9hZA",
            "not base64" => "!!!.???.***",
            "not json" => signer.Signed(ServiceAuthSigner.Encode("not json"), ServiceAuthSigner.Encode("not json")),
            "too long a token" => new string('a', 8193),
            _ => throw new ArgumentOutOfRangeException(nameof(flaw)),
        };
        var read = world.Pds.DocumentReads;

        var result = await Verifier(world.Node()).Verify(token, Audience, Method, Ct);

        Assert.Equal((outcome, null, null), (result.Outcome, result.Did, result.Handle));
        Assert.Equal(read, world.Pds.DocumentReads);
    }

    [Theory]
    [InlineData("did:web:evil.com%2Fpath")]
    [InlineData("did:web:evil.com%3F")]
    [InlineData("did:web:evil.com\n")]
    [InlineData("did:web:evil.com:path")]
    [InlineData("did:web:Evil.com")]
    [InlineData("did:web:evil.com%3A8443")]
    [InlineData("did:web:localhost%3A5220")]
    [InlineData("did:web:localhost")]
    [InlineData("did:web:169.254.169.254")]
    [InlineData("did:web:evil.com#atproto_labeler")]
    [InlineData("did:plc:aaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("did:plc:aaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("did:plc:AAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("did:plc:aaaaaaaaaaaaaaaaaaaaaaa1")]
    [InlineData("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa\n")]
    [InlineData("did:key:zQ3shuCPf2Fkw8aWDT41Tbq4mXi73WcpkEsgjZCpbWUgtG3kh")]
    public async Task An_issuer_that_is_not_exactly_a_plain_did_is_malformed_and_nothing_resolves_it(string issuer)
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var signer = new ServiceAuthSigner("secp256r1");
        var node = world.Node(options => options.DevelopmentHandles["this-issuer.test"] = issuer);

        var result = await Verifier(node).Verify(signer.Token(issuer, Audience, Method), Audience, Method, Ct);

        Assert.Equal(AtprotoServiceAuthOutcome.Malformed, result.Outcome);
        Assert.Equal(0, world.Pds.DocumentReads);
    }

    [Fact]
    public async Task A_did_web_with_a_port_goes_on_to_the_resolver_only_for_an_origin_the_transport_allows()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var signer = new ServiceAuthSigner("secp256r1");
        var token = signer.Token("did:web:localhost%3A1", Audience, Method);
        var node = world.Node(options => options.DevelopmentAllowedOrigins = ["https://localhost:1"]);

        Assert.NotEqual(AtprotoServiceAuthOutcome.Malformed, (await Verifier(node).Verify(token, Audience, Method, Ct)).Outcome);
        Assert.Equal(AtprotoServiceAuthOutcome.Malformed, (await Verifier(world.Node()).Verify(token, Audience, Method, Ct)).Outcome);
    }

    [Theory]
    [InlineData("another key")]
    [InlineData("tampered claims")]
    [InlineData("high S")]
    [InlineData("the other curve's algorithm")]
    [InlineData("a short signature")]
    [InlineData("no key in the document")]
    [InlineData("a key another controls")]
    public async Task A_token_the_issuers_key_did_not_sign_is_refused(string flaw)
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var signer = new ServiceAuthSigner("secp256r1");
        var did = Holding(world, signer);
        var genuine = signer.Token(did, Audience, Method);
        var forged = flaw switch
        {
            "another key" => new ServiceAuthSigner("secp256r1").Token(did, Audience, Method),
            "tampered claims" => string.Join('.', genuine.Split('.')[0], signer.Token(did, Audience, Method).Split('.')[1], genuine.Split('.')[2]),
            "high S" => signer.HighS(genuine),
            "the other curve's algorithm" => signer.Token(did, Audience, Method, header: h => h["alg"] = "ES256K"),
            "a short signature" => genuine[..^8],
            _ => genuine,
        };
        if (flaw == "no key in the document") world.Pds.SigningKey(did, null);
        if (flaw == "a key another controls") world.Pds.SigningKey(did, signer.Method("did:plc:someoneelse"));

        var result = await Verifier(world.Node()).Verify(forged, Audience, Method, Ct);
        Assert.Equal((AtprotoServiceAuthOutcome.BadSignature, null), (result.Outcome, result.Did));
    }

    [Fact]
    public async Task An_issuer_no_directory_knows_is_unknown_and_a_directory_that_cannot_be_reached_says_so_rather_than_refuse()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var signer = new ServiceAuthSigner("secp256r1");
        var stranger = SessionWorld.Did(99);
        var unknown = await Verifier(world.Node(options => options.DevelopmentHandles["stranger.test"] = stranger))
            .Verify(signer.Token(stranger, Audience, Method), Audience, Method, Ct);
        Assert.Equal(AtprotoServiceAuthOutcome.IssuerUnknown, unknown.Outcome);

        var token = signer.Token(Holding(world, signer), Audience, Method);
        var closed = world.Node(options =>
        {
            options.DevelopmentPlcDirectory = "http://127.0.0.1:1";
            options.DevelopmentAllowedOrigins = ["http://127.0.0.1:1"];
        });
        Assert.Equal(AtprotoServiceAuthOutcome.IssuerUnreachable, (await Verifier(closed).Verify(token, Audience, Method, Ct)).Outcome);
    }

    [Fact]
    public async Task A_handle_that_does_not_resolve_back_leaves_the_proof_standing_without_it()
    {
        await using var world = await SessionWorld.Start(accounts: 2);
        var signer = new ServiceAuthSigner("secp256k1");
        var did = Holding(world, signer);
        var node = world.Node(options =>
        {
            options.DevelopmentHandles[SessionWorld.HandleOf(did)] = world.Dids[1];
            options.DevelopmentHandles["this-did-resolves-here.test"] = did;
        });

        var result = await Verifier(node).Verify(signer.Token(did, Audience, Method), Audience, Method, Ct);

        Assert.Equal((AtprotoServiceAuthOutcome.Verified, did, null), (result.Outcome, result.Did, result.Handle));
    }

    [Theory]
    [InlineData("https://tangent.sylin.org", "did:web:tangent.sylin.org")]
    [InlineData("https://tangent.sylin.org/", "did:web:tangent.sylin.org")]
    [InlineData("https://tangent.sylin.org:8443", "did:web:tangent.sylin.org%3A8443")]
    [InlineData("https://example.test/notes/daily", "did:web:example.test:notes:daily")]
    [InlineData("http://localhost", "did:web:localhost")]
    [InlineData("http://localhost:5220", "did:web:localhost%3A5220")]
    [InlineData("http://127.0.0.1:5220", "did:web:localhost%3A5220")]
    [InlineData("http://[::1]:5220", "did:web:localhost%3A5220")]
    public void A_services_web_did_derives_from_its_base_url(string baseUrl, string did)
        => Assert.Equal(did, AtprotoServiceAuth.WebDid(baseUrl));

    // ---- The signing key, on vectors the official atproto implementations generated and on a real PLC document ----

    private const string Author = "did:example:author";

    private const string Vectors = """
[{"curve":"secp256k1","algorithm":"ES256K","multikey":"zQ3shuCPf2Fkw8aWDT41Tbq4mXi73WcpkEsgjZCpbWUgtG3kh","signingInput":"eyJhbGciOiJ0ZXN0In0.eyJpc3MiOiJkaWQ6ZXhhbXBsZTphdXRob3IifQ","signatureHex":"3ca74af623d46ac2cf80d0dfcea13e64b91db4c67ec2fb1ba01e9422caa5fe06516d5a285700616f76bea1d1ced98b508ec505f5ef177c2176d2e3c7b9308190"},{"curve":"secp256r1","algorithm":"ES256","multikey":"zDnaejR8NosRdn9e77oK5LSu1akYxRgNA9aS6bNvsziVWownc","signingInput":"eyJhbGciOiJ0ZXN0In0.eyJpc3MiOiJkaWQ6ZXhhbXBsZTphdXRob3IifQ","signatureHex":"bf1bb04425f5396aadf618fe6ae414a66a0368e8a1cc9978c0ebde5348e47c1630e5a5045436e968b8843411562a03d958c3be22acc8c72f7bd0b3cfe04e5304"}]
""";

    // A public PLC document and an expired service signature captured from a real PDS: this reads the key and the signature,
    // and an expired token is not valid service authorization.
    private const string PlcFixture = """
{"document":{"@context":["https://www.w3.org/ns/did/v1","https://w3id.org/security/suites/secp256k1-2019/v1"],"id":"did:plc:yblwluenzocgiqpmu4ip7six","alsoKnownAs":["at://tangent-authority.test"],"verificationMethod":[{"id":"#atproto","type":"EcdsaSecp256k1VerificationKey2019","controller":"did:plc:yblwluenzocgiqpmu4ip7six","publicKeyMultibase":"zNG8hhDBG9gTy1aMrHBVXQNoVWodeW8w56jfXt9GvmutDGB2LkskwtYpRhd5jE2KrLQnZJJePqY8cK1ncCi5cRkLQ"}],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"http://localhost:2583"}]},"expiredServiceSignature":{"signingInput":"eyJ0eXAiOiJKV1QiLCJhbGciOiJFUzI1NksifQ.eyJpYXQiOjE3ODg5ODgyOTUsImlzcyI6ImRpZDpwbGM6eWJsd2x1ZW56b2NnaXFwbXU0aXA3c2l4IiwiYXVkIjoiZGlkOnBsYzpzYTJmbDJ0MjduN202NHBibDR4YXpkc20jdGFuZ2VudCIsImV4cCI6MTc4ODk4ODI5OCwibHhtIjoiY29tLmF0cHJvdG8uc2ltcGxlc3BhY2UuY2hlY2tVc2VyQWNjZXNzIiwianRpIjoiODBmMDFkOWIzYWFjMzJhYjFmNjg0ZmZjMDEwN2ZlNWEifQ","signatureHex":"752c6c4b067c1639d204a7e9f4af83f2819fbd3d1c3b59c4642b2ef39759d6ce02e50c015ba25073ccf445015587cee4322d2cf30c925abd7f7eb4481dec2624"}}
""";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_multikey_verifies_its_official_signature_and_refuses_tampering_truncation_and_high_S(int number)
    {
        using var parsed = JsonDocument.Parse(Vectors);
        var vector = parsed.RootElement[number];
        var key = AtprotoSigningKey.From(Document(vector.GetProperty("multikey").GetString()!), Author);
        Assert.Equal(vector.GetProperty("algorithm").GetString(), key.Algorithm);
        var input = Encoding.UTF8.GetBytes(vector.GetProperty("signingInput").GetString()!);
        var signature = Convert.FromHexString(vector.GetProperty("signatureHex").GetString()!);

        Assert.True(key.Verifies(input, signature));
        Assert.False(key.Verifies(Encoding.UTF8.GetBytes("changed context"), signature));
        Assert.False(key.Verifies(input, signature[..63]));
        var altered = (byte[])signature.Clone();
        altered[0] ^= 1;
        Assert.False(key.Verifies(input, altered));
        var order = CustomNamedCurves.GetByName(vector.GetProperty("curve").GetString()!).N.ToByteArrayUnsigned();
        var highS = (new BigInteger(order, true, true) - new BigInteger(signature.AsSpan(32), true, true)).ToByteArray(true, true);
        altered = (byte[])signature.Clone();
        Array.Clear(altered, 32, 32);
        highS.CopyTo(altered, 64 - highS.Length);
        Assert.False(key.Verifies(input, altered));
    }

    [Theory]
    [InlineData("other-document")]
    [InlineData("other-controller")]
    [InlineData("other-key-id")]
    [InlineData("missing-key")]
    [InlineData("duplicate-key")]
    [InlineData("mismatched-key-type")]
    [InlineData("wrong-base")]
    [InlineData("invalid-base58")]
    [InlineData("leading-zero")]
    [InlineData("too-long")]
    public void A_hostile_or_ambiguous_key_in_a_document_is_refused(string change)
    {
        using var parsed = JsonDocument.Parse(Vectors);
        var document = Document(parsed.RootElement[0].GetProperty("multikey").GetString()!);
        var key = document.VerificationMethod[0];
        switch (change)
        {
            case "other-document": document.Id = "did:example:other"; break;
            case "other-controller": key.Controller = "did:example:other"; break;
            case "other-key-id": key.Id = "did:example:other#atproto"; break;
            case "missing-key": document.VerificationMethod.Clear(); break;
            case "duplicate-key": document.VerificationMethod.Add(new VerificationMethod { Id = "#atproto", Controller = Author, Type = key.Type, PublicKeyMultibase = key.PublicKeyMultibase }); break;
            case "mismatched-key-type": key.Type = "EcdsaSecp256k1VerificationKey2019"; break;
            case "wrong-base": key.PublicKeyMultibase = "m" + key.PublicKeyMultibase![1..]; break;
            case "invalid-base58": key.PublicKeyMultibase = "z0" + key.PublicKeyMultibase![2..]; break;
            case "leading-zero": key.PublicKeyMultibase = "z1" + key.PublicKeyMultibase![1..]; break;
            case "too-long": key.PublicKeyMultibase = "z" + new string('2', 128); break;
        }

        Assert.Throws<InvalidDataException>(() => AtprotoSigningKey.From(document, Author));
    }

    [Fact]
    public void A_relative_key_id_is_the_documents_own()
    {
        using var parsed = JsonDocument.Parse(Vectors);
        var document = Document(parsed.RootElement[0].GetProperty("multikey").GetString()!);
        document.VerificationMethod[0].Id = "#atproto";
        Assert.Equal("ES256K", AtprotoSigningKey.From(document, Author).Algorithm);
    }

    [Fact]
    public void A_plc_document_with_a_2019_key_type_verifies_a_real_service_signature()
    {
        using var fixture = JsonDocument.Parse(PlcFixture);
        var document = DidDocument.FromJson(fixture.RootElement.GetProperty("document").GetRawText());
        var signature = fixture.RootElement.GetProperty("expiredServiceSignature");
        Assert.True(AtprotoSigningKey.From(document, document!.Id).Verifies(Encoding.ASCII.GetBytes(signature.GetProperty("signingInput").GetString()!),
            Convert.FromHexString(signature.GetProperty("signatureHex").GetString()!)));
    }

    private static DidDocument Document(string multikey) => new()
    {
        Id = Author,
        VerificationMethod = [new VerificationMethod { Id = Author + "#atproto", Controller = Author, Type = "Multikey", PublicKeyMultibase = multikey }],
    };
}
