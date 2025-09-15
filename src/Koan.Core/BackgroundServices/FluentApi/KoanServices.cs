using Koan.Core.BackgroundServices;

public static class KoanServices
{
    public static IServiceActionBuilder Do<T>(string action, object? parameters = null) where T : class, IKoanBackgroundService
        => new ServiceBuilder<T>().Do(action, parameters);

    public static IServiceEventBuilder On<T>(string eventName) where T : class, IKoanBackgroundService
        => new ServiceBuilder<T>().On(eventName);

    public static IServiceQueryBuilder Query<T>() where T : class, IKoanBackgroundService
        => new ServiceBuilder<T>().Query();
}