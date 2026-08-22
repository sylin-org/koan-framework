namespace Koan.Web.Infrastructure;

/// <summary>
/// Whether a caller's <c>Cache-Control</c> / <c>X-Koan-Cache</c> header may steer this application's
/// server-side Entity cache.
/// </summary>
public sealed class KoanCacheControlOptions
{
    public const string SectionPath = "Koan:Web:CacheControl";

    /// <summary>
    /// Consent for Production, where callers are not necessarily the application's own. Development,
    /// Staging, Test, and CI honour the headers without it — see <c>KoanEnv.Gate</c> and ARCH-0128.
    /// </summary>
    public bool HonorClientHeaders { get; set; }
}
