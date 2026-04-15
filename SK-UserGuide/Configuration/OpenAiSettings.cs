namespace SK_UserGuide.Configuration;

/// <summary>
/// OpenAI connection settings.
/// SECURITY: ApiKey should be provided via environment variables
/// or .NET User Secrets, never committed to source control.
/// 
/// Usage with User Secrets:
///   dotnet user-secrets set "OpenAI:ApiKey" "your-key"
/// </summary>
public record OpenAiSettings
{
    /// <summary>
    /// OpenAI API key.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}
