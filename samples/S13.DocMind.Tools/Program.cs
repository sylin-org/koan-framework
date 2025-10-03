using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using S13.DocMind.Models;
using S13.DocMind.Services;

namespace S13.DocMind.Tools;

public static class Program
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> Main(string[] args)
    {
        var root = BuildRootCommand();
        return await root.InvokeAsync(args).ConfigureAwait(false);
    }

    private static RootCommand BuildRootCommand()
    {
        var baseOption = new Option<string?>("--base-url", "DocMind API base URL. Defaults to DOCMIND_BASE_URL env or http://localhost:5113.");

        var configCommand = new Command("config", "Fetch processing configuration and readiness state")
        {
            baseOption
        };
        configCommand.SetHandler(async (InvocationContext context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            var baseUrl = ResolveBaseUrl(context.ParseResult.GetValueForOption(baseOption));
            using var client = new DocMindProcessingClient(baseUrl);
            var config = await client.GetConfigAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(config, SerializerOptions));
        });

        var documentOption = new Option<string>("--document", "Document identifier to replay") { IsRequired = true };
        var stageOption = new Option<string?>("--stage", () => DocumentProcessingStage.ExtractText.ToString(), "Stage to enqueue");
        var resetOption = new Option<bool>("--reset", "Reset document state before replay");

        var replayCommand = new Command("replay", "Replay a document at a specific stage")
        {
            baseOption,
            documentOption,
            stageOption,
            resetOption
        };
        replayCommand.SetHandler(async (InvocationContext context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            var baseUrl = ResolveBaseUrl(context.ParseResult.GetValueForOption(baseOption));
            var document = context.ParseResult.GetValueForOption(documentOption);
            var stageRaw = context.ParseResult.GetValueForOption(stageOption);
            var reset = context.ParseResult.GetValueForOption(resetOption);
            var stage = ParseStage(stageRaw);

            using var client = new DocMindProcessingClient(baseUrl);
            var result = await client.ReplayAsync(new ProcessingReplayRequest
            {
                DocumentId = document,
                Stage = stage,
                Reset = reset
            }, cancellationToken).ConfigureAwait(false);

            Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
        });

        var forceOption = new Option<bool>("--force", "Force a projection rebuild before validation");
        var staleMinutesOption = new Option<int?>("--stale-minutes", () => 10, "Rebuild if projection is older than N minutes");
        var includeOverviewOption = new Option<bool>("--include-overview", "Include overview summary in the response");
        var includeCollectionsOption = new Option<bool>("--include-collections", "Include profile collection summaries");
        var includeQueueOption = new Option<bool>("--include-queue", "Include queue snapshot entries");

        var validateCommand = new Command("validate", "Validate the discovery projection against the current corpus")
        {
            baseOption,
            forceOption,
            staleMinutesOption,
            includeOverviewOption,
            includeCollectionsOption,
            includeQueueOption
        };
        validateCommand.SetHandler(async (InvocationContext context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            var baseUrl = ResolveBaseUrl(context.ParseResult.GetValueForOption(baseOption));
            using var client = new DocMindProcessingClient(baseUrl);
            var request = new DocumentDiscoveryValidationRequest
            {
                ForceRefresh = context.ParseResult.GetValueForOption(forceOption),
                RefreshIfOlderThanMinutes = context.ParseResult.GetValueForOption(staleMinutesOption),
                IncludeOverview = context.ParseResult.GetValueForOption(includeOverviewOption),
                IncludeCollections = context.ParseResult.GetValueForOption(includeCollectionsOption),
                IncludeQueueEntries = context.ParseResult.GetValueForOption(includeQueueOption)
            };

            var result = await client.ValidateDiscoveryAsync(request, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result, SerializerOptions));
        });

        var root = new RootCommand("DocMind operator CLI")
        {
            configCommand,
            replayCommand,
            validateCommand
        };

        return root;
    }

    private static DocumentProcessingStage ParseStage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DocumentProcessingStage.ExtractText;
        }

        if (Enum.TryParse<DocumentProcessingStage>(value, true, out var stage))
        {
            return stage;
        }

        throw new ArgumentException($"Unknown stage: {value}");
    }

    private static string ResolveBaseUrl(string? optionValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue!;
        }

        var env = Environment.GetEnvironmentVariable("DOCMIND_BASE_URL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env!;
        }

        return "http://localhost:5113";
    }
}
