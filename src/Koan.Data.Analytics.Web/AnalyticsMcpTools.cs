using System.Text.Json;
using Koan.Data.Analytics;
using Koan.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Analytics.Web;

/// <summary>
/// The agent door. Agents ask declared questions — never free-form SQL — so every refusal is loud,
/// every answer carries its provenance, and the vocabulary they see is exactly the vocabulary the
/// application declared.
/// </summary>
public static class AnalyticsMcpTools
{
    [McpTool(Name = "analytics.list_questions",
        Description = "List the analytics questions this application has declared, with each question's measure, grouping, and bounds.")]
    public static object ListQuestions(IServiceProvider services)
    {
        var entries = AnalyticsCatalog.All().Select(static question => new
        {
            question.Name,
            Entity = question.EntityType.Name,
            question.MeasureKind,
            MeasureMember = question.MeasureMember,
            GroupMember = question.GroupMember,
            question.RowCap
        });
        return new { Questions = entries, Count = AnalyticsCatalog.Count };
    }

    /// <param name="name">The declared question to run.</param>
    /// <param name="rowCap">Optional override of the question's row cap, within the host's ceiling.</param>
    [McpTool(Name = "analytics.ask",
        Description = "Run a declared analytics question by name and receive the answer with its provenance (engine, age, bounds). Free-form queries are not supported by design: ask a declared question or list the catalog.")]
    public static async Task<object> Ask(
        string name,
        IServiceProvider services,
        CancellationToken cancellationToken,
        int? rowCap = null,
        string? parametersJson = null)
    {
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            return new
            {
                Error = "unknown-question",
                Message = $"No analytics question named '{name}' is declared. This tool only answers declared questions.",
                Catalog = AnalyticsCatalog.Names()
            };
        }

        // Declared parameters are v0-optional: a question that declares none accepts no arguments, and an
        // attempt to pass any is refused rather than ignored.
        var bound = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(parametersJson))
        {
            try
            {
                using var document = JsonDocument.Parse(parametersJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return new { Error = "invalid-parameters", Message = "parameters must be a JSON object of { name: value }." };
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    bound[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText()
                    };
                }
            }
            catch (JsonException error)
            {
                return new { Error = "invalid-parameters", Message = error.Message };
            }
        }

        return await question.ExecuteAsync(services, rowCap ?? question.RowCap, bound, cancellationToken);
    }

    /// <param name="name">The declared materialized question to facet.</param>
    /// <param name="by">A declared column of the materialization.</param>
    /// <param name="since">Optional watermark from a previous facets or delta response; omit for the full distribution.</param>
    /// <param name="limit">Bucket ceiling; capped answers say so.</param>
    [McpTool(Name = "analytics.facets",
        Description = "Distinct values of one materialized analytics column with counts — the distribution, or with a watermark, what has been moving since. Refuses on-demand questions and undeclared columns.")]
    public static async Task<object> Facets(
        string name,
        string by,
        IServiceProvider services,
        CancellationToken cancellationToken,
        string? since = null,
        int limit = 100)
    {
        _ = services;
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            return new
            {
                Error = "unknown-question",
                Message = $"No analytics question named '{name}' is declared. This tool only answers declared questions.",
                Catalog = AnalyticsCatalog.Names()
            };
        }
        try
        {
            return await Analytics.Facets(name, by, since, limit, cancellationToken);
        }
        catch (NotSupportedException refusal)
        {
            return new { Error = "facets-refused", Message = refusal.Message };
        }
    }

    /// <param name="name">The declared materialized question to consume incrementally.</param>
    /// <param name="since">Optional watermark from a previous response; omit to start from the beginning.</param>
    /// <param name="limit">Row ceiling; capped answers say so.</param>
    [McpTool(Name = "analytics.delta",
        Description = "Rows a materialization wrote after a watermark, plus the next watermark — incremental consumption for agents. Pass back the cursor from the previous response unchanged.")]
    public static async Task<object> Delta(
        string name,
        IServiceProvider services,
        CancellationToken cancellationToken,
        string? since = null,
        int limit = 100)
    {
        _ = services;
        if (!AnalyticsCatalog.TryGet(name, out var question))
        {
            AnalyticsGapLog.Record(name);
            return new
            {
                Error = "unknown-question",
                Message = $"No analytics question named '{name}' is declared. This tool only answers declared questions.",
                Catalog = AnalyticsCatalog.Names()
            };
        }
        try
        {
            return await Analytics.Delta(name, since, limit, cancellationToken);
        }
        catch (NotSupportedException refusal)
        {
            return new { Error = "delta-refused", Message = refusal.Message };
        }
    }
}
