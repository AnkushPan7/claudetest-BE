namespace ClaudeCertPractice.Api.Configuration;

public class QuizSettings
{
    public const string SectionName = "Quiz";

    /// <summary>Json = fixed bank from questions.json; Ai = generate per session via Claude.</summary>
    public string QuestionSource { get; set; } = "Json";

    public string LearningUrl { get; set; } =
        "https://anthropic.skilljar.com/page/claude-partner-network-learning-path";

    /// <summary>When set, AI mode fetches and merges all URLs (unless the client overrides with a single URL).</summary>
    public string[] LearningUrls { get; set; } =
    [
        "https://anthropic.skilljar.com/page/claude-partner-network-learning-path",
        "https://docs.anthropic.com/en/docs/build-with-claude/overview",
    ];

    public string AnthropicModel { get; set; } = "claude-sonnet-4-20250514";

    public IReadOnlyList<string> GetLearningUrls()
    {
        var urls = LearningUrls is { Length: > 0 } ? LearningUrls : [LearningUrl];
        return urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public int MaxSourceCharacters { get; set; } = 80_000;

    public int MaxQuestionsPerSession { get; set; } = 60;

    public int AiBatchSize { get; set; } = 5;
}
