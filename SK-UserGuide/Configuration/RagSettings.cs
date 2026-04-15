namespace SK_UserGuide.Configuration;

/// <summary>
/// RAG pipeline configuration with tunable parameters.
/// These values were expanded from Ollama-era hard limits to leverage
/// Azure OpenAI's larger context window and faster inference.
/// </summary>
public record RagSettings
{
    /// <summary>
    /// Response mode: "ContextOnly" (no LLM) or "Hybrid" (LLM + context).
    /// </summary>
    public string ResponseMode { get; init; } = "Hybrid";

    /// <summary>
    /// Maximum token budget for retrieved context passed to LLM.
    /// Previously 800 (Ollama constraint), now expanded for Azure OpenAI.
    /// </summary>
    public int ContextTokenBudget { get; init; } = 3000;

    /// <summary>
    /// Token budget for context compressor output.
    /// Previously 300 (Ollama constraint).
    /// </summary>
    public int CompressorTokenBudget { get; init; } = 1500;

    /// <summary>
    /// Maximum sentences to include in summary.
    /// Previously 6.
    /// </summary>
    public int SummaryMaxSentences { get; init; } = 12;

    /// <summary>
    /// Target token count for summary generation.
    /// Previously 250.
    /// </summary>
    public int SummaryTargetTokens { get; init; } = 1000;

    /// <summary>
    /// Maximum sentences extracted per chunk during compression.
    /// Previously 5.
    /// </summary>
    public int MaxSentencesPerChunk { get; init; } = 8;
}
