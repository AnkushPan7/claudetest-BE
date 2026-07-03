using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public class UserService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _dataPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public UserService(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(dataDir);
        _dataPath = Path.Combine(dataDir, "users.json");
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

        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadStoreAsync(ct);
            if (store.Users.TryGetValue(normalizedEmail, out var existing))
            {
                existing.Name = trimmedName;
            }
            else
            {
                store.Users[normalizedEmail] = new StoredUser
                {
                    Email = normalizedEmail,
                    Name = trimmedName,
                    CreatedAt = DateTime.UtcNow,
                    Results = [],
                };
            }

            await SaveStoreAsync(store, ct);
            var user = store.Users[normalizedEmail];
            return new UserDto(user.Email, user.Name, user.CreatedAt);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UserHistoryDto?> GetHistoryAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail)) return null;

        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadStoreAsync(ct);
            if (!store.Users.TryGetValue(normalizedEmail, out var user))
                return null;

            var results = user.Results
                .OrderByDescending(r => r.CompletedAt)
                .Select(ToEntry)
                .ToList();

            return new UserHistoryDto(
                new UserDto(user.Email, user.Name, user.CreatedAt),
                results);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ResultHistoryEntry?> SaveResultAsync(
        string email,
        SaveResultRequest request,
        CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail))
            throw new ArgumentException("A valid email address is required.");

        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadStoreAsync(ct);
            if (!store.Users.TryGetValue(normalizedEmail, out var user))
                return null;

            var entry = new StoredResult
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = request.SessionId,
                CompletedAt = DateTime.UtcNow,
                Total = request.Total,
                Answered = request.Answered,
                Correct = request.Correct,
                PercentCorrect = request.PercentCorrect,
                SourceMode = request.SourceMode,
                ScaledScore = request.ScaledScore,
                Questions = request.Questions?.Select(ToStoredQuestion).ToList() ?? [],
            };

            user.Results.Add(entry);
            await SaveStoreAsync(store, ct);
            return ToEntry(entry);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AdminOverviewDto> GetAllUsersOverviewAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadStoreAsync(ct);
            var users = store.Users.Values
                .Select(BuildAdminOverview)
                .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new AdminOverviewDto(users.Count, users);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static AdminUserOverview BuildAdminOverview(StoredUser user)
    {
        var results = user.Results
            .OrderByDescending(r => r.CompletedAt)
            .ToList();

        var scores = results
            .Select(GetResultScore)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        return new AdminUserOverview(
            user.Email,
            user.Name,
            user.CreatedAt,
            results.Count,
            scores.Count > 0 ? scores[0] : null,
            scores.Count > 0 ? scores.Max() : null,
            scores.Count > 0 ? (int)Math.Round(scores.Average()) : null,
            results.Count > 0 ? results[0].CompletedAt : null);
    }

    private static int? GetResultScore(StoredResult result)
    {
        if (result.ScaledScore.HasValue) return result.ScaledScore.Value;
        if (result.PercentCorrect > 0) return (int)Math.Round(result.PercentCorrect * 10);
        return null;
    }

    public async Task<ResultDetailDto?> GetResultDetailAsync(
        string email,
        string resultId,
        CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail) || string.IsNullOrWhiteSpace(resultId))
            return null;

        await _lock.WaitAsync(ct);
        try
        {
            var store = await LoadStoreAsync(ct);
            if (!store.Users.TryGetValue(normalizedEmail, out var user))
                return null;

            var result = user.Results.FirstOrDefault(r =>
                r.Id.Equals(resultId, StringComparison.OrdinalIgnoreCase));
            if (result is null) return null;

            var questions = result.Questions
                .OrderBy(q => q.Index)
                .Select(ToQuestionReviewItem)
                .ToList();

            return new ResultDetailDto(ToEntry(result), questions);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static ResultHistoryEntry ToEntry(StoredResult r) =>
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

    private static StoredQuestion ToStoredQuestion(QuestionReviewItem q) =>
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

    private static QuestionReviewItem ToQuestionReviewItem(StoredQuestion q) =>
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

    private async Task<UserStore> LoadStoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_dataPath))
            return new UserStore();

        await using var stream = File.OpenRead(_dataPath);
        return await JsonSerializer.DeserializeAsync<UserStore>(stream, JsonOptions, ct)
            ?? new UserStore();
    }

    private async Task SaveStoreAsync(UserStore store, CancellationToken ct)
    {
        await using var stream = File.Create(_dataPath);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptions, ct);
    }

    private sealed class UserStore
    {
        public Dictionary<string, StoredUser> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class StoredUser
    {
        public string Email { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<StoredResult> Results { get; set; } = [];
    }

    private sealed class StoredResult
    {
        public string Id { get; set; } = "";
        public string SessionId { get; set; } = "";
        public DateTime CompletedAt { get; set; }
        public int Total { get; set; }
        public int Answered { get; set; }
        public int Correct { get; set; }
        public double PercentCorrect { get; set; }
        public string SourceMode { get; set; } = "";
        public int? ScaledScore { get; set; }
        public List<StoredQuestion> Questions { get; set; } = [];
    }

    private sealed class StoredQuestion
    {
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
}
