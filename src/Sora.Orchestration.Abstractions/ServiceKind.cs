namespace Sora.Orchestration;

/// <summary>
/// Unified service kinds for declarative discovery and UX grouping.
/// </summary>
public enum ServiceKind
{
    App = 0,
    Database = 1,
    Vector = 2,
    Ai = 3,
    Auth = 4,
    Messaging = 5,
    Storage = 6,
    Cache = 7,
    Search = 8,
    Other = 9,
    SecretsVault = 10
}
