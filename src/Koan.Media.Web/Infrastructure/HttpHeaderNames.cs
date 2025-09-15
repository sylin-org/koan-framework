namespace Koan.Media.Web.Infrastructure;

/// <summary>
/// Centralized HTTP header names used by Koan.Media.Web to avoid magic strings.
/// </summary>
public static class HttpHeaderNames
{
    public const string Range = "Range";
    public const string IfNoneMatch = "If-None-Match";
    public const string IfModifiedSince = "If-Modified-Since";
    public const string AcceptRanges = "Accept-Ranges";
    public const string ContentRange = "Content-Range";
    public const string ETag = "ETag";
    public const string LastModified = "Last-Modified";
    public const string CacheControl = "Cache-Control";
    public const string XMediaIgnoredParams = "X-Media-Ignored-Params";
    public const string XMediaVariant = "X-Media-Variant";
}
