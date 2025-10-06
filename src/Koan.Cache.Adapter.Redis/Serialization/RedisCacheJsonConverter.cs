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
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        var json = JsonConvert.SerializeObject(envelope, JsonDefaults.Settings);
        return json;
    }

    public static RedisCacheEnvelope DeserializeEnvelope(RedisValue value)
    {
        if (!value.HasValue)
        {
            throw new InvalidOperationException("Redis cache entry was missing payload.");
        }

        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Redis cache entry payload was empty.");
        }

        var envelope = JsonConvert.DeserializeObject<RedisCacheEnvelope>(json, JsonDefaults.Settings);
        return envelope ?? throw new InvalidOperationException("Unable to deserialize Redis cache envelope.");
    }

    public static RedisValue SerializeInvalidation(RedisInvalidationMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var json = JsonConvert.SerializeObject(message, JsonDefaults.Settings);
        return json;
    }

    public static RedisInvalidationMessage DeserializeInvalidation(RedisValue value)
    {
        if (!value.HasValue)
        {
            throw new InvalidOperationException("Redis cache invalidation payload was empty.");
        }

        var json = value.ToString();
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Redis cache invalidation payload was blank.");
        }

        var message = JsonConvert.DeserializeObject<RedisInvalidationMessage>(json, JsonDefaults.Settings);
        return message ?? throw new InvalidOperationException("Unable to deserialize Redis cache invalidation payload.");
    }
}
