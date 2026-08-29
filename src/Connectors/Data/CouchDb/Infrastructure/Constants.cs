namespace Koan.Data.Connector.CouchDb.Infrastructure;

public static class Constants
{
    public const string Provider = "couchdb";
    public const string Service = "couchdb";
    public const string DefaultSource = "Default";

    /// <summary>
    /// CouchDB namespaces documents by database, not by collection, so the route's database is a
    /// prefix and every entity container resolves to its own database under it.
    /// </summary>
    public const string DefaultDatabase = "koan";

    public static class Configuration
    {
        public const string Section = "Koan:Data:CouchDb";
        public const string Endpoint = Section + ":Endpoint";
        public const string ConnectionString = Section + ":ConnectionString";
        public const string Database = Section + ":Database";
        public const string UserId = Section + ":UserId";
        public const string Password = Section + ":Password";
    }

    /// <summary>Reserved document fields this adapter owns.</summary>
    public static class Storage
    {
        public const string Identity = "_id";
        public const string Rev = "_rev";
    }
}
