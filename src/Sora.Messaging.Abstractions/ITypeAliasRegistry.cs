namespace Sora.Messaging;

public interface ITypeAliasRegistry
{
    string GetAlias(Type type);
    Type? Resolve(string alias);
}