using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core.Semantics;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

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

    [Fact]
    public async Task Failed_serialization_does_not_publish_the_candidate_to_memory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-json-failed-write-{Guid.CreateVersion7():N}");
        try
        {
            var repository = FailureRepository(root);
            await repository.Upsert(new FailureProbe { Id = "one", Value = "stable" });
            var candidate = (await repository.Get("one"))!;
            candidate.Value = "must-not-leak";
            candidate.ThrowOnSerialize = true;

            await FluentActions.Invoking(() => repository.Upsert(candidate))
                .Should().ThrowAsync<JsonSerializationException>();

            var visible = await repository.Get("one");
            visible!.Value.Should().Be("stable");
            var cold = await FailureRepository(root).Get("one");
            cold!.Value.Should().Be("stable");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static JsonRepository<PersistenceProbe, string> Repository(string root) =>
        new(
            new JsonRoute("Test", root, Koan.Data.Abstractions.Sources.StorageLifecycle.Managed,
                Koan.Data.Abstractions.Sources.DataSourceAccess.ReadWrite),
            new DataSegmentationPlan(SegmentationPlan.Empty),
            new JsonAdapterFactory(),
            EmptyServiceProvider.Instance);

    private static JsonRepository<FailureProbe, string> FailureRepository(string root) =>
        new(
            new JsonRoute("Test", root, Koan.Data.Abstractions.Sources.StorageLifecycle.Managed,
                Koan.Data.Abstractions.Sources.DataSourceAccess.ReadWrite),
            new DataSegmentationPlan(SegmentationPlan.Empty),
            new JsonAdapterFactory(),
            EmptyServiceProvider.Instance);

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        internal static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class PersistenceProbe : Entity<PersistenceProbe>;

    private sealed class FailureProbe : Entity<FailureProbe>
    {
        private string _value = "";

        [JsonIgnore]
        public bool ThrowOnSerialize { get; set; }

        public string Value
        {
            get => ThrowOnSerialize ? throw new InvalidOperationException("serialization rejected") : _value;
            set => _value = value;
        }
    }
}
