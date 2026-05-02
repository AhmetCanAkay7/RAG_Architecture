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

        // 0. Repair common UTF-8 text that was decoded as a Windows code page.
        text = RepairMojibake(text);

        // 1. Unicode Normalization (universal requirement)
        text = text.Normalize(NormalizationForm.FormC);

        // 2. Fix common PDF hyphenation (word-\nbreak → wordbreak)
        text = Regex.Replace(text, @"(\w)-\r?\n(\w)", "$1$2");

        // 3. Reduce multiple spaces to single space (inline)
        text = Regex.Replace(text, @"[ \t]+", " ");

        // 4. Reduce multiple blank lines to single blank line
        text = Regex.Replace(text, @"(\r?\n){3,}", "\n\n");

        // 5. Trim line start/end whitespace
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .ToList();

        // 6. Filter broken lines
        lines = FilterBrokenLines(lines);

        return string.Join("\n", lines);
    }

    private static string RepairMojibake(string text)
    {
        if (!LooksLikeMojibake(text))
            return text;

        try
        {
            var bytes = Encoding.GetEncoding(1252).GetBytes(text);
            var repaired = Encoding.UTF8.GetString(bytes);
            return MojibakeScore(repaired) < MojibakeScore(text) ? repaired : text;
        }
        catch
        {
            return text;
        }
    }

    private static bool LooksLikeMojibake(string text)
    {
        return text.Contains('Ã') ||
               text.Contains('Ä') ||
               text.Contains('Å') ||
               text.Contains('Â') ||
               text.Contains('â');
    }

    private static int MojibakeScore(string text)
    {
        return text.Count(c => c is 'Ã' or 'Ä' or 'Å' or 'Â' or 'â' or '�');
    }

    /// <summary>
    /// Filter out broken/invalid lines.
    /// </summary>
    private List<string> FilterBrokenLines(List<string> lines)
    {
        return lines.Where(line =>
        {
            // Keep empty lines (paragraph separator)
            if (string.IsNullOrWhiteSpace(line)) return true;

            // Very short lines (likely broken)
            if (line.Length < 3) return false;

            // Lines with only numbers and symbols
            if (Regex.IsMatch(line, @"^[\d\s\.\-\(\)\[\]\{\}:;,]+$")) return false;

            // Lines without enough letters
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

        // Page number patterns
        var pagePatterns = new[]
        {
            $@"^\s*{pageNum}\s*$",                         // Just number
            $@"^\s*Page\s*{pageNum}\s*$",                  // "Page X"
            $@"^\s*{pageNum}\s*/\s*{totalPages}\s*$",      // "X / Y"
            @"^\s*[-–—]\s*\d+\s*[-–—]\s*$",               // "- X -"
            $@"^\s*{pageNum}\s*of\s*{totalPages}\s*$"      // "X of Y"
        };

        var filteredLines = lines.Where(line =>
            !pagePatterns.Any(p => Regex.IsMatch(line, p, RegexOptions.IgnoreCase))
        ).ToList();

        return string.Join("\n", filteredLines);
    }
}
