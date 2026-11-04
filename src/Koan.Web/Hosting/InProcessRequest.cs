using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Koan.Web.Hosting;

/// <summary>
/// One request of the application to answer in process (ARCH-0144): a method and an address, the headers and body it is given,
/// values handed to it as features, and whom it is read as. <see cref="KoanWebPipeline.Send"/> answers it.
/// </summary>
/// <param name="method">The HTTP method, such as <c>GET</c>.</param>
/// <param name="address">The path and query under the application's base URL, as a request line spells them:
/// <c>/places/open/topics?page=2</c>, naming no dot segment. The path base is the pipeline's to add.</param>
public sealed class InProcessRequest(string method, string address)
{
    public string Method { get; } = method;

    public string Address { get; } = address;

    /// <summary>The headers it is given, <c>Accept</c> and <c>Content-Type</c> among them, which win over any it carries of the request in
    /// hand. Never <c>Cookie</c> or <c>Authorization</c>: the reader's credential is the request in hand's to carry.</summary>
    public IHeaderDictionary Headers { get; } = new HeaderDictionary();

    public ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>Values handed to the request in process, each set as a feature of its <see cref="HttpContext"/>. Never one of ASP.NET's
    /// own server features, which the pipeline sets.</summary>
    public IFeatureCollection Features { get; } = new FeatureCollection();

    /// <summary>Read as nobody: nothing of the request in hand reaches it but its scheme and host, and its principal is empty.</summary>
    public bool AsNobody { get; init; }

    /// <summary>With no request in hand (STDIO, a command-line verb), the principal the caller's host gives. Never read while a request
    /// is in hand, whose own credential is the reader.</summary>
    public ClaimsPrincipal? Principal { get; init; }

    /// <summary>The headers of the caller's own transport, which the request never carries of the request in hand.</summary>
    public ISet<string> Withheld { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
