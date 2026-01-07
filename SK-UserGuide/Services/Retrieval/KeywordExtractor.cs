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
        "this", "that", "these", "those", "if", "then", "more", "less", "very",
        "about", "also", "into", "such", "make", "get", "see", "know", "just"
    };

    /// <summary>
    /// Extract keywords from a question for sparse search.
    /// Includes bigrams (2-word phrases) and trigrams (3-word phrases) for better precision.
    /// </summary>
    public static List<string> Extract(string text, int maxKeywords = 15)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var keywords = new List<string>();

        // 1. Extract special patterns first (error codes, CamelCase, etc.)
        var specialPatterns = ExtractSpecialPatterns(text);
        keywords.AddRange(specialPatterns);

        // 2. Extract important n-grams (bigrams and trigrams)
        var ngrams = ExtractNGrams(text);
        keywords.AddRange(ngrams);

        // 3. Tokenize normal words
        var words = Regex.Split(text.ToLower(CultureInfo.InvariantCulture), @"[\s\p{P}]+")
            .Where(w => w.Length > 2)
            .Where(w => !EnglishStopwords.Contains(w))
            .Where(w => !keywords.Any(k => k.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        keywords.AddRange(words);

        var result = keywords
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxKeywords)
            .ToList();

        // DEBUG logging
        Console.WriteLine($"[KeywordExtractor] Input: '{text}'");
        Console.WriteLine($"[KeywordExtractor] Extracted {result.Count} keywords: {string.Join(", ", result)}");

        return result;
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
        var camelCase = Regex.Matches(text, @"\b[A-Z][a-z]+[A-Z][a-zA-Z]*\b");
        patterns.AddRange(camelCase.Select(m => m.Value));

        // Quoted strings: "Save" button
        var quoted = Regex.Matches(text, @"""([^""]+)""");
        patterns.AddRange(quoted.Select(m => m.Groups[1].Value));

        // Screen/menu names: Settings Screen, User Menu
        var screenNames = Regex.Matches(text, @"\b[A-Z][a-z]+\s+(Screen|Menu|Page|Panel|Form|Dialog|Window)\b",
            RegexOptions.IgnoreCase);
        patterns.AddRange(screenNames.Select(m => m.Value));

        // Acronyms: SKU, API, ERP, MRP
        var acronyms = Regex.Matches(text, @"\b[A-Z]{2,}\b");
        patterns.AddRange(acronyms.Select(m => m.Value));

        return patterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    /// <summary>
    /// Extract important bigrams (2-word) and trigrams (3-word) phrases.
    /// Examples: "waste reduction rate", "carbon footprint", "demand forecasting"
    /// </summary>
    private static List<string> ExtractNGrams(string text)
    {
        var ngrams = new List<string>();

        // Tokenize into words (preserve case for now)
        var allWords = Regex.Split(text, @"[\s\p{P}]+")
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToArray();

        if (allWords.Length < 2)
            return ngrams;

        // Extract bigrams (2-word phrases)
        for (int i = 0; i < allWords.Length - 1; i++)
        {
            var word1 = allWords[i].ToLower();
            var word2 = allWords[i + 1].ToLower();

            // Skip if either word is a stopword or too short
            if (EnglishStopwords.Contains(word1) || EnglishStopwords.Contains(word2))
                continue;

            if (word1.Length <= 2 || word2.Length <= 2)
                continue;

            var bigram = $"{word1} {word2}";

            // Only add if it looks like a meaningful phrase (not purely generic)
            if (IsMeaningfulPhrase(word1, word2))
                ngrams.Add(bigram);
        }

        // Extract trigrams (3-word phrases)
        for (int i = 0; i < allWords.Length - 2; i++)
        {
            var word1 = allWords[i].ToLower();
            var word2 = allWords[i + 1].ToLower();
            var word3 = allWords[i + 2].ToLower();

            // Skip if middle word is a stopword (e.g., "waste of reduction")
            if (EnglishStopwords.Contains(word2))
                continue;

            if (word1.Length <= 2 || word2.Length <= 2 || word3.Length <= 2)
                continue;

            var trigram = $"{word1} {word2} {word3}";

            if (IsMeaningfulPhrase(word1, word2, word3))
                ngrams.Add(trigram);
        }

        return ngrams;
    }

    /// <summary>
    /// Check if a phrase is meaningful (heuristic-based).
    /// </summary>
    private static bool IsMeaningfulPhrase(params string[] words)
    {
        // Domain-specific keywords that indicate meaningful phrases
        var domainKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "waste", "reduction", "rate", "formula", "carbon", "footprint",
            "demand", "forecast", "inventory", "turnover", "safety", "stock",
            "reorder", "point", "economic", "order", "quantity", "capacity",
            "utilization", "productivity", "equipment", "effectiveness",
            "total", "cost", "ownership", "unit", "quality", "defect",
            "customer", "satisfaction", "delivery", "performance", "risk",
            "score", "mitigation", "water", "usage", "efficiency", "moving",
            "average", "exponential", "smoothing", "acceptable", "level"
        };

        // At least one word should be a domain keyword
        return words.Any(w => domainKeywords.Contains(w));
    }
}
