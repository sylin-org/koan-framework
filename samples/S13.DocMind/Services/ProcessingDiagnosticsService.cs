using System;
using System.Collections.Generic;
using System.Linq;
using S13.DocMind.Models;

namespace S13.DocMind.Services;

public interface IDocumentProcessingDiagnostics
{
    Task<IReadOnlyCollection<ProcessingQueueItem>> GetQueueAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProcessingTimelineEntry>> GetTimelineAsync(ProcessingTimelineQuery query, CancellationToken cancellationToken);
    Task<ProcessingRetryResult> RetryAsync(string fileId, ProcessingRetryRequest request, CancellationToken cancellationToken);
}

public class DocumentProcessingDiagnostics : IDocumentProcessingDiagnostics
{
    public async Task<IReadOnlyCollection<ProcessingQueueItem>> GetQueueAsync(CancellationToken cancellationToken)
    {
        var files = await Models.File.All();
        var analyses = await Analysis.All();

        var queueItems = files
            .Where(file => file.Status is "uploaded" or "extracting" or "extracted" or "assigned" or "processing" or "analyzing")
            .Select(file =>
            {
                var analysis = analyses.FirstOrDefault(a => a.Id == file.AnalysisId);

                return new ProcessingQueueItem
                {
                    FileId = file.Id!,
                    FileName = file.Name,
                    Status = file.Status,
                    DocumentTypeId = file.DocumentTypeId,
                    AssignedDate = file.AssignedDate,
                    UploadDate = file.UploadDate,
                    CompletedDate = file.CompletedDate,
                    ErrorMessage = file.ErrorMessage,
                    AnalysisStatus = analysis?.Status,
                    Confidence = analysis?.OverallConfidence,
                    Progress = CalculateProgress(file.Status)
                };
            })
            .OrderByDescending(item => item.UploadDate)
            .ToList();

        return queueItems;
    }

    public async Task<IReadOnlyCollection<ProcessingTimelineEntry>> GetTimelineAsync(ProcessingTimelineQuery query, CancellationToken cancellationToken)
    {
        var files = await Models.File.All();
        var analyses = await Analysis.All();

        var timeline = new List<ProcessingTimelineEntry>();

        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(query.DocumentTypeId) && !string.Equals(file.DocumentTypeId, query.DocumentTypeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (query.FromDate.HasValue && file.UploadDate < query.FromDate.Value)
            {
                continue;
            }

            if (query.ToDate.HasValue && file.UploadDate > query.ToDate.Value)
            {
                continue;
            }

            var analysis = analyses.FirstOrDefault(a => a.Id == file.AnalysisId);

            var steps = new List<ProcessingTimelineStep>
            {
                new("Uploaded", file.UploadDate, file.Status is "uploaded" ? ProcessingStepState.Current : ProcessingStepState.Completed)
            };

            if (file.ExtractedDate.HasValue)
            {
                steps.Add(new ProcessingTimelineStep("Extracted", file.ExtractedDate.Value, file.Status is "extracting" or "extracted" ? ProcessingStepState.Current : ProcessingStepState.Completed));
            }

            if (file.AssignedDate.HasValue)
            {
                steps.Add(new ProcessingTimelineStep("Type Assigned", file.AssignedDate.Value, file.Status is "assigned" ? ProcessingStepState.Current : ProcessingStepState.Completed));
            }

            if (analysis != null)
            {
                steps.Add(new ProcessingTimelineStep("Analysis Started", analysis.StartedDate, analysis.Status == "processing" ? ProcessingStepState.Current : ProcessingStepState.Completed));

                if (analysis.CompletedDate.HasValue)
                {
                    steps.Add(new ProcessingTimelineStep("Analysis Completed", analysis.CompletedDate.Value, analysis.Status == "completed" ? ProcessingStepState.Completed : ProcessingStepState.Current));
                }
            }

            if (file.CompletedDate.HasValue)
            {
                steps.Add(new ProcessingTimelineStep("Document Completed", file.CompletedDate.Value, file.Status == "completed" ? ProcessingStepState.Completed : ProcessingStepState.Current));
            }

            if (file.Status == "failed")
            {
                steps.Add(new ProcessingTimelineStep("Failed", file.LastErrorDate ?? file.CompletedDate ?? file.UploadDate, ProcessingStepState.Failed, file.ErrorMessage));
            }

            if (!string.IsNullOrEmpty(query.Status) && steps.All(step => !string.Equals(step.Name, query.Status, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            timeline.Add(new ProcessingTimelineEntry
            {
                FileId = file.Id!,
                FileName = file.Name,
                DocumentTypeId = file.DocumentTypeId,
                Steps = steps.OrderBy(step => step.Timestamp).ToList(),
                CurrentStatus = file.Status,
                Confidence = analysis?.OverallConfidence,
                ErrorMessage = file.ErrorMessage
            });
        }

        return timeline
            .OrderByDescending(entry => entry.Steps.Max(step => step.Timestamp))
            .ToList();
    }

    public async Task<ProcessingRetryResult> RetryAsync(string fileId, ProcessingRetryRequest request, CancellationToken cancellationToken)
    {
        var file = await Models.File.Get(fileId);
        if (file is null)
        {
            return ProcessingRetryResult.NotFound(fileId);
        }

        var analysis = !string.IsNullOrEmpty(file.AnalysisId)
            ? await Analysis.Get(file.AnalysisId)
            : null;

        if (request.ResetToStage is not null)
        {
            file.Status = request.ResetToStage;
        }
        else
        {
            file.Status = "uploaded";
        }

        file.ErrorMessage = null;
        file.LastErrorDate = null;
        file.CompletedDate = null;
        file.AssignedDate = null;
        file.ExtractedDate = null;

        await file.Save(cancellationToken);

        if (analysis != null && request.IncludeAnalysisReset)
        {
            analysis.Status = "processing";
            analysis.CompletedDate = null;
            analysis.ErrorMessage = null;
            analysis.RetryCount++;
            await analysis.Save(cancellationToken);
        }

        return ProcessingRetryResult.Success(file.Id!, file.Status, analysis?.Status);
    }

    private static int CalculateProgress(string status) => status switch
    {
        "uploaded" => 10,
        "extracting" => 25,
        "extracted" => 45,
        "assigned" => 60,
        "processing" => 75,
        "analyzing" => 90,
        "completed" => 100,
        _ => 0
    };
}

public record ProcessingTimelineQuery
{
    public string? DocumentTypeId { get; init; }
    public string? Status { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}

public class ProcessingTimelineEntry
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? DocumentTypeId { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyCollection<ProcessingTimelineStep> Steps { get; set; } = Array.Empty<ProcessingTimelineStep>();
}

public record ProcessingTimelineStep(string Name, DateTime Timestamp, ProcessingStepState State, string? Notes = null);

public enum ProcessingStepState
{
    Pending,
    Current,
    Completed,
    Failed
}

public class ProcessingQueueItem
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? DocumentTypeId { get; set; }
    public DateTime UploadDate { get; set; }
    public DateTime? AssignedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AnalysisStatus { get; set; }
    public double? Confidence { get; set; }
    public int Progress { get; set; }
}

public class ProcessingRetryRequest
{
    public string? ResetToStage { get; set; }
    public bool IncludeAnalysisReset { get; set; } = true;
}

public class ProcessingRetryResult
{
    private ProcessingRetryResult(bool success, string fileId, string? fileStatus, string? analysisStatus, string? message)
    {
        Success = success;
        FileId = fileId;
        FileStatus = fileStatus;
        AnalysisStatus = analysisStatus;
        Message = message;
    }

    public bool Success { get; }
    public string FileId { get; }
    public string? FileStatus { get; }
    public string? AnalysisStatus { get; }
    public string? Message { get; }

    public static ProcessingRetryResult NotFound(string fileId)
        => new(false, fileId, null, null, "File not found");

    public static ProcessingRetryResult Success(string fileId, string? fileStatus, string? analysisStatus)
        => new(true, fileId, fileStatus, analysisStatus, null);
}
