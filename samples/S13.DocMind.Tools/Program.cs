using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
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

        var replayCommand = new Command("replay", "Replay a document at a specific stage")
        {
            baseOption,
            new Option<string>("--document", "Document identifier to replay") { IsRequired = true },
            new Option<string?>("--stage", () => DocumentProcessingStage.ExtractText.ToString(), "Stage to enqueue"),
            new Option<bool>("--reset", "Reset document state before replay")
        };
        replayCommand.SetHandler(async (InvocationContext context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            var baseUrl = ResolveBaseUrl(context.ParseResult.GetValueForOption(baseOption));
            var document = context.ParseResult.GetValueForOption<string>("--document");
            var stageRaw = context.ParseResult.GetValueForOption<string?>("--stage");
            var reset = context.ParseResult.GetValueForOption<bool>("--reset");
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

        var validateCommand = new Command("validate", "Validate the discovery projection against the current corpus")
        {
            baseOption,
            new Option<bool>("--force", "Force a projection rebuild before validation"),
            new Option<int?>("--stale-minutes", () => 10, "Rebuild if projection is older than N minutes"),
            new Option<bool>("--include-overview", "Include overview summary in the response"),
            new Option<bool>("--include-collections", "Include profile collection summaries"),
            new Option<bool>("--include-queue", "Include queue snapshot entries")
        };
        validateCommand.SetHandler(async (InvocationContext context) =>
        {
            var cancellationToken = context.GetCancellationToken();
            var baseUrl = ResolveBaseUrl(context.ParseResult.GetValueForOption(baseOption));
            using var client = new DocMindProcessingClient(baseUrl);
            var request = new DocumentDiscoveryValidationRequest
            {
                ForceRefresh = context.ParseResult.GetValueForOption<bool>("--force"),
                RefreshIfOlderThanMinutes = context.ParseResult.GetValueForOption<int?>("--stale-minutes"),
                IncludeOverview = context.ParseResult.GetValueForOption<bool>("--include-overview"),
                IncludeCollections = context.ParseResult.GetValueForOption<bool>("--include-collections"),
                IncludeQueueEntries = context.ParseResult.GetValueForOption<bool>("--include-queue")
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
