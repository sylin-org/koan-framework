using System;
using System.IO;
using System.Linq;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Connector.Json.Tests.Support;

namespace Koan.Data.Connector.Json.Tests.Specs.Instructions;

public sealed class JsonInstructionsSpec
{
    private readonly ITestOutputHelper _output;

    public JsonInstructionsSpec(ITestOutputHelper output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    [Fact]
    public async Task Instruction_ensure_created_is_idempotent_and_prepares_storage_directory()
    {
        await TestPipeline.For<JsonInstructionsSpec>(_output, nameof(Instruction_ensure_created_is_idempotent_and_prepares_storage_directory))
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                await fixture.ResetAsync<InstructionProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                fixture.BindHost();

                var first = await fixture.Data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));
                var second = await fixture.Data.Execute<InstructionProbe, string, bool>(new Instruction(DataInstructions.EnsureCreated));

                first.Should().BeTrue();
                second.Should().BeTrue();
                Directory.Exists(fixture.RootPath).Should().BeTrue();
            })
            .RunAsync();
    }

    [Fact]
    public async Task Instruction_clear_returns_deleted_count_and_truncates_store()
    {
        await TestPipeline.For<JsonInstructionsSpec>(_output, nameof(Instruction_clear_returns_deleted_count_and_truncates_store))
            .Using<JsonConnectorFixture>("fixture", static ctx => JsonConnectorFixture.CreateAsync(ctx))
            .Arrange(static async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                await fixture.ResetAsync<InstructionProbe, string>();
            })
            .Assert(async ctx =>
            {
                var fixture = ctx.GetRequiredItem<JsonConnectorFixture>("fixture");
                fixture.BindHost();
                var partition = fixture.EnsurePartition(ctx);

                await using var lease = fixture.LeasePartition(partition);

                await InstructionProbe.UpsertAsync(new InstructionProbe { Name = "seed" });
                await InstructionProbe.UpsertAsync(new InstructionProbe { Name = "seed-2" });

                var countBefore = await InstructionProbe.Count.Exact();
                countBefore.Should().Be(2);

                var cleared = await fixture.Data.Execute<InstructionProbe, string, int>(new Instruction(DataInstructions.Clear));
                cleared.Should().Be(2);

                var remaining = await InstructionProbe.All(partition);
                remaining.Should().BeEmpty();

                var jsonFiles = Directory.Exists(fixture.RootPath)
                    ? Directory.EnumerateFiles(fixture.RootPath, "*.json", SearchOption.AllDirectories).ToArray()
                    : Array.Empty<string>();

                jsonFiles.Should().NotBeEmpty();
                foreach (var path in jsonFiles)
                {
                    var contents = await File.ReadAllTextAsync(path);
                    contents.Trim().Should().Be("[]");
                }
            })
            .RunAsync();
    }

    private sealed class InstructionProbe : Entity<InstructionProbe>
    {
        public string Name { get; set; } = string.Empty;
    }
}
