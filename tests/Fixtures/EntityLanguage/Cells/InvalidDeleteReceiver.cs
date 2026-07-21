public static class InvalidDeleteConsumer
{
    public static Task<bool> Delete() => new object().Delete();
}
