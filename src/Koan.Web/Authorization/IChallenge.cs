using Microsoft.AspNetCore.Http;

namespace Koan.Web.Authorization;

/// <summary>
/// ARCH-0146 — what a refusal asks of a caller who is not signed in: one challenge of <c>WWW-Authenticate</c> (RFC 9110 §11.6.1), whose
/// scheme the application or a connector supplies, such as <c>Example proof="https://example.org/.well-known/example-proof"</c>. Every
/// <c>401</c> Koan's refusals answer carries each one registered, so a client learns where to prove who it is without reading a sentence.
/// </summary>
public interface IChallenge
{
    /// <summary>The challenge for this request, its scheme first.</summary>
    string For(HttpContext http);
}
