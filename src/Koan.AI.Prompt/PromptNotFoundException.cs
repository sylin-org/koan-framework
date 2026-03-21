namespace Koan.AI.Prompt;

/// <summary>
/// Thrown when a prompt cannot be found in the PromptEntry catalog.
/// </summary>
public sealed class PromptNotFoundException : Exception
{
    public string PromptName { get; }
    public int? RequestedVersion { get; }

    public PromptNotFoundException(string name)
        : base($"No active prompt found with name '{name}'. " +
               $"Create a PromptEntry with Name=\"{name}\" and Status=Active.")
    {
        PromptName = name;
    }

    public PromptNotFoundException(string name, int version)
        : base($"No prompt found with name '{name}' version {version}.")
    {
        PromptName = name;
        RequestedVersion = version;
    }
}
