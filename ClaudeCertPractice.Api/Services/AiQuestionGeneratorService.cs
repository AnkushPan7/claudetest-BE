using System.Text;
using System.Text.Json;
using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Models;
using Microsoft.Extensions.Options;

namespace ClaudeCertPractice.Api.Services;

public class AiQuestionGeneratorService
{
    private readonly HttpClient _http;
    private readonly LearningContentService _content;
    private readonly ExamGuideService _examGuide;
    private readonly QuizSettings _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<AiQuestionGeneratorService> _logger;

    public AiQuestionGeneratorService(
        HttpClient http,
        LearningContentService content,
        ExamGuideService examGuide,
        IOptions<QuizSettings> settings,
        IConfiguration config,
        ILogger<AiQuestionGeneratorService> logger)
    {
        _http = http;
        _content = content;
        _examGuide = examGuide;
        _settings = settings.Value;
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured =>
        AnthropicApiKeyResolver.IsConfigured(_config);

    public async Task<List<Question>> GenerateAsync(
        int count,
        IReadOnlyList<string> learningUrls,
        int[]? sectionIds,
        CancellationToken ct = default)
    {
        var apiKey = GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "AI question generation requires ANTHROPIC_API_KEY or AnthropicApiKey in environment (or .env), or Quiz:AnthropicApiKey in configuration.");

        var urls = learningUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
            throw new ArgumentException("At least one learning URL is required.");

        var sourceText = await _content.FetchCombinedTextAsync(urls, ct);
        var sectionHint = _examGuide.BuildAiDomainHint(sectionIds);
        var urlsLine = string.Join("\n", urls.Select(u => $"- {u}"));

        var all = new List<Question>();
        var remaining = count;
        var batchNum = 0;

        while (remaining > 0)
        {
            var batchSize = Math.Min(remaining, _settings.AiBatchSize);
            batchNum++;
            var existingTitles = all.Select(q => q.Title).ToList();
            var batch = await GenerateBatchAsync(
                apiKey, sourceText, urlsLine, batchSize, sectionHint, batchNum, all.Count, existingTitles, ct);
            all.AddRange(batch);
            remaining -= batch.Count;
            if (batch.Count == 0)
                throw new InvalidOperationException("AI returned no questions; try again or use Json mode.");
        }

        for (var i = 0; i < all.Count; i++)
            all[i] = ShuffleCorrectAnswerPosition(all[i] with { Id = i + 1 });

        return all;
    }

    /// <summary>
    /// Randomly remaps A–D so the correct choice is not stuck on one letter
    /// (models often copy example "correctAnswer": "B" from the prompt).
    /// </summary>
    private static Question ShuffleCorrectAnswerPosition(Question q)
    {
        var letters = new[] { "A", "B", "C", "D" };
        var texts = letters
            .Select(l => q.Options.TryGetValue(l, out var t) ? t : "")
            .ToList();

        if (texts.Count != 4 || texts.Any(string.IsNullOrWhiteSpace))
            return q;

        var oldCorrect = (q.CorrectAnswer ?? "A").Trim().ToUpperInvariant();
        if (oldCorrect.Length == 0 || !"ABCD".Contains(oldCorrect[0]))
            oldCorrect = "A";
        else
            oldCorrect = oldCorrect[..1];

        if (!q.Options.ContainsKey(oldCorrect))
            oldCorrect = letters.FirstOrDefault(l => q.Options.ContainsKey(l)) ?? "A";

        // Fisher–Yates shuffle of option slots
        var order = new[] { 0, 1, 2, 3 };
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var newOptions = new Dictionary<string, string>();
        var oldToNew = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var newIdx = 0; newIdx < 4; newIdx++)
        {
            var oldIdx = order[newIdx];
            newOptions[letters[newIdx]] = texts[oldIdx];
            oldToNew[letters[oldIdx]] = letters[newIdx];
        }

        var newCorrect = oldToNew[oldCorrect];

        var explanation = q.Explanation ?? "";
        // Rewrite "Why X:" prefix to the new correct letter when present
        explanation = System.Text.RegularExpressions.Regex.Replace(
            explanation,
            $@"^Why\s+{oldCorrect}\s*:",
            $"Why {newCorrect}:",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Dictionary<string, string>? newOptionExplanations = null;
        if (q.OptionExplanations is { Count: > 0 })
        {
            newOptionExplanations = new Dictionary<string, string>();
            foreach (var (oldLetter, text) in q.OptionExplanations)
            {
                var key = oldLetter.Trim().ToUpperInvariant();
                if (oldToNew.TryGetValue(key, out var mapped))
                    newOptionExplanations[mapped] = text;
            }
        }

        return q with
        {
            Options = newOptions,
            CorrectAnswer = newCorrect,
            Explanation = explanation,
            OptionExplanations = newOptionExplanations,
        };
    }

    private async Task<List<Question>> GenerateBatchAsync(
        string apiKey,
        string sourceText,
        string learningUrlsLine,
        int count,
        string sectionHint,
        int batchNum,
        int alreadyGenerated,
        IReadOnlyList<string> existingTitles,
        CancellationToken ct)
    {
        var avoid = existingTitles.Count > 0
            ? string.Join("; ", existingTitles.Take(30))
            : "(none yet)";

        var batchNote = alreadyGenerated > 0
            ? $"- Must differ from prior questions in this session (batch {batchNum})"
            : "";

        var prompt = $$"""
            You are building practice exam questions for the Anthropic Claude Certified Architect Foundations (CCA-F) exam.

            Learning material URLs (text extracted from each page is below):
            {{learningUrlsLine}}

            Prioritize concepts from the Claude Partner Network learning path (agent skills, Claude API, MCP, Claude Code)
            when that Skilljar catalog page is included.

            {{sectionHint}}

            Generate exactly {{count}} NEW multiple-choice questions that:
            - Test concepts from the learning material (not trivia about the page itself)
            - Are scenario-based like a professional certification exam
            - Have exactly 4 options labeled A, B, C, D with one correct answer
            - Include a short title and a full scenario-based question stem (2-4 sentences setting up the situation before asking the question; do not abbreviate)
            - Include an explanation starting with "Why X:" for the correct letter
            - Also include brief reasons why each incorrect option is wrong
            - CRITICAL: Vary the correct answer letter across the set. Do NOT default to B. Across the {{count}} questions, distribute correctAnswer roughly evenly among A, B, C, and D (about {{count / 4}} each when possible). Never make every question have the same correct letter.
            - Do not repeat topics from these already-generated titles: [{{avoid}}]
            {{batchNote}}

            Return ONLY a JSON array (no markdown fences) with objects like this example
            (correctAnswer may be A, B, C, or D — this sample uses C only as an illustration):
            {
              "sectionId": 1,
              "title": "short topic",
              "text": "question stem",
              "options": { "A": "...", "B": "...", "C": "...", "D": "..." },
              "correctAnswer": "C",
              "explanation": "Why C: ...",
              "optionExplanations": {
                "A": "Why A is wrong: ...",
                "B": "Why B is wrong: ...",
                "D": "Why D is wrong: ..."
              }
            }

            {{_examGuide.AiSectionIdLine()}}

            Learning material excerpt:
            {{sourceText}}
            """;

        var body = new
        {
            model = _settings.AnthropicModel,
            max_tokens = 8192,
            messages = new[] { new { role = "user", content = prompt } },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Anthropic API error {Status}: {Body}", response.StatusCode, responseJson);
            throw new InvalidOperationException($"Anthropic API error ({(int)response.StatusCode}). Check API key and model.");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "[]";

        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var start = text.IndexOf('\n') + 1;
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) text = text[start..end].Trim();
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var raw = JsonSerializer.Deserialize<List<GeneratedQuestionDto>>(text, options) ?? [];

        return raw.Select((q, i) => new Question(
            Id: alreadyGenerated + i + 1,
            SectionId: q.SectionId is >= 1 and <= 3 ? q.SectionId : 1,
            Title: q.Title ?? "Practice question",
            Text: q.Text ?? "",
            Options: NormalizeOptions(q.Options),
            CorrectAnswer: (q.CorrectAnswer ?? "A").Trim().ToUpperInvariant()[..1],
            Explanation: q.Explanation ?? "",
            OptionExplanations: NormalizeOptions(q.OptionExplanations))).ToList();
    }

    private string? GetApiKey() => AnthropicApiKeyResolver.Resolve(_config);

    private static Dictionary<string, string> NormalizeOptions(Dictionary<string, string>? options)
    {
        var result = new Dictionary<string, string>();
        if (options is null) return result;
        foreach (var key in new[] { "A", "B", "C", "D" })
            if (options.TryGetValue(key, out var v) || options.TryGetValue(key.ToLowerInvariant(), out v))
                result[key] = v;
        return result;
    }

    private sealed class GeneratedQuestionDto
    {
        public int SectionId { get; set; }
        public string? Title { get; set; }
        public string? Text { get; set; }
        public Dictionary<string, string>? Options { get; set; }
        public string? CorrectAnswer { get; set; }
        public string? Explanation { get; set; }
        public Dictionary<string, string>? OptionExplanations { get; set; }
    }
}
