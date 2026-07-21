namespace Koan.AI.Prompt;

/// <summary>
/// A few-shot example carried by a structured prompt value.
/// </summary>
public sealed record Example(string Input, string Output)
{
    /// <summary>Format as text for inclusion in the prompt.</summary>
    public string ToText() => $"Example:\nInput: {Input}\nOutput: {Output}";
}
