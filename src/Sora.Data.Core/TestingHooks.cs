namespace Sora.Data.Core;

public static class TestHooks
{
    public static void ResetDataConfigs() => Configuration.AggregateConfigs.Reset();
}
