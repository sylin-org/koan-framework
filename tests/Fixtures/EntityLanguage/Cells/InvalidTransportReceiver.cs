using Koan.Communication;

public static class InvalidTransportConsumer
{
    public static Task Use(object value) => value.Transport.Send();
}
