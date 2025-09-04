using System;
using Sora.Data.Core;

namespace S8.Flow.Api.DebugHelpers
{
    public static class ProviderDebug
    {
        public static string GetProviderForStageRecordReading()
        {
            return AggregateConfigs.Get<Sora.Flow.Model.StageRecord<S8.Flow.Shared.Reading>, string>(Sora.Core.Hosting.App.AppHost.Current!).Provider;
        }
    }
}
