namespace Sora.Messaging.Provisioning;

/// <summary>
/// Exchange type enumeration (subset common to most brokers). Providers map to native types.
/// </summary>
public enum ExchangeType
{
    Direct = 0,
    Fanout = 1,
    Topic = 2,
    Headers = 3
}
