namespace Koan.Web.OpenGraph;

/// <summary>
/// Options for the OpenGraph head-injection pillar, bound from <c>Koan:Web:OpenGraph</c>.
/// Every toggle defaults to the fully capable behavior: a consumer gets the title element and the
/// canonical link without opting in.
/// </summary>
public sealed class OpenGraphOptions
{
    public const string SectionPath = "Koan:Web:OpenGraph";

    /// <summary>Master switch. When false the middleware is a clean passthrough.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Path to the SPA shell (index.html) on disk. When unset/unreadable the middleware passes through.</summary>
    public string? ShellPath { get; set; }

    /// <summary>Marker replaced with the rendered head block. When absent from the shell, the block is inserted before <c>&lt;/head&gt;</c>.</summary>
    public string PlaceholderMarker { get; set; } = "<!--KOAN_OPENGRAPH-->";

    public string? SiteName { get; set; }

    public string? DefaultDescription { get; set; }

    /// <summary>Root-relative or absolute fallback image for cards with no resolved image.</summary>
    public string? DefaultImage { get; set; }

    public string DefaultType { get; set; } = "website";

    public string TwitterCard { get; set; } = "summary_large_image";

    public string? Locale { get; set; }

    // Head vocabulary toggles. The seam carries more than og:; these gate the additive members.

    /// <summary>Rewrite the shell's <c>&lt;title&gt;</c> from the card title.</summary>
    public bool EmitTitleElement { get; set; } = true;

    /// <summary>Emit <c>&lt;link rel="canonical"&gt;</c> from the resolved url.</summary>
    public bool EmitCanonical { get; set; } = true;

    /// <summary>Emit the <c>twitter:card/title/description/image</c> block.</summary>
    public bool EmitTwitterTags { get; set; } = true;

    // Display-length truncation, a correctness concern owned once (like HTML-encoding). 0 disables.

    public int MaxTitleLength { get; set; } = 70;

    public int MaxDescriptionLength { get; set; } = 200;
}
