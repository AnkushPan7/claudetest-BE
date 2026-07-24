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
        return CreateWithId(Guid.NewGuid().ToString("N"), questions, sourceMode, bankId);
    }

    public QuizSession CreateWithId(
        string sessionId,
        IReadOnlyList<Question> questions,
        string sourceMode,
        string? bankId = null)
    {
        var session = new QuizSession
        {
            SessionId = sessionId,
            Questions = questions.ToList(),
            SourceMode = sourceMode,
            BankId = bankId,
        };
        _sessions[session.SessionId] = session;
        return session;
    }

    public QuizSession? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var s) ? s : null;

    public void ApplyAnswers(QuizSession session, IReadOnlyDictionary<int, string>? answers)
    {
        if (answers is null || answers.Count == 0) return;

        foreach (var (index, selected) in answers)
        {
            if (index < 0 || index >= session.Questions.Count) continue;
            var letter = selected.Trim().ToUpperInvariant();
            if (letter is not ("A" or "B" or "C" or "D")) continue;

            var q = session.Questions[index];
            var isCorrect = string.Equals(letter, q.CorrectAnswer, StringComparison.OrdinalIgnoreCase);
            session.Answers[index] = (letter, isCorrect);
        }
    }
}
