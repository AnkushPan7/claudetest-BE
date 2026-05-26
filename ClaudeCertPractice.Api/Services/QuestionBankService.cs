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

    public ExamMetadata GetMetadata()
    {
        var guide = _examGuide.Guide;
        return new ExamMetadata(
            guide.ExamTitle,
            guide.QuestionCount,
            _questions.Count,
            _examGuide.GetDomainSections(),
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
            ? _questions.Where(q => domainIds.Contains(_examGuide.LegacySectionToDomain(q.SectionId))).ToList()
            : _questions.ToList();

        var rng = Random.Shared;
        return pool.OrderBy(_ => rng.Next()).Take(Math.Min(count, pool.Count)).Select(q => q.Id).ToList();
    }
}
