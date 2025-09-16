namespace Koan.Scheduling;

public interface IHealthFacts
{
    IReadOnlyDictionary<string, string> GetFacts();
}