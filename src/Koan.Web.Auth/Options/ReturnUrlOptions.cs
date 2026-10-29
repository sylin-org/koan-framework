namespace Koan.Web.Auth.Options;

public sealed class ReturnUrlOptions
{
    public string DefaultPath { get; init; } = "/";
    public string[] AllowList { get; init; } = [];

    /// <summary>
    /// WEB-0080: where a failed sign-in lands for every provider, as a rooted path of this application such as
    /// <c>/sign-in</c>; the person arrives there with the failure's code in the <c>error</c> query. A challenge
    /// names its own landing beside <c>return</c> (<c>failure=/sign-in</c>), which wins over this. Null, the
    /// default, keeps each handler's own failure answer. The start is refused for a value
    /// <see cref="Infrastructure.SignInFailureReturn.TryAccept"/> refuses: one that could leave the application or its
    /// base path, could not be a header, or already names <c>error</c> in its query.
    /// Configure via: Koan:Web:Auth:ReturnUrl:FailurePath.
    /// </summary>
    public string? FailurePath { get; init; }
}