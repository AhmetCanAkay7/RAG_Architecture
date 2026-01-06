using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Extracts text from DOCX files using OpenXML SDK.
/// Handles paragraphs, tables, and basic formatting.
/// </summary>
public class DocxTextExtractor : ITextExtractor
{
    public string[] SupportedExtensions => new[] { ".docx" };

    public ExtractedDocument Extract(Stream fileStream, string fileName)
    {
        var textBuilder = new StringBuilder();

        using var doc = WordprocessingDocument.Open(fileStream, false);
        var body = doc.MainDocumentPart?.Document?.Body;

        if (body == null)
        {
            return new ExtractedDocument(fileName, "docx", new List<PageContent>
            {
                new PageContent(1, string.Empty)
            });
        }

        foreach (var element in body.ChildElements)
        {
            if (element is Paragraph paragraph)
            {
                var text = ExtractParagraphText(paragraph);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    textBuilder.AppendLine(text);
                }
            }
            else if (element is Table table)
            {
                var tableText = ExtractTableText(table);
                if (!string.IsNullOrWhiteSpace(tableText))
                {
                    textBuilder.AppendLine(tableText);
                }
            }
        }

        // DOCX files are treated as single page (like TXT)
        var pages = new List<PageContent>
        {
            new PageContent(1, textBuilder.ToString().Trim())
        };

        return new ExtractedDocument(fileName, "docx", pages);
    }

    /// <summary>
    /// Extract text from a paragraph, preserving heading markers.
    /// </summary>
    private string ExtractParagraphText(Paragraph paragraph)
    {
        var text = paragraph.InnerText;

        // Check if this is a heading (has style like Heading1, Heading2, etc.)
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrEmpty(styleId) && styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            // Convert Word heading to Markdown heading
            var level = ExtractHeadingLevel(styleId);
            var prefix = new string('#', level);
            return $"{prefix} {text}";
        }

        return text;
    }

    /// <summary>
    /// Extract heading level from style ID (e.g., "Heading1" -> 1).
    /// </summary>
    private int ExtractHeadingLevel(string styleId)
    {
        // Extract number from end of style name
        var numericPart = new string(styleId.Where(char.IsDigit).ToArray());
        if (int.TryParse(numericPart, out var level) && level >= 1 && level <= 6)
        {
            return level;
        }
        return 1; // Default to H1
    }

    /// <summary>
    /// Extract text from a table in Markdown format.
    /// </summary>
    private string ExtractTableText(Table table)
    {
        var sb = new StringBuilder();
        var rows = table.Elements<TableRow>().ToList();

        if (rows.Count == 0)
            return string.Empty;

        // Process each row
        for (int i = 0; i < rows.Count; i++)
        {
            var cells = rows[i].Elements<TableCell>().ToList();
            var cellTexts = cells.Select(c => c.InnerText.Trim()).ToList();

            // Format as Markdown table row
            sb.AppendLine("| " + string.Join(" | ", cellTexts) + " |");

            // Add separator after header row
            if (i == 0)
            {
                var separator = string.Join("|", cellTexts.Select(_ => "---"));
                sb.AppendLine("|" + separator + "|");
            }
        }

        return sb.ToString();
    }
}
