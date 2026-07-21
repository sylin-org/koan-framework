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
}
