namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Interface for text extraction from different file formats.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Extract text content from a file stream.
    /// </summary>
    /// <param name="fileStream">The file stream to read from</param>
    /// <param name="fileName">Original file name</param>
    /// <returns>Extracted document with pages</returns>
    ExtractedDocument Extract(Stream fileStream, string fileName);

    /// <summary>
    /// File extensions supported by this extractor.
    /// </summary>
    string[] SupportedExtensions { get; }
}
