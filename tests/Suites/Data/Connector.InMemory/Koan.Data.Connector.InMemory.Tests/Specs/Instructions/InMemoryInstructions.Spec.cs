using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Instructions;

public sealed class InMemoryInstructionsSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact]
    public async Task Instruction_clear_returns_deleted_count()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var partition = NewPartition("instructions");
        using var lease = Lease(partition);

        var data = host.Services.GetRequiredService<IDataService>();

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
