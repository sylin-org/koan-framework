namespace Koan.Web.Transformers;

internal sealed class TransformerBindings
{
    public List<Action<IServiceProvider>> Bindings { get; } = new();
}