using System;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.InMemory.Tests.Support;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Instructions;

public sealed class InMemoryInstructionsSpec
{
    private readonly ITestOutputHelper _output;

    public InMemoryInstructionsSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Instruction_clear_returns_deleted_count()
    {
        await TestPipeline.For<InMemoryInstructionsSpec>(_output, nameof(Instruction_clear_returns_deleted_count))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<InstructionProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();

                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                await InstructionProbe.UpsertAsync(new InstructionProbe { Name = "item" });

                var before = await InstructionProbe.Count.Exact();
                before.Should().Be(1);

                var cleared = await fixture.Data.Execute<InstructionProbe, string, int>(new Instruction(DataInstructions.Clear));

                var after = await InstructionProbe.Count.Exact();
                cleared.Should().BeInRange(0, (int)before);
                after.Should().Be(0);

                var remaining = await InstructionProbe.All(partition);
                remaining.Should().BeEmpty();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Instruction_ensure_created_is_idempotent()
    {
        await TestPipeline.For<InMemoryInstructionsSpec>(_output, nameof(Instruction_ensure_created_is_idempotent))
            .Using<InMemoryConnectorFixture>("fixture", static ctx => InMemoryConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                await fixture.ResetAsync<InstructionProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<InMemoryConnectorFixture>("fixture");
                fixture.BindHost();

                var first = await fixture.Data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));
                first.Should().BeTrue();

                var second = await fixture.Data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));
                second.Should().BeTrue();
            })
            .RunAsync();
    }

    private sealed class InstructionProbe : Entity<InstructionProbe>
    {
        public string Name { get; set; } = string.Empty;
    }
}
