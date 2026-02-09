namespace Koan.AI.Pipelines;

/// <summary>
/// Ambient context for pipeline execution carrying model, source, and options.
/// </summary>
internal sealed record PipelineContext
{
    public static PipelineContext Current => new();

    public string? Model { get; init; }
    public string? Source { get; init; }
    public string? SystemPrompt { get; init; }
    public object? Options { get; init; }

    public PipelineContext WithModel(string? model)
        => this with { Model = model ?? Model };

    public PipelineContext WithSource(string? source)
        => this with { Source = source ?? Source };

    public PipelineContext WithSystemPrompt(string? systemPrompt)
        => this with { SystemPrompt = systemPrompt ?? SystemPrompt };

    public PipelineContext WithOptions(object? options)
        => this with { Options = options ?? Options };
}
