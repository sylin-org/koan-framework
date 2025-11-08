using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Koan.Context.Middleware;

/// <summary>
/// Middleware that adds security headers to all responses to protect against XSS, clickjacking, and other attacks
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _options = configuration.GetSection("Koan:Context:Security:Headers").Get<SecurityHeadersOptions>()
            ?? new SecurityHeadersOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Content Security Policy (XSS Protection)
        if (_options.EnableCsp)
        {
            context.Response.Headers.Append("Content-Security-Policy", _options.CspDirective);
        }

        // X-Content-Type-Options: Prevents MIME type sniffing
        if (_options.EnableNoSniff)
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        }

        // X-Frame-Options: Prevents clickjacking
        if (_options.EnableFrameOptions)
        {
            context.Response.Headers.Append("X-Frame-Options", _options.FrameOptionsValue);
        }

        // X-XSS-Protection: Legacy XSS protection for older browsers
        if (_options.EnableXssProtection)
        {
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
        }

        // Referrer-Policy: Controls referrer information
        if (_options.EnableReferrerPolicy)
        {
            context.Response.Headers.Append("Referrer-Policy", _options.ReferrerPolicyValue);
        }

        // Permissions-Policy: Controls browser features
        if (_options.EnablePermissionsPolicy)
        {
            context.Response.Headers.Append("Permissions-Policy", _options.PermissionsPolicyValue);
        }

        // Strict-Transport-Security (HSTS): Forces HTTPS
        // Note: Only apply in production with HTTPS
        if (_options.EnableHsts && context.Request.IsHttps)
        {
            context.Response.Headers.Append("Strict-Transport-Security",
                $"max-age={_options.HstsMaxAge}; includeSubDomains; preload");
        }

        await _next(context);
    }
}

/// <summary>
/// Configuration options for security headers
/// </summary>
public class SecurityHeadersOptions
{
    public bool EnableCsp { get; set; } = true;
    public string CspDirective { get; set; } = "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public bool EnableNoSniff { get; set; } = true;

    public bool EnableFrameOptions { get; set; } = true;
    public string FrameOptionsValue { get; set; } = "DENY";

    public bool EnableXssProtection { get; set; } = true;

    public bool EnableReferrerPolicy { get; set; } = true;
    public string ReferrerPolicyValue { get; set; } = "strict-origin-when-cross-origin";

    public bool EnablePermissionsPolicy { get; set; } = true;
    public string PermissionsPolicyValue { get; set; } = "geolocation=(), microphone=(), camera=()";

    public bool EnableHsts { get; set; } = false; // Disabled by default (requires HTTPS in production)
    public int HstsMaxAge { get; set; } = 31536000; // 1 year in seconds
}
