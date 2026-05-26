namespace ClaudeCertPractice.Api.Configuration;

public static class AnthropicApiKeyResolver
{
    private static readonly string[] EnvVarNames =
    [
        "ANTHROPIC_API_KEY",
        "AnthropicApiKey",
        "Quiz__AnthropicApiKey",
    ];

    public static string? Resolve(IConfiguration? config = null)
    {
        var fromEnv = GetFromEnvironment();
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

        var fromConfig = config?["Quiz:AnthropicApiKey"];
        if (string.IsNullOrWhiteSpace(fromConfig)) return null;

        fromConfig = fromConfig.Trim();
        if (IsEnvPlaceholder(fromConfig))
            return GetFromEnvironment();

        return fromConfig;
    }

    public static bool IsConfigured(IConfiguration? config = null) =>
        !string.IsNullOrWhiteSpace(Resolve(config));

    private static string? GetFromEnvironment()
    {
        foreach (var name in EnvVarNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static bool IsEnvPlaceholder(string value) =>
        value.Equals("env.AnthropicApiKey", StringComparison.OrdinalIgnoreCase)
        || value.Equals("env:AnthropicApiKey", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("env.", StringComparison.OrdinalIgnoreCase);
}
