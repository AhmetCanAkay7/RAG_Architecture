using System.Text.RegularExpressions;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Formats raw LLM output into structured RagResponse.
/// </summary>
public class ResponseFormatter
{
    /// <summary>
    /// Format raw LLM answer into structured response.
    /// </summary>
    public RagResponse Format(
        string rawAnswer,
        CompressedContext context,
        string responseLanguage)
    {
        // Build citations from context items
        var citations = context.Items.Select(i => new Citation
        {
            Index = i.Index,
            DocName = i.DocName ?? "Bilinmeyen",
            Page = i.Page,
            Section = i.Section
        }).ToList();

        // Check for missing info indicators
        var missingInfo = ExtractMissingInfoWarning(rawAnswer, responseLanguage);

        // Split summary and details
        var (summary, details) = SplitSummaryAndDetails(rawAnswer);

        return new RagResponse
        {
            Summary = summary,
            Details = details,
            Citations = citations,
            MissingInfo = missingInfo,
            ResponseLanguage = responseLanguage
        };
    }

    private string? ExtractMissingInfoWarning(string answer, string language)
    {
        var missingIndicators = language == "TR"
            ? new[] { "bulunmamaktadır", "mevcut değil", "bulunamadı", "bilgi yok", "kaynaklarda yok" }
            : new[] { "not found", "not available", "no information", "cannot find", "not in the sources" };

        if (missingIndicators.Any(i => answer.Contains(i, StringComparison.OrdinalIgnoreCase)))
        {
            // Try to extract the specific missing info sentence
            var sentences = Regex.Split(answer, @"(?<=[.!?])\s+");
            var missingSentence = sentences.FirstOrDefault(s =>
                missingIndicators.Any(i => s.Contains(i, StringComparison.OrdinalIgnoreCase)));

            return missingSentence?.Trim() ??
                (language == "TR"
                    ? "Bazı bilgiler kaynaklarda bulunamadı."
                    : "Some information was not found in the sources.");
        }

        return null;
    }

    private (string Summary, string Details) SplitSummaryAndDetails(string answer)
    {
        // Clean up the answer
        var cleaned = answer.Trim();

        // Split into paragraphs/sections
        var paragraphs = Regex.Split(cleaned, @"\n\s*\n")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        if (paragraphs.Count == 0)
            return (cleaned, "");

        if (paragraphs.Count == 1)
        {
            // Single paragraph - try to split on first sentence
            var firstSentenceEnd = FindFirstSentenceEnd(paragraphs[0]);
            if (firstSentenceEnd > 0 && firstSentenceEnd < paragraphs[0].Length - 20)
            {
                return (
                    paragraphs[0][..(firstSentenceEnd + 1)].Trim(),
                    paragraphs[0][(firstSentenceEnd + 1)..].Trim()
                );
            }
            return (paragraphs[0], "");
        }

        // Multiple paragraphs - first is summary, rest is details
        var summary = paragraphs[0];
        var details = string.Join("\n\n", paragraphs.Skip(1));

        return (summary, details);
    }

    private int FindFirstSentenceEnd(string text)
    {
        var endChars = new[] { '.', '!', '?' };

        for (int i = 0; i < text.Length; i++)
        {
            if (endChars.Contains(text[i]))
            {
                // Make sure it's not a number (e.g., "1.2")
                if (i > 0 && char.IsDigit(text[i - 1]) && i < text.Length - 1 && char.IsDigit(text[i + 1]))
                    continue;

                // Make sure it's followed by space or end
                if (i == text.Length - 1 || char.IsWhiteSpace(text[i + 1]))
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Create a "no results" response.
    /// </summary>
    public RagResponse CreateNoResultsResponse(string language)
    {
        return new RagResponse
        {
            Summary = language == "TR"
                ? "Bu bilgi veritabanında bulunamadı."
                : "This information was not found in the database.",
            Details = language == "TR"
                ? "Lütfen sorunuzu farklı şekilde sormayı deneyin veya yöneticinize başvurun."
                : "Please try rephrasing your question or contact your administrator.",
            Citations = new List<Citation>(),
            MissingInfo = null,
            ResponseLanguage = language
        };
    }
}
