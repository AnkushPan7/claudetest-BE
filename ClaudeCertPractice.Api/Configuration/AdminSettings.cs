namespace ClaudeCertPractice.Api.Configuration;

public class AdminSettings
{
    public const string SectionName = "Admin";

    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
