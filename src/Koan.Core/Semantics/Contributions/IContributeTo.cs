using System.ComponentModel;

namespace Koan.Core.Semantics.Contributions;

/// <summary>
/// Infrastructure contract used by descriptor-backed <see cref="KoanModule"/> implementations to
/// contribute to one exact, concern-owned composition target.
/// </summary>
/// <typeparam name="TTarget">The exact target language owned by the receiving concern.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IContributeTo<TTarget>
{
    void Contribute(TTarget target);
}
