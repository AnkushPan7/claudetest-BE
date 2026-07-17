using ClaudeCertPractice.Api.Configuration;
using ClaudeCertPractice.Api.Models;
using ClaudeCertPractice.Api.Services;
using Microsoft.Extensions.Options;

namespace ClaudeCertPractice.Api.Endpoints;

public static class QuizEndpoints
{
    public static IEndpointRouteBuilder MapQuizEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quiz")
            .WithTags("Quiz");

        group.MapGet("/metadata", GetMetadata)
            .WithName("GetQuizMetadata")
            .Produces<ExamMetadata>();

        group.MapPost("/sessions", CreateSession)
            .WithName("CreateQuizSession")
            .Produces<SessionDto>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway);

        group.MapGet("/sessions/{sessionId}/questions/{index:int}", GetQuestion)
            .WithName("GetQuizQuestion")
            .Produces<QuestionPublicDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/sessions/{sessionId}/questions/{index:int}/answer", SubmitAnswer)
            .WithName("SubmitQuizAnswer")
            .Produces<AnswerSubmitDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/sessions/{sessionId}/review", GetReview)
            .WithName("GetQuizReview")
            .Produces<SessionReviewDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/sessions/{sessionId}/summary", GetSummary)
            .WithName("GetQuizSummary")
            .Produces<SessionSummaryDto>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static IResult GetMetadata(
        QuestionBankService bank,
        AiQuestionGeneratorService ai,
        IOptions<QuizSettings> settings)
    {
        var meta = bank.GetMetadata();
        var source = settings.Value.QuestionSource.Trim();
        var urls = settings.Value.GetLearningUrls();
        return Results.Ok(meta with
        {
            QuestionSource = source,
            AiGenerationAvailable = ai.IsConfigured,
            LearningUrl = urls[0],
            LearningUrls = urls,
            MaxQuestionsPerSession = settings.Value.MaxQuestionsPerSession,
        });
    }

    private static async Task<IResult> CreateSession(
        CreateSessionRequest request,
        QuestionBankService bank,
        QuizSessionService sessions,
        AiQuestionGeneratorService ai,
        IOptions<QuizSettings> settings,
        CancellationToken ct)
    {
        var quizSettings = settings.Value;
        var source = (request.Source ?? quizSettings.QuestionSource).Trim();
        var useAi = source.Equals("Ai", StringComparison.OrdinalIgnoreCase);

        if (useAi && !ai.IsConfigured)
            return Results.BadRequest("AI mode requires ANTHROPIC_API_KEY or AnthropicApiKey in environment (or .env), or Quiz:AnthropicApiKey.");

        List<Question> questions;

        if (useAi)
        {
            var learningUrls = string.IsNullOrWhiteSpace(request.LearningUrl)
                ? quizSettings.GetLearningUrls()
                : [request.LearningUrl.Trim()];
            var count = Math.Clamp(request.Count ?? 10, 1, quizSettings.MaxQuestionsPerSession);

            try
            {
                questions = await ai.GenerateAsync(count, learningUrls, request.SectionIds, ct);
            }
            catch (Exception ex)
            {
                return Results.Text(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        }
        else
        {
            var bankId = bank.NormalizeBankId(request.BankId);
            if (!bank.BankExists(bankId))
                return Results.BadRequest($"Unknown question bank: {request.BankId}");

            var meta = bank.GetMetadata(bankId);
            var available = bank.GetFilteredPoolCount(bankId, request.SectionIds);
            if (available == 0)
                return Results.BadRequest("No questions available for the selected domain(s).");

            var count = Math.Clamp(request.Count ?? meta.TotalQuestions, 1, meta.BankQuestionCount);
            if (count > available)
            {
                return Results.BadRequest(
                    $"Only {available} question{(available == 1 ? "" : "s")} available for the selected domain(s). " +
                    $"Reduce the question count or clear the domain filter.");
            }

            var ids = bank.PickRandomIds(bankId, count, request.SectionIds);
            questions = ids
                .Select(id => bank.GetById(bankId, id))
                .Where(q => q is not null)
                .Cast<Question>()
                .ToList();
        }

        if (questions.Count == 0)
            return Results.BadRequest("No questions available for this session.");

        var sessionBankId = useAi ? null : bank.NormalizeBankId(request.BankId);
        var session = sessions.Create(questions, useAi ? "Ai" : "Json", sessionBankId);

        return Results.Ok(new SessionDto(
            session.SessionId,
            session.Questions.Count,
            session.Questions.Select(q => q.Id).ToList(),
            session.SourceMode));
    }

    private static IResult GetQuestion(
        string sessionId,
        int index,
        QuizSessionService sessions,
        QuestionBankService bank,
        ExamGuideService examGuide)
    {
        var session = sessions.Get(sessionId);
        if (session is null) return Results.NotFound();

        if (index < 0 || index >= session.Questions.Count) return Results.BadRequest();

        var q = session.Questions[index];
        var sectionName = session.SourceMode == "Ai"
            ? examGuide.GetDomainName(q.SectionId)
            : bank.GetDomainNameForQuestion(session.BankId, q);

        return Results.Ok(new QuestionPublicDto(
            q.Id,
            q.SectionId,
            sectionName,
            q.Title,
            q.Text,
            q.Options,
            index,
            session.Questions.Count));
    }

    private static IResult SubmitAnswer(
        string sessionId,
        int index,
        AnswerRequest request,
        QuizSessionService sessions)
    {
        var session = sessions.Get(sessionId);
        if (session is null) return Results.NotFound();

        if (index < 0 || index >= session.Questions.Count) return Results.BadRequest();

        var q = session.Questions[index];

        var selected = (request.SelectedAnswer ?? "").Trim().ToUpperInvariant();
        if (selected.Length != 1 || !"ABCD".Contains(selected))
            return Results.BadRequest("SelectedAnswer must be A, B, C, or D.");

        var isCorrect = selected == q.CorrectAnswer.ToUpperInvariant();
        session.Answers[index] = (selected, isCorrect);

        return Results.Ok(new AnswerSubmitDto(
            index,
            session.Questions.Count,
            selected,
            q.CorrectAnswer.ToUpperInvariant(),
            isCorrect,
            q.Explanation,
            ExplanationHelper.ResolveWrongAnswerExplanation(q, selected),
            ExplanationHelper.ResolveOptionExplanations(q)));
    }

    private static IResult GetReview(
        string sessionId,
        QuizSessionService sessions,
        QuestionBankService bank,
        ExamGuideService examGuide)
    {
        var session = sessions.Get(sessionId);
        if (session is null) return Results.NotFound();

        var items = new List<QuestionReviewItem>();
        for (var i = 0; i < session.Questions.Count; i++)
        {
            var q = session.Questions[i];
            var sectionName = session.SourceMode == "Ai"
                ? examGuide.GetDomainName(q.SectionId)
                : bank.GetDomainNameForQuestion(session.BankId, q);

            if (!session.Answers.TryGetValue(i, out var answer))
            {
                items.Add(new QuestionReviewItem(
                    i,
                    sectionName,
                    q.Title,
                    q.Text,
                    q.Options,
                    null,
                    q.CorrectAnswer,
                    false,
                    q.Explanation,
                    false,
                    null,
                    ExplanationHelper.ResolveOptionExplanations(q)));
                continue;
            }

            items.Add(new QuestionReviewItem(
                i,
                sectionName,
                q.Title,
                q.Text,
                q.Options,
                answer.Selected,
                q.CorrectAnswer,
                answer.IsCorrect,
                q.Explanation,
                true,
                ExplanationHelper.ResolveWrongAnswerExplanation(q, answer.Selected),
                ExplanationHelper.ResolveOptionExplanations(q)));
        }

        return Results.Ok(new SessionReviewDto(sessionId, items));
    }

    private static IResult GetSummary(string sessionId, QuizSessionService sessions)
    {
        var session = sessions.Get(sessionId);
        if (session is null) return Results.NotFound();

        var total = session.Questions.Count;
        var answered = session.Answers.Count;
        var correct = session.Answers.Values.Count(a => a.IsCorrect);
        // Unanswered questions count as incorrect (official exam scoring).
        var pct = total == 0 ? 0 : Math.Round(100.0 * correct / total, 1);

        return Results.Ok(new SessionSummaryDto(sessionId, session.Questions.Count, answered, correct, pct));
    }
}
