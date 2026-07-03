using ClaudeCertPractice.Api.Models;
using ClaudeCertPractice.Api.Services;

namespace ClaudeCertPractice.Api.Endpoints;

public static class AdminEndpoints
{
    public const string AdminEmailHeader = "X-Admin-Email";
    public const string AdminPasswordHeader = "X-Admin-Password";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quiz/admin")
            .WithTags("Admin");

        group.MapPost("/login", Login)
            .WithName("AdminLogin")
            .Produces<AdminLoginDto>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/overview", GetOverview)
            .WithName("GetAdminOverview")
            .Produces<AdminOverviewDto>()
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static IResult Login(AdminLoginRequest request, AdminService admin)
    {
        if (!UserService.IsValidEmail(request.Email))
            return Results.Json(
                new { error = "Unauthorized", detail = "Invalid admin credentials." },
                statusCode: StatusCodes.Status401Unauthorized);

        if (!admin.ValidateCredentials(request.Email, request.Password))
            return Results.Json(
                new { error = "Unauthorized", detail = "Invalid admin credentials." },
                statusCode: StatusCodes.Status401Unauthorized);

        return Results.Ok(new AdminLoginDto(request.Email.Trim().ToLowerInvariant()));
    }

    private static async Task<IResult> GetOverview(
        HttpContext context,
        AdminService admin,
        UserService users,
        CancellationToken ct)
    {
        var email = context.Request.Headers[AdminEmailHeader].FirstOrDefault();
        var password = context.Request.Headers[AdminPasswordHeader].FirstOrDefault();
        if (!admin.ValidateCredentials(email, password))
            return Results.Json(
                new { error = "Forbidden", detail = "Admin access required." },
                statusCode: StatusCodes.Status403Forbidden);

        var overview = await users.GetAllUsersOverviewAsync(ct);
        return Results.Ok(overview);
    }
}
