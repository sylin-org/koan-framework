using System.Net;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0078: the forwarded-header posture for <c>Koan:Web:TrustedProxies</c>. Only the declared proxies are believed,
/// for one hop, for the scheme, the host and the client address. The host a proxy forwards is the origin the request
/// arrived on (WEB-0081); the absolute URLs Koan writes still come from <see cref="KoanWebBaseUrl"/>, which derives
/// from it only where nothing is configured or accepted. Fail-closed like <c>Koan:Web:Origin</c>: nothing declared
/// means no forwarded header is honoured, and a blank entry declares nothing.
/// </summary>
internal static class KoanWebTrustedProxies
{
    /// <summary>
    /// The forwarded-headers options for the declared proxies, or null when no proxy is declared once blank entries are
    /// ignored. ASP.NET believes forwarded headers from every peer when it knows no proxy and no network, so Koan never
    /// runs the middleware in that state: null means the middleware is not added.
    /// </summary>
    public static ForwardedHeadersOptions? ForwardedHeaders(IEnumerable<string?> declared)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
            ForwardLimit = 1,
        };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var entry in declared)
        {
            var trimmed = entry?.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (IPAddress.TryParse(trimmed, out var address)) options.KnownProxies.Add(address);
            else if (System.Net.IPNetwork.TryParse(trimmed, out var network)) options.KnownIPNetworks.Add(network);
            else
            {
                throw new InvalidOperationException(
                    $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.TrustedProxies} entry '{trimmed}' is " +
                    "not an IP address or a CIDR network such as 10.0.0.5 or 10.0.0.0/8.");
            }
        }

        return options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0 ? null : options;
    }
}
