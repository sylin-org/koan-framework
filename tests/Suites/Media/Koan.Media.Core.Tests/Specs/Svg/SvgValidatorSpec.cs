using System.Text;
using Koan.Media.Core.Formats;

namespace Koan.Media.Core.Tests.Specs.Svg;

/// <summary>
/// MEDIA-0006 §Decision.2: strict allowlist gate. The validator is the
/// single trust boundary for SVG payloads; every test below either
/// proves an allowed construct survives untouched or proves a forbidden
/// construct produces a typed <see cref="SvgValidationException"/> with
/// a kind classifier suitable for HTTP / job mapping.
/// </summary>
public sealed class SvgValidatorSpec
{
    private static byte[] Bytes(string svg) => Encoding.UTF8.GetBytes(svg);

    private const string ViewBox = "viewBox=\"0 0 100 100\"";

    /// <summary>Minimal valid SVG: just svg + path.</summary>
    [Fact]
    public void Accepts_minimal_svg_with_path()
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}><path d=\"M0 0 L100 100\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("g")]
    [InlineData("defs")]
    [InlineData("path")]
    [InlineData("circle")]
    [InlineData("rect")]
    [InlineData("line")]
    [InlineData("polyline")]
    [InlineData("polygon")]
    [InlineData("ellipse")]
    [InlineData("linearGradient")]
    [InlineData("radialGradient")]
    [InlineData("mask")]
    [InlineData("clipPath")]
    [InlineData("filter")]
    [InlineData("text")]
    [InlineData("tspan")]
    [InlineData("symbol")]
    public void Accepts_allowlisted_element(string elementName)
    {
        // Use a self-closing form for elements that don't need children.
        // tspan must live inside text; nest it accordingly.
        var inner = elementName switch
        {
            "tspan" => "<text><tspan>hi</tspan></text>",
            "linearGradient" or "radialGradient" => $"<defs><{elementName} id=\"g\"><stop offset=\"0\"/></{elementName}></defs>",
            "mask" or "clipPath" => $"<defs><{elementName} id=\"m\"><rect width=\"10\" height=\"10\"/></{elementName}></defs>",
            "filter" => "<defs><filter id=\"f\"><feGaussianBlur stdDeviation=\"2\"/></filter></defs>",
            "symbol" => "<defs><symbol id=\"s\"><rect width=\"10\" height=\"10\"/></symbol></defs>",
            "defs" => "<defs><rect width=\"10\" height=\"10\"/></defs>",
            "g" => "<g><rect width=\"10\" height=\"10\"/></g>",
            "text" => "<text>hi</text>",
            _ => $"<{elementName}/>",
        };
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>{inner}</svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().NotThrow($"<{elementName}> is on the documented allowlist");
    }

    [Fact]
    public void Accepts_use_referencing_internal_symbol()
    {
        // <use> with a fragment href is a documented allowlisted reference shape.
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                  "<defs><symbol id=\"s\"><rect width=\"10\" height=\"10\"/></symbol></defs>" +
                  "<use href=\"#s\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().NotThrow();
    }

    [Fact]
    public void Rejects_script_element()
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}><script>alert(1)</script></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ForbiddenConstruct)
            .WithMessage("*script*");
    }

    [Fact]
    public void Rejects_foreignObject_element()
    {
        // foreignObject inlines HTML — strictly forbidden.
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                  "<foreignObject width=\"100\" height=\"100\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ForbiddenConstruct)
            .WithMessage("*foreignObject*");
    }

    [Fact]
    public void Rejects_style_element_but_allows_inline_style_attribute()
    {
        var withStyleElement = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                               "<style>.x{fill:red}</style></svg>";
        var withStyleAttr = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                            "<rect width=\"10\" height=\"10\" style=\"fill:red\"/></svg>";

        ((Action)(() => SvgValidator.ValidateOrThrow(Bytes(withStyleElement))))
            .Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ForbiddenConstruct);

        ((Action)(() => SvgValidator.ValidateOrThrow(Bytes(withStyleAttr))))
            .Should().NotThrow("inline style ATTRIBUTE is allowed; only the style ELEMENT is forbidden");
    }

    [Theory]
    [InlineData("onclick")]
    [InlineData("onload")]
    [InlineData("onmouseover")]
    public void Rejects_on_event_handler_attribute(string handlerName)
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                  $"<rect width=\"10\" height=\"10\" {handlerName}=\"alert(1)\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ForbiddenConstruct)
            .WithMessage($"*{handlerName}*");
    }

    [Fact]
    public void Rejects_href_pointing_to_external_url()
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                  "<use href=\"http://example.com/x.svg\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ExternalReference);
    }

    [Fact]
    public void Accepts_href_pointing_to_data_uri()
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                  "<image href=\"data:image/png;base64,iVBORw0KGgo=\" width=\"10\" height=\"10\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().NotThrow();
    }

    [Fact]
    public void Accepts_href_pointing_to_fragment()
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                  "<defs><symbol id=\"s\"><rect width=\"10\" height=\"10\"/></symbol></defs>" +
                  "<use href=\"#s\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().NotThrow();
    }

    [Fact]
    public void Rejects_dtd_declaration()
    {
        // The fast-prefix gate catches "<!DOCTYPE" in the first 1 KiB.
        var svg = "<?xml version=\"1.0\"?>" +
                  "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" +
                  $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}><rect width=\"10\" height=\"10\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ForbiddenConstruct);
    }

    [Fact]
    public void Rejects_entity_declaration()
    {
        // ENTITY declarations are the substrate of the billion-laughs attack.
        var svg = "<?xml version=\"1.0\"?>" +
                  "<!DOCTYPE svg [<!ENTITY x \"hello\">]>" +
                  $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}><rect width=\"10\" height=\"10\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ForbiddenConstruct);
    }

    [Fact]
    public void Rejects_payload_exceeding_5_MiB_cap()
    {
        // The cap is enforced before the XmlReader sees a byte; build a
        // payload one byte past the documented cap.
        var size = SvgValidator.MaxSourceBytes + 1;
        var bytes = new byte[size];
        bytes[0] = (byte)'<';
        var act = () => SvgValidator.ValidateOrThrow(bytes);
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.OversizedInput);
    }

    [Fact]
    public void Rejects_deeply_nested_groups_as_billion_laughs_proxy()
    {
        // Build > MaxNestingDepth open <g> tags before the first content
        // node so the depth counter trips before the validator can return
        // for any other reason.
        var depth = SvgValidator.MaxNestingDepth + 4;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>");
        for (var i = 0; i < depth; i++) sb.Append("<g>");
        sb.Append("<rect width=\"10\" height=\"10\"/>");
        for (var i = 0; i < depth; i++) sb.Append("</g>");
        sb.Append("</svg>");

        var act = () => SvgValidator.ValidateOrThrow(Bytes(sb.ToString()));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.NestingTooDeep);
    }

    [Fact]
    public void Rejects_malformed_xml_with_typed_kind()
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}><rect></svg>"; // <rect> never closed
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.MalformedXml);
    }

    [Fact]
    public void Rejects_style_attribute_containing_url_reference()
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {ViewBox}>" +
                  "<rect width=\"10\" height=\"10\" style=\"background:url(http://example.com/x.png)\"/></svg>";
        var act = () => SvgValidator.ValidateOrThrow(Bytes(svg));
        act.Should().Throw<SvgValidationException>()
            .Where(ex => ex.Kind == SvgValidationKind.ForbiddenConstruct);
    }
}
