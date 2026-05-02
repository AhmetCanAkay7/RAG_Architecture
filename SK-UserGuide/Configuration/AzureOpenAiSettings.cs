namespace SK_UserGuide.Configuration;
public record AzureOpenAiSettings
{
    public string Endpoint { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string ChatDeploymentName { get; init; } = string.Empty;

    public string EmbeddingDeploymentName { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = "2025-04-01-preview";
}
