using System.Text.Json;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public class ExamGuideService
{
    private readonly ExamGuide _guide;

    public ExamGuideService(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "exam-guide.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("exam-guide.json not found.", path);

        using var stream = File.OpenRead(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _guide = JsonSerializer.Deserialize<ExamGuide>(stream, options)
            ?? throw new InvalidDataException("Failed to load exam-guide.json");
    }

    public ExamGuide Guide => _guide;

    public int LegacySectionToDomain(int sectionId)
    {
        if (sectionId is >= 4 and <= 5)
            return sectionId;

        return _guide.LegacySectionToDomain.TryGetValue(sectionId.ToString(), out var domainId)
            ? domainId
            : sectionId;
    }

    public string GetDomainName(int domainId) =>
        _guide.Domains.FirstOrDefault(d => d.Id == domainId)?.Name ?? "CCA-F Practice";

    public IReadOnlyList<SectionInfo> GetDomainSections() =>
        _guide.Domains
            .Select(d => new SectionInfo(d.Id, d.Name, $"{d.WeightPercent}% of exam"))
            .ToList();

    public string BuildAiDomainHint(int[]? domainIds)
    {
        if (domainIds is not { Length: > 0 })
        {
            return """
                Cover a balanced mix aligned to official CCA-F domain weights:
                - Domain 1: Agentic Architecture & Orchestration (27%)
                - Domain 2: Tool Design & MCP Integration (18%)
                - Domain 3: Claude Code Configuration & Workflows (20%)
                - Domain 4: Prompt Engineering & Structured Output (20%)
                - Domain 5: Context Management & Reliability (15%)
                Use scenario-based stems (customer support agent, Claude Code, multi-agent research, CI/CD, structured extraction).
                """;
        }

        var names = domainIds
            .Select(id => _guide.Domains.FirstOrDefault(d => d.Id == id)?.Name)
            .Where(n => n is not null);
        return $"Focus on these CCA-F exam domains: {string.Join("; ", names)}.";
    }

    public string AiSectionIdLine() =>
        """
        sectionId must be the official CCA-F domain id:
        1 = Agentic Architecture & Orchestration
        2 = Tool Design & MCP Integration
        3 = Claude Code Configuration & Workflows
        4 = Prompt Engineering & Structured Output
        5 = Context Management & Reliability
        """;
}
