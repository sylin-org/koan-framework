namespace Koan.Storage.Connector.S3.Infrastructure;

public static class S3StorageConstants
{
    public const string ProviderName = "s3";

    public static class Configuration
    {
        public const string Section = "Koan:Storage:Providers:S3";

        public static class Keys
        {
            public const string Endpoint = nameof(Endpoint);
            public const string AccessKey = nameof(AccessKey);
            public const string SecretKey = nameof(SecretKey);
            public const string BucketPrefix = nameof(BucketPrefix);
            public const string UseSsl = nameof(UseSsl);
            public const string Region = nameof(Region);
            public const string MossEndpoint = nameof(MossEndpoint);
            public const string ReplicaSet = nameof(ReplicaSet);
        }
    }

    public static class ZenGarden
    {
        /// <summary>
        /// Adapter ID used in ZenGarden offering binding.
        /// Maps to "storage" offering type in the garden.
        /// </summary>
        public const string AdapterId = "s3";

        /// <summary>
        /// Default offering name in Zen Garden tool registry.
        /// </summary>
        public const string DefaultOffering = "storage";
    }
}
