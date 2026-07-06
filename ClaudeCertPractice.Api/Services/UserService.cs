using System.Text.RegularExpressions;
using ClaudeCertPractice.Api.Data;
using ClaudeCertPractice.Api.Data.Entities;
using ClaudeCertPractice.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCertPractice.Api.Services;

public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return Regex.IsMatch(
            email.Trim(),
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.IgnoreCase);
    }

    public async Task<UserDto> RegisterAsync(string email, string name, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var trimmedName = (name ?? "").Trim();
        if (!IsValidEmail(normalizedEmail))
            throw new ArgumentException("A valid email address is required.");
        if (trimmedName.Length < 2)
            throw new ArgumentException("Name must be at least 2 characters.");

        var existing = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (existing is not null)
        {
            if (existing.Role == UserRoles.Admin)
                throw new ArgumentException("This email is reserved for admin use.");

            existing.Name = trimmedName;
            await _db.SaveChangesAsync(ct);
            return new UserDto(existing.Email, existing.Name, existing.CreatedAt);
        }

        var user = new UserEntity
        {
            Email = normalizedEmail,
            Name = trimmedName,
            Role = UserRoles.User,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return new UserDto(user.Email, user.Name, user.CreatedAt);
    }

    public async Task<UserHistoryDto?> GetHistoryAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail)) return null;

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Results)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.Role == UserRoles.User, ct);

        if (user is null) return null;

        var results = user.Results
            .OrderByDescending(r => r.CompletedAt)
            .Select(ToEntry)
            .ToList();

        return new UserHistoryDto(
            new UserDto(user.Email, user.Name, user.CreatedAt),
            results);
    }

    public async Task<ResultHistoryEntry?> SaveResultAsync(
        string email,
        SaveResultRequest request,
        CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail))
            throw new ArgumentException("A valid email address is required.");

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.Role == UserRoles.User, ct);
        if (user is null) return null;

        var entry = new ExamResultEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = user.Id,
            SessionId = request.SessionId,
            CompletedAt = DateTime.UtcNow,
            Total = request.Total,
            Answered = request.Answered,
            Correct = request.Correct,
            PercentCorrect = request.PercentCorrect,
            SourceMode = request.SourceMode,
            ScaledScore = request.ScaledScore,
        };

        if (request.Questions is not null)
        {
            foreach (var question in request.Questions)
            {
                entry.Questions.Add(ToStoredQuestion(question));
            }
        }

        _db.ExamResults.Add(entry);
        await _db.SaveChangesAsync(ct);
        return ToEntry(entry);
    }

    public async Task<ResultDetailDto?> GetResultDetailAsync(
        string email,
        string resultId,
        CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail) || string.IsNullOrWhiteSpace(resultId))
            return null;

        var result = await _db.ExamResults
            .AsNoTracking()
            .Include(r => r.Questions)
            .Include(r => r.User)
            .FirstOrDefaultAsync(
                r => r.Id == resultId && r.User.Email == normalizedEmail && r.User.Role == UserRoles.User,
                ct);

        if (result is null) return null;

        var questions = result.Questions
            .OrderBy(q => q.Index)
            .Select(ToQuestionReviewItem)
            .ToList();

        return new ResultDetailDto(ToEntry(result), questions);
    }

    public async Task<IReadOnlyList<AdminUserSummary>> GetAllUsersAsync(CancellationToken ct = default)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRoles.User)
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new AdminUserSummary(
                u.Email,
                u.Name,
                u.CreatedAt,
                u.Results.Count))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdminResultSummary>> GetAllResultsAsync(CancellationToken ct = default)
    {
        return await _db.ExamResults
            .AsNoTracking()
            .Where(r => r.User.Role == UserRoles.User)
            .OrderByDescending(r => r.CompletedAt)
            .Select(r => new AdminResultSummary(
                r.Id,
                r.User.Email,
                r.User.Name,
                r.SessionId,
                r.CompletedAt,
                r.Total,
                r.Answered,
                r.Correct,
                r.PercentCorrect,
                r.SourceMode,
                r.ScaledScore,
                r.Questions.Count > 0))
            .ToListAsync(ct);
    }

    public async Task<ResultDetailDto?> GetAdminResultDetailAsync(string resultId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resultId)) return null;

        var result = await _db.ExamResults
            .AsNoTracking()
            .Include(r => r.Questions)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == resultId && r.User.Role == UserRoles.User, ct);

        if (result is null) return null;

        var questions = result.Questions
            .OrderBy(q => q.Index)
            .Select(ToQuestionReviewItem)
            .ToList();

        return new ResultDetailDto(ToEntry(result), questions);
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static ResultHistoryEntry ToEntry(ExamResultEntity r) =>
        new(
            r.Id,
            r.SessionId,
            r.CompletedAt,
            r.Total,
            r.Answered,
            r.Correct,
            r.PercentCorrect,
            r.SourceMode,
            r.ScaledScore,
            r.Questions.Count > 0);

    private static ResultQuestionEntity ToStoredQuestion(QuestionReviewItem q) =>
        new()
        {
            Index = q.Index,
            SectionName = q.SectionName,
            Title = q.Title,
            Text = q.Text,
            Options = q.Options,
            SelectedAnswer = q.SelectedAnswer,
            CorrectAnswer = q.CorrectAnswer,
            IsCorrect = q.IsCorrect,
            Explanation = q.Explanation,
            Answered = q.Answered,
        };

    private static QuestionReviewItem ToQuestionReviewItem(ResultQuestionEntity q) =>
        new(
            q.Index,
            q.SectionName,
            q.Title,
            q.Text,
            q.Options,
            q.SelectedAnswer,
            q.CorrectAnswer,
            q.IsCorrect,
            q.Explanation,
            q.Answered);
}
