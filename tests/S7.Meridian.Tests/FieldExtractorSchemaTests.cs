using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using FluentAssertions;
using Koan.Samples.Meridian.Models;
using Koan.Samples.Meridian.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;

namespace S7.Meridian.Tests;

public sealed class FieldExtractorSchemaTests
{
    private readonly FieldExtractor _extractor;

    public FieldExtractorSchemaTests()
    {
        _extractor = new FieldExtractor(NullLogger<FieldExtractor>.Instance, new InMemoryEmbeddingCache(), new NoOpRunLogWriter());
    }

    [Theory]
    [InlineData("123.45", "{\"type\": \"number\"}", 123.45)]
    [InlineData("42", "{\"type\": \"integer\"}", 42L)]
    public void ValidateAgainstSchema_NormalizesNumericStrings(string value, string schemaJson, object expected)
    {
        var schema = JSchema.Parse(schemaJson);
        var token = new JValue(value);

        var (isValid, normalized, error) = InvokeValidate(token, schema);

        isValid.Should().BeTrue();
        error.Should().BeNull();
        normalized.Should().NotBeNull();
        normalized!.Type.Should().Be(expected is long ? JTokenType.Integer : JTokenType.Float);

        if (expected is long expectedLong)
        {
            normalized.Value<long>().Should().Be(expectedLong);
        }
        else
        {
            normalized.Value<double>().Should().BeApproximately((double)expected, 1e-6);
        }
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("False", false)]
    public void ValidateAgainstSchema_NormalizesBooleanStrings(string value, bool expected)
    {
        var schema = JSchema.Parse("{\"type\": \"boolean\"}");
        var token = new JValue(value);

        var (isValid, normalized, error) = InvokeValidate(token, schema);

        isValid.Should().BeTrue();
        error.Should().BeNull();
        normalized.Should().NotBeNull();
        normalized!.Type.Should().Be(JTokenType.Boolean);
        normalized.Value<bool>().Should().Be(expected);
    }

    private (bool IsValid, JToken? Normalized, string? Error) InvokeValidate(JToken? value, JSchema schema)
    {
        var method = typeof(FieldExtractor)
            .GetMethod("ValidateAgainstSchema", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Method not found");

        var parameters = new object?[] { value, schema, null, null };
        var result = (bool)method.Invoke(_extractor, parameters)!;
        var normalized = (JToken?)parameters[2];
        var error = (string?)parameters[3];

        return (result, normalized, error);
    }

    private sealed class InMemoryEmbeddingCache : IEmbeddingCache
    {
        private readonly ConcurrentDictionary<string, CachedEmbedding> _store = new();

        public Task<CachedEmbedding?> GetAsync(string contentHash, string modelId, string entityTypeName, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(Key(contentHash, modelId, entityTypeName), out var value) ? value : null);

        public Task SetAsync(string contentHash, string modelId, float[] embedding, string entityTypeName, CancellationToken ct = default)
        {
            var entry = new CachedEmbedding
            {
                ContentHash = contentHash,
                ModelId = modelId,
                Embedding = embedding,
                Dimension = embedding.Length,
                CachedAt = DateTimeOffset.UtcNow
            };
            _store[Key(contentHash, modelId, entityTypeName)] = entry;
            return Task.CompletedTask;
        }

        public Task<int> FlushAsync(CancellationToken ct = default)
        {
            var count = _store.Count;
            _store.Clear();
            return Task.FromResult(count);
        }

        public Task<CacheStats> GetStatsAsync(CancellationToken ct = default)
            => Task.FromResult(new CacheStats(_store.Count, 0, null, null));

        private static string Key(string hash, string model, string entity)
            => $"{entity}:{model}:{hash}";
    }

    private sealed class NoOpRunLogWriter : IRunLogWriter
    {
        public Task AppendAsync(RunLog log, CancellationToken ct)
            => Task.CompletedTask;
    }
}
