namespace SK_UserGuide.Configuration;

/// <summary>
/// Provider-agnostic LLM configuration.
/// Supports switching between providers by changing the Provider value.
/// Supported providers: "AzureOpenAI", "OpenAI", "Ollama"
/// </summary>
public record LlmSettings
{
    /// <summary>
    /// LLM provider identifier. Determines which connector to use.
    /// </summary>
    public string Provider { get; init; } = "AzureOpenAI";

    /// <summary>
    /// Chat completion model name.
    /// </summary>
    public string ChatModel { get; init; } = "gpt-5.2";

    /// <summary>
    /// Embedding model name.
    /// </summary>
    public string EmbeddingModel { get; init; } = "text-embedding-3-large";

    /// <summary>
    /// Embedding vector dimensions. Must match the collection vector size in Qdrant.
    /// text-embedding-3-large = 3072, text-embedding-3-small = 1536
    /// </summary>
    public int EmbeddingDimensions { get; init; } = 3072;

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
