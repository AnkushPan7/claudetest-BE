using System.Net;
using System.Text.RegularExpressions;
using ClaudeCertPractice.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ClaudeCertPractice.Api.Services;

public class LearningContentService
{
    private readonly HttpClient _http;
    private readonly QuizSettings _settings;

    public LearningContentService(HttpClient http, IOptions<QuizSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeCertPractice/1.0");
        _http.Timeout = TimeSpan.FromSeconds(45);
    }

    public Task<string> FetchTextAsync(string url, CancellationToken ct = default) =>
        FetchCombinedTextAsync([url], ct);

    public async Task<string> FetchCombinedTextAsync(
        IReadOnlyList<string> urls,
        CancellationToken ct = default)
    {
        if (urls.Count == 0)
            throw new ArgumentException("At least one learning URL is required.");

        var distinct = urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
            throw new ArgumentException("At least one learning URL is required.");

        var perUrlBudget = Math.Max(8_000, _settings.MaxSourceCharacters / distinct.Count);
        var parts = new List<string>();

        foreach (var url in distinct)
        {
            var text = await FetchPageTextAsync(url, perUrlBudget, ct);
            parts.Add($"--- Source: {url} ---\n{text}");
        }

        var combined = string.Join("\n\n", parts);
        if (combined.Length > _settings.MaxSourceCharacters)
            combined = combined[.._settings.MaxSourceCharacters];

        return combined;
    }

    private async Task<string> FetchPageTextAsync(string url, int maxChars, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"Learning URL must be a valid http(s) URL: {url}");

        using var response = await _http.GetAsync(uri, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Could not fetch learning URL ({(int)response.StatusCode} {response.ReasonPhrase}): {url}");

        var html = await response.Content.ReadAsStringAsync(ct);
        var text = StripHtml(html);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Learning URL returned no readable text: {url}");

        if (text.Length > maxChars)
            text = text[..maxChars];

        return text;
    }

    private static string StripHtml(string html)
    {
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = WebUtility.HtmlDecode(html);
        html = Regex.Replace(html, @"\s+", " ");
        return html.Trim();
    }
}
