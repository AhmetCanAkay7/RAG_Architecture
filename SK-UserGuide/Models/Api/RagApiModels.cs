namespace SK_UserGuide.Models.Api;

public record RagQuestionRequest
{
    public required string Question { get; init; }
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
