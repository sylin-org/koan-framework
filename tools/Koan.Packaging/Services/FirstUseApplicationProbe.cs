using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class FirstUseApplicationProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FirstUseEvidence> RunBuiltAsync(
        string applicationDirectory,
        string lane,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var total = Stopwatch.StartNew();
        var steps = new List<ApplicationStepEvidence>();
        var agentApprovalId = $"agent-{Guid.NewGuid():N}";
        await using var host = ApplicationProbeHost.Start(
            applicationDirectory,
            PackagingConstants.FirstUse.ProjectFile,
            "first-use");
        var http = host.Http;
        var mcp = new McpProbeClient(http);

        string selectedAdapter = "unknown";
        var factsConverged = false;
        var dryRunPreservedState = false;
        var agentMutationObserved = false;
        var remoteDeleteHidden = false;
        try
        {
            await StepAsync("startup-health", steps, () => host.WaitUntilReadyAsync(cancellationToken));

            var webFacts = await StepAsync("operator-facts", steps, async () =>
            {
                using var response = await http.GetAsync(PackagingConstants.FirstUse.FactsPath, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.GetProperty("complete").GetBoolean())
                    throw new InvalidOperationException("FirstUse runtime facts are incomplete.");

                var election = root.GetProperty("facts").EnumerateArray().FirstOrDefault(fact =>
                    fact.GetProperty("code").GetString() == PackagingConstants.FirstUse.AdapterSelectedCode
                    && fact.GetProperty("subject").GetString() == PackagingConstants.FirstUse.DefaultDataSubject);
                if (election.ValueKind == JsonValueKind.Undefined)
                    throw new InvalidOperationException("FirstUse facts do not contain the default data-adapter election.");

                var summary = election.GetProperty("summary").GetString() ?? "";
                selectedAdapter = summary.Contains("sqlite", StringComparison.OrdinalIgnoreCase) ? "sqlite" : summary;
                if (!string.Equals(selectedAdapter, "sqlite", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"FirstUse selected an unexpected data adapter: {summary}");
                return json;
            });

            await StepAsync("rest-business-result", steps, async () =>
            {
                using var create = await http.PostAsJsonAsync(
                    PackagingConstants.FirstUse.ApprovalsPath,
                    new { subject = "Approve supplier invoice" },
                    JsonOptions,
                    cancellationToken);
                create.EnsureSuccessStatusCode();
                var list = await ReadApprovalsAsync(http, cancellationToken);
                if (!ContainsSubject(list, "Approve supplier invoice"))
                    throw new InvalidOperationException("REST did not return the approval it created.");
            });

            await StepAsync("agent-initialize", steps, () =>
                mcp.InitializeAsync("koan-first-use-proof", cancellationToken));

            var catalog = await StepAsync("agent-discovery", steps, async () =>
            {
                var listed = await mcp.CallAsync("resources/list", null, cancellationToken);
                var uris = listed.GetProperty("result").GetProperty("resources").EnumerateArray()
                    .Select(resource => resource.GetProperty("uri").GetString())
                    .ToArray();
                if (!uris.Contains(PackagingConstants.FirstUse.EntityCatalogUri)
                    || !uris.Contains(PackagingConstants.FirstUse.RuntimeFactsUri))
                    throw new InvalidOperationException("MCP did not advertise the entity catalog and runtime facts resources.");

                var facts = await mcp.CallAsync("resources/read",
                    new { uri = PackagingConstants.FirstUse.RuntimeFactsUri }, cancellationToken);
                var mcpFacts = facts.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString();
                factsConverged = string.Equals(webFacts, mcpFacts, StringComparison.Ordinal);
                if (!factsConverged)
                    throw new InvalidOperationException("Web and MCP projected different runtime fact envelopes.");

                var resource = await mcp.CallAsync("resources/read",
                    new { uri = PackagingConstants.FirstUse.EntityCatalogUri }, cancellationToken);
                var text = resource.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()
                    ?? throw new InvalidOperationException("MCP entity catalog returned no text.");
                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            });

            string upsertTool = "";
            await StepAsync("agent-authorization", steps, () =>
            {
                var approval = catalog.GetProperty("entities").EnumerateArray().FirstOrDefault(entity =>
                    string.Equals(entity.GetProperty("name").GetString(), PackagingConstants.FirstUse.ApprovalEntity, StringComparison.OrdinalIgnoreCase));
                if (approval.ValueKind == JsonValueKind.Undefined)
                    throw new InvalidOperationException("MCP catalog did not contain the approval entity.");

                var verbs = approval.GetProperty("verbs").EnumerateArray().ToArray();
                var upsert = verbs.FirstOrDefault(verb => verb.GetProperty("operation").GetString() == "Upsert");
                upsertTool = upsert.ValueKind == JsonValueKind.Undefined
                    ? ""
                    : upsert.GetProperty("name").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(upsertTool))
                    throw new InvalidOperationException("The remote agent was not offered the approval upsert operation.");

                remoteDeleteHidden = verbs.All(verb => verb.GetProperty("operation").GetString() != "Delete");
                if (!remoteDeleteHidden)
                    throw new InvalidOperationException("The remote agent was offered the local-only approval delete operation.");
                return Task.CompletedTask;
            });

            await StepAsync("agent-dry-run", steps, async () =>
            {
                var before = await ReadApprovalsAsync(http, cancellationToken);
                _ = await mcp.CallToolAsync(upsertTool, new
                {
                    model = new { id = agentApprovalId, subject = "Approve travel request", state = "Approved" },
                    dry_run = true
                }, cancellationToken);
                var after = await ReadApprovalsAsync(http, cancellationToken);
                dryRunPreservedState = before.SequenceEqual(after, StringComparer.Ordinal)
                    && !ContainsId(after, agentApprovalId);
                if (!dryRunPreservedState)
                    throw new InvalidOperationException(
                        $"Agent dry-run changed approval state. Before=[{string.Join(", ", before)}]; after=[{string.Join(", ", after)}].");
            });

            await StepAsync("agent-execute", steps, async () =>
            {
                _ = await mcp.CallToolAsync(upsertTool, new
                {
                    model = new { id = agentApprovalId, subject = "Approve travel request", state = "Approved" }
                }, cancellationToken);
                var after = await ReadApprovalsAsync(http, cancellationToken);
                agentMutationObserved = ContainsId(after, agentApprovalId);
                if (!agentMutationObserved)
                    throw new InvalidOperationException("REST did not observe the approval written by the agent.");
            });
        }
        catch (Exception exception)
        {
            throw await host.FailureAsync($"FirstUse {lane} proof failed", exception);
        }

        var logs = await host.StopAsync();
        var startupReported = logs.StandardOutput.Contains("KOAN", StringComparison.OrdinalIgnoreCase)
            && logs.StandardOutput.Contains("ready", StringComparison.OrdinalIgnoreCase);
        if (!startupReported)
            throw new InvalidOperationException(
                $"FirstUse {lane} ran successfully but did not emit a recognizable Koan startup report.{Environment.NewLine}{logs.StandardOutput}{logs.StandardError}");
        if (logs.StandardOutput.Contains(PackagingConstants.ApplicationProbe.MissingWebRootWarning, StringComparison.Ordinal))
            throw new InvalidOperationException("FirstUse emitted a missing-web-root warning even though it is an API-only application.");

        total.Stop();
        return new FirstUseEvidence(
            lane,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            startedAt,
            Math.Round(total.Elapsed.TotalSeconds, 3),
            selectedAdapter,
            startupReported,
            factsConverged,
            dryRunPreservedState,
            agentMutationObserved,
            remoteDeleteHidden,
            steps);
    }

    private static async Task<string[]> ReadApprovalsAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(PackagingConstants.FirstUse.ApprovalsPath, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
            : document.RootElement.GetProperty("items").EnumerateArray();
        return items
            .Select(item => string.Join(
                '\u001f',
                item.TryGetProperty("id", out var id) ? id.ToString() : "",
                item.TryGetProperty("subject", out var subject) ? subject.GetString() : "",
                item.TryGetProperty("state", out var state) ? state.ToString() : ""))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsSubject(IEnumerable<string> snapshots, string subject) =>
        snapshots.Any(snapshot => snapshot.Split('\u001f').ElementAtOrDefault(1) == subject);

    private static bool ContainsId(IEnumerable<string> snapshots, string id) =>
        snapshots.Any(snapshot => snapshot.Split('\u001f').FirstOrDefault() == id);

    private static async Task StepAsync(
        string name,
        ICollection<ApplicationStepEvidence> steps,
        Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        await action();
        stopwatch.Stop();
        steps.Add(new ApplicationStepEvidence(name, Math.Round(stopwatch.Elapsed.TotalSeconds, 3)));
    }

    private static async Task<T> StepAsync<T>(
        string name,
        ICollection<ApplicationStepEvidence> steps,
        Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        stopwatch.Stop();
        steps.Add(new ApplicationStepEvidence(name, Math.Round(stopwatch.Elapsed.TotalSeconds, 3)));
        return result;
    }

}
