using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace Koan.Data.Connector.Redis.Runtime;

internal sealed class RedisSourceIntegration(RedisRoute route) : IDataSourceIntegration
{
    public SourceIntegrationCapabilities Capabilities =>
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar;

    public IDataSourceInspectorAdapter? Inspector => null;

    public bool Supports(IDataOperationBinding binding, OperationResultKind result) =>
        binding is FunctionOperationBinding && result is OperationResultKind.Records or OperationResultKind.Scalar;

    public bool EnforcesReadLane(DataReadLanePlan lane) => route.ReadLanes.ContainsKey(lane.Name);

    public async Task<INeutralRecordReader> ExecuteRecords(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var result = await Execute(plan, parameters, ct).ConfigureAwait(false);
        var rows = Array(result);
        var documents = new List<JObject>(Math.Min(rows.Length, plan.Limits.MaxRecords + 1));
        foreach (var row in rows.Take(plan.Limits.MaxRecords + 1))
        {
            var text = Scalar(row);
            if (System.Text.Encoding.UTF8.GetByteCount(text) > plan.Limits.MaxValueBytes)
                throw new InvalidDataException($"Redis Function '{plan.Name}' returned a row larger than MaxValueBytes.");
            documents.Add(JObject.Parse(text));
        }
        return RedisNeutralReader.Bounded(documents, plan.Limits.MaxRecords);
    }

    public async Task<SourceScalarResult> ExecuteScalar(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var result = await Execute(plan, parameters, ct).ConfigureAwait(false);
        if (result.IsNull) return new SourceScalarResult(0, 0, null);
        if (result.Resp2Type == ResultType.Array)
        {
            var values = Array(result);
            if (values.Length == 0) return new SourceScalarResult(0, 0, null);
            if (values.Length != 1) return new SourceScalarResult(values.Length, 1, null, "redis-array");
            result = values[0];
        }
        var value = NeutralScalar(result);
        return new SourceScalarResult(1, 1, value, result.Resp2Type.ToString());
    }

    private async Task<RedisResult> Execute(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct)
    {
        var binding = plan.Binding as FunctionOperationBinding
            ?? throw new NotSupportedException($"Redis does not support registered binding '{plan.Binding.Kind}'.");
        var lane = plan.Lane ?? throw new InvalidOperationException(
            $"Redis Function '{plan.Name}' requires a provider-enforced read lane.");
        if (!route.ReadLanes.TryGetValue(lane.Name, out var connectionString))
            throw new InvalidOperationException($"Read lane '{lane.Name}' is not configured for source '{plan.Source}'.");
        var byName = parameters.ToDictionary(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase);
        var keys = binding.Keys.Select(key => ResolveKey(key, byName)).ToArray();
        var arguments = new List<object>(2 + keys.Length + parameters.Count)
        {
            binding.Name,
            keys.Length
        };
        arguments.AddRange(keys.Cast<object>());
        arguments.AddRange(parameters.Select(static parameter => Argument(parameter.Value)));
        var database = route.Connections.GetConnection(connectionString).GetDatabase(route.Database);
        return await database.ExecuteAsync("FCALL_RO", arguments.ToArray()).WaitAsync(ct).ConfigureAwait(false);
    }

    private static string ResolveKey(
        string value,
        IReadOnlyDictionary<string, BoundOperationParameter> parameters)
    {
        if (!value.StartsWith('@')) return value;
        var name = value[1..];
        if (!parameters.TryGetValue(name, out var parameter) || parameter.Value is null)
            throw new InvalidOperationException($"Redis Function key '{value}' requires a non-null parameter named '{name}'.");
        return Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? "";
    }

    private static object Argument(object? value) => value switch
    {
        null => "",
        byte[] bytes => bytes,
        bool boolean => boolean ? "1" : "0",
        DateTime instant => instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset instant => instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    private static RedisResult[] Array(RedisResult result) => result.Resp2Type == ResultType.Array
        ? (RedisResult[]?)result ?? []
        : throw new InvalidDataException("Redis record Functions must return an array of JSON object strings.");

    private static string Scalar(RedisResult result) => result.Resp2Type switch
    {
        ResultType.BulkString or ResultType.SimpleString => (string?)result
            ?? throw new InvalidDataException("Redis Function returned a null record."),
        _ => throw new InvalidDataException("Redis record Functions must return JSON object strings.")
    };

    private static object? NeutralScalar(RedisResult result) => result.Resp2Type switch
    {
        ResultType.Integer => (long)result,
        ResultType.BulkString or ResultType.SimpleString => (string?)result,
        _ => result.ToString()
    };
}
