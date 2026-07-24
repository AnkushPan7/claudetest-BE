using System.Collections.Concurrent;
using System.Text.Json;
using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Data;
using ClaudeCertPractice.Api.Data.Entities;
using ClaudeCertPractice.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaudeCertPractice.Api.Services;

/// <summary>
/// Per-user AI generation jobs that keep running after the browser refreshes.
/// Progress is persisted so the client can resume polling by email + jobId.
/// </summary>
public class AiGenerationJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiGenerationJobService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public AiGenerationJobService(
        IServiceScopeFactory scopeFactory,
        ILogger<AiGenerationJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<GenerationJobDto> StartOrGetActiveAsync(
        string userEmail,
        int count,
        int[]? sectionIds,
        string? learningUrl,
        bool forceNew = false,
        CancellationToken ct = default)
    {
        var email = NormalizeEmail(userEmail);
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("User email is required to start AI generation.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<QuizSettings>>().Value;
        var ai = scope.ServiceProvider.GetRequiredService<AiQuestionGeneratorService>();

        if (!ai.IsConfigured)
            throw new InvalidOperationException(
                "AI mode requires ANTHROPIC_API_KEY or AnthropicApiKey in environment (or .env), or Quiz:AnthropicApiKey.");

        count = Math.Clamp(count, 1, settings.MaxQuestionsPerSession);

        var active = await db.AiGenerationJobs
            .Where(j => j.UserEmail == email
                && (j.Status == AiGenerationJobStatuses.Pending
                    || j.Status == AiGenerationJobStatuses.Running
                    || j.Status == AiGenerationJobStatuses.Completed))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (forceNew && active is not null
            && active.Status is AiGenerationJobStatuses.Pending or AiGenerationJobStatuses.Running)
        {
            if (_running.TryRemove(active.Id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            active.Status = AiGenerationJobStatuses.Cancelled;
            active.Error = "Replaced by a new generation request.";
            active.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            active = null;
        }

        // Only reuse in-flight generation so a browser refresh can keep polling.
        // Completed jobs are never reused here — "Generate new test" must create fresh questions.
        // In-progress exam resume is handled separately via the client's active-exam progress.
        if (active is not null && !forceNew
            && active.Status is AiGenerationJobStatuses.Pending or AiGenerationJobStatuses.Running)
        {
            return ToDto(active);
        }

        if (!forceNew)
        {
            var failed = await db.AiGenerationJobs
                .Where(j => j.UserEmail == email
                    && j.Status == AiGenerationJobStatuses.Failed
                    && j.RequestedCount == count
                    && j.UpdatedAt > DateTime.UtcNow.AddHours(-6))
                .OrderByDescending(j => j.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (failed is not null && failed.CompletedCount < failed.RequestedCount)
            {
                failed.Status = AiGenerationJobStatuses.Pending;
                failed.Error = null;
                failed.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                QueueJob(failed.Id);
                return ToDto(failed);
            }
        }

        var job = new AiGenerationJobEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            UserEmail = email,
            Status = AiGenerationJobStatuses.Pending,
            RequestedCount = count,
            CompletedCount = 0,
            SectionIdsJson = sectionIds is { Length: > 0 }
                ? JsonSerializer.Serialize(sectionIds)
                : null,
            LearningUrl = string.IsNullOrWhiteSpace(learningUrl) ? null : learningUrl.Trim(),
            QuestionsJson = "[]",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.AiGenerationJobs.Add(job);
        await db.SaveChangesAsync(ct);

        QueueJob(job.Id);
        return ToDto(job);
    }

    public async Task<GenerationJobDto?> GetAsync(string jobId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.AiGenerationJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        return job is null ? null : ToDto(job);
    }

    public async Task<GenerationJobDto?> GetActiveForUserAsync(string userEmail, CancellationToken ct = default)
    {
        var email = NormalizeEmail(userEmail);
        if (string.IsNullOrWhiteSpace(email))
            return null;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Active = still generating or failed mid-way (can resume generation).
        // Completed exams are resumed via stored exam progress, not via this endpoint.
        var job = await db.AiGenerationJobs.AsNoTracking()
            .Where(j => j.UserEmail == email
                && (j.Status == AiGenerationJobStatuses.Pending
                    || j.Status == AiGenerationJobStatuses.Running
                    || j.Status == AiGenerationJobStatuses.Failed))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return job is null ? null : ToDto(job);
    }

    public async Task CancelAsync(string jobId, string userEmail, CancellationToken ct = default)
    {
        var email = NormalizeEmail(userEmail);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.AiGenerationJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !string.Equals(job.UserEmail, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Generation job not found for this user.");

        if (job.Status is AiGenerationJobStatuses.Completed or AiGenerationJobStatuses.Failed or AiGenerationJobStatuses.Cancelled)
            return;

        if (_running.TryRemove(jobId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        job.Status = AiGenerationJobStatuses.Cancelled;
        job.Error = "Cancelled by user.";
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<SessionDto> CreateSessionFromJobAsync(
        string jobId,
        string userEmail,
        CancellationToken ct = default)
    {
        var email = NormalizeEmail(userEmail);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<QuizSessionService>();
        var bank = scope.ServiceProvider.GetRequiredService<QuestionBankService>();

        var job = await db.AiGenerationJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !string.Equals(job.UserEmail, email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Generation job not found for this user.");

        if (job.Status != AiGenerationJobStatuses.Completed)
            throw new InvalidOperationException($"Generation job is {job.Status}; wait until it completes.");

        var questions = DeserializeQuestions(job.QuestionsJson);
        if (questions.Count == 0)
            throw new InvalidOperationException("Generation job completed with no questions.");

        // Prefer an existing live session if this job already opened one.
        if (!string.IsNullOrWhiteSpace(job.SessionId))
        {
            var existing = sessions.Get(job.SessionId);
            if (existing is not null)
            {
                return new SessionDto(
                    existing.SessionId,
                    existing.Questions.Count,
                    existing.Questions.Select(q => q.Id).ToList(),
                    existing.SourceMode);
            }

            // Backend recycled — recreate with the same session id so client progress still matches.
            bank.AppendAiGenerated(questions);
            var restored = sessions.CreateWithId(job.SessionId, questions, "Ai", null);
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return new SessionDto(
                restored.SessionId,
                restored.Questions.Count,
                restored.Questions.Select(q => q.Id).ToList(),
                restored.SourceMode);
        }

        bank.AppendAiGenerated(questions);
        var session = sessions.Create(questions, "Ai", null);
        job.SessionId = session.SessionId;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new SessionDto(
            session.SessionId,
            session.Questions.Count,
            session.Questions.Select(q => q.Id).ToList(),
            session.SourceMode);
    }

    /// <summary>Re-queue incomplete jobs after process restart.</summary>
    public async Task ResumeInterruptedJobsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stale = await db.AiGenerationJobs
            .Where(j => j.Status == AiGenerationJobStatuses.Pending
                || j.Status == AiGenerationJobStatuses.Running)
            .OrderBy(j => j.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        foreach (var job in stale)
        {
            // Drop very old interrupted jobs instead of looping forever.
            if (job.CreatedAt < DateTime.UtcNow.AddHours(-6))
            {
                job.Status = AiGenerationJobStatuses.Failed;
                job.Error = "Generation timed out after server restart.";
                job.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            // Keep any partial questions and continue filling the shortfall.
            job.Status = AiGenerationJobStatuses.Pending;
            job.UpdatedAt = DateTime.UtcNow;
            QueueJob(job.Id);
        }

        await db.SaveChangesAsync(ct);
        if (stale.Count > 0)
            _logger.LogInformation("Resumed {Count} interrupted AI generation job(s).", stale.Count);
    }

    private void QueueJob(string jobId)
    {
        if (!_running.TryAdd(jobId, new CancellationTokenSource()))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await RunJobAsync(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AI generation job {JobId}", jobId);
            }
            finally
            {
                if (_running.TryRemove(jobId, out var cts))
                    cts.Dispose();
            }
        });
    }

    private async Task RunJobAsync(string jobId)
    {
        if (!_running.TryGetValue(jobId, out var cts))
            return;

        var ct = cts.Token;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<AiQuestionGeneratorService>();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<QuizSettings>>().Value;

        var job = await db.AiGenerationJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return;

        job.Status = AiGenerationJobStatuses.Running;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var existing = DeserializeQuestions(job.QuestionsJson);
            var need = Math.Max(0, job.RequestedCount - existing.Count);
            if (need == 0)
            {
                FinalizeCompleted(job, existing);
                await db.SaveChangesAsync(ct);
                return;
            }

            var learningUrls = string.IsNullOrWhiteSpace(job.LearningUrl)
                ? settings.GetLearningUrls()
                : [job.LearningUrl.Trim()];

            int[]? sectionIds = null;
            if (!string.IsNullOrWhiteSpace(job.SectionIdsJson))
            {
                try
                {
                    sectionIds = JsonSerializer.Deserialize<int[]>(job.SectionIdsJson);
                }
                catch (JsonException)
                {
                    sectionIds = null;
                }
            }

            var collected = new ConcurrentBag<Question>(existing);
            using var progressGate = new SemaphoreSlim(1, 1);

            var generated = await ai.GenerateAsync(
                need,
                learningUrls,
                sectionIds,
                async (batch, _, batchCt) =>
                {
                    foreach (var q in batch)
                        collected.Add(q);

                    await progressGate.WaitAsync(batchCt);
                    try
                    {
                        var combined = collected.Take(job.RequestedCount).ToList();
                        for (var i = 0; i < combined.Count; i++)
                            combined[i] = combined[i] with { Id = i + 1 };

                        job.QuestionsJson = JsonSerializer.Serialize(combined, JsonOptions);
                        job.CompletedCount = combined.Count;
                        job.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(batchCt);
                    }
                    finally
                    {
                        progressGate.Release();
                    }
                },
                ct);

            var all = existing.Concat(generated).Take(job.RequestedCount).ToList();
            for (var i = 0; i < all.Count; i++)
                all[i] = all[i] with { Id = i + 1 };

            if (all.Count == 0)
                throw new InvalidOperationException("AI returned no questions; try again.");

            FinalizeCompleted(job, all);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            job.Status = AiGenerationJobStatuses.Cancelled;
            job.Error = "Cancelled by user.";
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI generation job {JobId} failed", jobId);
            job.Status = AiGenerationJobStatuses.Failed;
            job.Error = SanitizeError(ex.Message);
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static void FinalizeCompleted(AiGenerationJobEntity job, List<Question> questions)
    {
        job.QuestionsJson = JsonSerializer.Serialize(questions, JsonOptions);
        job.CompletedCount = questions.Count;
        job.Status = AiGenerationJobStatuses.Completed;
        job.Error = null;
        job.UpdatedAt = DateTime.UtcNow;
    }

    private static List<Question> DeserializeQuestions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<Question>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static GenerationJobDto ToDto(AiGenerationJobEntity job) =>
        new(
            job.Id,
            job.UserEmail,
            job.Status,
            job.RequestedCount,
            job.CompletedCount,
            job.Error,
            job.SessionId,
            job.CreatedAt,
            job.UpdatedAt);

    private static string NormalizeEmail(string? email) =>
        (email ?? "").Trim().ToLowerInvariant();

    private static string SanitizeError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "AI generation failed. Please try again.";

        // Avoid dumping raw JSON parser paths to the UI when possible.
        if (message.Contains("invalid without a matching open", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Path: $", StringComparison.OrdinalIgnoreCase))
        {
            return "AI returned incomplete JSON for some questions. Progress was saved — tap Resume to continue, or start again.";
        }

        return message.Length > 400 ? message[..400] + "…" : message;
    }
}
