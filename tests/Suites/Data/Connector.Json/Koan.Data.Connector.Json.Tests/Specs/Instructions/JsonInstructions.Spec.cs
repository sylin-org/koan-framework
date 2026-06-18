using System.IO;
using System.Linq;
using Koan.Data.Abstractions.Instructions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Json.Tests.Specs.Instructions;

public sealed class JsonInstructionsSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact]
    public async Task Instruction_ensure_created_is_idempotent_and_prepares_storage_directory()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var first = await data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));
        var second = await data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));

        first.Should().BeTrue();
        second.Should().BeTrue();
        Directory.Exists(Fixture.RootPath).Should().BeTrue();
    }

    [Fact]
    public async Task Instruction_clear_returns_deleted_count_and_truncates_store()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        var partition = NewPartition("clear");
        using var lease = Lease(partition);

        await InstructionProbe.Upsert(new InstructionProbe { Name = "seed" });
        await InstructionProbe.Upsert(new InstructionProbe { Name = "seed-2" });

        var countBefore = await InstructionProbe.Count.Exact();
        countBefore.Should().Be(2);

        var cleared = await data.Execute<InstructionProbe, string, int>(new Instruction(DataInstructions.Clear));
        cleared.Should().Be(2);

        var remaining = await InstructionProbe.All(partition);
        remaining.Should().BeEmpty();

        var jsonFiles = Directory.Exists(Fixture.RootPath)
            ? Directory.EnumerateFiles(Fixture.RootPath, "*.json", SearchOption.AllDirectories).ToArray()
            : [];

        jsonFiles.Should().NotBeEmpty();
        foreach (var path in jsonFiles)
        {
            var contents = await File.ReadAllTextAsync(path);
            contents.Trim().Should().Be("[]");
        }
    }

    private sealed class InstructionProbe : Entity<InstructionProbe>
    {
        public string Name { get; set; } = "";
    }
}
