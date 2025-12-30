using System.Globalization;
using System.Text.RegularExpressions;

namespace SK_UserGuide.Services.Retrieval;

/// <summary>
/// Extracts keywords from text for sparse search.
/// Handles stopwords, special patterns (error codes, CamelCase), and normalization.
/// </summary>
public static class KeywordExtractor
{
    private static readonly HashSet<string> EnglishStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Question words
        "what", "how", "why", "when", "where", "which", "who", "whom",
        // Conjunctions
        "and", "or", "but", "because", "although", "however", "therefore",
        // Articles/Prepositions
        "a", "an", "the", "in", "on", "at", "to", "for", "of", "with", "by",
        // Pronouns
        "i", "you", "he", "she", "it", "we", "they", "my", "your", "his", "her",
        // Auxiliary verbs
        "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
        "do", "does", "did", "will", "would", "could", "should", "can", "may",
        // Common words
        "this", "that", "these", "those", "if", "then", "more", "less", "very"
    };

    /// <summary>
    /// Extract keywords from a question for sparse search.
    /// </summary>
    public static List<string> Extract(string text, int maxKeywords = 8)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var keywords = new List<string>();

        // 1. Extract special patterns first (error codes, CamelCase, etc.)
        var specialPatterns = ExtractSpecialPatterns(text);
        keywords.AddRange(specialPatterns);

        // 2. Tokenize normal words
        var words = Regex.Split(text.ToLower(CultureInfo.InvariantCulture), @"[\s\p{P}]+")
            .Where(w => w.Length > 2)
            .Where(w => !EnglishStopwords.Contains(w))
            .Where(w => !keywords.Contains(w, StringComparer.OrdinalIgnoreCase))
            .ToList();

        keywords.AddRange(words);

        return keywords
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxKeywords)
            .ToList();
    }

    /// <summary>
    /// Extract special patterns like error codes, screen names, etc.
    /// </summary>
    private static List<string> ExtractSpecialPatterns(string text)
    {
        var patterns = new List<string>();

        // Error codes: ERR-001, ERROR_5001, E1234
        var errorCodes = Regex.Matches(text, @"[A-Z]{2,}[-_]?\d+", RegexOptions.IgnoreCase);
        patterns.AddRange(errorCodes.Select(m => m.Value));

        // CamelCase: LoginScreen, OrderForm
        var camelCase = Regex.Matches(text, @"[A-Z][a-z]+[A-Z][a-zA-Z]*");
        patterns.AddRange(camelCase.Select(m => m.Value));

        // Quoted strings: "Save" button
        var quoted = Regex.Matches(text, @"""([^""]+)""");
        patterns.AddRange(quoted.Select(m => m.Groups[1].Value));

        // Screen/menu names: Settings Screen, User Menu
        var screenNames = Regex.Matches(text, @"[A-Z][a-z]+\s+(Screen|Menu|Page|Panel|Form|Dialog|Window)",
            RegexOptions.IgnoreCase);
        patterns.AddRange(screenNames.Select(m => m.Value));

        return patterns;
    }
}
