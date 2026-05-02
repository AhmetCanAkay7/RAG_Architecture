namespace SK_UserGuide.Configuration;

public record OpenAiSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string ChatModel { get; init; } = "gpt-4o";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
}
