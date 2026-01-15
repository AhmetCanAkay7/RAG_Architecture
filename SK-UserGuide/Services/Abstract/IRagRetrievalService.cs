namespace SK_UserGuide.Services.Abstract;

/// <summary>
/// Interface for RAG retrieval service with context compression and structured responses.
/// </summary>
public interface IRagRetrievalService
{
    /// <summary>
    /// Process a question with RAG pipeline.
    /// </summary>
    Task<string> AskAsync(string question);

    /// <summary>
    /// Process a question with RAG pipeline using streaming response.
    /// </summary>
    IAsyncEnumerable<string> AskStreamingAsync(string question, CancellationToken cancellationToken = default);
}
