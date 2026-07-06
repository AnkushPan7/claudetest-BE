namespace ClaudeCertPractice.Api.Data.Entities;

public class ResultQuestionEntity
{
    public int Id { get; set; }
    public string ExamResultId { get; set; } = "";
    public ExamResultEntity ExamResult { get; set; } = null!;
    public int Index { get; set; }
    public string SectionName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new();
    public string? SelectedAnswer { get; set; }
    public string CorrectAnswer { get; set; } = "";
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = "";
    public bool Answered { get; set; }
}
