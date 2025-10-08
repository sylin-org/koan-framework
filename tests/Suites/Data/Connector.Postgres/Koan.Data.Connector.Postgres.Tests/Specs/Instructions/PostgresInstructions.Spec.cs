using System;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Connector.Postgres.Tests.Support;

namespace Koan.Data.Connector.Postgres.Tests.Specs.Instructions;

public sealed class PostgresInstructionsSpec
{
    private readonly ITestOutputHelper _output;

    public PostgresInstructionsSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Instruction_clear_returns_deleted_count()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<PostgresInstructionsSpec>(_output, nameof(Instruction_clear_returns_deleted_count))
            .RequireDocker()
            .UsingPostgresContainer(database: databaseName)
            .Using<PostgresConnectorFixture>("fixture", static ctx => PostgresConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<PostgresConnectorFixture>("fixture");
                await fixture.ResetAsync<InstructionProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<PostgresConnectorFixture>("fixture");
                fixture.BindHost();

                var partition = fixture.EnsurePartition(ctx);
                await using var lease = fixture.LeasePartition(partition);

                await InstructionProbe.UpsertAsync(new InstructionProbe { Name = "item" });
                var before = await InstructionProbe.Count.Exact();
                before.Should().Be(1);

                var cleared = await fixture.Data.Execute<InstructionProbe, string, int>(new Instruction(DataInstructions.Clear));
                cleared.Should().BeGreaterThanOrEqualTo(0);

                var after = await InstructionProbe.Count.Exact();
                after.Should().Be(0);
            })
            .RunAsync();
    }

    [Fact]
    public async Task Instruction_ensure_created_is_idempotent()
    {
        var databaseName = $"koan_tests_{Guid.NewGuid():N}";

        await TestPipeline.For<PostgresInstructionsSpec>(_output, nameof(Instruction_ensure_created_is_idempotent))
            .RequireDocker()
            .UsingPostgresContainer(database: databaseName)
            .Using<PostgresConnectorFixture>("fixture", static ctx => PostgresConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<PostgresConnectorFixture>("fixture");
                await fixture.ResetAsync<InstructionProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<PostgresConnectorFixture>("fixture");
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
