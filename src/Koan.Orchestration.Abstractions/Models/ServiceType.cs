namespace Koan.Orchestration.Models;

/// <summary>
/// Canonical service types for both orchestration and runtime concerns.
/// Extended to support the full range of service categories.
/// </summary>
public enum ServiceType
{
    Service = 0,
    App = 1,
    Database = 2,
    Vector = 3,
    Ai = 4,
    Auth = 5,
    Messaging = 6,
    Storage = 7,
    Cache = 8,
    Search = 9,
    SecretsVault = 10,
    Other = 99
}
