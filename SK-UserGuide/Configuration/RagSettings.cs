namespace SK_UserGuide.Configuration;

public record RagSettings
{
    public string ResponseMode { get; init; } = "Hybrid";
    public int ContextTokenBudget { get; init; } = 3000;
}
