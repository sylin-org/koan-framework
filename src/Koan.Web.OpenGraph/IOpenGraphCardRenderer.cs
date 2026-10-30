using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.OpenGraph;

/// <summary>
/// Resolves the card for a request path and returns the full SPA shell with the head block injected,
/// or null when OpenGraph is disabled or the shell is unavailable (the caller then falls through to
/// the app's own fallback). This is the lower-level seam for apps that wire the render into a custom
/// endpoint instead of relying on Koan's automatically contributed middleware.
/// </summary>
public interface IOpenGraphCardRenderer
{
    Task<string?> RenderShellAsync(HttpRequest request, CancellationToken ct = default);

    /// <summary>
    /// The head block for a card a server-rendered page hands over, for the page to place in its own head: its title
    /// element where configured, canonical link, OpenGraph and Twitter tags, encoded, truncated and absolutized. It reads
    /// no shell, matches no route and reads no Entity, so what the card says and who may see it are the caller's. Empty
    /// when OpenGraph is disabled.
    /// </summary>
    string RenderHead(SocialCard card, HttpRequest request);
}
