using Sora.Core.BackgroundServices;

public static class SoraServices
{
    public static IServiceActionBuilder Do<T>(string action, object? parameters = null) where T : class, ISoraBackgroundService
        => new ServiceBuilder<T>().Do(action, parameters);

    public static IServiceEventBuilder On<T>(string eventName) where T : class, ISoraBackgroundService
        => new ServiceBuilder<T>().On(eventName);

    public static IServiceQueryBuilder Query<T>() where T : class, ISoraBackgroundService
        => new ServiceBuilder<T>().Query();
}