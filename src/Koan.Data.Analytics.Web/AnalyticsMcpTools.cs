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
        if (!string.IsNullOrWhiteSpace(parametersJson))
        {
            try
            {
                using var document = JsonDocument.Parse(parametersJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject().Any())
                    return new
                    {
                        Error = "parameters-not-accepted",
                        Message = "This question takes no parameters in this grammar version; declare a parameterized question instead."
                    };
            }
            catch (JsonException error)
            {
                return new { Error = "invalid-parameters", Message = error.Message };
            }
        }

        return await question.ExecuteAsync(services, rowCap ?? question.RowCap, cancellationToken);
    }
}
