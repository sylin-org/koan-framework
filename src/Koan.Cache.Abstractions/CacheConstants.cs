namespace Koan.Cache.Abstractions;

public static class CacheConstants
{
    public static class Capabilities
    {
        /// <summary>Stable identity for Entity-scoped cache policy and entry behavior.</summary>
        public const string Entity = "koan.cache.entity";
    }

    public static class Configuration
    {
        public const string Section = "Cache";
        public const string LocalProvider = "Cache:LocalProvider";
        public const string RemoteProvider = "Cache:RemoteProvider";
        public const string CoherenceMode = "Cache:CoherenceMode";
        public const string DefaultRegion = "default";
        public const string DefaultSingleflightTimeout = "Cache:DefaultSingleflightTimeout";
        public const string DefaultRegionKey = "Cache:DefaultRegion";
        public const string DefaultTier = "Cache:DefaultTier";
        public const string DefaultTtlSeconds = "Cache:DefaultTtlSeconds";
        public const string DefaultL1TtlSeconds = "Cache:DefaultL1TtlSeconds";
        public const string BroadcastInvalidationByDefault = "Cache:BroadcastInvalidationByDefault";

        public static class Redis
        {
            public const string Section = "Cache:Redis";
            // Endpoint configuration belongs to the shared Koan.Redis backend.
            public const string Channel = "Cache:Redis:Channel";
            public const string ChannelName = "Cache:Redis:ChannelName";
            public const string KeyPrefix = "Cache:Redis:KeyPrefix";
            public const string TagPrefix = "Cache:Redis:TagPrefix";
        }

        public static class Memory
        {
            public const string Section = "Cache:Memory";
            public const string TagIndexCapacity = "Cache:Memory:TagIndexCapacity";
        }
    }

    public static class ContentTypes
    {
        public const string Json = "application/json";
        public const string String = "text/plain; charset=utf-8";
        public const string Binary = "application/octet-stream";
    }

    public static class Metadata
    {
        public const string AbsoluteExpiration = "cache:absolute-expiration";
        public const string StaleUntil = "cache:stale-until";
        public const string ScopeId = "cache:scope-id";
        public const string Region = "cache:region";
    }
}
