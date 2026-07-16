using System.Collections.Concurrent;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public class QuizSession
{
    public required string SessionId { get; init; }
    public required List<Question> Questions { get; init; }
    public required string SourceMode { get; init; }
    public string? BankId { get; init; }
    public Dictionary<int, (string Selected, bool IsCorrect)> Answers { get; } = new();
}

public class QuizSessionService
{
    private readonly ConcurrentDictionary<string, QuizSession> _sessions = new();

    public QuizSession Create(IReadOnlyList<Question> questions, string sourceMode, string? bankId = null)
    {
        var session = new QuizSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Questions = questions.ToList(),
            SourceMode = sourceMode,
            BankId = bankId,
        };
        _sessions[session.SessionId] = session;
        return session;
    }

    public QuizSession? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s : null;
}
