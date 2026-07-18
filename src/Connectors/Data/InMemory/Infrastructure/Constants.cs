namespace Koan.Data.Connector.InMemory.Infrastructure;

internal static class Constants
{
    internal static class Provider
    {
        internal const string Name = "inmemory";
        internal const string Alias = "memory";
        internal const int Priority = -100;
    }

    internal static class Bootstrap
    {
        internal const string Storage = "Storage";
        internal const string Priority = "Priority";
    }
}
