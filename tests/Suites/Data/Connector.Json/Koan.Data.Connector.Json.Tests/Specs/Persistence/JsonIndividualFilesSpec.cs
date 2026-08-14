using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Capabilities;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Json.Tests.Specs.Persistence;

public sealed class JsonIndividualFilesSpec
{
    [Fact]
    public async Task Configured_path_gives_each_entity_one_independent_file_and_observes_external_edits()
    {
        var root = TempRoot("placement");
        try
        {
            await using var host = await Boot(root, "{id}/article.json");
            using var hostScope = AppHost.PushScope(host.Services);

            await new IndividualArticle { Id = "first-draft", Title = "First" }.Save();
            await new IndividualArticle { Id = "second-draft", Title = "Second" }.Save();

            var firstPath = Path.Combine(root, "first-draft", "article.json");
            var secondPath = Path.Combine(root, "second-draft", "article.json");
            File.Exists(firstPath).Should().BeTrue();
            File.Exists(secondPath).Should().BeTrue();
            JToken.Parse(await File.ReadAllTextAsync(firstPath)).Type.Should().Be(JTokenType.Object);

            var mediaPath = Path.Combine(root, "first-draft", "media", "keep.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
            await File.WriteAllTextAsync(mediaPath, "owned by the application");

            // Individual mode has no warm record snapshot: a Git checkout or another local file editor is visible on
            // the next read in this same host.
            var external = JObject.Parse(await File.ReadAllTextAsync(firstPath));
            external["title"] = "Externally edited";
            await File.WriteAllTextAsync(firstPath, external.ToString(Formatting.None));
            (await IndividualArticle.Get("first-draft"))!.Title.Should().Be("Externally edited");

            (await IndividualArticle.Remove("first-draft")).Should().BeTrue();
            File.Exists(firstPath).Should().BeFalse();
            File.Exists(mediaPath).Should().BeTrue("the JSON adapter owns the Entity file, not sibling media");
            File.Exists(secondPath).Should().BeTrue();
            (await IndividualArticle.All()).Should().ContainSingle(article => article.Id == "second-draft");
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Extension_data_preserves_imported_unknown_properties_across_read_write()
    {
        var root = TempRoot("metadata");
        var path = Path.Combine(root, "imported", "article.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path,
                """{"id":"imported","title":"Before","corpus":"legacy","arbitrary":{"rank":7}}""");

            await using var host = await Boot(root, "{id}/article.json");
            using var hostScope = AppHost.PushScope(host.Services);

            var article = (await IndividualArticle.Get("imported"))!;
            article.Metadata["corpus"]!.Value<string>().Should().Be("legacy");
            article.Metadata["arbitrary"]!["rank"]!.Value<int>().Should().Be(7);

            article.Title = "After";
            await article.Save();

            var persisted = JObject.Parse(await File.ReadAllTextAsync(path));
            persisted["title"]!.Value<string>().Should().Be("After");
            persisted["corpus"]!.Value<string>().Should().Be("legacy");
            persisted["arbitrary"]!["rank"]!.Value<int>().Should().Be(7);
            persisted.Property(nameof(IndividualArticle.Metadata), StringComparison.OrdinalIgnoreCase)
                .Should().BeNull("JsonExtensionData remains top-level rather than adding a metadata wrapper");
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Unsafe_identity_is_encoded_inside_the_root_and_unsafe_template_fails_correctively()
    {
        var safeRoot = TempRoot("safe-identity");
        try
        {
            await using var host = await Boot(safeRoot, "{id}/article.json");
            using var hostScope = AppHost.PushScope(host.Services);

            await new IndividualArticle { Id = "../Draft", Title = "Contained" }.Save();
            await new IndividualArticle { Id = "con", Title = "Portable" }.Save();
            var files = Directory.EnumerateFiles(safeRoot, "article.json", SearchOption.AllDirectories).ToArray();
            files.Should().HaveCount(2);
            files.Should().OnlyContain(path => Path.GetFullPath(path).StartsWith(Path.GetFullPath(safeRoot)));
            files.Should().OnlyContain(path => !Path.GetRelativePath(safeRoot, path).StartsWith(".."));
            (await IndividualArticle.Get("../Draft"))!.Title.Should().Be("Contained");
            (await IndividualArticle.Get("con"))!.Title.Should().Be("Portable");
        }
        finally
        {
            Delete(safeRoot);
        }

        var unsafeRoot = TempRoot("unsafe-template");
        try
        {
            await using var host = await Boot(unsafeRoot, "../{id}.json");
            using var hostScope = AppHost.PushScope(host.Services);

            await FluentActions.Invoking(() =>
                    new IndividualArticle { Id = "escape", Title = "Rejected" }.Save())
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*IndividualFilePath*relative*{id}*");
        }
        finally
        {
            Delete(unsafeRoot);
        }
    }

    [Fact]
    public async Task Individual_layout_uses_default_storage_path_and_does_not_claim_aggregate_bulk_writes()
    {
        var root = TempRoot("defaults");
        try
        {
            await using var host = await Boot(root, individualFilePath: null);
            using var hostScope = AppHost.PushScope(host.Services);

            await new IndividualArticle { Id = "one", Title = "One" }.Save();
            var path = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Should().ContainSingle().Subject;
            Path.GetDirectoryName(path).Should().NotBe(Path.GetFullPath(root));

            var repository = host.Services.GetRequiredService<IDataService>()
                .GetRepository<IndividualArticle, string>();
            var capabilities = DataCaps.Describe(repository, repository.GetType().Name);
            capabilities.Has(DataCaps.Write.BulkUpsert).Should().BeFalse();
            capabilities.Has(DataCaps.Write.BulkDelete).Should().BeFalse();
            capabilities.Has(DataCaps.Query.FilterExecution).Should().BeTrue();
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Storage_token_isolates_partitions_and_omitting_it_fails_closed_when_partitioned()
    {
        var isolatedRoot = TempRoot("partitioned");
        try
        {
            await using var host = await Boot(isolatedRoot, individualFilePath: null);
            using var hostScope = AppHost.PushScope(host.Services);

            await IndividualArticle.Upsert(
                new IndividualArticle { Id = "shared", Title = "Draft" },
                "draft");
            await IndividualArticle.Upsert(
                new IndividualArticle { Id = "shared", Title = "Published" },
                "published");

            (await IndividualArticle.Get("shared", "draft"))!.Title.Should().Be("Draft");
            (await IndividualArticle.Get("shared", "published"))!.Title.Should().Be("Published");
            Directory.EnumerateFiles(isolatedRoot, "*.json", SearchOption.AllDirectories)
                .Should().HaveCount(2);
        }
        finally
        {
            Delete(isolatedRoot);
        }

        var unqualifiedRoot = TempRoot("partition-rejected");
        try
        {
            await using var host = await Boot(unqualifiedRoot, "{id}/article.json");
            using var hostScope = AppHost.PushScope(host.Services);

            await FluentActions.Invoking(() => IndividualArticle.Upsert(
                    new IndividualArticle { Id = "shared", Title = "Rejected" },
                    "draft"))
                .Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*omits*{storage}*cannot isolate partition*draft*");
        }
        finally
        {
            Delete(unqualifiedRoot);
        }
    }

    private static Task<IntegrationHost> Boot(string root, string? individualFilePath)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "json",
            ["Koan:Data:Sources:Default:json:DirectoryPath"] = root,
            ["Koan:Data:Sources:Default:json:Layout"] = nameof(JsonStorageLayout.IndividualFiles)
        };
        if (individualFilePath is not null)
            settings["Koan:Data:Sources:Default:json:IndividualFilePath"] = individualFilePath;

        return KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(static services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);
    }

    private static string TempRoot(string purpose) =>
        Path.Combine(Path.GetTempPath(), $"koan-json-individual-{purpose}-{Guid.CreateVersion7():N}");

    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class IndividualArticle : Entity<IndividualArticle>
    {
        public string Title { get; set; } = "";

        [JsonExtensionData]
        public IDictionary<string, JToken> Metadata { get; set; } =
            new Dictionary<string, JToken>(StringComparer.Ordinal);
    }
}
