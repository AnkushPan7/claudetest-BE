using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public class QuestionBankService
{
    public const string DefaultBankId = "ankush";
    public const string RandomBankId = "random";
    public const string AiGeneratedBankId = "ai-generated";

    private static readonly JsonSerializerOptions PersistJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ExamGuideService _examGuide;
    private readonly Dictionary<string, LoadedBank> _banks;
    private readonly string _aiBankPath;
    private readonly object _aiBankLock = new();
    private readonly HashSet<string> _aiFingerprints = new(StringComparer.Ordinal);

    private sealed class LoadedBank
    {
        public LoadedBank(string id, string name, List<Question> questions)
        {
            Id = id;
            Name = name;
            Questions = questions;
        }

        public string Id { get; }
        public string Name { get; }
        public List<Question> Questions { get; }
    }

    public QuestionBankService(IWebHostEnvironment env, ExamGuideService examGuide)
    {
        _examGuide = examGuide;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dataDir = Path.Combine(env.ContentRootPath, "Data");
        _aiBankPath = Path.Combine(dataDir, "questions-ai-generated.json");

        var ankush = LoadBank(
            "ankush",
            "Ankush",
            Path.Combine(dataDir, "questions-ankush.json"),
            jsonOptions,
            allowEmpty: false);
        var yagnesh = LoadBank(
            "yagnesh",
            "Yagnesh",
            Path.Combine(dataDir, "questions-yagnesh.json"),
            jsonOptions,
            allowEmpty: false);
        var nilesh = LoadBank(
            "nilesh",
            "Nilesh",
            Path.Combine(dataDir, "questions-nilesh.json"),
            jsonOptions,
            allowEmpty: false);
        var aiGenerated = LoadBank(
            AiGeneratedBankId,
            "AI Generated bank",
            _aiBankPath,
            jsonOptions,
            allowEmpty: true);

        foreach (var q in aiGenerated.Questions)
            _aiFingerprints.Add(Fingerprint(q));

        // Clean any legacy letter-bound prefixes already stored in the AI bank.
        SanitizeLoadedAiBank(aiGenerated);

        // Combined pool first in the dropdown; IDs remapped so 1–60 per source bank do not collide.
        // AI Generated bank is separate (not merged into Random).
        _banks = new Dictionary<string, LoadedBank>
        {
            [RandomBankId] = BuildMergedRandomBank(ankush, yagnesh, nilesh),
            ["ankush"] = ankush,
            ["yagnesh"] = yagnesh,
            ["nilesh"] = nilesh,
            [AiGeneratedBankId] = aiGenerated,
        };
    }

    private void SanitizeLoadedAiBank(LoadedBank aiBank)
    {
        var changed = false;
        for (var i = 0; i < aiBank.Questions.Count; i++)
        {
            var q = aiBank.Questions[i];
            var explanation = ExplanationHelper.StripLetterBoundPrefixes(q.Explanation);
            var optionExplanations = SanitizeOptionExplanations(q.OptionExplanations, explanation, q.CorrectAnswer);
            if (explanation != (q.Explanation ?? "").Trim()
                || !OptionMapsEqual(q.OptionExplanations, optionExplanations))
            {
                aiBank.Questions[i] = q with
                {
                    Explanation = explanation,
                    OptionExplanations = optionExplanations,
                };
                changed = true;
            }
        }

        if (changed)
            PersistAiBankFile(_aiBankPath, aiBank.Questions);
    }

    private static Dictionary<string, string>? SanitizeOptionExplanations(
        Dictionary<string, string>? map,
        string? mainExplanation,
        string? correctAnswer)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map is not null)
        {
            foreach (var (letter, text) in map)
            {
                var key = letter.Trim().ToUpperInvariant();
                if (key is not ("A" or "B" or "C" or "D")) continue;
                var cleaned = ExplanationHelper.StripLetterBoundPrefixes(text);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    result[key] = cleaned;
            }
        }

        var correct = (correctAnswer ?? "").Trim().ToUpperInvariant();
        if (correct is "A" or "B" or "C" or "D"
            && (!result.ContainsKey(correct) || string.IsNullOrWhiteSpace(result[correct])))
        {
            var fromMain = ExplanationHelper.StripLetterBoundPrefixes(mainExplanation);
            if (!string.IsNullOrWhiteSpace(fromMain))
                result[correct] = fromMain;
        }

        return result.Count > 0 ? result : null;
    }

    private static bool OptionMapsEqual(
        Dictionary<string, string>? a,
        Dictionary<string, string>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var other) || other != v)
                return false;
        }
        return true;
    }

    private static LoadedBank LoadBank(
        string id,
        string name,
        string path,
        JsonSerializerOptions jsonOptions,
        bool allowEmpty)
    {
        if (!File.Exists(path))
        {
            if (allowEmpty)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                PersistAiBankFile(path, []);
                return new LoadedBank(id, name, []);
            }

            throw new FileNotFoundException($"Question bank file not found: {path}");
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var questions = root.TryGetProperty("questions", out var questionsEl)
            ? questionsEl.Deserialize<List<Question>>(jsonOptions) ?? []
            : [];

        if (questions.Count == 0 && !allowEmpty)
            throw new InvalidDataException($"No questions loaded from {path}");

        return new LoadedBank(id, name, questions);
    }

    /// <summary>
    /// Merges Ankush + Yagnesh + Nilesh into one pool with unique IDs (sources each reuse 1–60).
    /// </summary>
    private static LoadedBank BuildMergedRandomBank(params LoadedBank[] sources)
    {
        var merged = new List<Question>();
        var nextId = 1;
        foreach (var source in sources)
        {
            foreach (var q in source.Questions)
                merged.Add(q with { Id = nextId++ });
        }

        if (merged.Count == 0)
            throw new InvalidDataException("Random question bank has no questions.");

        return new LoadedBank(
            RandomBankId,
            "Random question (Ankush, Yagnesh, Nilesh)",
            merged);
    }

    /// <summary>
    /// Appends newly AI-generated questions to the AI Generated bank, skipping duplicates
    /// (same normalized title + stem text as an existing or earlier-in-batch question).
    /// </summary>
    /// <returns>Number of questions newly saved.</returns>
    public int AppendAiGenerated(IReadOnlyList<Question> questions)
    {
        if (questions.Count == 0)
            return 0;

        lock (_aiBankLock)
        {
            if (!_banks.TryGetValue(AiGeneratedBankId, out var aiBank))
                throw new InvalidOperationException("AI Generated bank is not registered.");

            var nextId = aiBank.Questions.Count == 0
                ? 1
                : aiBank.Questions.Max(q => q.Id) + 1;

            var added = new List<Question>();
            foreach (var q in questions)
            {
                var fingerprint = Fingerprint(q);
                if (!_aiFingerprints.Add(fingerprint))
                    continue;

                // Persist letter-free explanations so shuffled labels never leak into the bank.
                var sanitized = q with
                {
                    Id = nextId++,
                    Explanation = ExplanationHelper.StripLetterBoundPrefixes(q.Explanation),
                    OptionExplanations = SanitizeOptionExplanations(q.OptionExplanations, q.Explanation, q.CorrectAnswer),
                };
                added.Add(sanitized);
            }

            if (added.Count == 0)
                return 0;

            aiBank.Questions.AddRange(added);
            PersistAiBankFile(_aiBankPath, aiBank.Questions);
            return added.Count;
        }
    }

    private static void PersistAiBankFile(string path, List<Question> questions)
    {
        var payload = new
        {
            examTitle = "Claude Certified Architect – Foundations (CCA-F)",
            questions,
        };
        var json = JsonSerializer.Serialize(payload, PersistJsonOptions);
        File.WriteAllText(path, json);
    }

    private static string Fingerprint(Question q)
    {
        var title = NormalizeText(q.Title);
        var text = NormalizeText(q.Text);
        return $"{title}\n{text}";
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var collapsed = Regex.Replace(value.Trim(), @"\s+", " ");
        return collapsed.ToLowerInvariant();
    }

    public IReadOnlyList<QuestionBankInfo> GetBanks() =>
        _banks.Values
            .Select(b => new QuestionBankInfo(
                b.Id,
                b.Name,
                PracticePool(b.Id).Count,
                GetDomainSectionsWithCounts(b.Id)))
            .ToList();

    public bool BankExists(string? bankId) =>
        _banks.ContainsKey(NormalizeBankId(bankId));

    public string NormalizeBankId(string? bankId) =>
        string.IsNullOrWhiteSpace(bankId) ? DefaultBankId : bankId.Trim().ToLowerInvariant();

    private LoadedBank GetBank(string? bankId)
    {
        var id = NormalizeBankId(bankId);
        if (!_banks.TryGetValue(id, out var bank))
            throw new ArgumentException($"Unknown question bank: {bankId}");
        return bank;
    }

    private IReadOnlyList<Question> PracticePool(string? bankId)
    {
        var bank = GetBank(bankId);
        lock (_aiBankLock)
        {
            // Snapshot AI bank under lock so concurrent appends don't race enumeration.
            if (bank.Id == AiGeneratedBankId)
                return bank.Questions.ToList();
        }

        return bank.Questions;
    }

    public int GetFilteredPoolCount(string? bankId, int[]? domainIds)
    {
        var pool = PracticePool(bankId);
        if (domainIds is not { Length: > 0 })
            return pool.Count;

        return pool.Count(q =>
            domainIds.Contains(_examGuide.LegacySectionToDomain(q.SectionId)));
    }

    private IReadOnlyDictionary<int, int> GetDomainQuestionCounts(string? bankId) =>
        PracticePool(bankId)
            .GroupBy(q => _examGuide.LegacySectionToDomain(q.SectionId))
            .ToDictionary(g => g.Key, g => g.Count());

    public IReadOnlyList<SectionInfo> GetDomainSectionsWithCounts(string? bankId)
    {
        var counts = GetDomainQuestionCounts(bankId);
        return _examGuide.Guide.Domains
            .Select(d => new SectionInfo(
                d.Id,
                d.Name,
                $"{d.WeightPercent}% of exam",
                counts.GetValueOrDefault(d.Id, 0)))
            .ToList();
    }

    public ExamMetadata GetMetadata(string? bankId = null)
    {
        var bank = GetBank(bankId);
        var guide = _examGuide.Guide;
        var pool = PracticePool(bank.Id);
        return new ExamMetadata(
            guide.ExamTitle,
            guide.QuestionCount,
            pool.Count,
            GetDomainSectionsWithCounts(bank.Id),
            QuestionSource: "Json",
            AiGenerationAvailable: false,
            LearningUrl: null,
            LearningUrls: null,
            MaxQuestionsPerSession: 60,
            guide.PassingScore,
            guide.ScoreMin,
            guide.ScoreMax,
            guide.PracticePassingScore,
            guide.ResponseFormat,
            guide.ExamScenariosNote,
            guide.Scenarios,
            GetBanks(),
            bank.Id);
    }

    public IReadOnlyList<Question> GetAll(string? bankId) => PracticePool(bankId);

    public Question? GetById(string? bankId, int id) =>
        PracticePool(bankId).FirstOrDefault(q => q.Id == id);

    public string GetDomainNameForQuestion(string? bankId, Question q) =>
        _examGuide.GetDomainName(_examGuide.LegacySectionToDomain(q.SectionId));

    public List<int> PickRandomIds(string? bankId, int count, int[]? domainIds)
    {
        IEnumerable<Question> query = PracticePool(bankId);
        if (domainIds is { Length: > 0 })
        {
            query = query.Where(q =>
                domainIds.Contains(_examGuide.LegacySectionToDomain(q.SectionId)));
        }

        // Always shuffle so domain-filtered picks (and the merged random bank) stay unbiased.
        var pool = query.OrderBy(_ => Random.Shared.Next()).ToList();
        return pool.Take(Math.Min(count, pool.Count)).Select(q => q.Id).ToList();
    }
}
