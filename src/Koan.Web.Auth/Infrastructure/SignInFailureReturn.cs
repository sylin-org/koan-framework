using System.Diagnostics.CodeAnalysis;
using System.Text;
using Koan.Web.Auth.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// WEB-0080 — where a failed sign-in lands. The application names the landing per challenge, beside <c>return</c>
/// (<c>failure=/sign-in</c>, carried in the authentication properties), or for every provider in
/// <c>Koan:Web:Auth:ReturnUrl:FailurePath</c>; the per-challenge value wins. A handler that cannot complete a sign-in
/// asks here, and when a landing is named it sends the person there with one code from <see cref="SignInFailure"/>
/// in the <c>error</c> query and <c>Cache-Control: no-store</c>, never the provider's text. When nothing is named
/// the handler keeps its own answer.
/// </summary>
/// <remarks>
/// A landing is a rooted, application-relative path (<c>/sign-in</c>, <c>/sign-in?from=home</c>), placed under the
/// application's path base exactly once (SEC-0027): <c>/sign-in</c> under <c>/me</c> is <c>/me/sign-in</c>, and
/// <c>/me/sign-in</c> is <c>/me/me/sign-in</c>. It is written in printable ASCII from the characters a
/// path and a query carry, and, after percent-decoding, names no host (<c>//</c>), no <c>.</c> or <c>..</c> segment,
/// no backslash, no encoded slash, no control character and no character outside printable ASCII; its query does not
/// name <c>error</c>, which carries the code. Anything else is refused: the landing is where this application's own
/// page is, not a redirect target a caller chooses, and every accepted landing is a valid <c>Location</c> header.
/// </remarks>
public static class SignInFailureReturn
{
    /// <summary>The query key the landing receives the code in.</summary>
    public const string CodeParameter = AuthConstants.Query.Error;

    /// <summary>The sanitized code for <paramref name="failure"/>, as the landing receives it.</summary>
    public static string Code(SignInFailure failure) => failure switch
    {
        SignInFailure.ProviderUnreachable => "provider_unreachable",
        SignInFailure.HandleUnresolved => "handle_unresolved",
        SignInFailure.AccessDenied => "access_denied",
        SignInFailure.CallbackMismatch => "callback_mismatch",
        _ => "sign_in_failed",
    };

    /// <summary>
    /// Accept <paramref name="candidate"/> as a landing: a rooted path of this application. False, with the problem
    /// named, for anything that could leave the application, leave its base path, or fail as a header.
    /// </summary>
    public static bool TryAccept(string? candidate, [NotNullWhen(true)] out string? path, [NotNullWhen(false)] out string? problem)
    {
        path = null;
        problem = null;
        if (string.IsNullOrEmpty(candidate)) { problem = "it is empty"; return false; }
        foreach (var c in candidate)
        {
            if (c is < '!' or > '~') { problem = "it contains a space, a control character or a character outside printable ASCII"; return false; }
            if (c == '\\') { problem = "it contains a backslash"; return false; }
            if (!IsLandingCharacter(c))
            {
                problem = "it contains a character a path does not carry: a fragment's '#', a bracket, a quote or one a URL must encode";
                return false;
            }
        }
        if (candidate[0] != '/') { problem = "it does not start with '/'"; return false; }

        var split = candidate.IndexOf('?');
        var rawPath = split < 0 ? candidate : candidate[..split];
        var rawQuery = split < 0 ? "" : candidate[split..];
        if (!TryDecode(rawPath, inPath: true, out var decodedPath, out problem)) return false;
        if (!TryDecode(rawQuery, inPath: false, out _, out problem)) return false;
        if (decodedPath.StartsWith("//", StringComparison.Ordinal)) { problem = "it names a host"; return false; }
        foreach (var segment in decodedPath.Split('/'))
        {
            if (segment is "." or "..") { problem = "it contains a '.' or '..' segment"; return false; }
        }
        if (rawQuery.Length > 0 && QueryHelpers.ParseQuery(rawQuery).ContainsKey(CodeParameter))
        {
            problem = $"its query names '{CodeParameter}', which carries the failure code";
            return false;
        }
        path = candidate;
        return true;
    }

    // RFC 3986 pchar (unreserved, sub-delims, ':' and '@'), '%' for an encoded byte, '/' between segments and '?'
    // before the query; a fragment, brackets and the characters a URL must encode are not a landing's.
    private static bool IsLandingCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or '!' or '$' or '&' or '\'' or '(' or ')'
            or '*' or '+' or ',' or ';' or '=' or ':' or '@' or '%' or '/' or '?';

    // Percent-decode, refusing a malformed escape, an encoded control or non-ASCII byte, and, in the path, an encoded
    // slash or backslash (a browser or a proxy may read either as a separator).
    private static bool TryDecode(string value, bool inPath, out string decoded, [NotNullWhen(false)] out string? problem)
    {
        decoded = value;
        problem = null;
        if (!value.Contains('%')) return true;
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') { builder.Append(value[i]); continue; }
            if (i + 2 >= value.Length || !char.IsAsciiHexDigit(value[i + 1]) || !char.IsAsciiHexDigit(value[i + 2]))
            {
                problem = "it contains a '%' that does not encode a byte";
                return false;
            }
            var b = (char)Convert.ToByte(value.Substring(i + 1, 2), 16);
            if (b is < ' ' or > '~') { problem = "it encodes a control character or a character outside printable ASCII"; return false; }
            if (inPath && b is '/' or '\\') { problem = "it encodes a slash or a backslash in its path"; return false; }
            builder.Append(b);
            i += 2;
        }
        decoded = builder.ToString();
        return true;
    }

    /// <summary>True when <paramref name="candidate"/> is a landing <see cref="TryAccept"/> accepts.</summary>
    public static bool Accepts(string? candidate) => TryAccept(candidate, out _, out _);

    /// <summary>
    /// The landing named for this sign-in, under the application's path base exactly once (SEC-0027), or null when the
    /// application named none: the per-challenge value carried in <paramref name="properties"/> first, then the
    /// configured <see cref="ReturnUrlOptions.FailurePath"/>.
    /// </summary>
    public static string? Landing(HttpContext context, AuthenticationProperties? properties)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? landing = null;
        if (properties is not null && properties.Items.TryGetValue(AuthConstants.Items.FailurePath, out var carried) && Accepts(carried))
            landing = carried;
        landing ??= context.RequestServices.GetService<IOptions<AuthOptions>>()?.Value.ReturnUrl.FailurePath;
        if (!Accepts(landing)) return null;
        return ApplicationPaths.Under(context, landing!);
    }

    /// <summary>The landing for this sign-in with <paramref name="failure"/>'s code in its query, or null when none is named.</summary>
    public static string? Resolve(HttpContext context, AuthenticationProperties? properties, SignInFailure failure)
    {
        var landing = Landing(context, properties);
        return landing is null ? null : QueryHelpers.AddQueryString(landing, CodeParameter, Code(failure));
    }

    /// <summary>
    /// Send the person to the landing with <paramref name="failure"/>'s code, uncached. False, with the response
    /// untouched, when the application named no landing.
    /// </summary>
    public static bool Redirect(HttpContext context, AuthenticationProperties? properties, SignInFailure failure)
    {
        var url = Resolve(context, properties, failure);
        if (url is null) return false;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(url);
        return true;
    }
}
