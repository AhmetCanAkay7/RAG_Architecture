using Microsoft.SemanticKernel;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Optional query translator for converting non-English queries to English.
/// This is a lightweight, opt-in feature that uses the LLM for translation.
/// </summary>
public class QueryTranslator
{
    private readonly Kernel _kernel;
    private readonly bool _enabled;

    // Simple prompt for translation - no complex processing
    private const string TranslationPrompt = @"Translate the following text to English. Only output the translation, nothing else.
Text: {0}
Translation:";

    public QueryTranslator(Kernel kernel, bool enabled = false)
    {
        _kernel = kernel;
        _enabled = enabled;
    }

    /// <summary>
    /// Translate query to English if translation is enabled and query is not already in English.
    /// Returns original query if translation is disabled or query appears to be English.
    /// </summary>
    public async Task<TranslationResult> TranslateIfNeededAsync(string query)
    {
        if (!_enabled)
        {
            return new TranslationResult(query, false, null);
        }

        // Quick check: if query contains only ASCII letters, likely English
        if (IsLikelyEnglish(query))
        {
            return new TranslationResult(query, false, null);
        }

        try
        {
            var prompt = string.Format(TranslationPrompt, query);
            var result = await _kernel.InvokePromptAsync<string>(prompt);

            var translatedQuery = result?.Trim() ?? query;

            return new TranslationResult(translatedQuery, true, query);
        }
        catch
        {
            // On translation failure, use original query
            return new TranslationResult(query, false, null);
        }
    }

    /// <summary>
    /// Simple heuristic to check if text is likely English.
    /// Returns true if text contains only basic ASCII characters.
    /// </summary>
    private static bool IsLikelyEnglish(string text)
    {
        // If text contains non-ASCII letters, likely non-English
        foreach (var c in text)
        {
            if (char.IsLetter(c) && c > 127)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Result of query translation operation.
/// </summary>
public record TranslationResult(
    string Query,
    bool WasTranslated,
    string? OriginalQuery
);
