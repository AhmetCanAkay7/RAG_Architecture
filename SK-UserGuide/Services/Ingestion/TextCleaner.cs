using System.Text;
using System.Text.RegularExpressions;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Cleans extracted text by normalizing unicode, fixing encoding issues,
/// and removing broken/invalid content.
/// </summary>
public class TextCleaner
{
    /// <summary>
    /// Clean and normalize text content.
    /// </summary>
    public string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // 1. Unicode Normalization (Türkçe karakterler için kritik)
        text = text.Normalize(NormalizationForm.FormC);

        // 2. Yaygın PDF encoding sorunlarını düzelt
        text = FixTurkishEncodingIssues(text);

        // 3. Hyphenation düzeltme (kelime-\nbölünmesi → kelimebölünmesi)
        text = Regex.Replace(text, @"(\w)-\r?\n(\w)", "$1$2");

        // 4. Çoklu boşlukları tek boşluğa indir (satır içi)
        text = Regex.Replace(text, @"[ \t]+", " ");

        // 5. Çoklu boş satırları tek boş satıra indir
        text = Regex.Replace(text, @"(\r?\n){3,}", "\n\n");

        // 6. Satır başı/sonu boşlukları temizle
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .ToList();

        // 7. Bozuk satırları filtrele
        lines = FilterBrokenLines(lines);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Fix common Turkish character encoding issues from PDFs.
    /// </summary>
    private string FixTurkishEncodingIssues(string text)
    {
        // Yaygın UTF-8 → Latin1 encoding hataları
        var replacements = new Dictionary<string, string>
        {
            // Küçük harfler
            { "Ä±", "ı" },   // ı
            { "ÄŸ", "ğ" },   // ğ
            { "ÅŸ", "ş" },   // ş
            { "Ã¼", "ü" },   // ü
            { "Ã¶", "ö" },   // ö
            { "Ã§", "ç" },   // ç
            
            // Büyük harfler
            { "Ä°", "İ" },   // İ
            { "Äž", "Ğ" },   // Ğ
            { "Åž", "Ş" },   // Ş
            { "Ãœ", "Ü" },   // Ü
            { "Ã–", "Ö" },   // Ö
            { "Ã‡", "Ç" }    // Ç
        };

        foreach (var (bad, good) in replacements)
            text = text.Replace(bad, good);

        return text;
    }

    /// <summary>
    /// Filter out broken/invalid lines.
    /// </summary>
    private List<string> FilterBrokenLines(List<string> lines)
    {
        return lines.Where(line =>
        {
            // Boş satırları koru (paragraf ayırıcı)
            if (string.IsNullOrWhiteSpace(line)) return true;

            // Çok kısa satırlar (muhtemelen bozuk)
            if (line.Length < 3) return false;

            // Sadece sayı ve sembol içeren satırlar
            if (Regex.IsMatch(line, @"^[\d\s\.\-\(\)\[\]\{\}:;,]+$")) return false;

            // Yeterli harf içermeyen satırlar
            if (line.Count(c => char.IsLetter(c)) < 2) return false;

            return true;
        }).ToList();
    }

    /// <summary>
    /// Remove common header/footer patterns from page text.
    /// </summary>
    public string RemoveHeaderFooter(string text, int pageNum, int totalPages)
    {
        var lines = text.Split('\n').ToList();

        // Sayfa numarası pattern'leri
        var pagePatterns = new[]
        {
            $@"^\s*{pageNum}\s*$",                        // Sadece sayı
            $@"^\s*Sayfa\s*{pageNum}\s*$",                // "Sayfa X"
            $@"^\s*{pageNum}\s*/\s*{totalPages}\s*$",     // "X / Y"
            @"^\s*[-–—]\s*\d+\s*[-–—]\s*$",              // "- X -"
            $@"^\s*Page\s*{pageNum}\s*$",                 // "Page X"
            $@"^\s*{pageNum}\s*of\s*{totalPages}\s*$"     // "X of Y"
        };

        var filteredLines = lines.Where(line =>
            !pagePatterns.Any(p => Regex.IsMatch(line, p, RegexOptions.IgnoreCase))
        ).ToList();

        return string.Join("\n", filteredLines);
    }
}
