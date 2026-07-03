using ClaudeCertPractice.Api.Models;
using ClaudeCertPractice.Api.Services;

namespace ClaudeCertPractice.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quiz/users")
            .WithTags("Users");

        group.MapPost("/register", RegisterUser)
            .WithName("RegisterUser")
            .Produces<UserDto>()
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{email}/history", GetHistory)
            .WithName("GetUserHistory")
            .Produces<UserHistoryDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{email}/results", SaveResult)
            .WithName("SaveUserResult")
            .Produces<ResultHistoryEntry>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{email}/results/{resultId}", GetResultDetail)
            .WithName("GetUserResultDetail")
            .Produces<ResultDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> RegisterUser(
        RegisterUserRequest request,
        UserService users,
        CancellationToken ct)
    {
        try
        {
            var user = await users.RegisterAsync(request.Email, request.Name, ct);
            return Results.Ok(user);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> GetHistory(
        string email,
        UserService users,
        CancellationToken ct)
    {
        var history = await users.GetHistoryAsync(email, ct);
        return history is null ? Results.NotFound() : Results.Ok(history);
    }

    private static async Task<IResult> SaveResult(
        string email,
        SaveResultRequest request,
        UserService users,
        CancellationToken ct)
    {
        try
        {
            var entry = await users.SaveResultAsync(email, request, ct);
            return entry is null ? Results.NotFound("User not found. Register first.") : Results.Ok(entry);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static async Task<IResult> GetResultDetail(
        string email,
        string resultId,
        UserService users,
        CancellationToken ct)
    {
        var detail = await users.GetResultDetailAsync(email, resultId, ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }
}
