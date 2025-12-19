namespace SK_UserGuide.Configuration;

public record QdrantSettings
{
    public string Host { get; init; } = "http://localhost:6333";
    public int Port { get; init; } = 6333;
    public string Collection { get; init; } = "GuideCollection";
}
