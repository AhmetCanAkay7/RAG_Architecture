namespace SK_UserGuide.Configuration;

public record QdrantSettings
{
    /// <summary>
    /// Qdrant server host URL.
    /// </summary>
    public string Host { get; init; } = "http://localhost:6333";

    /// <summary>
    /// Qdrant server port.
    /// </summary>
    public int Port { get; init; } = 6333;

    /// <summary>
    /// Optional API key for Qdrant authentication.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Default collection name used when no tenant is specified.
    /// </summary>
    public string DefaultCollection { get; init; } = "default";
}
