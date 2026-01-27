using System.Globalization;
using System.Text.RegularExpressions;

namespace SK_UserGuide.Services.Retrieval;

/// <summary>
/// Extracts keywords for sparse search using "Negative Selection" (Stopword Filtering).
/// This allows the system to detect meaningful phrases dynamically without hardcoded domain lists.
/// </summary>
public static class KeywordExtractor
{
    // Comprehensive list of English "Noise" words (Stopwords)
    private static readonly HashSet<string> EnglishStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Articles & Prepositions
        "a", "an", "the", "in", "on", "at", "to", "for", "of", "with", "by", "from", "up", "about", "into", "over", "after", "through", "during", "before",
        // Conjunctions
        "and", "or", "but", "because", "although", "however", "therefore", "if", "then", "than", "so", "as", "while", "since",
        // Pronouns & Verbs (auxiliary)
        "i", "you", "he", "she", "it", "we", "they", "my", "your", "his", "her", "their", "our", "us", "them",
        "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did",
        "can", "could", "will", "would", "should", "may", "might", "must",
        // Common Quantifiers & Adverbs
        "this", "that", "these", "those", "some", "any", "all", "more", "most", "less", "very", "just", "only", "also", "too",
        // Interaction/Filler Words (Crucial for cleaning chat queries)
        "please", "help", "tell", "say", "ask", "know", "how", "what", "why", "when", "where", "which", "who", "whom",
        "show", "find", "search", "give", "need", "want", "look", "looking", "thanks", "thank", "hello", "hi", "hey"
    };

    public static List<string> Extract(string text, int maxKeywords = 15)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var keywords = new List<string>();

        // 1. High Priority: Special Patterns (Codes, Acronyms, Quoted Text)
        var specialPatterns = ExtractSpecialPatterns(text);
        keywords.AddRange(specialPatterns);

        // 2. Medium Priority: Meaningful Phrases (N-Grams)
        // We do this BEFORE splitting single words to prioritize "United States" over "United" and "States".
        var ngrams = ExtractNGrams(text);
        keywords.AddRange(ngrams);

        // 3. Low Priority: Single Content Words
        var tokens = Regex.Split(text, @"[\s\p{P}]+")
            .Where(w => w.Length > 2)
            .Select(w => w.ToLower(CultureInfo.InvariantCulture))
            .Where(w => !EnglishStopwords.Contains(w)) 
            .Distinct()
            .ToList();

        // Add single words (Deduplicating against what we already found)
        foreach (var token in tokens)
        {
            if (!keywords.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                keywords.Add(token);
            }
        }

        var result = keywords
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxKeywords)
            .ToList();

        // DEBUG logging
        Console.WriteLine($"[KeywordExtractor] Input: '{text}'");
        Console.WriteLine($"[KeywordExtractor] Extracted {result.Count} keywords: {string.Join(", ", result)}");

        return result;
    }

    private static List<string> ExtractSpecialPatterns(string text)
    {
        var patterns = new List<string>();

        // Technical Codes: ERR-001, 0x8000, ID_555
        var codes = Regex.Matches(text, @"\b([A-Z]{2,}[-_]?\d+|0x[0-9A-F]+)\b", RegexOptions.IgnoreCase);
        patterns.AddRange(codes.Select(m => m.Value));

        // CamelCase Terms: LoginScreen, iPhone, WiFi
        // (Regex explanation: Detects words with mixed Lower and Upper case)
        var camelCase = Regex.Matches(text, @"\b(?!\b[A-Z]+\b)(?!\b[a-z]+\b)[A-Za-z]+\b");
        patterns.AddRange(camelCase.Select(m => m.Value));

        // Quoted Strings: "System Failure"
        var quoted = Regex.Matches(text, @"""([^""]+)""");
        patterns.AddRange(quoted.Select(m => m.Groups[1].Value));

        // Acronyms: API, SQL, PDF (All caps, 2-5 letters)
        var acronyms = Regex.Matches(text, @"\b[A-Z]{2,5}\b");
        patterns.AddRange(acronyms.Select(m => m.Value));

        return patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
    }

    /// <summary>
    /// Identifies meaningful phrases by checking if two adjacent words are BOTH "Content Words" (non-stopwords).
    /// </summary>
    private static List<string> ExtractNGrams(string text)
    {
        var ngrams = new List<string>();

        // Split by punctuation/whitespace to avoid crossing sentence boundaries (e.g. "end. Start")
        var words = Regex.Split(text, @"[\s\p{P}]+")
                         .Where(w => !string.IsNullOrWhiteSpace(w))
                         .ToArray();

        if (words.Length < 2) return ngrams;

        for (int i = 0; i < words.Length - 1; i++)
        {
            var w1 = words[i].ToLower(CultureInfo.InvariantCulture);
            var w2 = words[i + 1].ToLower(CultureInfo.InvariantCulture);

            // LOGIC: A phrase is meaningful if BOTH words are NOT noise.
            bool w1IsContent = !EnglishStopwords.Contains(w1) && w1.Length > 2;
            bool w2IsContent = !EnglishStopwords.Contains(w2) && w2.Length > 2;

            if (w1IsContent && w2IsContent)
            {
                ngrams.Add($"{w1} {w2}");

                // Optional: Check Trigrams (3 words)
                if (i < words.Length - 2)
                {
                    var w3 = words[i + 2].ToLower(CultureInfo.InvariantCulture);
                    bool w3IsContent = !EnglishStopwords.Contains(w3) && w3.Length > 2;
                    
                    if (w3IsContent)
                    {
                        ngrams.Add($"{w1} {w2} {w3}");
                    }
                }
            }
        }

        return ngrams;
    }
}
