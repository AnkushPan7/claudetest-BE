namespace ClaudeCertPractice.Api.Models;

public record ExamMetadata(
    string ExamTitle,
    int TotalQuestions,
    int BankQuestionCount,
    IReadOnlyList<SectionInfo> Sections,
    string QuestionSource,
    bool AiGenerationAvailable,
    string? LearningUrl,
    IReadOnlyList<string>? LearningUrls,
    int MaxQuestionsPerSession,
    int PassingScore = 720,
    int ScoreMin = 100,
    int ScoreMax = 1000,
    int PracticePassingScore = 900,
    string? ResponseFormat = null,
    string? ExamScenariosNote = null,
    IReadOnlyList<ExamScenarioInfo>? Scenarios = null);

public record ExamScenarioInfo(int Id, string Name, IReadOnlyList<string> PrimaryDomains);

public record ExamGuide(
    string ExamTitle,
    int QuestionCount,
    int PassingScore,
    int ScoreMin,
    int ScoreMax,
    int PracticePassingScore,
    string ResponseFormat,
    string ExamScenariosNote,
    List<DomainInfo> Domains,
    List<ExamScenarioInfo> Scenarios,
    Dictionary<string, int> LegacySectionToDomain);

public record DomainInfo(int Id, string Name, int WeightPercent);

public record CreateSessionRequest(
    int? Count = null,
    int[]? SectionIds = null,
    string? Source = null,
    string? LearningUrl = null);

public record SessionDto(
    string SessionId,
    int TotalQuestions,
    IReadOnlyList<int> QuestionIds,
    string SourceMode);

public record SectionInfo(int Id, string Name, string Range);

public record Question(
    int Id,
    int SectionId,
    string Title,
    string Text,
    Dictionary<string, string> Options,
    string CorrectAnswer,
    string Explanation);

public record QuestionPublicDto(
    int Id,
    int SectionId,
    string SectionName,
    string Title,
    string Text,
    Dictionary<string, string> Options,
    int Index,
    int Total);

public record AnswerRequest(string SelectedAnswer);

public record AnswerSubmitDto(
    int Index,
    int Total,
    string SelectedAnswer,
    string CorrectAnswer,
    bool IsCorrect,
    string Explanation);

public record SessionSummaryDto(
    string SessionId,
    int Total,
    int Answered,
    int Correct,
    double PercentCorrect);

public record SessionReviewDto(
    string SessionId,
    IReadOnlyList<QuestionReviewItem> Questions);

public record QuestionReviewItem(
    int Index,
    string SectionName,
    string Title,
    string Text,
    Dictionary<string, string> Options,
    string? SelectedAnswer,
    string CorrectAnswer,
    bool IsCorrect,
    string Explanation,
    bool Answered);
