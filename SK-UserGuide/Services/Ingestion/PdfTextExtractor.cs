using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using ITextPdfExtractor = iText.Kernel.Pdf.Canvas.Parser.PdfTextExtractor;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Extracts text from PDF files using iText7.
/// Handles page-by-page extraction with header/footer removal.
/// </summary>
public class PdfTextExtractor : ITextExtractor
{
    private readonly TextCleaner _textCleaner;

    public string[] SupportedExtensions => new[] { ".pdf" };

    public PdfTextExtractor(TextCleaner textCleaner)
    {
        _textCleaner = textCleaner;
    }

    public ExtractedDocument Extract(Stream fileStream, string fileName)
    {
        using var pdfReader = new PdfReader(fileStream);
        using var pdfDoc = new PdfDocument(pdfReader);

        var pages = new List<PageContent>();
        var totalPages = pdfDoc.GetNumberOfPages();

        for (int i = 1; i <= totalPages; i++)
        {
            var page = pdfDoc.GetPage(i);
            var strategy = new SimpleTextExtractionStrategy();
            var rawText = ITextPdfExtractor.GetTextFromPage(page, strategy);

            // Header/footer temizliği
            var cleanedText = _textCleaner.RemoveHeaderFooter(rawText, i, totalPages);

            pages.Add(new PageContent(i, cleanedText));
        }

        return new ExtractedDocument(fileName, "pdf", pages);
    }
}
