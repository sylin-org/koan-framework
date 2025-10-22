namespace Koan.Service.Inbox.Connector.Redis;

/// <summary>
/// Provides convenience helpers for working with the Redis inbox connector endpoints.
/// </summary>
public static class RedisInboxRoutes
{
    /// <summary>
    /// Base route for inbox HTTP APIs.
    /// </summary>
    public const string Base = "v1/inbox";

    /// <summary>
    /// Route used to check the processing status of a message by key.
    /// </summary>
    /// <param name="key">Identifier for the inbox item.</param>
    /// <returns>Formatted route.</returns>
    public static string Status(string key) => $"{Base}/{key}";

    /// <summary>
    /// Route used to mark a message as processed.
    /// </summary>
    public const string MarkProcessed = $"{Base}/mark-processed";
}
