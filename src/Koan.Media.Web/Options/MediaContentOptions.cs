namespace Koan.Media.Web.Options;

/// <summary>
/// Options to control HTTP caching behavior for media content responses.
/// </summary>
public sealed class MediaContentOptions
{
    /// <summary>
    /// When true, the controller emits Cache-Control headers for GET/HEAD and 206 responses.
    /// </summary>
    public bool EnableCacheControl { get; set; } = true;

    /// <summary>
    /// When true, sets Cache-Control as public; otherwise private.
    /// </summary>
    public bool Public { get; set; } = true;

    /// <summary>
    /// Max-age for Cache-Control; when null, header is not emitted even if EnableCacheControl is true.
    /// </summary>
    public TimeSpan? MaxAge { get; set; } = TimeSpan.FromHours(1);
}
