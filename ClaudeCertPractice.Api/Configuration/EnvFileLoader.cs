namespace ClaudeCertPractice.Api.Configuration;

/// <summary>
/// Loads KEY=VALUE pairs from a .env file into process environment variables (without overriding existing values).
/// </summary>
public static class EnvFileLoader
{
    public static void LoadFromContentRoot(string contentRootPath)
    {
        var envPath = FindEnvFile(contentRootPath);
        if (envPath is null) return;

        foreach (var line in File.ReadAllLines(envPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
                value = value[1..^1];

            if (string.IsNullOrEmpty(key)) continue;
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindEnvFile(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent!)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
