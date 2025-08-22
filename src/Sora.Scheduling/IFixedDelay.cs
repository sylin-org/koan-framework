namespace Sora.Scheduling;

public interface IFixedDelay
{
    TimeSpan Delay { get; }
}