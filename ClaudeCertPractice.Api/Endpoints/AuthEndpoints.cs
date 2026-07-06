using ClaudeCertPractice.Api.Models;
using ClaudeCertPractice.Api.Services;

namespace ClaudeCertPractice.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin Auth");

        group.MapPost("/login", Login)
            .WithName("AdminLogin")
            .Produces<AdminLoginResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> Login(
        AdminLoginRequest request,
        AuthService auth,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest("Email and password are required.");

        var response = await auth.LoginAdminAsync(request.Email, request.Password, ct);
        return response is null ? Results.Unauthorized() : Results.Ok(response);
    }
}
