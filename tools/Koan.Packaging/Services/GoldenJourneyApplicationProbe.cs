using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class GoldenJourneyApplicationProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoldenJourneyEvidence> RunBuiltAsync(
        string applicationDirectory,
        string lane,
        CancellationToken cancellationToken)
    {
        var compositionLockfileObserved = CompositionLockfileProbe.Require(
            applicationDirectory,
            PackagingConstants.GoldenJourney.ApplicationName,
            PackagingConstants.ApplicationProbe.CoreModuleId,
            PackagingConstants.ApplicationProbe.SqliteModuleId,
            PackagingConstants.ApplicationProbe.JobsModuleId,
            PackagingConstants.ApplicationProbe.McpModuleId);
        var startedAt = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        var steps = new List<ApplicationStepEvidence>();
        var compositionLockfileMatched = false;
        var businessRuleObserved = false;
        var persistenceObserved = false;
        var reactiveWorkObserved = false;
        var jobsCompositionObserved = false;
        var factsConverged = false;
        var agentBoundaryObserved = false;
        var agentMutationObserved = false;
        var adapterRejectionExplained = false;
        var adapterRecoveryObserved = false;

        await using var host = ApplicationProbeHost.Start(
            applicationDirectory,
            PackagingConstants.GoldenJourney.ProjectFile,
            "golden-journey");
        var http = host.Http;
        var mcp = new McpProbeClient(http);

        try
        {
            await StepAsync("startup-health", steps, () => host.WaitUntilReadyAsync(cancellationToken));

            var webFacts = await StepAsync("operator-composition", steps, async () =>
            {
                using var response = await http.GetAsync(PackagingConstants.ApplicationProbe.FactsPath, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.GetProperty("complete").GetBoolean())
                    throw new InvalidOperationException("GoldenJourney runtime facts are incomplete.");
                compositionLockfileMatched = CompositionLockfileProbe.RequireRuntimeMatch(root);

                var facts = root.GetProperty("facts").EnumerateArray().ToArray();
                AssertSelected(facts, PackagingConstants.GoldenJourney.JobsLedgerSubject, PackagingConstants.GoldenJourney.DurableLedger);
                AssertSelected(facts, PackagingConstants.GoldenJourney.JobsTransportSubject, PackagingConstants.GoldenJourney.InProcessTransport);
                jobsCompositionObserved = true;
                return json;
            });

            var unassessedId = await StepAsync("meaningful-rest-result", steps, async () =>
            {
                var opened = await OpenAsync(http, "Review a routine supplier change", impact: 0, urgent: false, cancellationToken);
                var persisted = await ReadReviewAsync(http, opened, cancellationToken);
                persistenceObserved = persisted.GetProperty("title").GetString() == "Review a routine supplier change";
                if (!persistenceObserved) throw new InvalidOperationException("REST did not return the review it persisted.");
                return opened;
            });

            await StepAsync("agent-initialize", steps, () => mcp.InitializeAsync("koan-golden-journey-proof", cancellationToken));

            await StepAsync("agent-discovery", steps, async () =>
            {
                var tools = await mcp.CallAsync("tools/list", null, cancellationToken);
                var names = tools.GetProperty("result").GetProperty("tools").EnumerateArray()
                    .Select(tool => tool.GetProperty("name").GetString())
                    .ToArray();
                if (!names.Contains(PackagingConstants.GoldenJourney.PendingTool)
                    || !names.Contains(PackagingConstants.GoldenJourney.RecommendTool))
                    throw new InvalidOperationException("MCP did not advertise the bounded review workflow tools.");

                var self = await mcp.CallAsync("resources/read", new
                {
                    uri = PackagingConstants.ApplicationProbe.SelfUri
                }, cancellationToken);
                var selfText = self.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()
                    ?? throw new InvalidOperationException("MCP self-description returned no content.");
                using (var selfDocument = JsonDocument.Parse(selfText))
                {
                    var selfRoot = selfDocument.RootElement;
                    var selfTools = selfRoot.GetProperty(PackagingConstants.ApplicationProbe.CustomToolsProperty)
                        .EnumerateArray()
                        .Select(tool => tool.GetProperty("name").GetString())
                        .ToArray();
                    var prose = selfRoot.GetProperty("prose").GetString() ?? string.Empty;
                    if (!selfTools.Contains(PackagingConstants.GoldenJourney.PendingTool)
                        || !selfTools.Contains(PackagingConstants.GoldenJourney.RecommendTool)
                        || prose.Contains(PackagingConstants.ApplicationProbe.EmptySelfMessage, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("MCP self-description did not acknowledge its live review workflow tools.");
                }

                var facts = await mcp.CallAsync("resources/read", new
                {
                    uri = PackagingConstants.ApplicationProbe.RuntimeFactsUri
                }, cancellationToken);
                var mcpFacts = facts.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString();
                factsConverged = string.Equals(webFacts, mcpFacts, StringComparison.Ordinal);
                if (!factsConverged) throw new InvalidOperationException("Web and MCP projected different runtime fact envelopes.");
            });

            await StepAsync("agent-business-boundary", steps, async () =>
            {
                var result = await mcp.CallToolAsync(PackagingConstants.GoldenJourney.RecommendTool, new
                {
                    id = unassessedId,
                    disposition = "Approve",
                    rationale = "The evidence is sufficient."
                }, cancellationToken);
                using var outcome = JsonDocument.Parse(McpProbeClient.ToolText(result));
                agentBoundaryObserved = Property(outcome.RootElement, "code").GetString()
                    == PackagingConstants.GoldenJourney.NotReadyOutcome;
                if (!agentBoundaryObserved) throw new InvalidOperationException("The agent bypassed the assessment boundary.");
            });

            var assessedId = await StepAsync("reactive-assessment", steps, async () =>
            {
                var id = await OpenAsync(http, "Review a critical production change", impact: 2, urgent: false, cancellationToken);
                using var submit = await http.PostAsync($"{PackagingConstants.GoldenJourney.ReviewsPath}/{id}/assess", null, cancellationToken);
                submit.EnsureSuccessStatusCode();

                JsonElement assessment = default;
                for (var attempt = 0; attempt < PackagingConstants.ApplicationProbe.StartupAttempts; attempt++)
                {
                    using var response = await http.GetAsync($"{PackagingConstants.GoldenJourney.ReviewsPath}/{id}/assessment", cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                        assessment = document.RootElement.Clone();
                        if (assessment.GetProperty("status").GetString() == "Completed") break;
                    }
                    await Task.Delay(100, cancellationToken);
                }

                var review = await ReadReviewAsync(http, id, cancellationToken);
                businessRuleObserved = review.GetProperty("priority").GetString() == "Critical";
                reactiveWorkObserved = assessment.ValueKind != JsonValueKind.Undefined
                    && assessment.GetProperty("status").GetString() == "Completed"
                    && assessment.GetProperty("progress").GetDouble() == 1
                    && assessment.GetProperty("message").GetString() == "Ready for recommendation";
                if (!businessRuleObserved || !reactiveWorkObserved)
                    throw new InvalidOperationException("The reactive assessment did not produce the expected business result and progress.");

                var pending = await mcp.CallToolAsync(
                    PackagingConstants.GoldenJourney.PendingTool,
                    new { limit = 1 },
                    cancellationToken);
                using var pendingDocument = JsonDocument.Parse(McpProbeClient.ToolText(pending));
                if (!pendingDocument.RootElement.EnumerateArray().Any(item =>
                        string.Equals(Property(item, "id").GetString(), id, StringComparison.Ordinal)))
                    throw new InvalidOperationException("The bounded pending-review tool did not return the assessed request.");
                return id;
            });

            await StepAsync("agent-honest-dry-run", steps, async () =>
            {
                var result = await mcp.CallToolAsync(PackagingConstants.GoldenJourney.RecommendTool, new
                {
                    id = assessedId,
                    disposition = "Approve",
                    rationale = "The critical review is ready.",
                    dry_run = true
                }, cancellationToken);
                var diagnostics = result.GetProperty("meta").GetProperty("diagnostics");
                if (!diagnostics.GetProperty("dryRun").GetBoolean()
                    || diagnostics.GetProperty("rehearsable").GetBoolean()
                    || diagnostics.GetProperty("reason").GetString() != "custom-verb-uninspectable")
                    throw new InvalidOperationException("The custom mutation did not report its honest dry-run boundary.");
                var review = await ReadReviewAsync(http, assessedId, cancellationToken);
                if (TryProperty(review, "recommendation", out var recommendation)
                    && recommendation.ValueKind != JsonValueKind.Null)
                    throw new InvalidOperationException("The custom mutation changed state during dry-run.");
            });

            await StepAsync("agent-business-result", steps, async () =>
            {
                var result = await mcp.CallToolAsync(PackagingConstants.GoldenJourney.RecommendTool, new
                {
                    id = assessedId,
                    disposition = "Approve",
                    rationale = "The critical review is ready."
                }, cancellationToken);
                using var outcome = JsonDocument.Parse(McpProbeClient.ToolText(result));
                var review = await ReadReviewAsync(http, assessedId, cancellationToken);
                agentMutationObserved = Property(outcome.RootElement, "code").GetString()
                    == PackagingConstants.GoldenJourney.AcceptedOutcome
                    && review.GetProperty("recommendation").GetString() == "Approve"
                    && review.GetProperty("recommendedBy").GetString() == "agent";
                if (!agentMutationObserved) throw new InvalidOperationException("REST did not observe the agent's business recommendation.");
            });
        }
        catch (Exception exception)
        {
            throw await host.FailureAsync($"GoldenJourney {lane} proof failed", exception);
        }

        var logs = await host.StopAsync();
        if (!logs.StandardOutput.Contains("KOAN", StringComparison.OrdinalIgnoreCase)
            || !logs.StandardOutput.Contains("ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"GoldenJourney {lane} did not emit a recognizable Koan startup report.{Environment.NewLine}{logs.StandardOutput}{logs.StandardError}");
        if (logs.StandardOutput.Contains(PackagingConstants.ApplicationProbe.MissingWebRootWarning, StringComparison.Ordinal))
            throw new InvalidOperationException("GoldenJourney emitted a missing-web-root warning even though it is an API-only application.");

        await using (var rejected = ApplicationProbeHost.Start(
            applicationDirectory,
            PackagingConstants.GoldenJourney.ProjectFile,
            "golden-journey-rejected",
            new Dictionary<string, string?>
            {
                ["Koan__Data__Sources__Default__Adapter"] = PackagingConstants.GoldenJourney.UnavailableAdapter
            }))
        {
            await StepAsync("adapter-rejection-explained", steps, async () =>
            {
                var facts = await ReadFactsWhenAvailableAsync(rejected, cancellationToken);
                var rejection = facts.GetProperty("facts").EnumerateArray().FirstOrDefault(fact =>
                    fact.GetProperty("code").GetString() == PackagingConstants.GoldenJourney.AdapterRejectedCode
                    && fact.GetProperty("subject").GetString() == PackagingConstants.GoldenJourney.DefaultDataSubject);
                adapterRejectionExplained = rejection.ValueKind != JsonValueKind.Undefined
                    && rejection.GetProperty("state").GetString() == "rejected"
                    && rejection.GetProperty("reasonCode").GetString() == PackagingConstants.GoldenJourney.AdapterUnavailableReason
                    && rejection.GetProperty("correction").GetString()?.Contains("reference", StringComparison.OrdinalIgnoreCase) == true;
                if (!adapterRejectionExplained)
                    throw new InvalidOperationException("The unavailable adapter was not explained with a stable reason and correction.");
            });
            _ = await rejected.StopAsync();
        }

        await using (var recovered = ApplicationProbeHost.Start(
            applicationDirectory,
            PackagingConstants.GoldenJourney.ProjectFile,
            "golden-journey-recovered"))
        {
            await StepAsync("adapter-recovery", steps, async () =>
            {
                await recovered.WaitUntilReadyAsync(cancellationToken);
                var facts = await ReadFactsAsync(recovered.Http, cancellationToken);
                var election = facts.GetProperty("facts").EnumerateArray().FirstOrDefault(fact =>
                    fact.GetProperty("code").GetString() == PackagingConstants.GoldenJourney.AdapterSelectedCode
                    && fact.GetProperty("subject").GetString() == PackagingConstants.GoldenJourney.DefaultDataSubject);
                adapterRecoveryObserved = election.ValueKind != JsonValueKind.Undefined
                    && election.GetProperty("state").GetString() == "selected"
                    && election.GetProperty("summary").GetString()?.Contains(
                        PackagingConstants.GoldenJourney.SqliteAdapter,
                        StringComparison.OrdinalIgnoreCase) == true;
                if (!adapterRecoveryObserved)
                    throw new InvalidOperationException("Removing the unavailable adapter intent did not restore SQLite election.");
            });
            _ = await recovered.StopAsync();
        }

        total.Stop();
        return new GoldenJourneyEvidence(
            lane,
            startedAt,
            Math.Round(total.Elapsed.TotalSeconds, 3),
            compositionLockfileObserved,
            compositionLockfileMatched,
            businessRuleObserved,
            persistenceObserved,
            reactiveWorkObserved,
            jobsCompositionObserved,
            factsConverged,
            agentBoundaryObserved,
            agentMutationObserved,
            adapterRejectionExplained,
            adapterRecoveryObserved,
            steps);
    }

    private static async Task<string> OpenAsync(
        HttpClient http,
        string title,
        int impact,
        bool urgent,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            PackagingConstants.GoldenJourney.ReviewsPath,
            new { title, impact, urgent },
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Opening a review returned no id.");
    }

    private static async Task<JsonElement> ReadReviewAsync(HttpClient http, string id, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"{PackagingConstants.GoldenJourney.ReviewsPath}/{id}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> ReadFactsWhenAvailableAsync(
        ApplicationProbeHost host,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < PackagingConstants.ApplicationProbe.StartupAttempts; attempt++)
        {
            try
            {
                return await ReadFactsAsync(host.Http, cancellationToken);
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            await Task.Delay(PackagingConstants.ApplicationProbe.StartupPollMilliseconds, cancellationToken);
        }
        throw new InvalidOperationException("Runtime facts did not become reachable within the bounded startup window.");
    }

    private static async Task<JsonElement> ReadFactsAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(PackagingConstants.ApplicationProbe.FactsPath, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }

    private static void AssertSelected(IEnumerable<JsonElement> facts, string subject, string selection)
    {
        var fact = facts.FirstOrDefault(candidate => candidate.GetProperty("subject").GetString() == subject);
        if (fact.ValueKind == JsonValueKind.Undefined
            || !string.Equals(fact.GetProperty("state").GetString(), "selected", StringComparison.Ordinal)
            || !(fact.GetProperty("summary").GetString() ?? "").Contains(selection, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Runtime facts did not select '{selection}' for '{subject}'.");
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (TryProperty(element, name, out var value)) return value;
        throw new KeyNotFoundException($"JSON object does not contain '{name}'.");
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static async Task StepAsync(string name, ICollection<ApplicationStepEvidence> steps, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        await action();
        steps.Add(new ApplicationStepEvidence(name, Math.Round(stopwatch.Elapsed.TotalSeconds, 3)));
    }

    private static async Task<T> StepAsync<T>(string name, ICollection<ApplicationStepEvidence> steps, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        steps.Add(new ApplicationStepEvidence(name, Math.Round(stopwatch.Elapsed.TotalSeconds, 3)));
        return result;
    }
}
