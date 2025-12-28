using System.Text.RegularExpressions;

namespace SK_UserGuide.Services.LLM;

/// <summary>
/// Detects the language of user questions and document chunks.
/// Supports Turkish (TR) and English (EN).
/// </summary>
public static class LanguageDetector
{
    private static readonly HashSet<string> TurkishIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        // Soru kelimeleri
        "nasıl", "nedir", "neden", "hangi", "kaç", "kim", "nerede", "ne",
        // Soru ekleri
        "mı", "mi", "mu", "mü", "mısın", "misin", "musun", "müsün",
        // Bağlaçlar
        "ve", "veya", "ile", "için", "ama", "fakat", "ancak", "çünkü",
        // Edatlar
        "gibi", "kadar", "göre", "karşı", "üzere", "dolayı",
        // Yaygın fiiller
        "yapılır", "edilir", "olur", "olan", "olarak", "yapmak", "etmek", "olmak",
        "yapıyorum", "ediyorum", "istiyorum", "gerekiyor", "lazım",
        // Yaygın kelimeler
        "bir", "bu", "şu", "lütfen", "teşekkür", "merhaba", "selam"
    };

    private static readonly Regex TurkishCharPattern =
        new("[çğıöşüÇĞİÖŞÜ]", RegexOptions.Compiled);

    /// <summary>
    /// Detect the language of a text (question or chunk).
    /// Returns "TR" for Turkish, "EN" for English.
    /// </summary>
    public static string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "EN"; // Default

        // 1. Türkçe özel karakterler varsa kesin TR
        if (TurkishCharPattern.IsMatch(text))
            return "TR";

        // 2. Türkçe anahtar kelime kontrolü
        var words = Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(w => w.Length > 1)
            .ToList();

        var turkishWordCount = words.Count(w => TurkishIndicators.Contains(w));

        // En az 1 Türkçe anahtar kelime varsa TR
        if (turkishWordCount >= 1)
            return "TR";

        return "EN";
    }

    /// <summary>
    /// Get the language name for display.
    /// </summary>
    public static string GetLanguageName(string code) => code switch
    {
        "TR" => "Türkçe",
        "EN" => "English",
        _ => code
    };
}
