using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core.Semantics;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Json.Tests.Specs.Persistence;

public sealed class JsonPersistenceSafetySpec
{
    [Fact]
    public async Task Corrupt_store_fails_correctively_instead_of_becoming_empty()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-json-corruption-{Guid.CreateVersion7():N}");

        try
        {
            var initial = Repository(root);
            await initial.ExecuteAsync<bool>(new Instruction(DataInstructions.EnsureCreated));
            var path = Directory.EnumerateFiles(root, "*.json").Should().ContainSingle().Subject;
            await File.WriteAllTextAsync(path, "{ not-valid-json }");

            var reload = Repository(root);
            var act = () => reload.ExecuteAsync<bool>(new Instruction(DataInstructions.EnsureCreated));

            var failure = await act.Should().ThrowAsync<InvalidDataException>();
            failure.Which.Message.Should().Contain(path);
            failure.Which.Message.Should().Contain("never treated as empty");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static JsonRepository<PersistenceProbe, string> Repository(string root) =>
        new(
            Options.Create(new JsonDataOptions { DirectoryPath = root }),
            new DataSegmentationPlan(SegmentationPlan.Empty));

    private sealed class PersistenceProbe : Entity<PersistenceProbe>;
}
