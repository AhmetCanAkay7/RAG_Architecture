namespace SK_UserGuide.Configuration;

/// <summary>
/// Provider-agnostic LLM configuration.
/// Supports switching between providers by changing the Provider value.
/// Supported providers: "OpenAI"
/// </summary>
public record LlmSettings
{
    /// <summary>
    /// LLM provider identifier. Determines which connector to use.
    /// </summary>
    public string Provider { get; init; } = "OpenAI";

    /// <summary>
    /// Chat completion model name.
    /// </summary>
    public string ChatModel { get; init; } = "gpt-4o";

    /// <summary>
    /// Embedding model name.
    /// </summary>
    public string EmbeddingModel { get; init; } = "nomic-embed-text";

    /// <summary>
    /// Embedding vector dimensions. Must match the collection vector size in Qdrant.
    /// nomic-embed-text = 768
    /// </summary>
    public int EmbeddingDimensions { get; init; } = 768;

    /// <summary>
    /// Maximum tokens for LLM response generation.
    /// </summary>
    public int MaxTokens { get; init; } = 1500;

    /// <summary>
    /// Temperature for response generation (0.0 = deterministic, 1.0 = creative).
    /// </summary>
    public double Temperature { get; init; } = 0.3;

    /// <summary>
    /// Top-P (nucleus sampling) for response diversity.
    /// </summary>
    public double TopP { get; init; } = 0.9;
}
