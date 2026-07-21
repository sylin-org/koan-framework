namespace SnapVault.Infrastructure;

internal static class Constants
{
    internal static class Query
    {
        public const string Event = "event";
    }

    internal static class Paths
    {
        public const string PhotoSets = "/api/photosets";
        public const string Proofing = "/api/proofing";
        public const string Media = "/media";
    }

    internal static class RequestItems
    {
        public static readonly object GalleryGrant = new();
    }
}
