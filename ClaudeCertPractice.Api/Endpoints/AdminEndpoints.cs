using ClaudeCertPractice.Api.Data.Entities;
using ClaudeCertPractice.Api.Models;
using ClaudeCertPractice.Api.Services;

namespace ClaudeCertPractice.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

        group.MapGet("/users", GetUsers)
            .WithName("AdminGetUsers")
            .Produces<IReadOnlyList<AdminUserSummary>>();

        group.MapGet("/results", GetResults)
            .WithName("AdminGetResults")
            .Produces<IReadOnlyList<AdminResultSummary>>();

        group.MapGet("/results/{resultId}", GetResultDetail)
            .WithName("AdminGetResultDetail")
            .Produces<ResultDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetUsers(UserService users, CancellationToken ct)
    {
        var list = await users.GetAllUsersAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetResults(UserService users, CancellationToken ct)
    {
        var list = await users.GetAllResultsAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetResultDetail(
        string resultId,
        UserService users,
        CancellationToken ct)
    {
        var detail = await users.GetAdminResultDetailAsync(resultId, ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }
}
