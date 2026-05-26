using System.Collections.Concurrent;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public sealed class AiGenerationJobService
{
    private readonly ConcurrentDictionary<string, AiGenerationJobState> _jobs = new();

    public AiGenerationJobState Create(int targetCount, int totalBatches)
    {
        var job = new AiGenerationJobState(
            JobId: Guid.NewGuid().ToString("N"),
            Status: "running",
            TargetCount: targetCount,
            TotalBatches: totalBatches,
            CompletedBatches: 0,
            QuestionsGenerated: 0,
            Session: null,
            Error: null,
            CreatedAt: DateTimeOffset.UtcNow);

        _jobs[job.JobId] = job;
        return job;
    }

    public AiGenerationJobState? Get(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public void ReportBatch(string jobId, int completedBatches, int questionsGenerated)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        _jobs[jobId] = job with
        {
            CompletedBatches = completedBatches,
            QuestionsGenerated = questionsGenerated,
        };
    }

    public void Complete(string jobId, SessionDto session)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        _jobs[jobId] = job with
        {
            Status = "complete",
            Session = session,
            QuestionsGenerated = session.TotalQuestions,
            CompletedBatches = job.TotalBatches,
        };
    }

    public void Fail(string jobId, string error)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        _jobs[jobId] = job with { Status = "failed", Error = error };
    }
}

public sealed record AiGenerationJobState(
    string JobId,
    string Status,
    int TargetCount,
    int TotalBatches,
    int CompletedBatches,
    int QuestionsGenerated,
    SessionDto? Session,
    string? Error,
    DateTimeOffset CreatedAt);

public sealed record AiGenerationStatusDto(
    string JobId,
    string Status,
    int TargetCount,
    int TotalBatches,
    int CompletedBatches,
    int QuestionsGenerated,
    SessionDto? Session,
    string? Error);
