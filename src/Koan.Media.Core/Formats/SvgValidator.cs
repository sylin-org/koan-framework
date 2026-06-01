using System.Globalization;
using System.Text;
using System.Xml;
using Koan.Media.Abstractions.Recipes;

namespace Koan.Media.Core.Formats;

/// <summary>
/// Strict SVG validator: hard XML allowlist with DTD prohibited and
/// external entity resolution disabled. Per MEDIA-0006 §Decision.2.
/// Failure is terminal — the bytes are never stored, cached, or returned
/// to a downstream consumer. The validator does not clean SVG, it rejects.
/// </summary>
public static class SvgValidator
{
    /// <summary>Ingest cap for the <c>svg</c> format. Per ADR: 5 MiB.</summary>
    public const int MaxSourceBytes = 5 * 1024 * 1024;

    /// <summary>XmlReader document-character budget. Per ADR: 1,000,000.</summary>
    public const int MaxCharactersInDocument = 1_000_000;

    /// <summary>Maximum element nesting depth. Per ADR: 32.</summary>
    public const int MaxNestingDepth = 32;

    /// <summary>Maximum <c>stdDeviation</c> on <c>feGaussianBlur</c>. Per ADR: 10.</summary>
    public const double MaxBlurStdDev = 10.0;

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        // Structural
        "svg", "g", "defs", "symbol", "use", "marker",
        // Shapes
        "path", "circle", "rect", "line", "polyline", "polygon", "ellipse",
        // Text
        "text", "tspan", "title", "desc",
        // Paint
        "linearGradient", "radialGradient", "stop", "pattern",
        // Mask / Clip
        "mask", "clipPath",
        // Filter chain
        "filter",
        "feGaussianBlur", "feOffset", "feMerge", "feMergeNode", "feComposite",
        "feFlood", "feColorMatrix", "feBlend", "feConvolveMatrix",
        // Reference (raster image, data: URI only)
        "image",
    };

    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "script", "foreignObject", "iframe", "object", "embed", "applet",
        "meta", "link", "style", "a", "switch", "metadata",
        "animate", "animateMotion", "animateTransform", "set",
        "feTurbulence", "feDisplacementMap",
    };

    /// <summary>
    /// Validate the SVG bytes against the allowlist. Throws
    /// <see cref="SvgValidationException"/> on first violation, naming the
    /// offending element / attribute. Pure function — no I/O, no logger,
    /// no cache.
    /// </summary>
    public static void ValidateOrThrow(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));

        if (bytes.Length > MaxSourceBytes)
        {
            throw new SvgValidationException(
                $"SVG payload exceeds ingest cap ({bytes.Length} bytes > {MaxSourceBytes}).",
                kind: SvgValidationKind.OversizedInput);
        }

        // Fast prefix scan in the first 1 KiB — cheaper than building an
        // XmlReader for billion-laughs / DOCTYPE payloads.
        FastPrefixGate(bytes);

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharactersInDocument,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = true,
        };

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, settings);

        var depth = 0;
        try
        {
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        depth++;
                        if (depth > MaxNestingDepth)
                        {
                            throw new SvgValidationException(
                                $"SVG element nesting exceeds {MaxNestingDepth} levels.",
                                kind: SvgValidationKind.NestingTooDeep);
                        }
                        ValidateElement(reader);
                        if (reader.IsEmptyElement)
                        {
                            depth--;
                        }
                        break;
                    case XmlNodeType.EndElement:
                        depth--;
                        break;
                    case XmlNodeType.DocumentType:
                        throw new SvgValidationException(
                            "SVG contains a DOCTYPE declaration; DTD is prohibited.",
                            kind: SvgValidationKind.ForbiddenConstruct);
                }
            }
        }
        catch (XmlException ex)
        {
            throw new SvgValidationException(
                $"SVG is not well-formed XML: {ex.Message}",
                kind: SvgValidationKind.MalformedXml,
                inner: ex);
        }
    }

    private static void FastPrefixGate(byte[] bytes)
    {
        var prefixLen = Math.Min(bytes.Length, SvgFormat.HeaderSniffBytes);
        var text = Encoding.UTF8.GetString(bytes, 0, prefixLen).ToLowerInvariant();
        // These tokens never appear in a clean SVG body within the first 1 KiB.
        if (text.Contains("<!doctype", StringComparison.Ordinal))
        {
            throw new SvgValidationException(
                "SVG contains a DOCTYPE declaration; DTD is prohibited.",
                kind: SvgValidationKind.ForbiddenConstruct);
        }
        if (text.Contains("<!entity", StringComparison.Ordinal))
        {
            throw new SvgValidationException(
                "SVG contains an ENTITY declaration; internal entities are prohibited.",
                kind: SvgValidationKind.ForbiddenConstruct);
        }
        if (text.Contains("<script", StringComparison.Ordinal))
        {
            throw new SvgValidationException(
                "SVG contains a <script> element; scripting is prohibited.",
                kind: SvgValidationKind.ForbiddenConstruct);
        }
    }

    private static void ValidateElement(XmlReader reader)
    {
        var local = reader.LocalName;

        if (Forbidden.Contains(local))
        {
            throw new SvgValidationException(
                $"SVG element <{local}> is on the forbidden list.",
                kind: SvgValidationKind.ForbiddenConstruct);
        }

        if (!Allowed.Contains(local))
        {
            throw new SvgValidationException(
                $"SVG element <{local}> is not in the allowlist.",
                kind: SvgValidationKind.ForbiddenConstruct);
        }

        if (!reader.HasAttributes) return;

        var moved = reader.MoveToFirstAttribute();
        while (moved)
        {
            ValidateAttribute(reader, local);
            moved = reader.MoveToNextAttribute();
        }
        reader.MoveToElement();
    }

    private static void ValidateAttribute(XmlReader reader, string elementLocalName)
    {
        var name = reader.LocalName;
        var prefix = reader.Prefix;
        var value = reader.Value;

        // Event handlers (onclick, onload, …) — reject any attribute whose
        // local name starts with "on" regardless of element.
        if (name.Length >= 3 && name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            throw new SvgValidationException(
                $"SVG attribute '{name}' on <{elementLocalName}> looks like an event handler.",
                kind: SvgValidationKind.ForbiddenConstruct);
        }

        // href / xlink:href: must be fragment (#…) or data: URI.
        if (string.Equals(name, "href", StringComparison.Ordinal))
        {
            ValidateHrefValue(value, elementLocalName, prefix == "xlink" ? "xlink:href" : "href");
        }

        // style="…" forbidden constructs.
        if (string.Equals(name, "style", StringComparison.Ordinal))
        {
            var styleLower = value.ToLowerInvariant();
            if (styleLower.Contains("url(", StringComparison.Ordinal)
                || styleLower.Contains("expression(", StringComparison.Ordinal)
                || styleLower.Contains("@import", StringComparison.Ordinal)
                || styleLower.Contains("javascript:", StringComparison.Ordinal))
            {
                throw new SvgValidationException(
                    $"SVG style attribute on <{elementLocalName}> contains a forbidden construct.",
                    kind: SvgValidationKind.ForbiddenConstruct);
            }
        }

        // Numeric guard: feGaussianBlur stdDeviation cap.
        if (string.Equals(elementLocalName, "feGaussianBlur", StringComparison.Ordinal)
            && string.Equals(name, "stdDeviation", StringComparison.Ordinal))
        {
            if (TryParseFirstNumeric(value, out var stdDev) && stdDev > MaxBlurStdDev)
            {
                throw new SvgValidationException(
                    $"feGaussianBlur stdDeviation={stdDev:F2} exceeds the {MaxBlurStdDev:F1} cap.",
                    kind: SvgValidationKind.ForbiddenConstruct);
            }
        }
    }

    private static void ValidateHrefValue(string value, string elementLocalName, string attrLabel)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return; // empty href is harmless; spec allows it.
        if (trimmed.StartsWith("#", StringComparison.Ordinal)) return;
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
        throw new SvgValidationException(
            $"SVG attribute '{attrLabel}' on <{elementLocalName}> references an external resource.",
            kind: SvgValidationKind.ExternalReference);
    }

    private static bool TryParseFirstNumeric(string value, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var span = value.AsSpan().TrimStart();
        var cut = span.Length;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (!(char.IsDigit(c) || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E'))
            {
                cut = i;
                break;
            }
        }
        return double.TryParse(span[..cut], NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}

/// <summary>Classification of a <see cref="SvgValidationException"/>.</summary>
public enum SvgValidationKind
{
    /// <summary>Input exceeds the per-pipeline byte cap.</summary>
    OversizedInput = 0,

    /// <summary>XML is not well-formed.</summary>
    MalformedXml = 1,

    /// <summary>A forbidden element, attribute, or construct appears.</summary>
    ForbiddenConstruct = 2,

    /// <summary>An <c>href</c> / <c>xlink:href</c> references an external resource.</summary>
    ExternalReference = 3,

    /// <summary>Element nesting exceeds the depth cap.</summary>
    NestingTooDeep = 4,
}

/// <summary>
/// Thrown when <see cref="SvgValidator"/> rejects an SVG payload. Carries
/// a <see cref="Kind"/> classifier so HTTP / job surfaces can map to the
/// appropriate response without parsing the message.
/// </summary>
public sealed class SvgValidationException : Exception
{
    public SvgValidationException(string message, SvgValidationKind kind, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public SvgValidationKind Kind { get; }
}
