namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Represents a structural block detected in the document.
/// </summary>
public enum BlockType
{
    Empty,
    Heading,
    Paragraph,
    StepList,
    BulletList
}

/// <summary>
/// A structural block with its type, content, and page information.
/// </summary>
public record StructuralBlock(
    BlockType Type,
    string Text,
    int? Page
);

/// <summary>
/// Result of chunking operation.
/// </summary>
public record ChunkResult
{
    public required int Index { get; init; }
    public required string Text { get; init; }
    public required string SectionTitle { get; init; }
    public int? Page { get; init; }
    public required int EstimatedTokens { get; init; }
    public bool HasOverlap { get; init; }
}

/// <summary>
/// Page content from extracted document.
/// </summary>
public record PageContent(int PageNumber, string Text);

/// <summary>
/// Extracted document with pages.
/// </summary>
public record ExtractedDocument(
    string FileName,
    string SourceType,
    List<PageContent> Pages
);

/// <summary>
/// Result of ingestion operation.
/// </summary>
public record IngestionResult
{
    public bool Success { get; init; }
    public string? DocId { get; init; }
    public int? Version { get; init; }
    public int? ChunkCount { get; init; }
    public long? ProcessingTimeMs { get; init; }
    public string Message { get; init; } = "";
}
