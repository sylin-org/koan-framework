namespace Koan.Scheduling;

public interface ICronScheduled
{
    string Cron { get; }
    TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
}