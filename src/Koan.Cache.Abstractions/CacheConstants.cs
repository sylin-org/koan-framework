namespace Koan.Cache.Abstractions;

public static class CacheConstants
{
    public static class Configuration
    {
        public const string Section = "Cache";
        public const string ProviderKey = "Cache:Provider";
        public const string DefaultRegion = "default";

        public static class Redis
        {
            public const string Section = "Cache:Redis";
            public const string Configuration = "Cache:Redis:Configuration";
            public const string Channel = "Cache:Redis:Channel";
            public const string ChannelName = "Cache:Redis:ChannelName";
            public const string KeyPrefix = "Cache:Redis:KeyPrefix";
            public const string TagPrefix = "Cache:Redis:TagPrefix";
        }

        public static class Memory
        {
            public const string Section = "Cache:Memory";
            public const string TagIndexCapacity = "Cache:Memory:TagIndexCapacity";
            public const string EnableStaleWhileRevalidate = "Cache:Memory:EnableStaleWhileRevalidate";
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
