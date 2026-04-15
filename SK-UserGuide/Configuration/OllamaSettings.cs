namespace SK_UserGuide.Configuration;

/// <summary>
/// Ollama connection settings.
/// </summary>
public record OllamaSettings
{
    /// <summary>
    /// Ollama endpoint URL.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:11434";
}
