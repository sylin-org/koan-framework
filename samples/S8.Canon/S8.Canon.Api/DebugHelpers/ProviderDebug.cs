using System;
using Koan.Data.Core;

namespace S8.Canon.Api.DebugHelpers
{
    public static class ProviderDebug
    {
        public static string GetProviderForStageRecordReading()
        {
            return AggregateConfigs.Get<Koan.Canon.Model.StageRecord<S8.Canon.Shared.Reading>, string>(Koan.Core.Hosting.App.AppHost.Current!).Provider;
        }
    }
}
