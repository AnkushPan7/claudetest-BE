using System.Text.Json;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public class QuestionBankService
{
    private readonly ExamGuideService _examGuide;
    private readonly IReadOnlyList<Question> _questions;

    public QuestionBankService(IWebHostEnvironment env, ExamGuideService examGuide)
    {
        _examGuide = examGuide;

        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "Data", "questions.json"),
            Path.Combine(env.ContentRootPath, "..", "..", "..", "scripts", "questions-source.json"),
            Path.Combine(env.ContentRootPath, "..", "..", "scripts", "questions-source.json"),
        };
        var path = candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("questions.json not found. Run scripts/build-questions.ps1.");

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        _questions = root.GetProperty("questions")
            .Deserialize<List<Question>>(jsonOptions)
            ?? [];

        if (_questions.Count == 0)
            throw new InvalidDataException("No questions loaded from " + path);
    }

    private IReadOnlyList<Question> PracticePool => _questions;

    public int GetFilteredPoolCount(int[]? domainIds)
    {
        if (domainIds is not { Length: > 0 })
            return PracticePool.Count;

        return PracticePool.Count(q =>
            domainIds.Contains(_examGuide.LegacySectionToDomain(q.SectionId)));
    }

    private IReadOnlyDictionary<int, int> GetDomainQuestionCounts() =>
        PracticePool
            .GroupBy(q => _examGuide.LegacySectionToDomain(q.SectionId))
            .ToDictionary(g => g.Key, g => g.Count());

    public IReadOnlyList<SectionInfo> GetDomainSectionsWithCounts()
    {
        var counts = GetDomainQuestionCounts();
        return _examGuide.Guide.Domains
            .Select(d => new SectionInfo(
                d.Id,
                d.Name,
                $"{d.WeightPercent}% of exam",
                counts.GetValueOrDefault(d.Id, 0)))
            .ToList();
    }

    public ExamMetadata GetMetadata()
    {
        var guide = _examGuide.Guide;
        return new ExamMetadata(
            guide.ExamTitle,
            guide.QuestionCount,
            PracticePool.Count,
            GetDomainSectionsWithCounts(),
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
            guide.Scenarios);
    }

    public IReadOnlyList<Question> GetAll() => _questions;

    public Question? GetById(int id) => _questions.FirstOrDefault(q => q.Id == id);

    public string GetDomainNameForQuestion(Question q) =>
        _examGuide.GetDomainName(_examGuide.LegacySectionToDomain(q.SectionId));

    public List<int> PickRandomIds(int count, int[]? domainIds)
    {
        var pool = domainIds is { Length: > 0 }
            ? PracticePool.Where(q => domainIds.Contains(_examGuide.LegacySectionToDomain(q.SectionId)))
                .OrderBy(q => q.Id)
                .ToList()
            : PracticePool.OrderBy(_ => Random.Shared.Next()).ToList();

        return pool.Take(Math.Min(count, pool.Count)).Select(q => q.Id).ToList();
    }
}
