namespace SK_UserGuide.Configuration;

public record RagSettings
{
    /// <summary>
    /// Response mode: "ContextOnly" or "Hybrid"
    /// </summary>
    public string ResponseMode { get; init; } = "Hybrid";
}
