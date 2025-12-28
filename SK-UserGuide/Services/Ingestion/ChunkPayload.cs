namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Qdrant point payload schema for document chunks.
/// Contains all required metadata fields for retrieval and versioning.
/// </summary>
public record ChunkPayload
{
    // === Zorunlu Alanlar ===

    /// <summary>Stable document ID - hash of filename</summary>
    public required string DocId { get; init; }

    /// <summary>Original file name</summary>
    public required string DocName { get; init; }

    /// <summary>Source type: "pdf" or "txt"</summary>
    public required string SourceType { get; init; }

    /// <summary>Version number - increments per upload</summary>
    public required int Version { get; init; }

    /// <summary>UTC timestamp of creation</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Chunk index within document (0, 1, 2, ...)</summary>
    public required int ChunkIndex { get; init; }

    /// <summary>Actual chunk text content</summary>
    public required string Text { get; init; }

    /// <summary>Document language - always "tr" for this project</summary>
    public required string Language { get; init; }

    /// <summary>SHA256 hash of text for deduplication</summary>
    public required string ContentHash { get; init; }

    // === Opsiyonel Alanlar ===

    /// <summary>PDF page number (if applicable)</summary>
    public int? Page { get; init; }

    /// <summary>Section/heading title (if detected)</summary>
    public string? SectionTitle { get; init; }

    /// <summary>Estimated token count</summary>
    public int? EstimatedTokens { get; init; }

    /// <summary>Whether this chunk contains overlap from previous chunk</summary>
    public bool? HasOverlap { get; init; }
}
