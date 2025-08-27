using Microsoft.Extensions.Logging;

namespace Sora.Recipe.Abstractions;

internal static class RecipeLog
{
    public static class Events
    {
        public static readonly EventId Applying = new(41000, nameof(Applying));
        public static readonly EventId AppliedOk = new(41001, nameof(AppliedOk));
        public static readonly EventId SkippedNotActive = new(41002, nameof(SkippedNotActive));
        public static readonly EventId SkippedShouldApplyFalse = new(41003, nameof(SkippedShouldApplyFalse));
        public static readonly EventId DryRun = new(41004, nameof(DryRun));
        public static readonly EventId ApplyFailed = new(41005, nameof(ApplyFailed));
    }
}