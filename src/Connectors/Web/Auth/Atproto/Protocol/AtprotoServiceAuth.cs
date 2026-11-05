using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarpaNet.Identity;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>
/// Verifies an inbound AT Protocol service-auth token (SEC-0034), in the order that keeps a stranger's token from reaching
/// the network: its shape, algorithm, audience, method and times; then its signature against the signing key in the issuer
/// DID's verified document. An application decides what the proved DID may do, and <see cref="SignIn"/> ends as a browser's
/// sign-in does.
/// </summary>
public sealed partial class AtprotoServiceAuth
{
    private readonly AtprotoSessions sessions;
    private readonly TimeProvider time;
    private readonly ILogger<AtprotoServiceAuth> logger;

    internal AtprotoServiceAuth(AtprotoSessions sessions, TimeProvider time, ILogger<AtprotoServiceAuth> logger)
    {
        this.sessions = sessions;
        this.time = time;
        this.logger = logger;
    }

    /// <summary>
    /// The did:web a service answers as at <paramref name="baseUrl"/>: its host, with a port other than the scheme's default
    /// encoded after it and each path segment after that. Every loopback address is <c>localhost</c>.
    /// </summary>
    public static string WebDid(string baseUrl)
    {
        var address = new Uri(baseUrl, UriKind.Absolute);
        var did = new StringBuilder("did:web:").Append(address.IsLoopback ? "localhost" : Uri.EscapeDataString(address.IdnHost));
        if (!address.IsDefaultPort) did.Append("%3A").Append(address.Port);
        foreach (var segment in address.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)) did.Append(':').Append(segment);
        return did.ToString();
    }

    /// <summary>
    /// Verifies <paramref name="token"/> as addressed to <paramref name="audience"/>, exactly as the application states it,
    /// and bound to <paramref name="method"/>. Every refusal is an outcome; only cancellation throws. A token verifies each
    /// time it is presented until it expires.
    /// </summary>
    public async Task<AtprotoServiceAuthResult> Verify(string token, string audience, string method, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(audience);
        ArgumentException.ThrowIfNullOrEmpty(method);

        var read = Read(token, audience, method);
        if (read.Refused is { } refused) return Refused(refused);
        var (issuer, algorithm, expires) = (read.Issuer!, read.Algorithm!, read.Expires);

        DidDocument document;
        try { document = await sessions.ResolveDid(issuer, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception failure) when (IsNetwork(failure))
        {
            logger.LogDebug("The DID document of a service-auth issuer could not be read: {Failure}", failure.GetType().Name);
            return Refused(AtprotoServiceAuthOutcome.IssuerUnreachable);
        }
        catch (Exception failure)
        {
            logger.LogDebug("A service-auth issuer's DID resolved to nothing usable: {Failure}", failure.GetType().Name);
            return Refused(AtprotoServiceAuthOutcome.IssuerUnknown);
        }

        if (!SignedBy(document, issuer, algorithm, token)) return Refused(AtprotoServiceAuthOutcome.BadSignature);

        // Resolving took time, and a token's life is short.
        if (expires <= time.GetUtcNow().ToUnixTimeSeconds()) return Refused(AtprotoServiceAuthOutcome.Expired);

        return new AtprotoServiceAuthResult(AtprotoServiceAuthOutcome.Verified, issuer, await sessions.VerifiedHandle(document, ct));
    }

    /// <summary>
    /// Signs in the DID <paramref name="proof"/> proved, as the callback of a browser's sign-in ends: the same claims, the
    /// same login arriving as its person (SEC-0040) — the person it is bound to, or a new one the first time — and the same
    /// cookie, through every sign-in contributor, as a sign-in of this provider.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="proof"/> proved nothing.</exception>
    public async Task SignIn(HttpContext context, AtprotoServiceAuthResult proof)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!proof.Verified) throw new ArgumentException("Only a verified token signs anyone in.", nameof(proof));
        await LoginArrival.Arrive(context, null, AtprotoSessions.External(proof.Did!, proof.Handle));
        var properties = new AuthenticationProperties();
        properties.Items[AuthConstants.Items.Provider] = Constants.Provider;
        await context.SignInAsync(AuthenticationExtensions.CookieScheme, AtprotoSessions.Principal(proof.Did!, proof.Handle), properties);
    }

    private AtprotoServiceAuthResult Refused(AtprotoServiceAuthOutcome outcome)
    {
        logger.LogDebug("A service-auth token was refused: {Outcome}", outcome);
        return new AtprotoServiceAuthResult(outcome);
    }

    // The SDK wraps what its resolver meets. A directory that answered that it has no such DID has said something about the
    // token; one that did not answer has said nothing.
    private static bool IsNetwork(Exception failure)
    {
        for (var cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is System.Net.Sockets.SocketException or TimeoutException or TaskCanceledException) return true;
            if (cause is HttpRequestException answered)
                return answered.StatusCode is not { } status || status >= System.Net.HttpStatusCode.InternalServerError || status == System.Net.HttpStatusCode.TooManyRequests;
        }

        return false;
    }

    private static bool SignedBy(DidDocument document, string issuer, string algorithm, string token)
    {
        try
        {
            var key = AtprotoSigningKey.From(document, issuer);
            var signed = token.LastIndexOf('.');
            return key.Algorithm == algorithm
                && key.Verifies(Encoding.ASCII.GetBytes(token[..signed]), WebEncoders.Base64UrlDecode(token[(signed + 1)..]));
        }
        catch (Exception failure) when (failure is InvalidDataException or FormatException) { return false; }
    }

    private readonly record struct Claims(AtprotoServiceAuthOutcome? Refused, string? Issuer = null, string? Algorithm = null, long Expires = 0);

    // What a token says, read with no network. Every wrong kind of value throws from its accessor and reads as malformed.
    private Claims Read(string token, string audience, string method)
    {
        try
        {
            if (token.Length > Constants.ServiceAuthLongestToken) return new Claims(AtprotoServiceAuthOutcome.Malformed);
            var parts = token.Split('.');
            if (parts.Length != 3) return new Claims(AtprotoServiceAuthOutcome.Malformed);
            using var headerDocument = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0]));
            using var payloadDocument = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
            var (header, payload) = (headerDocument.RootElement, payloadDocument.RootElement);

            if (HasDuplicates(header) || HasDuplicates(payload) || header.TryGetProperty("crit", out _)
                || header.GetProperty("typ").GetString() != "JWT"
                || (header.TryGetProperty("kid", out var kid) && kid.GetString() != "#atproto"))
                return new Claims(AtprotoServiceAuthOutcome.Malformed);

            var algorithm = header.GetProperty("alg").GetString();
            if (algorithm is not ("ES256" or "ES256K")) return new Claims(AtprotoServiceAuthOutcome.UnsupportedAlgorithm);

            var issuer = payload.GetProperty("iss").GetString();
            if (issuer is null || !IsPlainDid(issuer)) return new Claims(AtprotoServiceAuthOutcome.Malformed);

            if (payload.GetProperty("aud").GetString() != audience) return new Claims(AtprotoServiceAuthOutcome.WrongAudience);
            if (payload.GetProperty("lxm").GetString() != method) return new Claims(AtprotoServiceAuthOutcome.WrongMethod);

            var now = time.GetUtcNow().ToUnixTimeSeconds();
            var longest = (long)Constants.ServiceAuthLongestLife.TotalSeconds;
            var (expires, issued) = (payload.GetProperty("exp").GetInt64(), payload.GetProperty("iat").GetInt64());
            if (expires <= now || expires > now + longest || expires <= issued || expires - issued > longest
                || issued > now + (long)Constants.ServiceAuthIssuedAhead.TotalSeconds
                || (payload.TryGetProperty("nbf", out var before) && before.GetInt64() > now))
                return new Claims(AtprotoServiceAuthOutcome.Expired);

            return new Claims(null, issuer, algorithm, expires);
        }
        catch (Exception failure) when (failure is JsonException or FormatException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or OverflowException)
        {
            return new Claims(AtprotoServiceAuthOutcome.Malformed);
        }
    }

    // What reaches the resolver comes from a token no one has checked yet, so an issuer is exactly a did:plc, or a did:web of
    // a plain lowercase host with no path, and a port only where the transport would reach it. Nothing else is resolved.
    private bool IsPlainDid(string did)
    {
        if (DidPlc().IsMatch(did)) return true;
        var web = DidWeb().Match(did);
        if (!web.Success) return false;
        var host = web.Groups["host"].Value;
        return web.Groups["port"] is { Success: true } port
            ? sessions.AdmitsPort(host, port.Value)
            : host.Contains('.') && char.IsAsciiLetterLower(host[host.LastIndexOf('.') + 1]);
    }

    [GeneratedRegex(@"\Adid:plc:[a-z2-7]{24}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DidPlc();

    [GeneratedRegex(@"\Adid:web:(?<host>[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)*)(?:%3A(?<port>[0-9]{1,5}))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex DidWeb();

    // Two parsers reading one duplicated name differently is how a token says two things.
    private static bool HasDuplicates(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) return true;
        return false;
    }
}
