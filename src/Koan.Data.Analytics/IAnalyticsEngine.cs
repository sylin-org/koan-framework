namespace Koan.Data.Analytics;

/// <summary>
/// A connector's declaration that it can serve as the analytics execution substrate — today as the
/// accelerator behind materialized answers, eventually as their home. Implemented by the connector and
/// registered by its module; the pillar refuses to compose without one (DATA-0123).
/// </summary>
public interface IAnalyticsEngine
{
    string Name { get; }
}
