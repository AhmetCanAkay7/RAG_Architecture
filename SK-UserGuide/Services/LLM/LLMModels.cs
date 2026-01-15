namespace SK_UserGuide.Services.LLM;

/// <summary>
/// A compressed context item with metadata.
/// </summary>
public record ContextItem
{
    public required int Index { get; init; }
    public required string Text { get; init; }
    public string? DocName { get; init; }
    public int? Page { get; init; }
    public string? Section { get; init; }
    public required string Language { get; init; }
    public double OriginalScore { get; init; }

    /// <summary>
    /// Estimated token count (~4 chars/token for English).
    /// </summary>
    public int EstimatedTokens => (int)(Text.Length / 4.0);
}

/// <summary>
/// Result of context compression.
/// </summary>
public class CompressedContext
{
    public List<ContextItem> Items { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public int EstimatedTokens => Items.Sum(i => i.EstimatedTokens);
    public double AverageScore => Items.Count > 0 ? Items.Average(i => i.OriginalScore) : 0;
}
