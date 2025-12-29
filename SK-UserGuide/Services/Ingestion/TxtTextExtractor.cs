using System.Text;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Extracts text from plain text files.
/// </summary>
public class TxtTextExtractor : ITextExtractor
{
    public string[] SupportedExtensions => new[] { ".txt" };

    public ExtractedDocument Extract(Stream fileStream, string fileName)
    {
        // Try UTF-8 first, fallback to Windows-1254 (Turkish) if BOM not present
        string text;

        // Reset stream position
        fileStream.Position = 0;

        // Check for BOM
        var bom = new byte[3];
        fileStream.Read(bom, 0, 3);
        fileStream.Position = 0;

        Encoding encoding;
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            encoding = Encoding.UTF8;
        }
        else if (bom[0] == 0xFF && bom[1] == 0xFE)
        {
            encoding = Encoding.Unicode; // UTF-16 LE
        }
        else
        {
            // Default to UTF-8 for Turkish content
            encoding = new UTF8Encoding(false);
        }

        using var reader = new StreamReader(fileStream, encoding, detectEncodingFromByteOrderMarks: true);
        text = reader.ReadToEnd();

        // Normalize Unicode (important for Turkish characters)
        text = text.Normalize(NormalizationForm.FormC);

        // TXT dosyaları için tek sayfa olarak kabul ediyoruz
        var pages = new List<PageContent>
        {
            new PageContent(1, text)
        };

        return new ExtractedDocument(fileName, "txt", pages);
    }
}
