using System;
using Koan.Data.Core;

namespace S8.Flow.Api.DebugHelpers
{
    public static class ProviderDebug
    {
        public static string GetProviderForStageRecordReading()
        {
            return AggregateConfigs.Get<Koan.Flow.Model.StageRecord<S8.Flow.Shared.Reading>, string>(Koan.Core.Hosting.App.AppHost.Current!).Provider;
        }
    }
}
