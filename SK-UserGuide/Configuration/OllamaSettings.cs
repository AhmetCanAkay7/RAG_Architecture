namespace SK_UserGuide.Configuration;

public record OllamaSettings
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string ChatModel { get; init; } = "llama3:8b";
    public string EmbeddingModel { get; init; } = "nomic-embed-text";
}
