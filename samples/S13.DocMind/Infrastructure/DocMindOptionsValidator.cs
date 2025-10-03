using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace S13.DocMind.Infrastructure;

public sealed class DocMindOptionsValidator : IValidateOptions<DocMindOptions>
{
    public ValidateOptionsResult Validate(string? name, DocMindOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Options cannot be null");
        }

        var failures = new List<string>();

        if (options.Manual is null)
        {
            failures.Add("DocMind:Manual section must be configured.");
        }

        if (options.Storage.AllowedContentTypes is null || options.Storage.AllowedContentTypes.Length == 0)
        {
            failures.Add("DocMind:Storage:AllowedContentTypes must include at least one entry.");
        }

        if (options.Processing.WorkerBatchSize > options.Processing.QueueCapacity)
        {
            failures.Add("DocMind:Processing:WorkerBatchSize cannot exceed QueueCapacity.");
        }

        if (options.Processing.RetryInitialDelaySeconds > options.Processing.RetryMaxDelaySeconds)
        {
            failures.Add("DocMind:Processing:RetryInitialDelaySeconds must be less than or equal to RetryMaxDelaySeconds.");
        }

        if (options.Processing.MaxConcurrency > options.Processing.QueueCapacity)
        {
            failures.Add("DocMind:Processing:MaxConcurrency should not be higher than QueueCapacity to avoid starving the queue.");
        }

        if (options.Processing.ChunkSizeTokens % 50 != 0)
        {
            failures.Add("DocMind:Processing:ChunkSizeTokens should be a multiple of 50 to align with tokenizer heuristics.");
        }

        if (options.Manual.MaxDocuments < 2)
        {
            failures.Add("DocMind:Manual:MaxDocuments must allow at least two documents for cross-document synthesis.");
        }

        if (options.Manual.MaxPromptTokens <= options.Processing.ChunkSizeTokens)
        {
            failures.Add("DocMind:Manual:MaxPromptTokens should exceed Processing:ChunkSizeTokens to support multi-chunk prompts.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
