using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Models;
using Microsoft.Extensions.Options;

namespace ClaudeCertPractice.Api.Services;

public class AiQuestionGeneratorService
{
    private static readonly Regex LetterBoundPrefix = new(
        @"^(?:Why\s+[A-D](?:\s+is\s+(?:wrong|incorrect|right|correct))?\s*:|Option\s+[A-D]\s*[:.]?)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmbeddedWhyLetter = new(
        @"\bWhy\s+[A-D](?:\s+is\s+(?:wrong|incorrect|right|correct))?\s*:\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly LearningContentService _content;
    private readonly ExamGuideService _examGuide;
    private readonly QuestionBankService _banks;
    private readonly QuizSettings _settings;
    private readonly IConfiguration _config;
    private readonly ILogger<AiQuestionGeneratorService> _logger;

    public AiQuestionGeneratorService(
        HttpClient http,
        LearningContentService content,
        ExamGuideService examGuide,
        QuestionBankService banks,
        IOptions<QuizSettings> settings,
        IConfiguration config,
        ILogger<AiQuestionGeneratorService> logger)
    {
        _http = http;
        _content = content;
        _examGuide = examGuide;
        _banks = banks;
        _settings = settings.Value;
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured =>
        AnthropicApiKeyResolver.IsConfigured(_config);

    public Task<List<Question>> GenerateAsync(
        int count,
        IReadOnlyList<string> learningUrls,
        int[]? sectionIds,
        CancellationToken ct = default) =>
        GenerateAsync(count, learningUrls, sectionIds, onBatchComplete: null, ct);

    /// <param name="onBatchComplete">
    /// Called after each successful batch with that batch's questions and the running total count.
    /// Used by resume-capable generation jobs to persist progress.
    /// </param>
    public async Task<List<Question>> GenerateAsync(
        int count,
        IReadOnlyList<string> learningUrls,
        int[]? sectionIds,
        Func<IReadOnlyList<Question>, int, CancellationToken, Task>? onBatchComplete,
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
        var aiSourceCap = Math.Max(4_000, _settings.AiMaxSourceCharacters);
        if (sourceText.Length > aiSourceCap)
            sourceText = sourceText[..aiSourceCap];

        var sectionHint = _examGuide.BuildAiDomainHint(sectionIds);
        var urlsLine = string.Join("\n", urls.Select(u => $"- {u}"));
        var exemplars = BuildStyleExemplars(sectionIds);
        var systemPrompt = BuildCachedSystemPrompt(urlsLine, sectionHint, exemplars, sourceText);

        // Smaller batches finish faster and are less likely to truncate mid-JSON (optionExplanations.D).
        var batchSize = Math.Clamp(_settings.AiBatchSize, 1, 8);
        var parallel = Math.Clamp(_settings.AiMaxParallelBatches, 1, 16);
        var retries = Math.Clamp(_settings.AiBatchRetries, 0, 4);

        var batchCounts = new List<int>();
        var remaining = count;
        while (remaining > 0)
        {
            var size = Math.Min(remaining, batchSize);
            batchCounts.Add(size);
            remaining -= size;
        }

        var bag = new ConcurrentBag<(int BatchIndex, List<Question> Questions)>();
        var completedTotal = 0;
        using var gate = new SemaphoreSlim(parallel);

        var tasks = batchCounts.Select(async (wanted, batchIndex) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                List<Question> best = [];
                Exception? lastError = null;

                for (var attempt = 0; attempt <= retries; attempt++)
                {
                    try
                    {
                        var need = wanted - best.Count;
                        if (need <= 0) break;

                        var batch = await GenerateBatchAsync(
                            apiKey,
                            systemPrompt,
                            need,
                            batchIndex + 1,
                            batchCounts.Count,
                            attempt,
                            ct);

                        if (batch.Count > 0)
                            best.AddRange(batch);

                        if (best.Count >= wanted)
                            break;

                        _logger.LogWarning(
                            "AI batch {Batch}/{Total} attempt {Attempt} returned {Got}/{Wanted}; retrying shortfall.",
                            batchIndex + 1, batchCounts.Count, attempt + 1, best.Count, wanted);
                    }
                    catch (Exception ex) when (attempt < retries)
                    {
                        lastError = ex;
                        _logger.LogWarning(ex,
                            "AI batch {Batch}/{Total} attempt {Attempt} failed; retrying.",
                            batchIndex + 1, batchCounts.Count, attempt + 1);
                    }
                }

                if (best.Count == 0)
                {
                    throw lastError ?? new InvalidOperationException(
                        $"AI returned no questions for batch {batchIndex + 1}; try again.");
                }

                var finalized = best.Take(wanted).Select(FinalizeQuestion).ToList();
                bag.Add((batchIndex, finalized));

                if (onBatchComplete is not null)
                {
                    var totalSoFar = Interlocked.Add(ref completedTotal, finalized.Count);
                    await onBatchComplete(finalized, totalSoFar, ct);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        var all = bag
            .OrderBy(x => x.BatchIndex)
            .SelectMany(x => x.Questions)
            .Take(count)
            .ToList();

        if (all.Count == 0)
            throw new InvalidOperationException("AI returned no questions; try again or use Json mode.");

        if (all.Count < count)
        {
            _logger.LogWarning(
                "AI generation produced {Got}/{Wanted} questions after parallel batches.",
                all.Count, count);
        }

        for (var i = 0; i < all.Count; i++)
            all[i] = all[i] with { Id = i + 1 };

        return all;
    }

    private string BuildCachedSystemPrompt(
        string learningUrlsLine,
        string sectionHint,
        string styleExemplars,
        string sourceText)
    {
        return $$"""
            You are writing CCA-F (Claude Certified Architect – Foundations) practice questions
            that match the difficulty and style of the REAL certification exam question bank.

            Learning material URLs (excerpted below):
            {{learningUrlsLine}}

            {{sectionHint}}

            === STYLE EXEMPLARS FROM THE REAL QUESTION BANK (match this quality) ===
            {{styleExemplars}}

            Study the exemplars carefully. Real exam items share these traits:
            - Long scenario stems with concrete metrics, failure modes, constraints, or trade-offs.
            - Ask for the MOST EFFECTIVE / BEST / MOST LIKELY approach — not a memorized definition.
            - All four options are plausible; wrong answers are near-misses.
            - Explanations are direct technical prose. Never use letter labels like "Why A is wrong".

            Rules for every question you generate:
            - Exactly 4 options labeled A, B, C, D with ONE correct answer
            - Short title + full scenario stem (2–4 sentences with concrete details)
            - "explanation": 1–2 sentences why the correct option is right (NO letter prefix)
            - "optionExplanations" for ALL of A, B, C, D: ONE short sentence each (no quotes inside strings)
            - CRITICAL: Vary correctAnswer across A/B/C/D; do not default to B
            - Return ONLY a complete valid JSON array (no markdown fences, no commentary, no trailing text)

            Example object shape:
            {
              "sectionId": 1,
              "title": "short topic",
              "text": "full scenario stem…",
              "options": { "A": "...", "B": "...", "C": "...", "D": "..." },
              "correctAnswer": "C",
              "explanation": "Direct reason the correct approach works.",
              "optionExplanations": {
                "A": "Why this approach fails or is incomplete.",
                "B": "Why this approach fails or is incomplete.",
                "C": "Why this is the best approach.",
                "D": "Why this approach fails or is incomplete."
              }
            }

            {{_examGuide.AiSectionIdLine()}}

            Learning material excerpt:
            {{sourceText}}
            """;
    }

    /// <summary>
    /// Strip letter-bound explanation prefixes, then shuffle A–D so the correct
    /// choice is not stuck on one letter. Explanations travel with option *content*,
    /// never with the original letter label.
    /// </summary>
    private static Question FinalizeQuestion(Question q)
    {
        q = SanitizeExplanationLetters(q);
        return ShuffleCorrectAnswerPosition(q);
    }

    private static Question SanitizeExplanationLetters(Question q)
    {
        var explanation = CleanExplanationText(q.Explanation);

        Dictionary<string, string>? optionExplanations = null;
        if (q.OptionExplanations is { Count: > 0 })
        {
            optionExplanations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (letter, text) in q.OptionExplanations)
            {
                var key = letter.Trim().ToUpperInvariant();
                if (key is not ("A" or "B" or "C" or "D")) continue;
                var cleaned = CleanExplanationText(text);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    optionExplanations[key] = cleaned;
            }
        }

        return q with
        {
            Explanation = explanation,
            OptionExplanations = optionExplanations,
        };
    }

    private static string CleanExplanationText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var cleaned = text.Trim();
        for (var i = 0; i < 3; i++)
        {
            var next = LetterBoundPrefix.Replace(cleaned, "");
            if (next == cleaned) break;
            cleaned = next.Trim();
        }

        cleaned = EmbeddedWhyLetter.Replace(cleaned, "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

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

        Dictionary<string, string>? newOptionExplanations = null;
        if (q.OptionExplanations is { Count: > 0 })
        {
            newOptionExplanations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (oldLetter, text) in q.OptionExplanations)
            {
                var key = oldLetter.Trim().ToUpperInvariant();
                if (oldToNew.TryGetValue(key, out var mapped))
                    newOptionExplanations[mapped] = CleanExplanationText(text);
            }
        }

        return q with
        {
            Options = newOptions,
            CorrectAnswer = newCorrect,
            Explanation = CleanExplanationText(q.Explanation),
            OptionExplanations = newOptionExplanations,
        };
    }

    private string BuildStyleExemplars(int[]? sectionIds)
    {
        var pool = _banks.GetAll("ankush").ToList();
        if (pool.Count == 0)
            return "(no exemplars available)";

        IEnumerable<Question> candidates = pool;
        if (sectionIds is { Length: > 0 })
        {
            var filtered = pool
                .Where(q => sectionIds.Contains(_examGuide.LegacySectionToDomain(q.SectionId)))
                .ToList();
            if (filtered.Count >= 2)
                candidates = filtered;
        }

        // Two short exemplars keep the prompt lighter for parallel generation.
        var picked = candidates
            .OrderBy(_ => Random.Shared.Next())
            .Take(2)
            .Select(q => new
            {
                q.SectionId,
                q.Title,
                text = Truncate(q.Text, 280),
                options = q.Options.ToDictionary(
                    kv => kv.Key,
                    kv => Truncate(kv.Value, 160)),
                q.CorrectAnswer,
                explanation = Truncate(CleanExplanationText(q.Explanation), 180),
            })
            .ToList();

        return JsonSerializer.Serialize(picked, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..(max - 1)].TrimEnd() + "…";
    }

    private async Task<List<Question>> GenerateBatchAsync(
        string apiKey,
        string systemPrompt,
        int count,
        int batchNum,
        int totalBatches,
        int attempt,
        CancellationToken ct)
    {
        var userPrompt = $"""
            Generate exactly {count} NEW multiple-choice questions now.
            This is batch {batchNum} of {totalBatches} (attempt {attempt + 1}).
            Cover DIFFERENT topics from other parallel batches — diversify domains and scenarios.
            Keep EVERY optionExplanation to one short sentence so the JSON is never truncated.
            Escape any quotes inside string values. Close every object and the array.
            Return ONLY a JSON array of {count} objects.
            """;

        var maxTokens = Math.Clamp(_settings.AiMaxTokens, 4096, 64_000);
        // Enough room per question for stem + 4 options + short explanations without huge latency.
        maxTokens = Math.Min(64_000, Math.Max(maxTokens, count * 2_200));

        var body = new Dictionary<string, object?>
        {
            ["model"] = _settings.AnthropicModel,
            ["max_tokens"] = maxTokens,
            ["system"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = systemPrompt,
                    ["cache_control"] = new Dictionary<string, string> { ["type"] = "ephemeral" },
                },
            },
            ["messages"] = new object[]
            {
                new { role = "user", content = userPrompt },
            },
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
        var root = doc.RootElement;
        var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
        if (string.Equals(stopReason, "max_tokens", StringComparison.OrdinalIgnoreCase))
            _logger.LogWarning("AI batch {Batch} hit max_tokens; will salvage complete JSON objects.", batchNum);

        var text = root
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "[]";

        var raw = ParseGeneratedQuestions(text);
        if (raw.Count == 0)
            throw new InvalidOperationException("AI returned unparseable or empty JSON; try again.");

        return raw.Select((q, i) => MapDto(q, i + 1)).ToList();
    }

    internal static List<GeneratedQuestionDto> ParseGeneratedQuestions(string text)
    {
        text = StripMarkdownFences(text.Trim());
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Prefer a full-array deserialize when the model returned complete JSON.
        try
        {
            var direct = JsonSerializer.Deserialize<List<GeneratedQuestionDto>>(text, ParseOptions);
            if (direct is { Count: > 0 })
                return direct;
        }
        catch (JsonException)
        {
            // Fall through to salvage mode for truncated / slightly malformed arrays.
        }

        var arraySlice = ExtractJsonArraySlice(text);
        if (arraySlice is not null)
        {
            try
            {
                var fromSlice = JsonSerializer.Deserialize<List<GeneratedQuestionDto>>(arraySlice, ParseOptions);
                if (fromSlice is { Count: > 0 })
                    return fromSlice;
            }
            catch (JsonException)
            {
                // Continue to per-object salvage.
            }

            var salvaged = new List<GeneratedQuestionDto>();
            foreach (var objJson in ExtractCompleteJsonObjects(arraySlice))
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<GeneratedQuestionDto>(objJson, ParseOptions);
                    if (dto is not null && !string.IsNullOrWhiteSpace(dto.Text))
                        salvaged.Add(dto);
                }
                catch (JsonException)
                {
                    // Skip incomplete trailing object from a truncated response.
                }
            }

            if (salvaged.Count > 0)
                return salvaged;
        }

        return [];
    }

    private static string StripMarkdownFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var start = text.IndexOf('\n') + 1;
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        if (end > start)
            return text[start..end].Trim();
        return text.Trim('`').Trim();
    }

    private static string? ExtractJsonArraySlice(string text)
    {
        var start = text.IndexOf('[');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }

        // Truncated array — return from '[' to end so object salvage can still run.
        return text[start..];
    }

    private static IEnumerable<string> ExtractCompleteJsonObjects(string text)
    {
        var start = -1;
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return text[start..(i + 1)];
                    start = -1;
                }
            }
        }
    }

    private static Question MapDto(GeneratedQuestionDto q, int id)
    {
        var options = NormalizeOptions(q.Options);
        var correct = (q.CorrectAnswer ?? "A").Trim().ToUpperInvariant();
        if (correct.Length == 0 || !"ABCD".Contains(correct[0]))
            correct = "A";
        else
            correct = correct[..1];

        var optionExplanations = NormalizeOptions(q.OptionExplanations);
        if (!optionExplanations.ContainsKey(correct)
            || string.IsNullOrWhiteSpace(optionExplanations[correct]))
        {
            var fromMain = CleanExplanationText(q.Explanation);
            if (!string.IsNullOrWhiteSpace(fromMain))
                optionExplanations[correct] = fromMain;
        }

        return new Question(
            Id: id,
            SectionId: q.SectionId is >= 1 and <= 5 ? q.SectionId : 1,
            Title: q.Title ?? "Practice question",
            Text: q.Text ?? "",
            Options: options,
            CorrectAnswer: correct,
            Explanation: q.Explanation ?? "",
            OptionExplanations: optionExplanations);
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

    internal sealed class GeneratedQuestionDto
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
