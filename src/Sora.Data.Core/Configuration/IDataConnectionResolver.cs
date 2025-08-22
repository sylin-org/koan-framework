namespace Sora.Data.Core.Configuration;

public interface IDataConnectionResolver
{
    string? Resolve(string providerId, string name);
}