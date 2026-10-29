using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Protocol;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// The protocol steps the K6 specs drive over real HTTP, written once: PKCE, client registration, the code flow up to
/// its code, the token exchange, the device request and poll, and a token signed by the host's own key under an issuer
/// of the spec's choosing. Every path is given under the host's path base.
/// </summary>
internal static class OAuthTestFlows
{
    public static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = OpaqueToken.New(32);
        return (verifier, OpaqueToken.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    /// <summary>Save <paramref name="client"/> into <paramref name="services"/>' host, whichever host is ambient.</summary>
    public static async Task Register(IServiceProvider services, OAuthClient client)
    {
        using var scope = AppHost.PushScope(services);
        client.CreatedUtc = DateTimeOffset.UtcNow;
        await client.Save(Ct);
    }

    public static Task RegisterCodeClient(IServiceProvider services, string clientId, string redirectUri, string name = "Code Client")
        => Register(services, new OAuthClient { Id = clientId, ClientName = name, RedirectUris = [redirectUri] });

    public static Task RegisterDeviceClient(IServiceProvider services, string clientId, string name = "Device Client")
        => Register(services, new OAuthClient { Id = clientId, ClientName = name, GrantTypes = [OAuthGrantTypes.DeviceCode] });

    public static string Query(string url, string key)
    {
        var at = url.IndexOf('?');
        return at < 0 ? "" : QueryHelpers.ParseQuery(url[(at + 1)..]).TryGetValue(key, out var value) ? value.ToString() : "";
    }

    public static string Authorize(string pathBase, string clientId, string redirectUri, string challenge, string resource, string state = "s")
        => QueryHelpers.AddQueryString(pathBase + "/oauth/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["scope"] = "notes.read",
            ["resource"] = resource,
            ["state"] = state,
        });

    /// <summary>Approve <paramref name="id"/> (a rid or a user code) as a form post, following nothing.</summary>
    public static Task<HttpResponseMessage> Decide(HttpClient browser, string pathBase, string id, string verb)
        => browser.PostAsync($"{pathBase}/oauth/request/{Uri.EscapeDataString(id)}/{verb}", new FormUrlEncodedContent([]), Ct);

    public static async Task<string> Redeem(HttpClient http, string pathBase, string clientId, string code, string verifier, string redirectUri)
        => (await RedeemBoth(http, pathBase, clientId, code, verifier, redirectUri)).Access;

    /// <summary>The token exchange, answering the access token and the refresh token.</summary>
    public static async Task<(string Access, string Refresh)> RedeemBoth(HttpClient http, string pathBase, string clientId, string code, string verifier, string redirectUri)
    {
        using var res = await http.PostAsync(pathBase + "/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = OAuthGrantTypes.AuthorizationCode,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier,
        }), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync(Ct));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return (doc.RootElement.GetProperty("access_token").GetString()!, doc.RootElement.GetProperty("refresh_token").GetString()!);
    }

    public static async Task<(string DeviceCode, string UserCode)> StartDevice(HttpClient http, string pathBase, string clientId, string resource)
    {
        using var res = await http.PostAsync(pathBase + "/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = "notes.read",
            ["resource"] = resource,
        }), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync(Ct));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return (doc.RootElement.GetProperty("device_code").GetString()!, doc.RootElement.GetProperty("user_code").GetString()!);
    }

    public static async Task<string> PollForToken(HttpClient http, string pathBase, string clientId, string deviceCode)
    {
        using var res = await http.PostAsync(pathBase + "/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = OAuthGrantTypes.DeviceCode,
            ["device_code"] = deviceCode,
            ["client_id"] = clientId,
        }), Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync(Ct));
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    public static async Task<string> MetadataIssuer(HttpClient http, string pathBase)
    {
        using var res = await http.GetAsync(pathBase + "/.well-known/oauth-authorization-server", Ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(Ct));
        return doc.RootElement.GetProperty("issuer").GetString()!;
    }

    public static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    /// <summary>A token signed by the host's own active key, under <paramref name="issuer"/>: authentic but not this
    /// application's issuer when the name differs; expired ten minutes ago when <paramref name="expired"/>.</summary>
    public static string Sign(IServiceProvider services, string issuer, string audience, string subject = "alice", bool expired = false)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = services.GetRequiredService<IIssuerKeyStore>().GetKeyRing().Active;
        var now = DateTime.UtcNow;
        return handler.WriteToken(handler.CreateJwtSecurityToken(
            issuer: issuer,
            audience: audience,
            subject: new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, subject)]),
            notBefore: expired ? now.AddMinutes(-30) : now.AddMinutes(-1),
            expires: expired ? now.AddMinutes(-10) : now.AddMinutes(5),
            issuedAt: expired ? now.AddMinutes(-30) : now,
            signingCredentials: new SigningCredentials(key, EcdsaKeyRing.Algorithm)));
    }
}
