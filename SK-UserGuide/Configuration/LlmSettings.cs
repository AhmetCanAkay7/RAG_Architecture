namespace SK_UserGuide.Configuration;
public record LlmSettings
{
    public string Provider { get; init; } = "OpenAI";
    public string ChatModel { get; init; } = "gpt-4o";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; init; } = 1536;
    public int MaxTokens { get; init; } = 1500;
    public double Temperature { get; init; } = 0.3;
    public double TopP { get; init; } = 0.9;
}
