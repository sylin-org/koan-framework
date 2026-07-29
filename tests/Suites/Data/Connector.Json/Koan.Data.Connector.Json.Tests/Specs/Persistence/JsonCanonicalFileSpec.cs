using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Testing.Integration;

namespace Koan.Data.Connector.Json.Tests.Specs.Persistence;

public sealed class JsonCanonicalFileSpec
{
    [Fact]
    public async Task Source_aliases_for_one_canonical_file_share_one_live_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-json-alias-{Guid.CreateVersion7():N}");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
                    ["Koan:Data:Sources:Default:Adapter"] = "json",
                    ["Koan:Data:Json:DirectoryPath"] = root,
                    ["Koan:Data:Sources:AliasA:Adapter"] = "json",
                    ["Koan:Data:Sources:AliasA:json:DirectoryPath"] = root,
                    ["Koan:Data:Sources:AliasB:Adapter"] = "json",
                    ["Koan:Data:Sources:AliasB:json:DirectoryPath"] = Path.Combine(root, ".")
                })
                .ConfigureServices(static services => services.AddKoan())
                .StartAsync(TestContext.Current.CancellationToken);
            AppHost.Current = host.Services;

            using (EntityContext.Source("AliasA"))
                await new AliasProbe { Id = "a", Value = "first" }.Save();
            using (EntityContext.Source("AliasB"))
                await new AliasProbe { Id = "b", Value = "second" }.Save();

            using (EntityContext.Source("AliasA"))
                (await AliasProbe.All()).Select(static item => item.Id).Should().Equal("a", "b");

            await Task.WhenAll(Enumerable.Range(0, 20).Select(async index =>
            {
                using (EntityContext.Source(index % 2 == 0 ? "AliasA" : "AliasB"))
                    await new AliasProbe { Id = $"parallel-{index:D2}", Value = "coordinated" }.Save();
            }));

            using (EntityContext.Source("AliasB"))
                (await AliasProbe.All()).Should().HaveCount(22);
        }
        finally
        {
            AppHost.Current = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Host_file_admission_accepts_exactly_1024_canonical_files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-json-bound-{Guid.CreateVersion7():N}");
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSettings(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Koan:Environment"] = "Test",
                    ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
                    ["Koan:Data:Sources:Default:Adapter"] = "json",
                    ["Koan:Data:Json:DirectoryPath"] = root
                })
                .ConfigureServices(static services => services.AddKoan())
                .StartAsync(TestContext.Current.CancellationToken);
            AppHost.Current = host.Services;

            for (var index = 0; index < 1024; index++)
            {
                using (EntityContext.Partition($"partition-{index:D4}"))
                    (await AliasProbe.All()).Should().BeEmpty();
            }

            Func<Task> overflow = async () =>
            {
                using (EntityContext.Partition("partition-overflow"))
                    await AliasProbe.All();
            };
            await overflow.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*host bound of 1024 canonical entity files*");
        }
        finally
        {
            AppHost.Current = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AliasProbe : Entity<AliasProbe>
    {
        public string Value { get; set; } = "";
    }
}
