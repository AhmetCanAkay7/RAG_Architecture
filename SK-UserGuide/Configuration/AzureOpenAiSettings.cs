namespace SK_UserGuide.Configuration;

/// <summary>
/// Azure OpenAI connection settings.
/// SECURITY: ApiKey and Endpoint should be provided via environment variables
/// or .NET User Secrets, never committed to source control.
/// 
/// Usage with User Secrets:
///   dotnet user-secrets set "AzureOpenAI:ApiKey" "your-key"
///   dotnet user-secrets set "AzureOpenAI:Endpoint" "https://your-resource.openai.azure.com/"
///   dotnet user-secrets set "AzureOpenAI:ChatDeploymentName" "your-chat-deployment"
///   dotnet user-secrets set "AzureOpenAI:EmbeddingDeploymentName" "your-embedding-deployment"
/// </summary>
public record AzureOpenAiSettings
{
    /// <summary>
    /// Azure OpenAI resource endpoint URL.
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API key.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Deployment name for chat completion model in Azure.
    /// Note: This is NOT the model name. It's the deployment name you created in Azure portal.
    /// </summary>
    public string ChatDeploymentName { get; init; } = string.Empty;

    /// <summary>
    /// Deployment name for embedding model in Azure.
    /// </summary>
    public string EmbeddingDeploymentName { get; init; } = string.Empty;

    /// <summary>
    /// Azure OpenAI API version.
    /// </summary>
    public string ApiVersion { get; init; } = "2025-04-01-preview";
}
