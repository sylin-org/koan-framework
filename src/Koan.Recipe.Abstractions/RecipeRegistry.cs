namespace Koan.Recipe.Abstractions;

internal static class RecipeRegistry
{
    private static readonly List<Type> Registered = new();
    public static void Register<T>() where T : IKoanRecipe => Register(typeof(T));
    public static void Register(Type t)
    {
        if (!typeof(IKoanRecipe).IsAssignableFrom(t)) throw new ArgumentException("Not a recipe type", nameof(t));
        if (!Registered.Contains(t)) Registered.Add(t);
    }
    public static IReadOnlyList<Type> GetRegistered() => Registered.ToArray();
}