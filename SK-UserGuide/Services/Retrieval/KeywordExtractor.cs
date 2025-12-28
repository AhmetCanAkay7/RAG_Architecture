using System.Globalization;
using System.Text.RegularExpressions;

namespace SK_UserGuide.Services.Retrieval;

/// <summary>
/// Extracts keywords from Turkish text for sparse search.
/// Handles stopwords, special patterns (error codes, CamelCase), and normalization.
/// </summary>
public static class KeywordExtractor
{
    private static readonly HashSet<string> TurkishStopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Soru kelimeleri
        "ne", "nasıl", "nedir", "neden", "hangi", "kaç", "kim", "nerede",
        // Bağlaçlar
        "ve", "veya", "ile", "için", "ama", "fakat", "ancak", "çünkü",
        // Ekler/Edatlar
        "bir", "bu", "şu", "o", "da", "de", "mi", "mı", "mu", "mü",
        // Zamirler
        "ben", "sen", "biz", "siz", "onlar", "kendi",
        // Yardımcı fiiller
        "var", "yok", "olur", "oldu", "olan", "olarak", "gibi",
        // Genel
        "ise", "eğer", "daha", "en", "çok", "az", "sonra", "önce"
    };

    /// <summary>
    /// Extract keywords from a question for sparse search.
    /// </summary>
    public static List<string> Extract(string text, int maxKeywords = 8)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var keywords = new List<string>();

        // 1. Özel pattern'leri önce yakala (hata kodları, CamelCase, vs.)
        var specialPatterns = ExtractSpecialPatterns(text);
        keywords.AddRange(specialPatterns);

        // 2. Normal kelimeleri tokenize et
        var words = Regex.Split(text.ToLower(new CultureInfo("tr-TR")), @"[\s\p{P}]+")
            .Where(w => w.Length > 2)
            .Where(w => !TurkishStopwords.Contains(w))
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

        // Error codes: ERR-001, HATA_5001, E1234
        var errorCodes = Regex.Matches(text, @"[A-ZÇĞİÖŞÜ]{2,}[-_]?\d+", RegexOptions.IgnoreCase);
        patterns.AddRange(errorCodes.Select(m => m.Value));

        // CamelCase: LoginScreen, SiparisFormu
        var camelCase = Regex.Matches(text, @"[A-ZÇĞİÖŞÜ][a-zçğıöşü]+[A-ZÇĞİÖŞÜ][a-zA-ZçğıöşüÇĞİÖŞÜ]*");
        patterns.AddRange(camelCase.Select(m => m.Value));

        // Quoted strings: "Kaydet" butonu
        var quoted = Regex.Matches(text, @"""([^""]+)""");
        patterns.AddRange(quoted.Select(m => m.Groups[1].Value));

        // Screen/menu names in Turkish: Ayarlar Ekranı, Kullanıcı Menüsü
        var screenNames = Regex.Matches(text, @"[A-ZÇĞİÖŞÜ][a-zçğıöşü]+\s+(Ekranı|Menüsü|Sayfası|Paneli|Formu)",
            RegexOptions.IgnoreCase);
        patterns.AddRange(screenNames.Select(m => m.Value));

        return patterns;
    }
}
