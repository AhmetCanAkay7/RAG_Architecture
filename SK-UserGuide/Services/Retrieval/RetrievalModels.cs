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

    /// <summary>
    /// Estimated token count for this chunk.
    /// </summary>
    public int EstimatedTokens => (int)(Text.Length / 3.5);
}

/// <summary>
/// Result of the hybrid retrieval process.
/// </summary>
public record RetrievalResult
{
    /// <summary>
    /// Assembled context string for LLM.
    /// </summary>
    public required string Context { get; init; }

    /// <summary>
    /// Citation list for sources.
    /// </summary>
    public required List<string> Citations { get; init; }

    /// <summary>
    /// Selected chunks after all filtering.
    /// </summary>
    public required List<ScoredChunk> SelectedChunks { get; init; }

    /// <summary>
    /// Number of chunks in final selection.
    /// </summary>
    public int ChunkCount => SelectedChunks.Count;

    /// <summary>
    /// Total candidates before filtering.
    /// </summary>
    public int TotalCandidates { get; init; }

    /// <summary>
    /// Total tokens used in context.
    /// </summary>
    public int TokensUsed { get; init; }
}
