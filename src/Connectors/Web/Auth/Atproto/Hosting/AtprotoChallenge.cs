using System.Text.Json;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Microsoft.AspNetCore.Authentication;

namespace Koan.Web.Auth.Connector.Atproto;

public static class AtprotoChallenge
{
    /// <summary>Request additional declared permissions from trusted server code. Ordinary sign-in requests only atproto.</summary>
    public static AuthenticationProperties WithScopes(AuthenticationProperties properties, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(scopes);
        properties.Items[Constants.RequestedScopes] = JsonSerializer.Serialize(scopes);
        return properties;
    }

    /// <summary>Asks <paramref name="identifier"/>, a handle or DID, for the grant: the account a signed-in person already
    /// is, so the provider asks them and no one else. It takes the place of any <c>identifier</c> in the request's query.</summary>
    public static AuthenticationProperties ForAccount(AuthenticationProperties properties, string identifier)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        properties.Items[Constants.Account] = identifier;
        return properties;
    }
}
