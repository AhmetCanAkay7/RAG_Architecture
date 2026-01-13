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
    /// Estimated token count for compressed text.
    /// Uses character-based estimation since original token count doesn't apply after sentence extraction.
    /// </summary>
    public int EstimatedTokens => (int)(Text.Length / 4.0);  // ~4 chars/token for English
}

/// <summary>
/// Result of context compression.
/// </summary>
public class CompressedContext
{
    public List<ContextItem> Items { get; set; } = new();

    public string Summary { get; set; } = string.Empty;
    public int EstimatedTokens => Items.Sum(i => i.EstimatedTokens);

    /// <summary>
    /// Average relevance score.
    /// </summary>
    public double AverageScore => Items.Count > 0
        ? Items.Average(i => i.OriginalScore)
        : 0;
}

/// <summary>
/// Citation reference for a source.
/// </summary>
public record Citation
{
    public required int Index { get; init; }
    public required string DocName { get; init; }
    public int? Page { get; init; }
    public string? Section { get; init; }

    public override string ToString()
    {
        var parts = new List<string> { DocName };
        if (Page.HasValue) parts.Add($"Page {Page}");
        if (!string.IsNullOrEmpty(Section)) parts.Add(Section);
        return $"[{Index}] {string.Join(" > ", parts)}";
    }
}

/// <summary>
/// Structured RAG response.
/// </summary>
public record RagResponse
{
    /// <summary>
    /// Brief summary (1-2 sentences)
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Detailed answer (formatted by question type)
    /// </summary>
    public required string Details { get; init; }

    /// <summary>
    /// Source citations
    /// </summary>
    public required List<Citation> Citations { get; init; }

    /// <summary>
    /// Missing information warning (if any)
    /// </summary>
    public string? MissingInfo { get; init; }

    /// <summary>
    /// Response language (TR/EN)
    /// </summary>
    public required string ResponseLanguage { get; init; }

    /// <summary>
    /// Full formatted response for display
    /// </summary>
    public string ToDisplayString()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine(Summary);

        if (!string.IsNullOrWhiteSpace(Details))
        {
            sb.AppendLine();
            sb.AppendLine(Details);
        }

        if (Citations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(ResponseLanguage == "TR" ? "📚 **Kaynaklar:**" : "📚 **Sources:**");
            sb.AppendLine();
            foreach (var cite in Citations)
            {
                sb.AppendLine($"- {cite}");
            }
        }

        if (!string.IsNullOrWhiteSpace(MissingInfo))
        {
            sb.AppendLine();
            sb.AppendLine($"⚠️ {MissingInfo}");
        }

        return sb.ToString();
    }
}
