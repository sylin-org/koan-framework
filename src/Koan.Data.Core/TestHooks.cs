namespace Koan.Data.Core;

public static class TestHooks
{
    public static void ResetDataConfigs() => AggregateConfigs.Reset();
}
