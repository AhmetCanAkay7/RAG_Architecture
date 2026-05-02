namespace SK_UserGuide.Models.Api;

public record RagQuestionRequest
{
    /// <summary>
    /// The user's question.
    /// </summary>
    public required string Question { get; init; }

    /// <summary>
    /// Tenant identifier for collection routing.
    /// Required for collection routing.
    /// Examples: "hr-bot", "planning-team", "it-support"
    /// </summary>
    public required string TenantId { get; init; }
}

public record RagAnswerResponse
{
    public required string Answer { get; init; }
    public List<SourceInfo> Sources { get; init; } = new();
    public bool FromCache { get; init; }
}

public record SourceInfo
{
    public required string DocName { get; init; }
    public string? SectionTitle { get; init; }
    public float Score { get; init; }
}
