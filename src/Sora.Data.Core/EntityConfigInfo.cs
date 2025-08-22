using Sora.Data.Core.Configuration;

namespace Sora.Data.Core;

public sealed record EntityConfigInfo(
    string EntityType,
    string KeyType,
    string Provider,
    string? IdProperty,
    IReadOnlyList<(string Key, string Type)> Bags
);