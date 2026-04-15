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
    /// </summary>
    public int ContextTokenBudget { get; init; } = 3000;
}
