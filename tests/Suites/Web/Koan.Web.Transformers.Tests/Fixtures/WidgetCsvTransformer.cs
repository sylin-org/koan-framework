using System.IO;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers.Tests.Fixtures;

/// <summary>
/// Terminal-stage transformer for the Widget type. Produces <c>text/csv</c>. Used to verify
/// Accept-based negotiation and pipeline-then-terminal composition.
/// </summary>
public sealed class WidgetCsvTransformer : IEntityTransformer<Widget, string>
{
    public IReadOnlyList<string> AcceptContentTypes => new[] { "text/csv" };

    public Task<object> Transform(Widget model, HttpContext httpContext)
        => Task.FromResult<object>($"id,name,enriched,adminTagged\n{model.Id},{model.Name},{model.Enriched},{model.AdminTagged}\n");

    public Task<object> TransformMany(IEnumerable<Widget> models, HttpContext httpContext)
    {
        var sb = new StringBuilder("id,name,enriched,adminTagged\n");
        foreach (var m in models)
        {
            sb.Append($"{m.Id},{m.Name},{m.Enriched},{m.AdminTagged}\n");
        }
        return Task.FromResult<object>(sb.ToString());
    }

    public Task<Widget> Parse(Stream body, string contentType, HttpContext httpContext)
        => throw new NotSupportedException("CSV input parsing not exercised in these tests.");

    public Task<IReadOnlyList<Widget>> ParseMany(Stream body, string contentType, HttpContext httpContext)
        => throw new NotSupportedException("CSV input parsing not exercised in these tests.");
}
