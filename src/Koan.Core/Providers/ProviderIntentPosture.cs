using System.ComponentModel;

namespace Koan.Core.Providers;

/// <summary>How strongly a caller intended one provider identity.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum ProviderIntentPosture
{
    Automatic,
    Preferred,
    Required
}
