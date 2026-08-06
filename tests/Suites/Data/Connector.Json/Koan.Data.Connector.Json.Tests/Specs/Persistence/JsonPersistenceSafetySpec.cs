using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Json.Tests.Specs.Persistence;

public sealed class JsonPersistenceSafetySpec
{
    private const long MaximumFileBytes = 64L * 1024 * 1024;

    [Fact]
    public async Task Corrupt_store_fails_correctively_instead_of_becoming_empty()
    {
        var root = TempRoot("corruption");

        try
        {
            await using (var writer = await Boot(root))
            {
                AppHost.Current = writer.Services;
                await new PersistenceProbe { Value = "stable" }.Save();
            }

            var path = Directory.EnumerateFiles(root, "*.json").Should().ContainSingle().Subject;
            await File.WriteAllTextAsync(path, "{ not-valid-json }");

            await using var reader = await Boot(root);
            AppHost.Current = reader.Services;
            var failure = await FluentActions.Invoking(() => PersistenceProbe.All())
                .Should().ThrowAsync<InvalidDataException>();

            failure.Which.Message.Should().Contain(path);
            failure.Which.Message.Should().Contain("never treated as empty");
        }
        finally
        {
            AppHost.Current = null;
            Delete(root);
        }
    }

    [Fact]
    public async Task Failed_serialization_publishes_neither_memory_nor_disk()
    {
        var root = TempRoot("failed-write");
        try
        {
            await using (var writer = await Boot(root))
            {
                AppHost.Current = writer.Services;
                await new FailureProbe { Id = "one", Value = "stable" }.Save();
                var candidate = (await FailureProbe.Get("one"))!;
                candidate.Value = "must-not-leak";
                candidate.ThrowOnSerialize = true;

                await FluentActions.Invoking(() => candidate.Save())
                    .Should().ThrowAsync<JsonSerializationException>();

                (await FailureProbe.Get("one"))!.Value.Should().Be("stable");
            }

            await using var reader = await Boot(root);
            AppHost.Current = reader.Services;
            (await FailureProbe.Get("one"))!.Value.Should().Be("stable");
        }
        finally
        {
            AppHost.Current = null;
            Delete(root);
        }
    }

    [Fact]
    public async Task Pure_scope_diagnostics_do_not_create_the_store_directory()
    {
        var root = TempRoot("diagnostics");
        var defaultRoot = Path.Combine(root, "default");
        var inspectedRoot = Path.Combine(root, "inspected");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
                    ["Koan:Data:Sources:Default:Adapter"] = "json",
                    ["Koan:Data:Json:DirectoryPath"] = defaultRoot,
                    ["Koan:Data:Sources:Inspected:Adapter"] = "json",
                    ["Koan:Data:Sources:Inspected:json:DirectoryPath"] = inspectedRoot
                })
                .ConfigureServices(static services => services.AddKoan())
                .StartAsync(TestContext.Current.CancellationToken);

            using (EntityContext.Source("Inspected"))
                host.Services.GetRequiredService<IDataService>()
                    .GetScopeDiagnostics<PersistenceProbe, string>();

            Directory.Exists(defaultRoot).Should().BeTrue("elected health may probe the default source");
            Directory.Exists(inspectedRoot).Should().BeFalse("pure diagnostics must not touch its routed source");
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task Duplicate_persisted_identity_fails_instead_of_silently_overwriting()
    {
        var root = TempRoot("duplicate-id");
        try
        {
            await using (var writer = await Boot(root))
            {
                AppHost.Current = writer.Services;
                await new PersistenceProbe { Id = "one", Value = "first" }.Save();
                await new PersistenceProbe { Id = "two", Value = "second" }.Save();
            }

            var path = Directory.EnumerateFiles(root, "*.json").Should().ContainSingle().Subject;
            var array = JArray.Parse(await File.ReadAllTextAsync(path));
            array.Add(array[0]!.DeepClone());
            await File.WriteAllTextAsync(path, array.ToString(Formatting.None));

            await using var reader = await Boot(root);
            AppHost.Current = reader.Services;
            await FluentActions.Invoking(() => PersistenceProbe.All())
                .Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*duplicate*identity*one*");
        }
        finally
        {
            AppHost.Current = null;
            Delete(root);
        }
    }

    [Fact]
    public async Task Oversized_store_fails_before_json_materialization()
    {
        var root = TempRoot("oversized");
        try
        {
            await using (var writer = await Boot(root))
            {
                AppHost.Current = writer.Services;
                await new PersistenceProbe { Value = "seed" }.Save();
            }

            var path = Directory.EnumerateFiles(root, "*.json").Should().ContainSingle().Subject;
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(MaximumFileBytes + 1);
            }

            await using var reader = await Boot(root);
            AppHost.Current = reader.Services;
            await FluentActions.Invoking(() => PersistenceProbe.All())
                .Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*exceeds*64 MiB*");
        }
        finally
        {
            AppHost.Current = null;
            Delete(root);
        }
    }

    private static Task<IntegrationHost> Boot(string root) => KoanIntegrationHost.Configure()
        .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "json",
            ["Koan:Data:Json:DirectoryPath"] = root
        })
        .ConfigureServices(static services => services.AddKoan())
        .StartAsync(TestContext.Current.CancellationToken);

    private static string TempRoot(string purpose) =>
        Path.Combine(Path.GetTempPath(), $"koan-json-{purpose}-{Guid.CreateVersion7():N}");

    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class PersistenceProbe : Entity<PersistenceProbe>
    {
        public string Value { get; set; } = "";
    }

    private sealed class FailureProbe : Entity<FailureProbe>
    {
        private string _value = "";

        [JsonIgnore]
        public bool ThrowOnSerialize { get; set; }

        public string Value
        {
            get => ThrowOnSerialize ? throw new JsonSerializationException("serialization rejected") : _value;
            set => _value = value;
        }
    }
}
