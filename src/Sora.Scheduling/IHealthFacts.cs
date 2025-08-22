namespace Sora.Scheduling;

public interface IHealthFacts
{
    IReadOnlyDictionary<string, string> GetFacts();
}