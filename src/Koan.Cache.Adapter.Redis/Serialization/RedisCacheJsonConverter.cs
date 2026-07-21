using System;
using Koan.Cache.Adapter.Redis.Stores;
using Koan.Core.Json;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Koan.Cache.Adapter.Redis.Serialization;

internal static class RedisCacheJsonConverter
{
    public static RedisValue SerializeEnvelope(RedisCacheEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonConvert.SerializeObject(envelope, JsonDefaults.Settings);
    }

    public static RedisCacheEnvelope DeserializeEnvelope(RedisValue value)
    {
        if (!value.HasValue)
            throw new InvalidOperationException("Redis cache entry was missing payload.");

        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Redis cache entry payload was empty.");

        var envelope = JsonConvert.DeserializeObject<RedisCacheEnvelope>(json, JsonDefaults.Settings);
        return envelope ?? throw new InvalidOperationException("Unable to deserialize Redis cache envelope.");
    }
}
