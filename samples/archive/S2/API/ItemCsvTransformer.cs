using S2.Api.Controllers;
using Koan.Web.Transformers;
using System.Text;

namespace S2.Api;

public sealed class ItemCsvTransformer : IEntityTransformer<Item, string>
{
    public IReadOnlyList<string> AcceptContentTypes => new[] { "text/csv" };

    public Task<object> Transform(Item model, HttpContext httpContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name");
        sb.AppendLine($"{Escape(model.Id)},{Escape(model.Name)}");
        return Task.FromResult<object>(sb.ToString());
    }

    public Task<object> TransformMany(IEnumerable<Item> models, HttpContext httpContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Name");
        foreach (var m in models)
            sb.AppendLine($"{Escape(m.Id)},{Escape(m.Name)}");
        return Task.FromResult<object>(sb.ToString());
    }

    public async Task<Item> Parse(Stream body, string contentType, HttpContext httpContext)
    {
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) throw new InvalidOperationException("CSV requires header and one row");
        var cols = lines[1].Split(',', 2);
        return new Item { Id = cols[0], Name = cols.Length > 1 ? cols[1] : "" };
    }

    public async Task<IReadOnlyList<Item>> ParseMany(Stream body, string contentType, HttpContext httpContext)
    {
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        var text = await reader.ReadToEndAsync();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return [];
        return lines.Skip(1).Select(l =>
        {
            var cols = l.Split(',', 2);
            return new Item { Id = cols[0], Name = cols.Length > 1 ? cols[1] : "" };
        }).ToList();
    }

    private static string Escape(string? s)
        => (s ?? "").Replace("\"", "\"\"").Replace(",", "\\,");
}
