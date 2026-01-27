namespace SK_UserGuide.Services.Ingestion;

/// <summary>
/// Qdrant point payload schema for document chunks.
/// Contains all required metadata fields for retrieval.
/// </summary>
public record ChunkPayload
{
    public required string DocId { get; init; }
    public required string DocName { get; init; }
    public required string SourceType { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Text { get; init; }
    public required string Language { get; init; }
    public required string ContentHash { get; init; }
    public int? Page { get; init; }
    public string? SectionTitle { get; init; }
    public int? EstimatedTokens { get; init; }
    public bool? HasOverlap { get; init; }
}
