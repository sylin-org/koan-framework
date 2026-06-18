using Koan.Data.Abstractions.Instructions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Instructions;

public sealed class MongoInstructionsSpec(MongoFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MongoFixture>(fixture, output)
{
    [Fact]
    public async Task Instruction_clear_returns_deleted_count()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        var partition = NewPartition("clear");
        using var lease = Lease(partition);

        await InstructionProbe.Upsert(new InstructionProbe { Name = "item" });

        var before = await InstructionProbe.Count.Exact();
        before.Should().Be(1);

        var cleared = await data.Execute<InstructionProbe, string, int>(new Instruction(DataInstructions.Clear));

        var after = await InstructionProbe.Count.Exact();
        cleared.Should().BeInRange(0, (int)before);
        after.Should().Be(0);

        var remaining = await InstructionProbe.All(partition);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task Instruction_ensure_created_is_idempotent()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();

        var first = await data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));
        first.Should().BeTrue();

        var second = await data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));
        second.Should().BeTrue();
    }

    private sealed class InstructionProbe : Entity<InstructionProbe>
    {
        public string Name { get; set; } = "";
    }
}
