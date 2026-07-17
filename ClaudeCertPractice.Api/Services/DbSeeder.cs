using System.Text.Json;
using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Data;
using ClaudeCertPractice.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaudeCertPractice.Api.Services;

public static class DbSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task InitializeAsync(IServiceProvider services, IWebHostEnvironment env)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<IOptions<AuthSettings>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DbSeeder");

        await db.Database.MigrateAsync();
        await EnsureResultQuestionOptionExplanationsColumnAsync(db, logger);

        await EnsureAdminUserAsync(db, auth, logger);
        await ImportLegacyUsersJsonIfNeededAsync(db, env, logger);
    }

    /// <summary>
    /// Guarantees OptionExplanations exists even if an earlier empty migration was already applied.
    /// </summary>
    private static async Task EnsureResultQuestionOptionExplanationsColumnAsync(
        AppDbContext db,
        ILogger logger)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "ResultQuestions"
                ADD COLUMN IF NOT EXISTS "OptionExplanations" jsonb NULL;
                """);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not ensure OptionExplanations column on ResultQuestions.");
        }
    }

    private static async Task EnsureAdminUserAsync(
        AppDbContext db,
        AuthSettings auth,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(auth.AdminPassword))
        {
            logger.LogWarning("Auth:AdminPassword is not set; admin user was not seeded.");
            return;
        }

        var adminEmail = auth.AdminEmail.Trim().ToLowerInvariant();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
        if (existing is null)
        {
            db.Users.Add(new UserEntity
            {
                Email = adminEmail,
                Name = "Admin",
                Role = UserRoles.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(auth.AdminPassword),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded admin user {Email}", adminEmail);
            return;
        }

        if (existing.Role != UserRoles.Admin)
        {
            existing.Role = UserRoles.Admin;
            existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(auth.AdminPassword);
            await db.SaveChangesAsync();
            logger.LogInformation("Promoted {Email} to admin", adminEmail);
        }
    }

    private static async Task ImportLegacyUsersJsonIfNeededAsync(
        AppDbContext db,
        IWebHostEnvironment env,
        ILogger logger)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRoles.User))
            return;

        var jsonPath = Path.Combine(env.ContentRootPath, "Data", "users.json");
        if (!File.Exists(jsonPath))
            return;

        await using var stream = File.OpenRead(jsonPath);
        var legacy = await JsonSerializer.DeserializeAsync<LegacyUserStore>(stream, JsonOptions);
        if (legacy?.Users is null || legacy.Users.Count == 0)
            return;

        var imported = 0;
        foreach (var (email, legacyUser) in legacy.Users)
        {
            var normalizedEmail = email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == normalizedEmail))
                continue;

            var user = new UserEntity
            {
                Email = normalizedEmail,
                Name = legacyUser.Name,
                Role = UserRoles.User,
                CreatedAt = legacyUser.CreatedAt,
            };

            foreach (var legacyResult in legacyUser.Results)
            {
                var result = new ExamResultEntity
                {
                    Id = string.IsNullOrWhiteSpace(legacyResult.Id)
                        ? Guid.NewGuid().ToString("N")
                        : legacyResult.Id,
                    SessionId = legacyResult.SessionId,
                    CompletedAt = legacyResult.CompletedAt,
                    Total = legacyResult.Total,
                    Answered = legacyResult.Answered,
                    Correct = legacyResult.Correct,
                    PercentCorrect = legacyResult.PercentCorrect,
                    SourceMode = legacyResult.SourceMode,
                    ScaledScore = legacyResult.ScaledScore,
                };

                foreach (var legacyQuestion in legacyResult.Questions)
                {
                    result.Questions.Add(new ResultQuestionEntity
                    {
                        Index = legacyQuestion.Index,
                        SectionName = legacyQuestion.SectionName,
                        Title = legacyQuestion.Title,
                        Text = legacyQuestion.Text,
                        Options = legacyQuestion.Options,
                        SelectedAnswer = legacyQuestion.SelectedAnswer,
                        CorrectAnswer = legacyQuestion.CorrectAnswer,
                        IsCorrect = legacyQuestion.IsCorrect,
                        Explanation = legacyQuestion.Explanation,
                        Answered = legacyQuestion.Answered,
                    });
                }

                user.Results.Add(result);
            }

            db.Users.Add(user);
            imported++;
        }

        if (imported > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Imported {Count} users from legacy users.json", imported);
        }
    }

    private sealed class LegacyUserStore
    {
        public Dictionary<string, LegacyUser> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LegacyUser
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<LegacyResult> Results { get; set; } = [];
    }

    private sealed class LegacyResult
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
        public List<LegacyQuestion> Questions { get; set; } = [];
    }

    private sealed class LegacyQuestion
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
