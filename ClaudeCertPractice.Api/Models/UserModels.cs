namespace ClaudeCertPractice.Api.Models;

public record RegisterUserRequest(string Email, string Name);

public record UserDto(string Email, string Name, DateTime CreatedAt);

public record SaveResultRequest(
    string SessionId,
    int Total,
    int Answered,
    int Correct,
    double PercentCorrect,
    string SourceMode,
    int? ScaledScore,
    IReadOnlyList<QuestionReviewItem>? Questions = null);

public record ResultHistoryEntry(
    string Id,
    string SessionId,
    DateTime CompletedAt,
    int Total,
    int Answered,
    int Correct,
    double PercentCorrect,
    string SourceMode,
    int? ScaledScore,
    bool HasDetail = false);

public record ResultDetailDto(
    ResultHistoryEntry Summary,
    IReadOnlyList<QuestionReviewItem> Questions);

public record UserHistoryDto(
    UserDto User,
    IReadOnlyList<ResultHistoryEntry> Results);

public record AdminLoginRequest(string Email, string Password);

public record AdminLoginResponse(string Token, DateTime ExpiresAt);

public record AdminUserSummary(
    string Email,
    string Name,
    DateTime CreatedAt,
    int ResultCount);

public record AdminResultSummary(
    string Id,
    string UserEmail,
    string UserName,
    string SessionId,
    DateTime CompletedAt,
    int Total,
    int Answered,
    int Correct,
    double PercentCorrect,
    string SourceMode,
    int? ScaledScore,
    bool HasDetail);
