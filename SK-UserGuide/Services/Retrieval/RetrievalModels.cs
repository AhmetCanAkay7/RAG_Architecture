namespace SK_UserGuide.Services.Retrieval;

/// <summary>
/// A chunk with its fused score and metadata for retrieval.
/// </summary>
public record ScoredChunk
{
    public required ulong Id { get; init; }
    public required double Score { get; init; }
    public required string Text { get; init; }
    public string? DocId { get; init; }
    public string? DocName { get; init; }
    public string? SectionTitle { get; init; }
    public int? Page { get; init; }
    public int? ChunkIndex { get; init; }
    public int? Version { get; init; }
    public int EstimatedTokens { get; init; }
}
public record RetrievalResult
{
    public required string Context { get; init; }
    public required List<string> Citations { get; init; }
    public required List<ScoredChunk> SelectedChunks { get; init; }
    public int ChunkCount => SelectedChunks.Count;
    public int TotalCandidates { get; init; }
    public int TokensUsed { get; init; }
}
