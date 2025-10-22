using System.Text.Json.Serialization;
using Koan.Cache.Adapter.Redis.Stores;

namespace Koan.Cache.Adapter.Redis.Serialization;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(RedisCacheEnvelope))]
[JsonSerializable(typeof(RedisInvalidationMessage))]
internal partial class RedisCacheSerializerContext : JsonSerializerContext
{
}
