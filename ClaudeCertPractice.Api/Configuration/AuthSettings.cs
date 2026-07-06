namespace ClaudeCertPractice.Api.Configuration;

public class AuthSettings
{
    public const string SectionName = "Auth";

    public string JwtSecret { get; set; } = "";
    public string AdminEmail { get; set; } = "admin@appunik.com";
    public string AdminPassword { get; set; } = "";
    public int JwtExpiryHours { get; set; } = 24;
}
