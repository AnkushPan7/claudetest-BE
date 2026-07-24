namespace ClaudeCertPractice.Api.Models;

public record StartGenerationJobRequest(
    string UserEmail,
    int? Count = null,
    int[]? SectionIds = null,
    string? LearningUrl = null,
    bool ForceNew = false);

public record GenerationJobDto(
    string JobId,
    string UserEmail,
    string Status,
    int RequestedCount,
    int CompletedCount,
    string? Error,
    string? SessionId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
