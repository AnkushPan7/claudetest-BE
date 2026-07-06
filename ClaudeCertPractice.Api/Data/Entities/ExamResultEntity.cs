namespace ClaudeCertPractice.Api.Data.Entities;

public class ExamResultEntity
{
    public string Id { get; set; } = "";
    public int UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string SessionId { get; set; } = "";
    public DateTime CompletedAt { get; set; }
    public int Total { get; set; }
    public int Answered { get; set; }
    public int Correct { get; set; }
    public double PercentCorrect { get; set; }
    public string SourceMode { get; set; } = "";
    public int? ScaledScore { get; set; }
    public ICollection<ResultQuestionEntity> Questions { get; set; } = [];
}
