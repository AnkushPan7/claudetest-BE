namespace ClaudeCertPractice.Api.Data.Entities;

public class AiGenerationJobEntity
{
    public string Id { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string Status { get; set; } = AiGenerationJobStatuses.Pending;
    public int RequestedCount { get; set; }
    public int CompletedCount { get; set; }
    public string? SectionIdsJson { get; set; }
    public string? LearningUrl { get; set; }
    public string? QuestionsJson { get; set; }
    public string? Error { get; set; }
    public string? SessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class AiGenerationJobStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}
