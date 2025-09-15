namespace Koan.Data.Mongo;

internal static class MongoConstants
{
    public const string DefaultLocalUri = "mongodb://localhost:27017";
    public const string DefaultComposeUri = "mongodb://mongo:27017";
    public const string EnvList = Infrastructure.Constants.Discovery.EnvList; // comma/semicolon-separated list
}