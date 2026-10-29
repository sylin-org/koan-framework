using System.Net;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0078: the forwarded-header posture for <c>Koan:Web:TrustedProxies</c>. Only the declared proxies are believed,
/// for one hop, for the scheme and the client address; the public host comes from <see cref="KoanWebBaseUrl"/>.
/// Fail-closed like <c>Koan:Web:Origin</c>: nothing declared means no forwarded header is honoured.
/// </summary>
internal static class KoanWebTrustedProxies
{
    public static ForwardedHeadersOptions ForwardedHeaders(IEnumerable<string> declared)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
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

        return options;
    }
}
