using System.Text.RegularExpressions;
using ClaudeCertPractice.Api.Models;

namespace ClaudeCertPractice.Api.Services;

public static class ExplanationHelper
{
    private static readonly Regex OptionStart = new(
        @"(?:^Why\s+([A-D])\s*:\s*)|(?:(?:^|[.!?•]\s+|;\s+|\n\s*)(?:Option\s+)?([A-D])(?=\s*[:.]|\s+(?:only|is|may|means|forces|requires|penalises|penalizes|adds|meets|with|still|alone|would|can|does|fixes|treats|works|requests|keeps|applies|relies|leaves|causes|measures|flags|over-engineers|can't|cannot|doesn't|don't|won't|isn't|aren't|not\b)))|(?:\s([A-D])(?=\s+(?:only|is|may|means|meets|works|treats|requires|penalises|penalizes|adds|forces|still|alone|would|can|does|fixes|measures|flags|can't|cannot|doesn't|don't|won't|isn't|aren't|not\b)))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? ResolveWrongAnswerExplanation(Question question, string? selectedAnswer) =>
        ResolveWrongAnswerExplanation(
            question.Explanation,
            question.CorrectAnswer,
            selectedAnswer,
            question.OptionExplanations);

    public static Dictionary<string, string>? ResolveOptionExplanations(Question question)
    {
        var notes = ExtractOptionNotes(question.Explanation, question.OptionExplanations);
        if (notes.Count == 0) return question.OptionExplanations is { Count: > 0 }
            ? question.OptionExplanations
            : null;

        // Prefer explicit map entries; fill gaps from parsed explanation text.
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var letter in new[] { "A", "B", "C", "D" })
        {
            if (question.OptionExplanations is not null
                && question.OptionExplanations.TryGetValue(letter, out var fromMap)
                && !string.IsNullOrWhiteSpace(fromMap))
            {
                merged[letter] = fromMap.Trim();
            }
            else if (notes.TryGetValue(letter, out var note) && !string.IsNullOrWhiteSpace(note))
            {
                merged[letter] = note.Trim();
            }
        }

        return merged.Count > 0 ? merged : null;
    }

    public static string? ResolveWrongAnswerExplanation(
        string explanation,
        string correctAnswer,
        string? selectedAnswer,
        Dictionary<string, string>? optionExplanations = null)
    {
        if (string.IsNullOrWhiteSpace(selectedAnswer)) return null;

        var selected = selectedAnswer.Trim().ToUpperInvariant();
        var correct = correctAnswer.Trim().ToUpperInvariant();
        if (selected == correct) return null;

        if (optionExplanations is not null
            && optionExplanations.TryGetValue(selected, out var fromMap)
            && !string.IsNullOrWhiteSpace(fromMap))
        {
            return fromMap.Trim();
        }

        var notes = ExtractOptionNotes(explanation, optionExplanations);
        return notes.TryGetValue(selected, out var note) ? note : null;
    }

    public static Dictionary<string, string> ExtractOptionNotes(
        string? explanation,
        Dictionary<string, string>? optionExplanations = null)
    {
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (optionExplanations is not null)
        {
            foreach (var (letter, text) in optionExplanations)
            {
                var key = letter.Trim().ToUpperInvariant();
                if ((key is "A" or "B" or "C" or "D") && !string.IsNullOrWhiteSpace(text))
                    notes[key] = text.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(explanation)) return notes;

        var starts = new List<(string Letter, int Index)>();
        foreach (Match match in OptionStart.Matches(explanation))
        {
            var letter = (match.Groups[1].Success ? match.Groups[1].Value
                : match.Groups[2].Success ? match.Groups[2].Value
                : match.Groups[3].Value).ToUpperInvariant();

            if (letter is not ("A" or "B" or "C" or "D")) continue;
            if (starts.Count > 0)
            {
                var last = starts[^1];
                if (last.Letter == letter && match.Index - last.Index < 10) continue;
            }

            starts.Add((letter, match.Index));
        }

        for (var i = 0; i < starts.Count; i++)
        {
            var cur = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1].Index : explanation.Length;
            var chunk = explanation[cur.Index..end].Trim().TrimStart('.', '!', '?', '•', ';', ' ');
            if (string.IsNullOrWhiteSpace(chunk)) continue;
            if (!notes.ContainsKey(cur.Letter))
                notes[cur.Letter] = chunk;
        }

        return notes;
    }
}
