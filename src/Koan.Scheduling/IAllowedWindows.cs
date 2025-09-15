namespace Koan.Scheduling;

public interface IAllowedWindows
{
    // List of windows in local time of TimeZone (default UTC): tuples of (start,end), 24h clock.
    IReadOnlyList<(TimeSpan start, TimeSpan end)> Windows { get; }
    TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
}