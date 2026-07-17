namespace Koan.Core.Hosting.App;

/// <summary>
/// Describes why a terse Koan API could not reach a service owned by the active application host.
/// </summary>
public sealed class KoanHostContextException : InvalidOperationException
{
    /// <summary>
    /// Identifies the unavailable part of the host context.
    /// </summary>
    public enum FailureKind
    {
        MissingHost,
        DisposedHost,
        MissingService
    }

    internal KoanHostContextException(
        string operation,
        Type requiredService,
        FailureKind failure,
        Exception? innerException = null)
        : base(BuildMessage(operation, requiredService, failure), innerException)
    {
        Operation = operation;
        RequiredService = requiredService;
        Failure = failure;
    }

    /// <summary>
    /// Gets the framework operation that required the host context.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the service the operation requires.
    /// </summary>
    public Type RequiredService { get; }

    /// <summary>
    /// Gets the reason the service could not be reached.
    /// </summary>
    public FailureKind Failure { get; }

    private static string BuildMessage(string operation, Type requiredService, FailureKind failure)
    {
        var reason = failure switch
        {
            FailureKind.MissingHost => "no Koan application host is active",
            FailureKind.DisposedHost => "the active Koan application host has already been disposed",
            FailureKind.MissingService => "the active Koan application host does not provide the required service",
            _ => "the Koan application host is unavailable"
        };

        return $"Koan cannot perform '{operation}' because {reason}. " +
               $"Required service: '{requiredService.FullName ?? requiredService.Name}'. " +
               "Start the application through services.AddKoan() and a Koan host, use " +
               "IServiceCollection.StartKoan() for synchronous console startup, or enter " +
               "AppHost.PushScope(provider) with the required module composed.";
    }
}
