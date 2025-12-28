namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Extracts text from plain text files.
/// </summary>
public class TxtTextExtractor : ITextExtractor
{
    public string[] SupportedExtensions => new[] { ".txt" };

    public ExtractedDocument Extract(Stream fileStream, string fileName)
    {
        using var reader = new StreamReader(fileStream);
        var text = reader.ReadToEnd();

        // TXT dosyaları için tek sayfa olarak kabul ediyoruz
        var pages = new List<PageContent>
        {
            new PageContent(1, text)
        };

        return new ExtractedDocument(fileName, "txt", pages);
    }
}
