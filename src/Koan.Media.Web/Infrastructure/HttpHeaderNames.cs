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
    public const string Vary = "Vary";

    // Recipe pipeline diagnostics (MEDIA-0004 §8)
    public const string XKoanMediaRecipe = "X-Koan-Media-Recipe";
    public const string XKoanMediaRecipeHash = "X-Koan-Media-RecipeHash";
    public const string XKoanMediaSourceFormat = "X-Koan-Media-SourceFormat";
    public const string XKoanMediaOutputFormat = "X-Koan-Media-OutputFormat";
    public const string XKoanMediaFrameCount = "X-Koan-Media-FrameCount";
    public const string XKoanMediaIgnoredParams = "X-Koan-Media-IgnoredParams";
}
