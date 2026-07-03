using ClaudeCertPractice.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ClaudeCertPractice.Api.Services;

public class AdminService
{
    private readonly string _adminEmail;
    private readonly string _adminPassword;

    public AdminService(IOptions<AdminSettings> options, IConfiguration configuration)
    {
        _adminEmail = (
            configuration["ADMIN_EMAIL"]
            ?? Environment.GetEnvironmentVariable("ADMIN_EMAIL")
            ?? options.Value.Email
            ?? ""
        ).Trim().ToLowerInvariant();

        _adminPassword =
            configuration["ADMIN_PASSWORD"]
            ?? Environment.GetEnvironmentVariable("ADMIN_PASSWORD")
            ?? options.Value.Password
            ?? "";
    }

    public bool ValidateCredentials(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return false;
        if (string.IsNullOrWhiteSpace(_adminEmail) || string.IsNullOrWhiteSpace(_adminPassword))
            return false;

        return string.Equals(email.Trim(), _adminEmail, StringComparison.OrdinalIgnoreCase)
            && password == _adminPassword;
    }
}
