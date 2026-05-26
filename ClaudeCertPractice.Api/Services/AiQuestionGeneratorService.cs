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
            all[i] = all[i] with { Id = i + 1 };

        return all;
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
            - Include a short title, clear question text, and explanation starting with "Why X:" for the correct letter
            - Do not repeat topics from these already-generated titles: [{{avoid}}]
            {{batchNote}}

            Return ONLY a JSON array (no markdown fences) with objects:
            {
              "sectionId": 1,
              "title": "short topic",
              "text": "question stem",
              "options": { "A": "...", "B": "...", "C": "...", "D": "..." },
              "correctAnswer": "B",
              "explanation": "Why B: ..."
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
            Explanation: q.Explanation ?? "")).ToList();
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
    }
}
