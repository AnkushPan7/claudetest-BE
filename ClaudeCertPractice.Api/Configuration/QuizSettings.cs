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

    public string AnthropicModel { get; set; } = "claude-haiku-4-5";

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

    /// <summary>
    /// Cap on learning-material excerpt sent to the model (smaller = faster TTFT).
    /// </summary>
    public int AiMaxSourceCharacters { get; set; } = 12_000;

    public int MaxQuestionsPerSession { get; set; } = 60;

    /// <summary>Questions requested per Anthropic call. Keep modest so JSON is not truncated.</summary>
    public int AiBatchSize { get; set; } = 3;

    /// <summary>How many Anthropic calls to run at once (wall-clock speed for 60-question exams).</summary>
    public int AiMaxParallelBatches { get; set; } = 12;

    /// <summary>Output token budget per batch call.</summary>
    public int AiMaxTokens { get; set; } = 12_288;

    /// <summary>Retries per batch when JSON is incomplete or empty.</summary>
    public int AiBatchRetries { get; set; } = 2;
}
