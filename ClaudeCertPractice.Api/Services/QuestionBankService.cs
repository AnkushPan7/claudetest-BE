using System.Text.Json;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public class QuestionBankService
{
    public const string DefaultBankId = "ankush";

    private readonly ExamGuideService _examGuide;
    private readonly IReadOnlyDictionary<string, LoadedBank> _banks;

    private sealed record LoadedBank(
        string Id,
        string Name,
        IReadOnlyList<Question> Questions);

    public QuestionBankService(IWebHostEnvironment env, ExamGuideService examGuide)
    {
        _examGuide = examGuide;

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dataDir = Path.Combine(env.ContentRootPath, "Data");

        _banks = new Dictionary<string, LoadedBank>
        {
            ["ankush"] = LoadBank(
                "ankush",
                "Ankush",
                Path.Combine(dataDir, "questions-ankush.json"),
                jsonOptions),
            ["yagnesh"] = LoadBank(
                "yagnesh",
                "Yagnesh",
                Path.Combine(dataDir, "questions-yagnesh.json"),
                jsonOptions),
            ["nilesh"] = LoadBank(
                "nilesh",
                "Nilesh",
                Path.Combine(dataDir, "questions-nilesh.json"),
                jsonOptions),
        };
    }

    private static LoadedBank LoadBank(string id, string name, string path, JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Question bank file not found: {path}");

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var questions = root.GetProperty("questions")
            .Deserialize<List<Question>>(jsonOptions)
            ?? [];

        if (questions.Count == 0)
            throw new InvalidDataException($"No questions loaded from {path}");

        return new LoadedBank(id, name, questions);
    }

    public IReadOnlyList<QuestionBankInfo> GetBanks() =>
        _banks.Values
            .Select(b => new QuestionBankInfo(
                b.Id,
                b.Name,
                b.Questions.Count,
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

    private IReadOnlyList<Question> PracticePool(string? bankId) => GetBank(bankId).Questions;

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
        return new ExamMetadata(
            guide.ExamTitle,
            guide.QuestionCount,
            bank.Questions.Count,
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
        var pool = domainIds is { Length: > 0 }
            ? PracticePool(bankId)
                .Where(q => domainIds.Contains(_examGuide.LegacySectionToDomain(q.SectionId)))
                .OrderBy(q => q.Id)
                .ToList()
            : PracticePool(bankId).OrderBy(_ => Random.Shared.Next()).ToList();

        return pool.Take(Math.Min(count, pool.Count)).Select(q => q.Id).ToList();
    }
}
